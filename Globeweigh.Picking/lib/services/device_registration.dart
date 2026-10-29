import 'dart:math';

import 'package:device_info_plus/device_info_plus.dart';
import 'package:flutter/services.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:shorebird_code_push/shorebird_code_push.dart';

import '../api/api_client.dart';
import '../models/update_info.dart';

/// Registers this device with the server on startup — the mobile equivalent of
/// the desktop apps' RegisterDevice (upsert into dbo.Device keyed on DeviceName).
class DeviceRegistration {
  DeviceRegistration._();

  static const String _kDeviceName = 'gw_device_name';
  static const String applicationName = 'Globeweigh.Picking';
  static const MethodChannel _channel = MethodChannel('globeweigh/shortcut');

  /// Best-effort: gathers a stable device name + app version and calls the API.
  /// Never throws — registration must not block or break startup.
  static Future<void> registerOnStartup() async {
    try {
      await _register();
    } catch (_) {
      // Swallow — the app works without registration; we just won't appear in
      // the Device table this run.
    }
  }

  /// Registers (refreshing IP/version + last-seen) and returns any queued update
  /// for this device. Called from the home screen so the user can update. Returns
  /// null on any error (best-effort).
  static Future<UpdateInfo?> checkForUpdate() async {
    try {
      return await _register();
    } catch (_) {
      return null;
    }
  }

  /// The stable device name (model + persisted suffix) shown in the UI and used
  /// for registration — the same value sent to the server.
  static Future<String> deviceName() => _stableDeviceName();

  static Future<UpdateInfo> _register() async {
    final deviceName = await _stableDeviceName();
    final pkg = await PackageInfo.fromPlatform();
    return ApiClient().registerDevice(
      deviceName: deviceName,
      applicationName: applicationName,
      currentVersion: pkg.version,
      currentPatch: await _currentPatch(),
    );
  }

  /// The Shorebird patch this run is on, or null on the bare release, off Shorebird
  /// (`flutter run`), or if the updater will not say. Never throws.
  static Future<int?> _currentPatch() async {
    try {
      final updater = ShorebirdUpdater();
      if (!updater.isAvailable) return null;
      return (await updater.readCurrentPatch())?.number;
    } catch (_) {
      return null;
    }
  }

  /// The device name used for registration. Prefers the name the user set in
  /// Settings > About phone > Device name (read natively). Falls back to a stable
  /// model + persisted random suffix for devices with no name set — which includes
  /// a device still on its factory name (the native side reports those as unset,
  /// since every unit of a model shares one and registration keys on the name).
  static Future<String> _stableDeviceName() async {
    // 1. The user-set About-phone name is authoritative when present.
    try {
      final set = await _channel.invokeMethod<String>('getDeviceName');
      if (set != null && set.trim().isNotEmpty) return set.trim();
    } catch (_) {
      // Native call unavailable — fall through to the generated name.
    }

    // 2. Fallback: a stable model + random suffix, persisted once.
    final prefs = await SharedPreferences.getInstance();
    final existing = prefs.getString(_kDeviceName);
    if (existing != null && existing.isNotEmpty) return existing;

    var base = 'Android';
    try {
      final android = await DeviceInfoPlugin().androidInfo;
      base = android.model.isNotEmpty ? android.model : android.device;
    } catch (_) {
      // Fall back to the generic base.
    }
    final suffix =
        Random().nextInt(0xFFFFFF).toRadixString(16).toUpperCase().padLeft(6, '0');
    final name = '$base-$suffix';
    await prefs.setString(_kDeviceName, name);
    return name;
  }
}
