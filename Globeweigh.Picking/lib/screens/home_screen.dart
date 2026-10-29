import 'dart:async';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:ota_update/ota_update.dart';
import 'package:package_info_plus/package_info_plus.dart';

import '../api/api_client.dart';
import '../config.dart';
import '../models/update_info.dart';
import '../services/device_registration.dart';
import '../services/patch_updater.dart';
import '../services/session.dart';
import '../services/vnc_companion.dart';
import '../theme/app_colors.dart';
import '../widgets/gradient_bg.dart';
import '../widgets/wordmark.dart';
import 'login_screen.dart';
import 'pending_loads_screen.dart';

/// Landing screen — navy immersive entry with the Globeweigh wordmark and the
/// primary "Pending Loads" action. Also checks for a queued release and lets the
/// user install it in-app (the deployment "pull" flow).
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  final _api = ApiClient();

  UpdateInfo? _update;
  OtaStatus? _otaStatus;
  String _otaPercent = '0';
  String? _otaError;

  String? _deviceName;
  String? _ipAddress;
  String? _version;

  int? _pendingCount;
  Timer? _pendingTimer;
  // "Later" on the update overlay hides it until this time (it re-appears — the
  // update must stay hard to miss, but can't hard-block work during a delivery).
  DateTime? _updateSnoozedUntil;

  // Over-the-air Dart patch (Shorebird) — see PatchUpdater. Its restart prompt can be put
  // off the same way as the APK prompt, and comes back the same way until the app restarts.
  final _patch = PatchUpdater.instance;
  DateTime? _restartSnoozedUntil;

  @override
  void initState() {
    super.initState();
    _checkForUpdate();
    _patch.state.addListener(_onPatchChanged);
    _patch.checkAndDownload();
    _loadDeviceInfo();
    _loadPendingCount();
    // Remote support (droidVNC-NG): nudge its VNC server running — headless on a
    // provisioned device, a safe no-op everywhere else — and offer the one-time
    // install on devices that don't have it yet.
    VncCompanion.ensureRunning();
    _maybeOfferVncInstall();
    // Keep the home-screen count fresh AND keep re-checking for a queued release —
    // the tablets are left running for weeks, so a one-shot startup check meant a
    // pushed update was never seen until the next app restart.
    _pendingTimer = Timer.periodic(const Duration(minutes: 1), (_) {
      _loadPendingCount();
      _checkForUpdate();
      _patch.checkAndDownload();
    });
  }

  @override
  void dispose() {
    _pendingTimer?.cancel();
    _patch.state.removeListener(_onPatchChanged);
    super.dispose();
  }

  void _onPatchChanged() {
    if (mounted) setState(() {});
  }

  // One-time offer to install the droidVNC-NG remote-support companion (a separate
  // APK Android won't let us bundle-install silently). Declining never asks again;
  // a device can also be provisioned entirely via scripts/provision-vnc.ps1.
  Future<void> _maybeOfferVncInstall() async {
    if (!await VncCompanion.shouldOfferInstall()) return;
    await VncCompanion.markOffered();
    if (!mounted) return;
    final install = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Remote support'),
        content: const Text(
            'Install the remote-support module so the office can view this '
            "tablet's screen when helping you?"),
        actions: [
          TextButton(
              onPressed: () => Navigator.pop(ctx, false),
              child: const Text('Not now')),
          FilledButton(
              onPressed: () => Navigator.pop(ctx, true),
              child: const Text('Install')),
        ],
      ),
    );
    if (install != true) return;
    VncCompanion.installCompanion().listen((_) {}, onError: (Object e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Remote support install failed: $e')),
      );
    });
  }

  Future<void> _loadPendingCount() async {
    try {
      final rows = await _api.getPending();
      if (!mounted) return;
      setState(() => _pendingCount = rows.length);
    } catch (_) {
      // Leave the last known count (or unknown) if the server can't be reached.
    }
  }

  Future<void> _loadDeviceInfo() async {
    final name = await DeviceRegistration.deviceName();
    String? version;
    try {
      version = (await PackageInfo.fromPlatform()).version;
    } catch (_) {}
    final ip = await _localIpAddress();
    if (!mounted) return;
    setState(() {
      _deviceName = name;
      _version = version;
      _ipAddress = ip;
    });
  }

  /// The device's own LAN IPv4 (e.g. the WiFi address). Prefers a private-range
  /// address; best-effort, returns null if it can't be determined.
  static Future<String?> _localIpAddress() async {
    try {
      final ifaces = await NetworkInterface.list(
        type: InternetAddressType.IPv4,
        includeLoopback: false,
      );
      final addrs = [
        for (final i in ifaces) ...i.addresses.map((a) => a.address),
      ];
      if (addrs.isEmpty) return null;
      return addrs.firstWhere(
        (a) => a.startsWith('192.168.') || a.startsWith('10.') || a.startsWith('172.'),
        orElse: () => addrs.first,
      );
    } catch (_) {
      return null;
    }
  }

  Future<void> _checkForUpdate() async {
    final info = await DeviceRegistration.checkForUpdate();
    if (!mounted) return;
    setState(() => _update = (info?.pendingUpdate ?? false) ? info : null);
  }

  // The full-screen update prompt shows whenever a release is queued, except during
  // a "Later" snooze (which expires, so it keeps coming back until installed).
  bool get _showUpdateOverlay =>
      _update != null &&
      (_updateSnoozedUntil == null ||
          DateTime.now().isAfter(_updateSnoozedUntil!));

  // A downloaded patch wants a restart. A queued APK release outranks it (a full release
  // carries the patch's code and more), so the two prompts never stack.
  bool get _showRestartOverlay =>
      _update == null &&
      _patch.state.value == PatchState.restartRequired &&
      (_restartSnoozedUntil == null ||
          DateTime.now().isAfter(_restartSnoozedUntil!));

  Future<void> _openPendingLoads(BuildContext context) async {
    await Navigator.of(context).push(
      MaterialPageRoute(builder: (_) => const PendingLoadsScreen()),
    );
    _loadPendingCount();
  }

  void _startUpdate() {
    final pkg = _update?.pendingPackageFileName;
    if (pkg == null) return;
    final url =
        '${AppConfig.apiBaseUrl}/api/devices/apk?fileName=${Uri.encodeComponent(pkg)}';
    setState(() {
      _otaError = null;
      _otaStatus = OtaStatus.DOWNLOADING;
      _otaPercent = '0';
    });
    try {
      OtaUpdate()
          .execute(url, destinationFilename: 'globeweigh-picking-update.apk')
          .listen((OtaEvent event) {
        if (!mounted) return;
        setState(() {
          _otaStatus = event.status;
          _otaPercent = event.value ?? _otaPercent;
          if (event.status != OtaStatus.DOWNLOADING &&
              event.status != OtaStatus.INSTALLING) {
            _otaError = event.value;
          }
        });
      });
    } catch (e) {
      setState(() => _otaError = '$e');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.navy,
      body: Stack(
        children: [
          _homeBody(context),
          // Front-and-center update prompt — scrim over the whole home screen so a
          // queued release can't be missed.
          if (_showUpdateOverlay)
            Positioned.fill(
              child: ColoredBox(
                color: Colors.black.withValues(alpha: 0.65),
                child: Center(
                  child: SingleChildScrollView(
                    padding: const EdgeInsets.all(24),
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 520),
                      child: _UpdateOverlay(
                        version: _update?.pendingVersion,
                        status: _otaStatus,
                        percent: _otaPercent,
                        error: _otaError,
                        onUpdate: _startUpdate,
                        onLater: () => setState(() => _updateSnoozedUntil =
                            DateTime.now().add(const Duration(minutes: 10))),
                      ),
                    ),
                  ),
                ),
              ),
            )
          // Same treatment once a patch is on the device: front and centre, over a scrim,
          // with the one button that matters.
          else if (_showRestartOverlay)
            Positioned.fill(
              child: ColoredBox(
                color: Colors.black.withValues(alpha: 0.65),
                child: Center(
                  child: SingleChildScrollView(
                    padding: const EdgeInsets.all(24),
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 520),
                      child: _RestartOverlay(
                        patchNumber: _patch.nextPatch,
                        onRestart: PatchUpdater.restartApp,
                        onLater: () => setState(() => _restartSnoozedUntil =
                            DateTime.now().add(const Duration(minutes: 10))),
                      ),
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }

  Widget _homeBody(BuildContext context) {
    return GradientBg(
        child: SafeArea(
          child: Column(
            children: [
              // Top bar: dev-only DB switch on the left, the signed-in user +
              // Sign out prominent on the right.
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 0),
                child: Row(
                  children: [
                    // Dev-only: flip the running debug build between the local and
                    // PROD API/DB. Hidden entirely in release builds.
                    if (kDebugMode) _DbSwitch(onChanged: _loadPendingCount),
                    const Spacer(),
                    Container(
                      padding: const EdgeInsets.symmetric(
                          horizontal: 16, vertical: 8),
                      decoration: BoxDecoration(
                        color: Colors.white.withValues(alpha: 0.08),
                        borderRadius: BorderRadius.circular(24),
                        border: Border.all(
                            color: Colors.white.withValues(alpha: 0.18)),
                      ),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Container(
                            width: 28,
                            height: 28,
                            decoration: BoxDecoration(
                              color: AppColors.accent.withValues(alpha: 0.18),
                              shape: BoxShape.circle,
                            ),
                            child: const Icon(Icons.person,
                                color: AppColors.accent, size: 17),
                          ),
                          const SizedBox(width: 10),
                          Text(
                            Session.current?.fullName ?? '—',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(
                              fontFamily: 'Poppins',
                              fontSize: 15,
                              fontWeight: FontWeight.w600,
                              color: Colors.white,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(width: 10),
                    OutlinedButton.icon(
                      icon: const Icon(Icons.logout, size: 18),
                      label: const Text('Sign out'),
                      style: OutlinedButton.styleFrom(
                        foregroundColor: Colors.white,
                        side: BorderSide(
                            color: Colors.white.withValues(alpha: 0.35)),
                        padding: const EdgeInsets.symmetric(
                            horizontal: 16, vertical: 10),
                        shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(24)),
                        textStyle: const TextStyle(
                          fontFamily: 'Poppins',
                          fontSize: 14,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      onPressed: _signOut,
                    ),
                  ],
                ),
              ),
              Expanded(
                child: Center(
                  child: SingleChildScrollView(
                    padding:
                        const EdgeInsets.symmetric(horizontal: 28, vertical: 24),
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(maxWidth: 440),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                    const Center(child: Wordmark(iconSize: 64, fontSize: 34)),
                    if (_update != null) ...[
                      const SizedBox(height: 28),
                      _UpdateBanner(
                        version: _update!.pendingVersion,
                        status: _otaStatus,
                        percent: _otaPercent,
                        error: _otaError,
                        onUpdate: _startUpdate,
                      ),
                    ] else if (_patch.state.value.showsBanner) ...[
                      // A patch on its way (or waiting on a restart the picker put off).
                      const SizedBox(height: 28),
                      _PatchBanner(
                        state: _patch.state.value,
                        error: _patch.error,
                        onRetry: _patch.checkAndDownload,
                        onRestart: PatchUpdater.restartApp,
                      ),
                    ],
                    const SizedBox(height: 48),
                    ElevatedButton.icon(
                      icon: const Icon(Icons.local_shipping_outlined, size: 24),
                      label: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const Text('Pending Loads'),
                          if (_pendingCount != null) ...[
                            const SizedBox(width: 10),
                            Container(
                              constraints: const BoxConstraints(minWidth: 26),
                              padding: const EdgeInsets.symmetric(
                                  horizontal: 8, vertical: 2),
                              decoration: BoxDecoration(
                                color: Colors.white.withValues(alpha: 0.28),
                                borderRadius: BorderRadius.circular(20),
                              ),
                              child: Text(
                                '$_pendingCount',
                                textAlign: TextAlign.center,
                                style: const TextStyle(
                                  fontFamily: 'Poppins',
                                  fontSize: 15,
                                  fontWeight: FontWeight.w700,
                                  color: Colors.white,
                                ),
                              ),
                            ),
                          ],
                        ],
                      ),
                      style: ElevatedButton.styleFrom(
                        minimumSize: const Size.fromHeight(60),
                        textStyle: const TextStyle(
                          fontFamily: 'Poppins',
                          fontSize: 17,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      onPressed: () => _openPendingLoads(context),
                    ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
              _DeviceInfoPanel(
                name: _deviceName,
                ipAddress: _ipAddress,
                version: _version,
                patch: _patch.currentPatch,
              ),
            ],
          ),
        ),
    );
  }

  void _signOut() {
    Session.signOut();
    Navigator.of(context).pushReplacement(
      MaterialPageRoute(builder: (_) => const LoginScreen()),
    );
  }
}

/// Green "update available" card with progress while downloading/installing.
class _UpdateBanner extends StatelessWidget {
  final String? version;
  final OtaStatus? status;
  final String percent;
  final String? error;
  final VoidCallback onUpdate;

  const _UpdateBanner({
    required this.version,
    required this.status,
    required this.percent,
    required this.error,
    required this.onUpdate,
  });

  bool get _busy =>
      status == OtaStatus.DOWNLOADING || status == OtaStatus.INSTALLING;

  @override
  Widget build(BuildContext context) {
    final pct = int.tryParse(percent) ?? 0;
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.accent.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppColors.accent.withValues(alpha: 0.5)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const Icon(Icons.system_update, color: AppColors.accent),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  version != null ? 'Update available — v$version' : 'Update available',
                  style: const TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 15,
                    fontWeight: FontWeight.w600,
                    color: Colors.white,
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          if (status == OtaStatus.DOWNLOADING) ...[
            ClipRRect(
              borderRadius: BorderRadius.circular(6),
              child: LinearProgressIndicator(
                value: pct / 100.0,
                minHeight: 8,
                backgroundColor: Colors.white.withValues(alpha: 0.15),
                valueColor: const AlwaysStoppedAnimation(AppColors.accent),
              ),
            ),
            const SizedBox(height: 6),
            Text('Downloading $pct%',
                style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 13,
                    color: Colors.white.withValues(alpha: 0.7))),
          ] else if (status == OtaStatus.INSTALLING) ...[
            Text('Installing…',
                style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 13,
                    color: Colors.white.withValues(alpha: 0.7))),
          ] else ...[
            if (error != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Text('Update failed: $error',
                    style: const TextStyle(
                        fontFamily: 'Poppins', fontSize: 13, color: Colors.white)),
              ),
            ElevatedButton.icon(
              icon: const Icon(Icons.download, size: 20),
              label: Text(error != null ? 'Retry' : 'Update Now'),
              style: ElevatedButton.styleFrom(
                backgroundColor: AppColors.accent,
                minimumSize: const Size.fromHeight(48),
              ),
              onPressed: _busy ? null : onUpdate,
            ),
          ],
        ],
      ),
    );
  }
}

/// Full-screen, front-and-center update prompt shown over a scrim whenever a
/// release is queued for this device. Big icon + version + a large Update Now
/// button; "Later" snoozes it for 10 minutes (it re-appears until installed).
class _UpdateOverlay extends StatelessWidget {
  final String? version;
  final OtaStatus? status;
  final String percent;
  final String? error;
  final VoidCallback onUpdate;
  final VoidCallback onLater;

  const _UpdateOverlay({
    required this.version,
    required this.status,
    required this.percent,
    required this.error,
    required this.onUpdate,
    required this.onLater,
  });

  @override
  Widget build(BuildContext context) {
    final pct = int.tryParse(percent) ?? 0;
    return Container(
      padding: const EdgeInsets.fromLTRB(32, 36, 32, 24),
      decoration: BoxDecoration(
        color: AppColors.navy,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: AppColors.accent.withValues(alpha: 0.6)),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.5),
            blurRadius: 30,
            offset: const Offset(0, 10),
          ),
        ],
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Center(
            child: Container(
              width: 84,
              height: 84,
              decoration: BoxDecoration(
                color: AppColors.accent.withValues(alpha: 0.15),
                shape: BoxShape.circle,
              ),
              child:
                  const Icon(Icons.system_update, color: AppColors.accent, size: 44),
            ),
          ),
          const SizedBox(height: 20),
          Text(
            'Update available',
            textAlign: TextAlign.center,
            style: const TextStyle(
              fontFamily: 'Poppins',
              fontSize: 24,
              fontWeight: FontWeight.w700,
              color: Colors.white,
            ),
          ),
          if (version != null) ...[
            const SizedBox(height: 4),
            Text(
              'Version $version is ready to install',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontFamily: 'Poppins',
                fontSize: 15,
                fontWeight: FontWeight.w500,
                color: Colors.white.withValues(alpha: 0.7),
              ),
            ),
          ],
          const SizedBox(height: 24),
          if (status == OtaStatus.DOWNLOADING) ...[
            ClipRRect(
              borderRadius: BorderRadius.circular(6),
              child: LinearProgressIndicator(
                value: pct / 100.0,
                minHeight: 10,
                backgroundColor: Colors.white.withValues(alpha: 0.15),
                valueColor: const AlwaysStoppedAnimation(AppColors.accent),
              ),
            ),
            const SizedBox(height: 8),
            Text(
              'Downloading $pct%',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontFamily: 'Poppins',
                fontSize: 14,
                color: Colors.white.withValues(alpha: 0.7),
              ),
            ),
          ] else if (status == OtaStatus.INSTALLING) ...[
            Text(
              'Installing…',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontFamily: 'Poppins',
                fontSize: 14,
                color: Colors.white.withValues(alpha: 0.7),
              ),
            ),
          ] else ...[
            if (error != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Text(
                  'Update failed: $error',
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 13,
                    color: Colors.white,
                  ),
                ),
              ),
            ElevatedButton.icon(
              icon: const Icon(Icons.download, size: 24),
              label: Text(error != null ? 'Retry Update' : 'Update Now'),
              style: ElevatedButton.styleFrom(
                backgroundColor: AppColors.accent,
                foregroundColor: Colors.white,
                minimumSize: const Size.fromHeight(60),
                textStyle: const TextStyle(
                  fontFamily: 'Poppins',
                  fontSize: 18,
                  fontWeight: FontWeight.w700,
                ),
              ),
              onPressed: onUpdate,
            ),
            const SizedBox(height: 8),
            TextButton(
              onPressed: onLater,
              style: TextButton.styleFrom(
                foregroundColor: Colors.white.withValues(alpha: 0.6),
                textStyle: const TextStyle(
                  fontFamily: 'Poppins',
                  fontSize: 14,
                  fontWeight: FontWeight.w500,
                ),
              ),
              child: const Text('Later'),
            ),
          ],
        ],
      ),
    );
  }
}

/// Card under the wordmark while an over-the-air patch is on its way, has landed and
/// waits on a restart the picker put off, or failed to fetch — the APK banner's green,
/// so an update looks like an update whichever way it arrives.
class _PatchBanner extends StatelessWidget {
  final PatchState state;
  final String? error;
  final VoidCallback onRetry;
  final VoidCallback onRestart;

  const _PatchBanner({
    required this.state,
    required this.error,
    required this.onRetry,
    required this.onRestart,
  });

  @override
  Widget build(BuildContext context) {
    final (title, detail) = switch (state) {
      PatchState.downloading => (
          'Updating…',
          'Downloading the latest version — you can carry on working',
        ),
      PatchState.restartRequired => (
          'Update installed',
          'Restart the app to use the new version',
        ),
      _ => ('Update failed', error ?? 'Will try again shortly'),
    };
    return Container(
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: AppColors.accent.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: AppColors.accent.withValues(alpha: 0.5)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (state == PatchState.downloading)
                const Padding(
                  padding: EdgeInsets.all(2),
                  child: SizedBox(
                    width: 20,
                    height: 20,
                    child: CircularProgressIndicator(
                      strokeWidth: 2.5,
                      valueColor: AlwaysStoppedAnimation(AppColors.accent),
                    ),
                  ),
                )
              else
                Icon(
                  state == PatchState.restartRequired
                      ? Icons.restart_alt
                      : Icons.error_outline,
                  color: AppColors.accent,
                ),
              const SizedBox(width: 10),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      style: const TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 15,
                        fontWeight: FontWeight.w600,
                        color: Colors.white,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      detail,
                      style: TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 13,
                        color: Colors.white.withValues(alpha: 0.7),
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
          if (state == PatchState.restartRequired) ...[
            const SizedBox(height: 12),
            ElevatedButton.icon(
              icon: const Icon(Icons.restart_alt, size: 20),
              label: const Text('Restart app'),
              style: ElevatedButton.styleFrom(
                backgroundColor: AppColors.accent,
                minimumSize: const Size.fromHeight(48),
              ),
              onPressed: onRestart,
            ),
          ] else if (state == PatchState.failed) ...[
            const SizedBox(height: 12),
            ElevatedButton.icon(
              icon: const Icon(Icons.refresh, size: 20),
              label: const Text('Retry'),
              style: ElevatedButton.styleFrom(
                backgroundColor: AppColors.accent,
                minimumSize: const Size.fromHeight(48),
              ),
              onPressed: onRetry,
            ),
          ],
        ],
      ),
    );
  }
}

/// Front-and-centre prompt once a patch is on the device: the new version only runs from
/// the next launch, so the one thing to do is restart — the big button does it. "Later"
/// puts it off for 10 minutes (the banner under the wordmark keeps the button to hand).
class _RestartOverlay extends StatelessWidget {
  final int? patchNumber;
  final VoidCallback onRestart;
  final VoidCallback onLater;

  const _RestartOverlay({
    required this.patchNumber,
    required this.onRestart,
    required this.onLater,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.fromLTRB(32, 36, 32, 24),
      decoration: BoxDecoration(
        color: AppColors.navy,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: AppColors.accent.withValues(alpha: 0.6)),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.5),
            blurRadius: 30,
            offset: const Offset(0, 10),
          ),
        ],
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Center(
            child: Container(
              width: 84,
              height: 84,
              decoration: BoxDecoration(
                color: AppColors.accent.withValues(alpha: 0.15),
                shape: BoxShape.circle,
              ),
              child: const Icon(Icons.check_circle_outline,
                  color: AppColors.accent, size: 44),
            ),
          ),
          const SizedBox(height: 20),
          const Text(
            'Update installed',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 24,
              fontWeight: FontWeight.w700,
              color: Colors.white,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            patchNumber != null
                ? 'Patch $patchNumber is ready. Restart the app to use the new version.'
                : 'Restart the app to use the new version.',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 15,
              fontWeight: FontWeight.w500,
              color: Colors.white.withValues(alpha: 0.7),
            ),
          ),
          const SizedBox(height: 24),
          ElevatedButton.icon(
            icon: const Icon(Icons.restart_alt, size: 24),
            label: const Text('Restart app'),
            style: ElevatedButton.styleFrom(
              backgroundColor: AppColors.accent,
              foregroundColor: Colors.white,
              minimumSize: const Size.fromHeight(60),
              textStyle: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 18,
                fontWeight: FontWeight.w700,
              ),
            ),
            onPressed: onRestart,
          ),
          const SizedBox(height: 8),
          TextButton(
            onPressed: onLater,
            style: TextButton.styleFrom(
              foregroundColor: Colors.white.withValues(alpha: 0.6),
              textStyle: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 14,
                fontWeight: FontWeight.w500,
              ),
            ),
            child: const Text('Later'),
          ),
        ],
      ),
    );
  }
}

/// Bottom-left device identity panel — device name, LAN IP and app version (with the
/// patch it is on, when it is on one). Shown muted so it sits quietly beneath the
/// primary actions.
class _DeviceInfoPanel extends StatelessWidget {
  final String? name;
  final String? ipAddress;
  final String? version;
  final int? patch;

  const _DeviceInfoPanel({this.name, this.ipAddress, this.version, this.patch});

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerLeft,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(24, 4, 24, 16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            _row(Icons.smartphone, name ?? '…'),
            _row(Icons.wifi, ipAddress ?? '…'),
            _row(
              Icons.info_outline,
              version == null
                  ? '…'
                  : patch == null
                      ? 'v$version'
                      : 'v$version · patch $patch',
            ),
          ],
        ),
      ),
    );
  }

  Widget _row(IconData icon, String text) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 1.5),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 14, color: Colors.white.withValues(alpha: 0.40)),
          const SizedBox(width: 7),
          Text(
            text,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 12,
              fontWeight: FontWeight.w400,
              letterSpacing: 0.2,
              color: Colors.white.withValues(alpha: 0.55),
            ),
          ),
        ],
      ),
    );
  }
}

/// Dev-only pill (bottom-right of the home screen) that flips the running debug build
/// between the local and PROD API/DB — the mobile equivalent of the TauriAdmin DB
/// switcher. Tapping opens a chooser; picking a target persists the override, re-resolves
/// the API base URL and refreshes the home counts. Never built in release.
class _DbSwitch extends StatefulWidget {
  final VoidCallback onChanged;

  const _DbSwitch({required this.onChanged});

  @override
  State<_DbSwitch> createState() => _DbSwitchState();
}

class _DbSwitchState extends State<_DbSwitch> {
  DbTarget _current = AppConfig.currentTarget;

  bool get _isProd => _current == DbTarget.prod;

  // Match the TauriAdmin DB switcher: PROD green, Local amber/yellow.
  static const Color _prodColor = Color(0xFF4CAF50);
  static const Color _localColor = Color(0xFFF5B301);
  static Color _colorFor(DbTarget t) =>
      t == DbTarget.prod ? _prodColor : _localColor;

  Future<void> _pick() async {
    final chosen = await showModalBottomSheet<DbTarget>(
      context: context,
      backgroundColor: AppColors.navy,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(16)),
      ),
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            const Padding(
              padding: EdgeInsets.fromLTRB(20, 16, 20, 8),
              child: Align(
                alignment: Alignment.centerLeft,
                child: Text(
                  'Switch database',
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 15,
                    fontWeight: FontWeight.w600,
                    color: Colors.white,
                  ),
                ),
              ),
            ),
            for (final target in DbTarget.values)
              ListTile(
                leading: Icon(
                  target == DbTarget.prod
                      ? Icons.cloud_outlined
                      : Icons.computer_outlined,
                  color: _colorFor(target),
                ),
                title: Text(
                  target == DbTarget.prod ? 'PROD' : 'Local',
                  style: const TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 15,
                    fontWeight: FontWeight.w600,
                    color: Colors.white,
                  ),
                ),
                trailing: target == _current
                    ? const Icon(Icons.check, color: Colors.white70, size: 20)
                    : null,
                onTap: () => Navigator.of(context).pop(target),
              ),
          ],
        ),
      ),
    );
    if (chosen == null || chosen == _current) return;
    await AppConfig.setDbTargetOverride(chosen);
    await AppConfig.resolve();
    if (!mounted) return;
    setState(() => _current = AppConfig.currentTarget);
    widget.onChanged();
  }

  @override
  Widget build(BuildContext context) {
    final color = _colorFor(_current);
    return InkWell(
      onTap: _pick,
      borderRadius: BorderRadius.circular(20),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
        decoration: BoxDecoration(
          color: color.withValues(alpha: 0.12),
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: color.withValues(alpha: 0.55)),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 8,
              height: 8,
              decoration: BoxDecoration(color: color, shape: BoxShape.circle),
            ),
            const SizedBox(width: 7),
            Text(
              'DB: ${_isProd ? 'PROD' : 'Local'}',
              style: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 12,
                fontWeight: FontWeight.w600,
                color: Colors.white,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
