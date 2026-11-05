import 'dart:convert';

import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../../data/models.dart';

class SavedCredentials {
  final String username;
  final String password;
  const SavedCredentials({required this.username, required this.password});
}

/// The session (token + who it's for), and the "Keep me signed in" sign-in, in
/// the platform keychain.
///
/// The API has no refresh token and no "who am I" call, so the user comes
/// back from here at startup. Its tokens last Jwt:ExpireHours, or never (0,
/// reported as year 9999); when one does run out, staying signed in means
/// signing in again quietly with the remembered credentials (AuthController).
class AuthStorage {
  static const _tokenKey = 'token';
  static const _expiresKey = 'token_expires_utc';
  static const _userKey = 'user';
  static const _usernameKey = 'saved_username';
  static const _passwordKey = 'saved_password';
  static const _signedOutKey = 'signed_out';

  final FlutterSecureStorage _storage;

  AuthStorage([FlutterSecureStorage? storage]) : _storage = storage ?? const FlutterSecureStorage();

  /// The stored token, or null when there is none or it has expired.
  Future<String?> readToken() async {
    final token = await _storage.read(key: _tokenKey);
    final expires = DateTime.tryParse(await _storage.read(key: _expiresKey) ?? '');
    if (token == null || token.isEmpty || expires == null) return null;
    // A minute's grace, so a request isn't sent with a token about to lapse.
    if (expires.isBefore(DateTime.now().toUtc().add(const Duration(minutes: 1)))) return null;
    return token;
  }

  /// The user the stored token was issued to.
  Future<User?> readUser() async {
    final json = await _storage.read(key: _userKey);
    if (json == null) return null;
    try {
      return User.fromJson(jsonDecode(json) as Map<String, dynamic>);
    } catch (_) {
      return null;
    }
  }

  Future<void> writeSession(Session session) async {
    await _storage.write(key: _tokenKey, value: session.token);
    await _storage.write(key: _expiresKey, value: session.expiresUtc.toUtc().toIso8601String());
    await _storage.write(key: _userKey, value: jsonEncode(session.user.toJson()));
  }

  Future<void> clearSession() async {
    await _storage.delete(key: _tokenKey);
    await _storage.delete(key: _expiresKey);
    await _storage.delete(key: _userKey);
  }

  /// The remembered sign-in. The password may be blank: Evergreen accounts
  /// can have none.
  Future<SavedCredentials?> readSavedCredentials() async {
    final username = await _storage.read(key: _usernameKey);
    final password = await _storage.read(key: _passwordKey);
    if (username == null || username.isEmpty || password == null) return null;
    return SavedCredentials(username: username, password: password);
  }

  Future<void> writeSavedCredentials(String username, String password) async {
    await _storage.write(key: _usernameKey, value: username);
    await _storage.write(key: _passwordKey, value: password);
  }

  Future<void> clearSavedCredentials() async {
    await _storage.delete(key: _usernameKey);
    await _storage.delete(key: _passwordKey);
  }

  /// Set by Sign out: the saved details stay to fill in the login form, but
  /// launching the app must not quietly sign back in with them.
  Future<bool> readSignedOut() async => await _storage.read(key: _signedOutKey) == '1';

  Future<void> writeSignedOut(bool signedOut) =>
      signedOut ? _storage.write(key: _signedOutKey, value: '1') : _storage.delete(key: _signedOutKey);
}
