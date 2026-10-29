import '../models/touch_user.dart';

/// The signed-in factory user for this app run. In-memory only — the app asks
/// for a login on every startup (mirrors the TauriTouch behaviour where a
/// session doesn't survive closing the app).
class Session {
  static TouchUser? current;

  static int? get userId => current?.id;

  static void signOut() => current = null;
}
