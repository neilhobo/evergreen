import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Which API / database a build talks to. "local" is the dev API (→ local dev DB);
/// "prod" is the production server (→ prod DB).
enum DbTarget { local, prod }

/// App-wide configuration.
class AppConfig {
  /// The Globeweigh.Api targets — all on port 5099.
  static const String _localBaseUrl = 'http://192.168.1.140:5099'; // dev PC, office LAN
  static const String _prodBaseUrl = 'http://192.168.250.5:5099'; // production server
  // The Android emulator NATs the host's loopback to 10.0.2.2, so the dev API
  // (bound on the host at 0.0.0.0:5099) is reachable there from inside the AVD.
  static const String _emulatorBaseUrl = 'http://10.0.2.2:5099';

  /// Dev-only manual override of the auto-probed target (mirrors the TauriAdmin DB
  /// switcher). When set in a debug build it wins over [resolve]'s auto-detection so a
  /// developer can flip a running dev build between the local and PROD API/DB.
  static const String _kDbTargetPref = 'gw_db_target'; // 'local' | 'prod'

  /// Base URL of the API. Resolved once at startup by [resolve]. Defaults to PRODUCTION so
  /// the client tablet is safe even before (or if) resolution can't run.
  static String apiBaseUrl = _prodBaseUrl;

  /// The target the current [apiBaseUrl] points at — drives the dev-only DB badge.
  static DbTarget get currentTarget =>
      apiBaseUrl == _prodBaseUrl ? DbTarget.prod : DbTarget.local;

  /// Choose the API target by *probing which server actually answers*, ordered by a network
  /// hint so the common case is instant. The subnet alone is NOT reliable — a customer site's
  /// WiFi can hand out 192.168.1.x just like our office, which would point us at the wrong
  /// (unreachable) server — so we verify reachability instead of trusting the subnet.
  /// No permissions or location services needed.
  static Future<void> resolve() async {
    // Dev-only manual override wins over auto-probing (release builds always auto-detect).
    if (kDebugMode) {
      final override = await getDbTargetOverride();
      if (override != null) {
        apiBaseUrl = await _urlForTarget(override);
        return;
      }
      // No saved override — honor a build-time default target if one was passed via
      // --dart-define=GW_TARGET=prod|local (used by `npm run dev`, which defaults to prod).
      // The in-app switcher (which sets getDbTargetOverride) still wins over this.
      const define = String.fromEnvironment('GW_TARGET');
      if (define == 'prod') {
        apiBaseUrl = _prodBaseUrl;
        return;
      }
      if (define == 'local') {
        apiBaseUrl = await _urlForTarget(DbTarget.local);
        return;
      }
    }
    try {
      final ifaces = await NetworkInterface.list(
        type: InternetAddressType.IPv4,
        includeLoopback: false,
      );
      bool has(String prefix) =>
          ifaces.any((i) => i.addresses.any((a) => a.address.startsWith(prefix)));

      // The emulator can only ever reach the host dev API.
      if (has('10.0.2.')) {
        apiBaseUrl = _emulatorBaseUrl;
        return;
      }

      // Probe order: most-likely target first by subnet hint, the other as fallback.
      // 192.168.1.x looks like our office → try dev first; otherwise try production first.
      final candidates = has('192.168.1.')
          ? [_localBaseUrl, _prodBaseUrl]
          : [_prodBaseUrl, _localBaseUrl];

      for (final base in candidates) {
        if (await _reachable(base)) {
          apiBaseUrl = base;
          return;
        }
      }
      // Neither answered (e.g. server down / WiFi not ready) — keep the best guess; the app
      // will retry on its own polls and recover once a server is reachable.
      apiBaseUrl = candidates.first;
    } catch (_) {
      apiBaseUrl = _prodBaseUrl;
    }
  }

  /// The base URL for a manually-chosen [target]. "local" resolves to the emulator host
  /// when running in the AVD (loopback NAT), otherwise the office-LAN dev PC.
  static Future<String> _urlForTarget(DbTarget target) async {
    if (target == DbTarget.prod) return _prodBaseUrl;
    return await _isEmulator() ? _emulatorBaseUrl : _localBaseUrl;
  }

  /// True when running inside the Android emulator (it owns a 10.0.2.x interface).
  static Future<bool> _isEmulator() async {
    try {
      final ifaces = await NetworkInterface.list(
        type: InternetAddressType.IPv4,
        includeLoopback: false,
      );
      return ifaces
          .any((i) => i.addresses.any((a) => a.address.startsWith('10.0.2.')));
    } catch (_) {
      return false;
    }
  }

  /// The persisted dev override, or null when none is set (→ auto-detect).
  static Future<DbTarget?> getDbTargetOverride() async {
    final prefs = await SharedPreferences.getInstance();
    switch (prefs.getString(_kDbTargetPref)) {
      case 'local':
        return DbTarget.local;
      case 'prod':
        return DbTarget.prod;
      default:
        return null;
    }
  }

  /// Persist (or, with null, clear) the dev override. Call [resolve] afterwards to apply.
  static Future<void> setDbTargetOverride(DbTarget? target) async {
    final prefs = await SharedPreferences.getInstance();
    if (target == null) {
      await prefs.remove(_kDbTargetPref);
    } else {
      await prefs.setString(
          _kDbTargetPref, target == DbTarget.local ? 'local' : 'prod');
    }
  }

  /// True if the API at [base] answers within a short timeout. Any HTTP response counts —
  /// we only care that the server is reachable, not the status code.
  static Future<bool> _reachable(String base) async {
    final client = HttpClient()
      ..connectionTimeout = const Duration(milliseconds: 1000);
    try {
      final req = await client
          .getUrl(Uri.parse('$base/api/picking/pending'))
          .timeout(const Duration(milliseconds: 1200));
      final resp = await req.close().timeout(const Duration(milliseconds: 1200));
      await resp.drain<void>();
      return true;
    } catch (_) {
      return false;
    } finally {
      client.close(force: true);
    }
  }
}
