import '../core/api/api_client.dart';
import 'models.dart';

/// The sign-in and Switch user calls on Globeweigh.Api (AuthController),
/// shared with the admin web app.
class AuthApi {
  final ApiClient _client;
  AuthApi(this._client);

  Future<Session> login(String username, String password) async {
    final res = await _client.dio.post<Map<String, dynamic>>(
      '/api/auth/admin-login',
      data: {'username': username, 'password': password},
    );
    return Session.fromJson(res.data!);
  }

  /// Everyone the signed-in admin may switch into.
  Future<List<SwitchUserRow>> switchUsers() async {
    final res = await _client.dio.get<List<dynamic>>('/api/auth/switch-users');
    return (res.data ?? const []).map((j) => SwitchUserRow.fromJson(j as Map<String, dynamic>)).toList();
  }

  /// Sign in as another user, no password; the session replaces the admin's.
  Future<Session> switchUser(int targetUserId) async {
    final res = await _client.dio.post<Map<String, dynamic>>(
      '/api/auth/switch-user',
      data: {'targetUserId': targetUserId},
    );
    return Session.fromJson(res.data!);
  }

  /// Back to the admin's own session (the admin is read from the token).
  Future<Session> switchBack() async {
    final res = await _client.dio.post<Map<String, dynamic>>('/api/auth/switch-back');
    return Session.fromJson(res.data!);
  }
}
