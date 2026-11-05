import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../auth/auth_controller.dart';

/// Over every signed-in screen: while an admin is switched to another user,
/// an orange "Viewing as" bar (TauriAdmin's SwitchUserBanner) above [child],
/// with a one-tap return to the admin's own account. Otherwise just [child].
class SwitchUserFrame extends ConsumerStatefulWidget {
  final Widget child;
  const SwitchUserFrame({super.key, required this.child});

  @override
  ConsumerState<SwitchUserFrame> createState() => _SwitchUserFrameState();
}

class _SwitchUserFrameState extends ConsumerState<SwitchUserFrame> {
  bool _returning = false;

  Future<void> _return() async {
    setState(() => _returning = true);
    try {
      await ref.read(authControllerProvider.notifier).switchBack();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(apiMessage(e))));
      }
    } finally {
      // Never leave the button stuck: the bar stays and the admin can retry.
      if (mounted) setState(() => _returning = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = ref.watch(authControllerProvider);
    final user = auth is AuthAuthenticated ? auth.user : null;
    if (user == null || !user.isSwitched) return widget.child;

    final by = user.impersonatedByName;
    return Column(
      children: [
        DecoratedBox(
          decoration: const BoxDecoration(
            gradient: LinearGradient(colors: [Color(0xFFF47B20), Color(0xFFE8630A)]),
            boxShadow: [BoxShadow(color: Color(0x59F47B20), blurRadius: 6, offset: Offset(0, 2))],
          ),
          // Above the screen's Scaffold, so no Material ancestor of its own.
          child: Material(
            type: MaterialType.transparency,
            child: SafeArea(
              bottom: false,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 6, 10, 6),
                child: Row(
                  children: [
                    const Icon(Icons.person_outline, size: 16, color: Colors.white),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text.rich(
                        TextSpan(
                          children: [
                            const TextSpan(text: 'Viewing as '),
                            TextSpan(
                              text: user.displayName,
                              style: const TextStyle(fontWeight: FontWeight.w700),
                            ),
                            if (by != null && by.isNotEmpty) TextSpan(text: ' — switched by $by'),
                          ],
                        ),
                        style: const TextStyle(
                          fontFamily: 'Poppins',
                          fontSize: 12,
                          fontWeight: FontWeight.w500,
                          color: Colors.white,
                          decoration: TextDecoration.none,
                        ),
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    const SizedBox(width: 8),
                    OutlinedButton(
                      onPressed: _returning ? null : _return,
                      style: OutlinedButton.styleFrom(
                        foregroundColor: Colors.white,
                        disabledForegroundColor: Colors.white70,
                        backgroundColor: Colors.white.withValues(alpha: 0.15),
                        side: BorderSide(color: Colors.white.withValues(alpha: 0.7)),
                        shape: const StadiumBorder(),
                        padding: const EdgeInsets.symmetric(horizontal: 12),
                        minimumSize: const Size(0, 30),
                        tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                        textStyle: const TextStyle(fontFamily: 'Poppins', fontSize: 12, fontWeight: FontWeight.w600),
                      ),
                      child: Text(_returning ? 'Returning…' : 'Return'),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
        // The bar has taken the status-bar inset; the screen below must not
        // pad for it again.
        Expanded(
          child: MediaQuery.removePadding(context: context, removeTop: true, child: widget.child),
        ),
      ],
    );
  }
}
