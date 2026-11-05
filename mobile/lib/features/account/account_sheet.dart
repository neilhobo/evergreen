import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';

import '../../core/api/api_client.dart';
import '../../core/api/api_config.dart';
import '../../theme/colors.dart';
import '../app_version/patch_controller.dart';
import '../app_version/patch_gate.dart';
import '../auth/auth_controller.dart';
import 'switch_user_sheet.dart';

/// The installed "semver+build", for the account sheet. Local; no API call.
final installedVersionProvider = FutureProvider<String>((ref) async {
  final pkg = await PackageInfo.fromPlatform();
  return '${pkg.version}+${pkg.buildNumber}';
});

/// Who's signed in, the version (with any Shorebird patch), a manual update
/// check, and Sign out.
Future<void> showAccountSheet(BuildContext context) =>
    showModalBottomSheet<void>(context: context, showDragHandle: true, builder: (_) => const _AccountSheet());

class _AccountSheet extends ConsumerStatefulWidget {
  const _AccountSheet();

  @override
  ConsumerState<_AccountSheet> createState() => _AccountSheetState();
}

class _AccountSheetState extends ConsumerState<_AccountSheet> {
  bool _checking = false;
  String? _result;

  Future<void> _checkForUpdates() async {
    setState(() {
      _checking = true;
      _result = null;
    });
    try {
      final patches = ref.read(patchControllerProvider.notifier);
      await patches.check();
      if (!mounted) return;
      if (ref.read(patchControllerProvider).phase == PatchPhase.readyToRestart) {
        // Over the sheet: its ref drives the dialog's buttons.
        await showPatchReadyDialog(context, ref);
        return;
      }
      setState(
        () => _result = patches.isAvailable ? "You're on the latest version." : "Updates only come to release builds.",
      );
    } catch (e) {
      if (mounted) setState(() => _result = "Couldn't check just now — try again shortly.");
    } finally {
      if (mounted) setState(() => _checking = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = ref.watch(authControllerProvider);
    final user = auth is AuthAuthenticated ? auth.user : null;
    final version = ref.watch(installedVersionProvider).valueOrNull;
    final patch = ref.watch(patchControllerProvider).currentPatch;

    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (user != null) ...[
              Text(user.displayName, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600)),
              Text(user.username, style: const TextStyle(color: AppColors.textMuted)),
              const SizedBox(height: 16),
            ],
            _Row('Version', [version ?? '…', if (patch != null) 'patch $patch'].join(' · ')),
            _Row('Server', Uri.parse(ApiConfig.baseUrl).host),
            const SizedBox(height: 12),
            OutlinedButton.icon(
              onPressed: _checking ? null : _checkForUpdates,
              icon: _checking
                  ? const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2))
                  : const Icon(Icons.system_update_outlined, size: 18),
              label: const Text('Check for updates'),
            ),
            if (_result != null)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                  _result!,
                  textAlign: TextAlign.center,
                  style: const TextStyle(fontSize: 12, color: AppColors.textMuted),
                ),
              ),
            // Admins only (the API re-checks). No switching from inside a
            // switch: return to your own account first.
            if (user != null && user.canSwitchUser) ...[
              const SizedBox(height: 8),
              OutlinedButton.icon(
                onPressed: () {
                  final nav = Navigator.of(context);
                  final host = nav.context;
                  nav.pop();
                  showSwitchUserSheet(host);
                },
                icon: const Icon(Icons.switch_account_outlined, size: 18),
                label: const Text('Switch user'),
              ),
            ],
            if (user != null && user.isSwitched) ...[
              const SizedBox(height: 8),
              OutlinedButton.icon(
                onPressed: () {
                  final messenger = ScaffoldMessenger.of(context);
                  Navigator.of(context).pop();
                  ref
                      .read(authControllerProvider.notifier)
                      .switchBack()
                      .catchError((Object e) => messenger.showSnackBar(SnackBar(content: Text(apiMessage(e)))));
                },
                icon: const Icon(Icons.undo, size: 18),
                label: const Text('Return to my account'),
              ),
            ],
            const SizedBox(height: 8),
            FilledButton.icon(
              style: FilledButton.styleFrom(backgroundColor: AppColors.navy),
              onPressed: () {
                Navigator.of(context).pop();
                ref.read(authControllerProvider.notifier).logout();
              },
              icon: const Icon(Icons.logout, size: 18),
              label: const Text('Sign out'),
            ),
          ],
        ),
      ),
    );
  }
}

class _Row extends StatelessWidget {
  final String label;
  final String value;
  const _Row(this.label, this.value);

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 4),
    child: Row(
      children: [
        Text(label, style: const TextStyle(color: AppColors.textMuted)),
        const Spacer(),
        Text(value, style: const TextStyle(fontWeight: FontWeight.w500)),
      ],
    ),
  );
}
