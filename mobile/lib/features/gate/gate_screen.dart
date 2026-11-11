import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:video_player/video_player.dart';

import '../../core/api/api_client.dart';
import '../../data/gate_api.dart';
import '../auth/auth_controller.dart';
import '../sales/sales_format.dart' show confirm, showError;

final gateApiProvider = Provider<GateApi>((ref) => GateApi(ref.watch(apiClientProvider)));

const _ground = Color(0xFFF2F4F7);
const _ink = Color(0xFF111827);
const _muted = Color(0xFF4B5563);
const _dark = Color(0xFF111827);
const _green = Color(0xFF15803D);

/// A reading older than this isn't live: the bridge has gone quiet (as TauriAdmin).
const _staleMs = 5000;

/// Gate & Barrier (design B, "Camera first", with design A's weight cards): the
/// live camera on top with Main / Gate / Production tabs, Bridge 1 and 2, then
/// the Gate and Barrier tiles. The Cameras switch (and the camera picked) is
/// remembered per user on this phone, so the screen opens as it was left.
class GateScreen extends ConsumerStatefulWidget {
  const GateScreen({super.key});

  @override
  ConsumerState<GateScreen> createState() => _GateScreenState();
}

class _GateScreenState extends ConsumerState<GateScreen> with WidgetsBindingObserver {
  static const _storage = FlutterSecureStorage();

  List<GateCamera> _cameras = const [];
  String? _stream; // the camera on screen
  bool _camerasOn = true;
  bool _prefsLoaded = false;

  VideoPlayerController? _video;
  String? _videoError;
  int _videoGen = 0; // drops a slow start that a newer one has replaced

  RelayState _relay = const RelayState();
  bool _relayChecked = false;
  WeightReading? _w1;
  WeightReading? _w2;
  bool _busy = false;
  bool _polling = false;
  Timer? _timer;

  String get _prefKey => 'gate.${ref.read(signedInUserIdProvider) ?? 0}';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _start();
  }

  Future<void> _start() async {
    // The remembered switch and camera first, so the screen never flashes the wrong state.
    try {
      final on = await _storage.read(key: '$_prefKey.cameras');
      _stream = await _storage.read(key: '$_prefKey.camera');
      _camerasOn = on != 'off';
    } catch (_) {
      // Unreadable storage: the defaults (cameras on, the first one).
    }
    if (!mounted) return;
    setState(() => _prefsLoaded = true);
    _poll();
    _timer = Timer.periodic(const Duration(seconds: 1), (_) => _poll());
    try {
      final cams = await ref.read(gateApiProvider).cameras();
      if (!mounted) return;
      setState(() {
        _cameras = cams;
        if (cams.every((c) => c.stream != _stream)) _stream = cams.isEmpty ? null : cams.first.stream;
      });
      if (_camerasOn) _openVideo();
    } catch (e) {
      if (mounted) setState(() => _videoError = apiMessage(e));
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Live video and polling only while the screen is in front: a backgrounded player would
    // keep pulling the stream over mobile data.
    if (state == AppLifecycleState.resumed) {
      _timer ??= Timer.periodic(const Duration(seconds: 1), (_) => _poll());
      if (_camerasOn && _video == null) _openVideo();
    } else if (state == AppLifecycleState.paused) {
      _timer?.cancel();
      _timer = null;
      _closeVideo();
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    _video?.dispose();
    super.dispose();
  }

  Future<void> _poll() async {
    if (_polling) return;
    _polling = true;
    final api = ref.read(gateApiProvider);
    try {
      final results = await Future.wait<Object?>([
        api.state().then<Object?>((s) => s, onError: (_) => null),
        api.weight(bridge: 1).then<Object?>((w) => w, onError: (_) => null),
        api.weight(bridge: 2).then<Object?>((w) => w, onError: (_) => null),
      ]);
      if (!mounted) return;
      setState(() {
        _relay = results[0] as RelayState? ?? const RelayState();
        _relayChecked = true;
        _w1 = results[1] as WeightReading? ?? _w1?.lost();
        _w2 = results[2] as WeightReading? ?? _w2?.lost();
      });
    } finally {
      _polling = false;
    }
  }

  Future<void> _openVideo() async {
    final stream = _stream;
    if (stream == null) return;
    final gen = ++_videoGen;
    _closeVideo(keepGen: true);
    setState(() => _videoError = null);
    try {
      final url = await ref.read(gateApiProvider).videoUrl(stream);
      final c = VideoPlayerController.networkUrl(url, videoPlayerOptions: VideoPlayerOptions(mixWithOthers: true));
      await c.initialize();
      if (!mounted || gen != _videoGen) {
        await c.dispose();
        return;
      }
      await c.setVolume(0);
      await c.play();
      setState(() => _video = c);
    } catch (e) {
      if (mounted && gen == _videoGen) setState(() => _videoError = 'Camera unavailable. ${apiMessage(e)}');
    }
  }

  void _closeVideo({bool keepGen = false}) {
    if (!keepGen) _videoGen++;
    final c = _video;
    _video = null;
    c?.dispose();
  }

  Future<void> _setCamerasOn(bool on) async {
    setState(() => _camerasOn = on);
    if (on) {
      _openVideo();
    } else {
      setState(_closeVideo);
    }
    try {
      await _storage.write(key: '$_prefKey.cameras', value: on ? 'on' : 'off');
    } catch (_) {
      // Not remembered this once; the switch still works.
    }
  }

  Future<void> _pick(GateCamera cam) async {
    if (cam.stream == _stream) return;
    setState(() => _stream = cam.stream);
    _openVideo();
    try {
      await _storage.write(key: '$_prefKey.camera', value: cam.stream);
    } catch (_) {}
  }

  Future<void> _command({required bool gate}) async {
    final open = gate ? !_relay.gateOpen : !_relay.barrierOpen;
    final what = gate ? 'gate' : 'barrier';
    if (!await confirm(context, '${open ? 'Open' : 'Close'} the $what?', ok: open ? 'Open' : 'Close')) return;
    setState(() => _busy = true);
    try {
      final s = await ref.read(gateApiProvider).set(gate: gate, open: open);
      if (mounted) setState(() => _relay = s);
    } catch (e) {
      if (mounted) showError(context, apiMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final top = MediaQuery.of(context).padding.top;
    final controlsLive = _relayChecked && _relay.connected;
    return Scaffold(
      backgroundColor: _ground,
      // Nothing to refresh here (it polls every second) and a bounce showed a white gap
      // above the dark header: the page stays put when dragged (2026-11-06).
      body: ListView(
        physics: const ClampingScrollPhysics(),
        padding: EdgeInsets.zero,
        children: [
          Container(
            color: _dark,
            padding: EdgeInsets.fromLTRB(14, top + 10, 14, 12),
            child: Row(
              children: [
                _DarkSquare(
                  tooltip: 'Home',
                  onPressed: () => Navigator.of(context).pop(),
                  child: const Icon(Icons.arrow_back, color: Colors.white, size: 21),
                ),
                const SizedBox(width: 10),
                const Expanded(
                  child: Text(
                    'Gate & Barrier',
                    style: TextStyle(fontSize: 20, fontWeight: FontWeight.w700, color: Colors.white),
                  ),
                ),
                if (_prefsLoaded)
                  OutlinedButton.icon(
                    onPressed: () => _setCamerasOn(!_camerasOn),
                    icon: Icon(_camerasOn ? Icons.videocam_outlined : Icons.videocam_off_outlined, size: 18),
                    label: Text(_camerasOn ? 'Cameras on' : 'Cameras off'),
                    style: OutlinedButton.styleFrom(
                      foregroundColor: Colors.white,
                      side: BorderSide(color: Colors.white.withValues(alpha: 0.35)),
                      minimumSize: const Size(0, 40),
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(10)),
                      textStyle: const TextStyle(fontFamily: 'Poppins', fontSize: 13, fontWeight: FontWeight.w600),
                    ),
                  ),
              ],
            ),
          ),
          if (_prefsLoaded && _camerasOn) ...[
            _VideoBox(video: _video, error: _videoError, cameraName: _cameraName, onRetry: _openVideo),
            Container(
              color: _dark,
              padding: const EdgeInsets.fromLTRB(14, 8, 14, 12),
              child: Row(
                children: [
                  for (final (k, cam) in _cameras.indexed) ...[
                    if (k > 0) const SizedBox(width: 4),
                    Expanded(
                      child: _Tab(label: cam.name, selected: cam.stream == _stream, onTap: () => _pick(cam)),
                    ),
                  ],
                ],
              ),
            ),
          ],
          Padding(
            padding: const EdgeInsets.fromLTRB(14, 14, 14, 0),
            child: Row(
              children: [
                Expanded(
                  child: _WeightCard(label: 'Bridge 1', reading: _w1),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _WeightCard(label: 'Bridge 2', reading: _w2),
                ),
              ],
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(14, 12, 14, 0),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: _RelayTile(
                    label: 'Gate',
                    open: _relay.gateOpen,
                    enabled: controlsLive && !_busy,
                    onPressed: () => _command(gate: true),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _RelayTile(
                    label: 'Barrier',
                    open: _relay.barrierOpen,
                    enabled: controlsLive && !_busy,
                    onPressed: () => _command(gate: false),
                  ),
                ),
              ],
            ),
          ),
          Padding(
            padding: EdgeInsets.fromLTRB(18, 14, 18, 24 + MediaQuery.of(context).padding.bottom),
            child: Row(
              children: [
                Container(
                  width: 8,
                  height: 8,
                  decoration: BoxDecoration(
                    color: !_relayChecked
                        ? const Color(0xFF9CA3AF)
                        : _relay.connected
                        ? const Color(0xFF16A34A)
                        : const Color(0xFFDC2626),
                    shape: BoxShape.circle,
                  ),
                ),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    !_relayChecked
                        ? 'Connecting to the relay…'
                        : _relay.connected
                        ? 'Relay connected'
                        : 'Relay not connected. Gate and barrier controls are unavailable — check the relay IO card.',
                    style: const TextStyle(fontSize: 13, color: _muted),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  String get _cameraName => _cameras.where((c) => c.stream == _stream).map((c) => c.name).firstOrNull ?? 'Camera';
}

extension on WeightReading {
  /// The last reading, marked not live (the API itself didn't answer).
  WeightReading lost() => WeightReading(weight: weight, ageMs: -1, isConnected: false, dataLostMs: dataLostMs);
}

class _VideoBox extends StatelessWidget {
  final VideoPlayerController? video;
  final String? error;
  final String cameraName;
  final VoidCallback onRetry;
  const _VideoBox({required this.video, required this.error, required this.cameraName, required this.onRetry});

  @override
  Widget build(BuildContext context) {
    final v = video;
    return AspectRatio(
      aspectRatio: 16 / 10,
      child: Container(
        color: const Color(0xFF1E2633),
        child: Stack(
          fit: StackFit.expand,
          children: [
            if (v != null && v.value.isInitialized)
              FittedBox(
                fit: BoxFit.cover,
                clipBehavior: Clip.hardEdge,
                child: SizedBox(width: v.value.size.width, height: v.value.size.height, child: VideoPlayer(v)),
              )
            else if (error != null)
              Center(
                child: Padding(
                  padding: const EdgeInsets.all(24),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        error!,
                        textAlign: TextAlign.center,
                        style: const TextStyle(color: Color(0xFFCBD5E1), fontSize: 13),
                      ),
                      const SizedBox(height: 10),
                      OutlinedButton(
                        onPressed: onRetry,
                        style: OutlinedButton.styleFrom(
                          foregroundColor: Colors.white,
                          side: const BorderSide(color: Color(0xFF64748B)),
                        ),
                        child: const Text('Try again'),
                      ),
                    ],
                  ),
                ),
              )
            else
              const Center(child: CircularProgressIndicator(color: Colors.white70)),
            Positioned(
              left: 12,
              top: 12,
              child: Container(
                padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                decoration: BoxDecoration(color: Colors.black54, borderRadius: BorderRadius.circular(6)),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(
                      width: 7,
                      height: 7,
                      decoration: BoxDecoration(
                        color: v != null ? const Color(0xFFEF4444) : const Color(0xFF9CA3AF),
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: 6),
                    Text(
                      v != null ? 'LIVE · $cameraName' : cameraName,
                      style: const TextStyle(color: Colors.white, fontSize: 11, fontWeight: FontWeight.w700),
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _Tab extends StatelessWidget {
  final String label;
  final bool selected;
  final VoidCallback onTap;
  const _Tab({required this.label, required this.selected, required this.onTap});

  @override
  Widget build(BuildContext context) => Material(
    color: selected ? Colors.white : Colors.transparent,
    borderRadius: BorderRadius.circular(9),
    child: InkWell(
      borderRadius: BorderRadius.circular(9),
      onTap: onTap,
      child: SizedBox(
        height: 40,
        child: Center(
          child: Text(
            label,
            style: TextStyle(
              fontSize: 13,
              fontWeight: FontWeight.w600,
              color: selected ? _ink : const Color(0xFFE5E7EB),
            ),
          ),
        ),
      ),
    ),
  );
}

/// Design A's weight card: the label, the figure, greyed when not live.
class _WeightCard extends StatelessWidget {
  final String label;
  final WeightReading? reading;
  const _WeightCard({required this.label, required this.reading});

  static String _format(String raw) {
    final n = num.tryParse(raw.trim());
    if (n == null) return raw.trim().isEmpty ? '—' : raw.trim();
    final s = n.round().abs().toString().replaceAllMapped(RegExp(r'\B(?=(\d{3})+(?!\d))'), (_) => ',');
    return n < 0 ? '-$s' : s;
  }

  static String _outage(int ms) {
    final secs = ms ~/ 1000;
    if (secs < 60) return '${secs}s';
    final mins = secs ~/ 60;
    if (mins < 60) return '${mins}m';
    return '${mins ~/ 60}h ${mins % 60}m';
  }

  @override
  Widget build(BuildContext context) {
    final r = reading;
    final live = r != null && r.isConnected && r.ageMs >= 0 && r.ageMs < _staleMs;
    final value = r == null ? '—' : _format(r.weight);
    final zero = value == '0';
    final lostMs = r == null ? -1 : (r.dataLostMs != null && r.dataLostMs! >= 0 ? r.dataLostMs! : r.ageMs);
    return Container(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(18),
        boxShadow: const [BoxShadow(color: Color(0x0F101828), blurRadius: 8, offset: Offset(0, 2))],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label.toUpperCase(),
            style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700, color: _muted, letterSpacing: 0.5),
          ),
          const SizedBox(height: 4),
          Text.rich(
            TextSpan(
              children: [
                TextSpan(text: value),
                if (value != '—')
                  const TextSpan(
                    text: ' kg',
                    style: TextStyle(fontSize: 14, fontWeight: FontWeight.w600, color: _muted),
                  ),
              ],
            ),
            style: TextStyle(
              fontSize: 26,
              fontWeight: FontWeight.w800,
              color: live && !zero ? _ink : const Color(0xFF6B7280),
              fontFeatures: const [FontFeature.tabularFigures()],
            ),
          ),
          if (!live && r != null)
            Text(
              lostMs >= 0 ? 'No data for ${_outage(lostMs)}' : 'Not connected',
              style: const TextStyle(fontSize: 12, color: Color(0xFFB45309)),
            ),
        ],
      ),
    );
  }
}

/// Design B's tile: what it is, whether it is open, and the one button that changes it.
class _RelayTile extends StatelessWidget {
  final String label;
  final bool open;
  final bool enabled;
  final VoidCallback onPressed;
  const _RelayTile({required this.label, required this.open, required this.enabled, required this.onPressed});

  @override
  Widget build(BuildContext context) {
    final fg = open ? const Color(0xFF14532D) : _ink;
    return Container(
      padding: const EdgeInsets.all(18),
      decoration: BoxDecoration(
        color: open ? const Color(0xFFDCFCE7) : Colors.white,
        borderRadius: BorderRadius.circular(20),
        boxShadow: open ? null : const [BoxShadow(color: Color(0x14101828), blurRadius: 8, offset: Offset(0, 2))],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            label.toUpperCase(),
            style: TextStyle(
              fontSize: 13,
              fontWeight: FontWeight.w700,
              letterSpacing: 0.5,
              color: open ? const Color(0xFF166534) : _muted,
            ),
          ),
          const SizedBox(height: 10),
          Text(
            open ? 'Open' : 'Closed',
            style: TextStyle(fontSize: 26, fontWeight: FontWeight.w800, color: fg),
          ),
          const SizedBox(height: 18),
          open
              ? OutlinedButton(
                  onPressed: enabled ? onPressed : null,
                  style: OutlinedButton.styleFrom(
                    minimumSize: const Size.fromHeight(46),
                    backgroundColor: Colors.white,
                    foregroundColor: fg,
                    side: BorderSide(color: enabled ? fg : const Color(0xFFCBD5E1), width: 2),
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                    textStyle: const TextStyle(fontFamily: 'Poppins', fontSize: 15, fontWeight: FontWeight.w700),
                  ),
                  child: const Text('Close'),
                )
              : FilledButton(
                  onPressed: enabled ? onPressed : null,
                  style: FilledButton.styleFrom(
                    minimumSize: const Size.fromHeight(46),
                    backgroundColor: _green,
                    foregroundColor: Colors.white,
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                    textStyle: const TextStyle(fontFamily: 'Poppins', fontSize: 15, fontWeight: FontWeight.w700),
                  ),
                  child: const Text('Open'),
                ),
        ],
      ),
    );
  }
}

class _DarkSquare extends StatelessWidget {
  final String tooltip;
  final VoidCallback onPressed;
  final Widget child;
  const _DarkSquare({required this.tooltip, required this.onPressed, required this.child});

  @override
  Widget build(BuildContext context) => Material(
    color: Colors.white.withValues(alpha: 0.1),
    borderRadius: BorderRadius.circular(12),
    child: IconButton(
      tooltip: tooltip,
      onPressed: onPressed,
      icon: child,
      constraints: const BoxConstraints.tightFor(width: 44, height: 44),
    ),
  );
}
