import 'package:dio/dio.dart';

import 'api_config.dart';
import 'auth_storage.dart';

/// Dio set up for Globeweigh.Api: the bearer token on every request, a quiet
/// sign-in again when the token has expired, and a short retry while the IIS app
/// pool recycles after a deploy.
class ApiClient {
  final Dio dio;
  final AuthStorage _storage;

  // Signs in again with the remembered credentials; null when that isn't
  // possible (nothing remembered, or the password has changed).
  Future<String?> Function()? _reauthenticate;
  // Single flight: every request that 401s while a sign-in is running waits
  // for the same one.
  Future<String?>? _reauthFuture;

  static const _retryBudget = Duration(seconds: 20);
  static const _retryableStatusCodes = {502, 503, 504};

  ApiClient({AuthStorage? storage})
    : _storage = storage ?? AuthStorage(),
      dio = Dio(
        BaseOptions(
          baseUrl: ApiConfig.baseUrl,
          connectTimeout: const Duration(seconds: 10),
          receiveTimeout: const Duration(seconds: 30),
          headers: {'Content-Type': 'application/json'},
        ),
      ) {
    dio.interceptors.add(_interceptor());
  }

  void setReauthenticator(Future<String?> Function() handler) => _reauthenticate = handler;

  Interceptor _interceptor() => InterceptorsWrapper(
    onRequest: (options, handler) async {
      final token = await _storage.readToken();
      if (token != null) options.headers['Authorization'] = 'Bearer $token';
      options.extra.putIfAbsent('_firstAttemptMs', () => DateTime.now().millisecondsSinceEpoch);
      handler.next(options);
    },
    onError: (err, handler) async {
      final path = err.requestOptions.path;
      // A 401 from /auth/admin-login is a wrong password, not an expired session.
      final isAuthCall = path.contains('/api/auth/');
      if (err.response?.statusCode == 401 &&
          !isAuthCall &&
          err.requestOptions.extra['retried'] != true &&
          _reauthenticate != null) {
        try {
          final reauth = _reauthFuture ??= _reauthenticate!().whenComplete(() => _reauthFuture = null);
          final token = await reauth;
          if (token == null) return handler.next(_sessionExpired(err));
          final retry = err.requestOptions
            ..headers['Authorization'] = 'Bearer $token'
            ..extra['retried'] = true;
          return handler.resolve(await dio.fetch(retry));
        } on DioException catch (e) {
          return handler.next(e);
        }
      }

      // The API restarting (a deploy, a settings change): retry with a
      // growing pause until the budget runs out.
      if (_isRetryable(err) && _withinBudget(err.requestOptions)) {
        final attempt = (err.requestOptions.extra['_attempts'] as int? ?? 0) + 1;
        err.requestOptions.extra['_attempts'] = attempt;
        await Future<void>.delayed(Duration(milliseconds: 600 * attempt));
        try {
          return handler.resolve(await dio.fetch(err.requestOptions));
        } on DioException catch (e) {
          return handler.next(e);
        }
      }
      handler.next(err);
    },
  );

  bool _isRetryable(DioException err) {
    final code = err.response?.statusCode;
    if (code != null && _retryableStatusCodes.contains(code)) return true;
    return err.type == DioExceptionType.connectionError || err.type == DioExceptionType.connectionTimeout;
  }

  bool _withinBudget(RequestOptions options) {
    final start = options.extra['_firstAttemptMs'] as int?;
    return start == null || DateTime.now().millisecondsSinceEpoch - start < _retryBudget.inMilliseconds;
  }

  DioException _sessionExpired(DioException err) => DioException(
    requestOptions: err.requestOptions,
    response: err.response,
    type: err.type,
    error: const SessionExpired(),
    message: 'Your session has expired. Please sign in again.',
  );
}

/// The token was rejected and signing in again didn't work.
class SessionExpired implements Exception {
  const SessionExpired();
  @override
  String toString() => 'Your session has expired. Please sign in again.';
}

/// What to show for a failed call: the API's own message where it sent one
/// ({ message } or validation problem details), else a plain description.
String apiMessage(Object e) {
  if (e is DioException) {
    if (e.error is SessionExpired) return e.error.toString();
    final data = e.response?.data;
    if (data is Map) {
      final message = data['message'];
      if (message is String && message.trim().isNotEmpty) return message.trim();
      final errors = data['errors'];
      if (errors is Map) {
        return errors.values.expand((v) => v is List ? v : [v]).join(' ');
      }
    }
    final code = e.response?.statusCode;
    if (code == 429) return 'Too many attempts — please wait a minute and try again.';
    if (code != null) return 'The server answered $code.';
    return "Can't reach Evergreen. Check your connection.";
  }
  return e.toString();
}
