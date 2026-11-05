import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../theme/colors.dart';
import '../account/account_sheet.dart';
import '../auth/auth_controller.dart';
import '../sales/sales_shell.dart';

/// UserRole.Sales in Globeweigh.TauriAdmin/src/types.ts.
const salesRoleId = 6;

/// Where a sign-in lands: the Sales module for the Sales role (as TauriAdmin
/// gives a rep only My Orders + Customers), else a placeholder until the app
/// has screens for other roles.
class HomeScreen extends ConsumerWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final auth = ref.watch(authControllerProvider);
    final user = auth is AuthAuthenticated ? auth.user : null;
    // Keyed by who is signed in (and on whose behalf), so a Switch user starts
    // the shell afresh: its tab belongs to the other session.
    if (user != null && user.roleIds.contains(salesRoleId)) return SalesShell(key: ValueKey(user.sessionKey));

    return Scaffold(
      appBar: AppBar(
        title: const Text('Evergreen'),
        actions: [
          IconButton(
            icon: const Icon(Icons.account_circle_outlined),
            tooltip: 'Account',
            onPressed: () => showAccountSheet(context),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
        children: [
          Card(
            child: Padding(
              padding: const EdgeInsets.all(20),
              child: Row(
                children: [
                  CircleAvatar(
                    radius: 24,
                    backgroundColor: AppColors.primary.withValues(alpha: 0.12),
                    child: const Icon(Icons.person_outline, color: AppColors.primary),
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        const Text('Signed in as', style: TextStyle(fontSize: 13, color: AppColors.textMuted)),
                        Text(
                          user?.displayName ?? '',
                          style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
