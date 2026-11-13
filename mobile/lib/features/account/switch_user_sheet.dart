import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../data/models.dart';
import '../../theme/colors.dart';
import '../auth/auth_controller.dart';

/// The users the signed-in admin may switch into. autoDispose: fetched fresh
/// each time the picker opens.
final _switchUsersProvider = FutureProvider.autoDispose<List<SwitchUserRow>>(
  (ref) => ref.watch(authApiProvider).switchUsers(),
);

/// Switch user (TauriAdmin's SwitchUserDialog): pick someone and the app signs
/// in as them, no password; the orange bar then offers the way back.
Future<void> showSwitchUserSheet(BuildContext context) => showModalBottomSheet<void>(
  context: context,
  isScrollControlled: true,
  showDragHandle: true,
  builder: (_) => FractionallySizedBox(heightFactor: 0.85, child: const _SwitchUserSheet()),
);

class _SwitchUserSheet extends ConsumerStatefulWidget {
  const _SwitchUserSheet();

  @override
  ConsumerState<_SwitchUserSheet> createState() => _SwitchUserSheetState();
}

class _SwitchUserSheetState extends ConsumerState<_SwitchUserSheet> {
  final _filter = TextEditingController();
  int? _switchingId;
  String? _error;

  @override
  void dispose() {
    _filter.dispose();
    super.dispose();
  }

  Future<void> _pick(SwitchUserRow u) async {
    setState(() {
      _switchingId = u.id;
      _error = null;
    });
    try {
      await ref.read(authControllerProvider.notifier).switchTo(u.id);
      if (mounted) Navigator.of(context).pop();
    } catch (e) {
      if (mounted) {
        setState(() {
          _error = apiMessage(e);
          _switchingId = null;
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final users = ref.watch(_switchUsersProvider);
    final q = _filter.text.trim().toLowerCase();

    return Padding(
      padding: EdgeInsets.fromLTRB(16, 0, 16, MediaQuery.of(context).viewInsets.bottom),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text('Switch user', style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600)),
          const SizedBox(height: 2),
          const Text(
            'Sign in as another user — no password required.',
            style: TextStyle(fontSize: 12, color: AppColors.textMuted),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _filter,
            onChanged: (_) => setState(() {}),
            style: const TextStyle(fontSize: 14),
            decoration: const InputDecoration(
              hintText: 'Filter users…',
              hintStyle: TextStyle(fontSize: 14, color: AppColors.textMuted),
              prefixIcon: Icon(Icons.search, size: 18),
              isDense: true,
            ),
          ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                _error!,
                style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w500, color: AppColors.error),
              ),
            ),
          const SizedBox(height: 8),
          Expanded(
            child: users.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (e, _) => Center(
                child: Text(apiMessage(e), style: const TextStyle(color: AppColors.textMuted)),
              ),
              data: (all) {
                final shown = all
                    .where(
                      (u) =>
                          q.isEmpty || (u.username ?? '').toLowerCase().contains(q) || u.name.toLowerCase().contains(q),
                    )
                    .toList();
                if (shown.isEmpty) {
                  return const Center(
                    child: Text('No users.', style: TextStyle(color: AppColors.textMuted)),
                  );
                }
                return ListView.separated(
                  padding: const EdgeInsets.only(bottom: 16),
                  itemCount: shown.length,
                  separatorBuilder: (_, _) => const SizedBox(height: 4),
                  // The server sends my most used first, then everyone A to Z; a heading
                  // marks each part when there is a most-used part to mark.
                  itemBuilder: (_, i) {
                    final row = _UserRow(
                      user: shown[i],
                      switching: _switchingId == shown[i].id,
                      onTap: shown[i].disabled || _switchingId != null ? null : () => _pick(shown[i]),
                    );
                    final starts = shown[0].frequent && (i == 0 || shown[i - 1].frequent != shown[i].frequent);
                    if (!starts) return row;
                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Padding(
                          padding: EdgeInsets.fromLTRB(2, i == 0 ? 0 : 10, 2, 6),
                          child: Text(
                            shown[i].frequent ? 'MOST USED' : 'EVERYONE ELSE',
                            style: const TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.w600,
                              letterSpacing: 0.6,
                              color: AppColors.textMuted,
                            ),
                          ),
                        ),
                        row,
                      ],
                    );
                  },
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

class _UserRow extends StatelessWidget {
  final SwitchUserRow user;
  final bool switching;
  final VoidCallback? onTap;
  const _UserRow({required this.user, required this.switching, required this.onTap});

  @override
  Widget build(BuildContext context) {
    final roles = user.roleIds.map(UserRole.label).join(', ');
    return Material(
      color: const Color(0xFFF8FAFC),
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(10),
        side: const BorderSide(color: AppColors.border),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(10),
        onTap: onTap,
        child: Opacity(
          opacity: user.disabled ? 0.55 : 1,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            child: Row(
              children: [
                // One line: the name, then the role to its right (before the Switch tag).
                Expanded(
                  child: Text(
                    user.displayName,
                    style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
                    overflow: TextOverflow.ellipsis,
                  ),
                ),
                if (roles.isNotEmpty) ...[
                  const SizedBox(width: 8),
                  Text(roles, style: const TextStyle(fontSize: 11, color: AppColors.textMuted)),
                ],
                const SizedBox(width: 8),
                _Tag(
                  user.disabled
                      ? 'Disabled'
                      : switching
                      ? 'Switching…'
                      : 'Switch',
                  color: user.disabled ? AppColors.textMuted : AppColors.primary,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _Tag extends StatelessWidget {
  final String text;
  final Color color;
  const _Tag(this.text, {required this.color});

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
    decoration: BoxDecoration(color: color.withValues(alpha: 0.10), borderRadius: BorderRadius.circular(999)),
    child: Text(
      text,
      style: TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: color),
    ),
  );
}
