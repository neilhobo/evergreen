import 'dart:io' show Platform, exit;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../theme/colors.dart';
import '../auth/auth_controller.dart';
import 'patch_controller.dart';
import 'store_version_controller.dart';

/// Wraps the app shell and surfaces Shorebird code-push activity:
///
/// * a "Downloading update…" bar while the patch is being pulled down,
/// * a one-shot dialog when it lands, asking the user to restart,
/// * a green bar that stays put (and reopens the dialog on tap) if they
///   choose "Later".
///
/// It also surfaces a new store build (TestFlight on iOS) from
/// [StoreVersionController]: an "Update available" dialog with an Update button,
/// a navy bar that stays after "Later", and a dialog that can't be dismissed
/// when the installed build is below the manifest's minimum.
///
/// Both checks run on mount, on every resume from background, and on a fresh
/// sign-in.
class PatchGate extends ConsumerStatefulWidget {
  final Widget child;
  const PatchGate({super.key, required this.child});

  @override
  ConsumerState<PatchGate> createState() => _PatchGateState();
}

class _PatchGateState extends ConsumerState<PatchGate> with WidgetsBindingObserver {
  bool _dialogOpen = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    // Not awaited: a slow Shorebird call must never hold up the first frame.
    WidgetsBinding.instance.addPostFrameCallback((_) => _checkAll());
  }

  void _checkAll() {
    ref.read(patchControllerProvider.notifier).check();
    ref.read(storeVersionControllerProvider.notifier).check();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Patches are published far more often than the app is cold-started.
    if (state == AppLifecycleState.resumed) _checkAll();
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(authControllerProvider, (prev, next) {
      if (next is AuthAuthenticated && prev is! AuthAuthenticated) _checkAll();
    });

    final patch = ref.watch(patchControllerProvider);
    final store = ref.watch(storeVersionControllerProvider);

    // A new store build: the prompt once per build (every time while it's
    // required). Whichever dialog is already on screen goes first.
    if (store.updateAvailable && (store.updateRequired || !store.promptDismissed) && !_dialogOpen) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _promptStoreUpdate());
    }

    // Fires the moment the download completes, and again on a later resume if
    // the patch was already on disk when this process started.
    if (patch.phase == PatchPhase.readyToRestart && !patch.restartPromptDismissed && !_dialogOpen) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _promptRestart());
    }

    return Column(
      children: [
        if (store.updateAvailable && store.promptDismissed && !store.updateRequired)
          _StoreUpdateBar(
            latest: store.latest,
            onUpdate: () => ref.read(storeVersionControllerProvider.notifier).openStore(),
          ),
        if (patch.phase == PatchPhase.downloading) const _DownloadingBar(),
        if (patch.phase == PatchPhase.readyToRestart && patch.restartPromptDismissed)
          _RestartBar(onTap: () => ref.read(patchControllerProvider.notifier).reopenRestartPrompt()),
        Expanded(child: widget.child),
      ],
    );
  }

  Future<void> _promptStoreUpdate() async {
    if (_dialogOpen || !mounted) return;
    final store = ref.read(storeVersionControllerProvider);
    if (!store.updateAvailable || (store.promptDismissed && !store.updateRequired)) return;
    _dialogOpen = true;
    try {
      await showStoreUpdateDialog(context, ref, required: store.updateRequired);
    } finally {
      _dialogOpen = false;
    }
  }

  Future<void> _promptRestart() async {
    if (_dialogOpen || !mounted) return;
    _dialogOpen = true;
    try {
      await showPatchReadyDialog(context, ref);
    } finally {
      _dialogOpen = false;
    }
  }
}

/// The "update downloaded — restart to apply" prompt. Shared with the manual
/// check in the account sheet.
Future<void> showPatchReadyDialog(BuildContext context, WidgetRef ref) {
  return showDialog<void>(
    context: context,
    builder: (ctx) => AlertDialog(
      title: const Row(
        children: [
          Icon(Icons.check_circle, color: AppColors.ok, size: 22),
          SizedBox(width: 8),
          Text('Update ready'),
        ],
      ),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Evergreen has finished downloading an update.'),
          const SizedBox(height: 10),
          Text(
            Platform.isIOS
                // iOS gives apps no supported way to relaunch themselves.
                ? 'Close Evergreen completely (swipe it away in the app switcher) '
                      'and open it again to start using the update.'
                : 'Restart Evergreen to start using it.',
            style: const TextStyle(fontSize: 13, color: AppColors.textMuted),
          ),
        ],
      ),
      actions: [
        TextButton(
          onPressed: () {
            ref.read(patchControllerProvider.notifier).dismissRestartPrompt();
            Navigator.of(ctx).pop();
          },
          child: const Text('Later'),
        ),
        if (Platform.isIOS)
          FilledButton(
            onPressed: () {
              ref.read(patchControllerProvider.notifier).dismissRestartPrompt();
              Navigator.of(ctx).pop();
            },
            child: const Text('Got it'),
          )
        else
          FilledButton.icon(
            icon: const Icon(Icons.restart_alt, size: 18),
            label: const Text('Restart'),
            onPressed: _restartAndroid,
          ),
      ],
    ),
  );
}

/// "A new version is available" with an Update button that opens TestFlight.
/// [required] = the installed build is below the minimum: no Later, no dismiss.
Future<void> showStoreUpdateDialog(BuildContext context, WidgetRef ref, {required bool required}) {
  final store = ref.read(storeVersionControllerProvider);
  final notifier = ref.read(storeVersionControllerProvider.notifier);
  return showDialog<void>(
    context: context,
    barrierDismissible: !required,
    builder: (ctx) => PopScope(
      canPop: !required,
      onPopInvokedWithResult: (didPop, _) {
        if (didPop) notifier.dismissPrompt();
      },
      child: AlertDialog(
        title: Row(
          children: [
            const Icon(Icons.system_update, color: AppColors.primary, size: 22),
            const SizedBox(width: 8),
            Text(required ? 'Update required' : 'Update available'),
          ],
        ),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              required
                  ? 'This version of Evergreen is no longer supported. Update to keep using it.'
                  : 'A new version of Evergreen is available.',
            ),
            const SizedBox(height: 10),
            Text(
              'Installed ${store.installed}  ·  New ${store.latest}',
              style: const TextStyle(fontSize: 12, color: AppColors.textMuted),
            ),
            if (Platform.isIOS) ...[
              const SizedBox(height: 6),
              const Text(
                'Update opens TestFlight — tap Update there.',
                style: TextStyle(fontSize: 12, color: AppColors.textMuted),
              ),
            ],
          ],
        ),
        actions: [
          if (!required) TextButton(onPressed: () => Navigator.of(ctx).pop(), child: const Text('Later')),
          FilledButton.icon(
            icon: const Icon(Icons.download, size: 18),
            label: const Text('Update'),
            onPressed: () {
              // Required: the dialog stays up behind TestFlight, so coming back
              // without updating still finds the app blocked.
              if (!required) Navigator.of(ctx).pop();
              notifier.openStore();
            },
          ),
        ],
      ),
    ),
  );
}

const _appChannel = MethodChannel('evergreen/app');

/// Shorebird only swaps in a patch when a new process starts, so MainActivity
/// queues a fresh launch and ends this process.
Future<void> _restartAndroid() async {
  try {
    await _appChannel.invokeMethod<void>('restart');
  } catch (_) {
    await SystemNavigator.pop();
    await Future<void>.delayed(const Duration(milliseconds: 300));
    exit(0);
  }
}

class _DownloadingBar extends StatelessWidget {
  const _DownloadingBar();

  @override
  Widget build(BuildContext context) {
    return Material(
      color: AppColors.navy,
      child: SafeArea(
        bottom: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: const [
            Padding(
              padding: EdgeInsets.fromLTRB(14, 8, 14, 8),
              child: Row(
                children: [
                  SizedBox(
                    width: 15,
                    height: 15,
                    child: CircularProgressIndicator(strokeWidth: 2, valueColor: AlwaysStoppedAnimation(Colors.white)),
                  ),
                  SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      'Downloading update…',
                      style: TextStyle(fontSize: 13, fontWeight: FontWeight.w500, color: Colors.white),
                    ),
                  ),
                ],
              ),
            ),
            // Shorebird reports no byte progress, so this stays indeterminate.
            LinearProgressIndicator(
              minHeight: 2,
              backgroundColor: Colors.white24,
              valueColor: AlwaysStoppedAnimation(Colors.white),
            ),
          ],
        ),
      ),
    );
  }
}

class _RestartBar extends StatelessWidget {
  final VoidCallback onTap;
  const _RestartBar({required this.onTap});

  @override
  Widget build(BuildContext context) {
    return Material(
      color: AppColors.ok,
      child: SafeArea(
        bottom: false,
        child: InkWell(
          onTap: onTap,
          child: const Padding(
            padding: EdgeInsets.fromLTRB(14, 9, 14, 9),
            child: Row(
              children: [
                Icon(Icons.check_circle_outline, size: 16, color: Colors.white),
                SizedBox(width: 10),
                Expanded(
                  child: Text(
                    'Update ready — restart Evergreen to apply',
                    style: TextStyle(fontSize: 13, fontWeight: FontWeight.w500, color: Colors.white),
                  ),
                ),
                Icon(Icons.chevron_right, size: 18, color: Colors.white),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _StoreUpdateBar extends StatelessWidget {
  final String latest;
  final VoidCallback onUpdate;
  const _StoreUpdateBar({required this.latest, required this.onUpdate});

  @override
  Widget build(BuildContext context) {
    return Material(
      color: AppColors.navy,
      child: SafeArea(
        bottom: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(14, 4, 6, 4),
          child: Row(
            children: [
              const Icon(Icons.system_update, size: 16, color: Colors.white),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  'New version available ($latest)',
                  style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w500, color: Colors.white),
                ),
              ),
              TextButton(
                onPressed: onUpdate,
                style: TextButton.styleFrom(foregroundColor: AppColors.primaryBright),
                child: const Text('Update', style: TextStyle(fontWeight: FontWeight.w600)),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
