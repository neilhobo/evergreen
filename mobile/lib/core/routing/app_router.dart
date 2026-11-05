import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../features/account/switch_user_banner.dart';
import '../../features/app_version/patch_gate.dart';
import '../../features/auth/auth_controller.dart';
import '../../features/auth/login_screen.dart';
import '../../features/auth/splash_screen.dart';
import '../../features/home/home_screen.dart';

/// Splash while the stored session is checked, the login screen when signed
/// out, and home when signed in; auth changes move between them.
final routerProvider = Provider<GoRouter>((ref) {
  // Bumped on every auth change so go_router re-runs the redirect.
  final authChanges = ValueNotifier<int>(0);
  ref.listen(authControllerProvider, (_, _) => authChanges.value++);
  ref.onDispose(authChanges.dispose);

  return GoRouter(
    initialLocation: '/',
    refreshListenable: authChanges,
    redirect: (context, state) {
      final auth = ref.read(authControllerProvider);
      final at = state.matchedLocation;
      if (auth is AuthInitial) return at == '/splash' ? null : '/splash';
      if (auth is AuthAuthenticated) {
        return at == '/splash' || at == '/login' ? '/' : null;
      }
      // Signed out, or signing in (AuthLoading keeps the login screen up).
      return at == '/login' ? null : '/login';
    },
    routes: [
      GoRoute(path: '/splash', builder: (_, _) => const SplashScreen()),
      GoRoute(path: '/login', builder: (_, _) => const LoginScreen()),
      ShellRoute(
        // Shorebird patch download + restart prompt, over every signed-in screen,
        // under the orange "Viewing as" bar while an admin is switched to a user.
        builder: (_, _, child) => SwitchUserFrame(child: PatchGate(child: child)),
        routes: [GoRoute(path: '/', builder: (_, _) => const HomeScreen())],
      ),
    ],
  );
});
