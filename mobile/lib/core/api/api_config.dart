class ApiConfig {
  ApiConfig._();

  /// Globeweigh.Api on evergreen, through the Cloudflare tunnel (the same
  /// host the admin web app uses off-site). Override for local development
  /// with `--dart-define=EVERGREEN_API_BASE_URL=http://10.0.2.2:5099` (the
  /// Android emulator's name for this PC's localhost).
  static const String _prodBaseUrl = 'https://evergreen.specdigital.com';

  static String get baseUrl {
    const fromEnv = String.fromEnvironment('EVERGREEN_API_BASE_URL');
    return fromEnv.isNotEmpty ? fromEnv : _prodBaseUrl;
  }
}
