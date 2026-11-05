import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../core/api/auth_storage.dart';
import '../../data/auth_api.dart';
import '../../data/models.dart';

final authStorageProvider = Provider<AuthStorage>((ref) => AuthStorage());

final apiClientProvider = Provider<ApiClient>((ref) => ApiClient(storage: ref.watch(authStorageProvider)));

final authApiProvider = Provider<AuthApi>((ref) => AuthApi(ref.watch(apiClientProvider)));

sealed class AuthState {
  const AuthState();
}

/// Checking the stored session at startup (the splash screen).
class AuthInitial extends AuthState {
  const AuthInitial();
}

class AuthLoading extends AuthState {
  const AuthLoading();
}

class AuthAuthenticated extends AuthState {
  final User user;
  const AuthAuthenticated(this.user);
}

class AuthUnauthenticated extends AuthState {
  final String? message;
  const AuthUnauthenticated([this.message]);
}

final authControllerProvider = StateNotifierProvider<AuthController, AuthState>((ref) {
  final controller = AuthController(
    api: ref.watch(authApiProvider),
    storage: ref.watch(authStorageProvider),
    client: ref.watch(apiClientProvider),
  );
  controller.bootstrap();
  return controller;
});

/// The signed-in user's id, null when signed out. Data providers watch this so
/// they refetch for a new sign-in and drop what the last user saw.
final signedInUserIdProvider = Provider<int?>((ref) {
  final auth = ref.watch(authControllerProvider);
  return auth is AuthAuthenticated ? auth.user.id : null;
});

class AuthController extends StateNotifier<AuthState> {
  final AuthApi api;
  final AuthStorage storage;

  AuthController({required this.api, required this.storage, required ApiClient client}) : super(const AuthInitial()) {
    client.setReauthenticator(_reauthenticate);
  }

  // From "Sign out" until the next sign-in: requests still in flight that 401
  // must not sign the user straight back in with the remembered password.
  bool _signingOut = false;

  /// At startup: the stored session if its token is still good, else a quiet
  /// sign-in with the saved details (unless the user signed out), else the
  /// login screen. A network failure keeps trying (the splash stays up) rather
  /// than signing the user out.
  Future<void> bootstrap() async {
    var attempt = 0;
    while (true) {
      try {
        final user = await storage.readUser();
        if (user != null && await storage.readToken() != null) {
          state = AuthAuthenticated(user);
          return;
        }
        final saved = await storage.readSavedCredentials();
        if (saved == null || await storage.readSignedOut()) {
          state = const AuthUnauthenticated();
          return;
        }
        await _signIn(saved.username, saved.password);
        return;
      } catch (e) {
        if (state is! AuthInitial) return;
        if (_isRejected(e)) {
          await storage.clearSession();
          state = AuthUnauthenticated(e is AccountDisabled ? e.toString() : null);
          return;
        }
        attempt++;
        await Future<void>.delayed(Duration(seconds: attempt < 5 ? 2 : 5));
      }
    }
  }

  Future<void> login(String username, String password, {required bool remember}) async {
    _signingOut = false;
    state = const AuthLoading();
    try {
      await _signIn(username, password);
      await storage.writeSignedOut(false);
      if (remember) {
        await storage.writeSavedCredentials(username, password);
      } else {
        await storage.clearSavedCredentials();
      }
    } catch (e) {
      // The API's own message for a wrong password ("Invalid details entered").
      state = AuthUnauthenticated(e is AccountDisabled ? e.toString() : apiMessage(e));
    }
  }

  Future<void> logout() async {
    _signingOut = true;
    await storage.clearSession();
    // Saved details stay, to fill in the login form (as on the admin web app),
    // but the next launch stops at it rather than signing straight back in.
    await storage.writeSignedOut(true);
    state = const AuthUnauthenticated();
  }

  /// Switch user: sign in AS another user, no password. The admin's saved
  /// details stay as they are, so a refused token mid-switch (or Sign out)
  /// lands back on the admin's own account. Throws the API's error, for the
  /// picker to show; the current session is untouched on failure.
  Future<void> switchTo(int targetUserId) => _adopt(api.switchUser(targetUserId));

  /// Back to the admin's own account.
  Future<void> switchBack() => _adopt(api.switchBack());

  Future<void> _adopt(Future<Session> call) async {
    final session = await call;
    await storage.writeSession(session);
    state = AuthAuthenticated(session.user);
  }

  Future<String> _signIn(String username, String password) async {
    final session = await api.login(username, password);
    // admin-login doesn't refuse a disabled account; the admin web app turns
    // it away on the client, and so does this.
    if (session.user.disabled) throw const AccountDisabled();
    await storage.writeSession(session);
    state = AuthAuthenticated(session.user);
    return session.token;
  }

  /// The token has been refused mid-session (expired, or the server key rotated): sign in again with
  /// the remembered credentials so the user stays where they are.
  Future<String?> _reauthenticate() async {
    if (_signingOut) return null;
    final saved = await storage.readSavedCredentials();
    if (saved != null) {
      try {
        return await _signIn(saved.username, saved.password);
      } on DioException catch (e) {
        // Offline: keep the session; the caller sees a network error.
        if (!_isRejected(e)) rethrow;
      } on AccountDisabled {
        // Falls through to signing out.
      }
    }
    await storage.clearSession();
    state = const AuthUnauthenticated('Your session has expired. Please sign in again.');
    return null;
  }

  bool _isRejected(Object e) =>
      e is SessionExpired ||
      e is AccountDisabled ||
      (e is DioException && (e.error is SessionExpired || e.response?.statusCode == 401));
}

class AccountDisabled implements Exception {
  const AccountDisabled();
  @override
  String toString() => 'Account is disabled';
}
