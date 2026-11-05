import 'dart:io' show Platform, exit;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../theme/colors.dart';
import '../auth/auth_controller.dart';
import 'patch_controller.dart';

/// Wraps the app shell and surfaces Shorebird code-push activity:
///
/// * a "Downloading update…" bar while the patch is being pulled down,
/// * a one-shot dialog when it lands, asking the user to restart,
/// * a green bar that stays put (and reopens the dialog on tap) if they
///   choose "Later".
///
/// Checks run on mount, on every resume from background, and on a fresh
/// sign-in — the same triggers the store-version gate uses.
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
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(patchControllerProvider.notifier).check();
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Patches are published far more often than the app is cold-started.
    if (state == AppLifecycleState.resumed) {
      ref.read(patchControllerProvider.notifier).check();
    }
  }

  @override
  Widget build(BuildContext context) {
    ref.listen(authControllerProvider, (prev, next) {
      if (next is AuthAuthenticated && prev is! AuthAuthenticated) {
        ref.read(patchControllerProvider.notifier).check();
      }
    });

    final patch = ref.watch(patchControllerProvider);

    // Fires the moment the download completes, and again on a later resume if
    // the patch was already on disk when this process started.
    if (patch.phase == PatchPhase.readyToRestart && !patch.restartPromptDismissed && !_dialogOpen) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _promptRestart());
    }

    return Column(
      children: [
        if (patch.phase == PatchPhase.downloading) const _DownloadingBar(),
        if (patch.phase == PatchPhase.readyToRestart && patch.restartPromptDismissed)
          _RestartBar(onTap: () => ref.read(patchControllerProvider.notifier).reopenRestartPrompt()),
        Expanded(child: widget.child),
      ],
    );
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
