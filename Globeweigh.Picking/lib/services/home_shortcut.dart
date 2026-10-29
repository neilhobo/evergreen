import 'dart:io';

import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// Asks Android to pin the app to the home screen — once, on first launch.
/// The launcher shows a one-tap "Add to Home screen?" confirmation.
class HomeShortcut {
  HomeShortcut._();

  static const MethodChannel _channel = MethodChannel('globeweigh/shortcut');
  static const String _kPrompted = 'home_pin_prompted';

  static Future<void> requestPinOnce() async {
    if (!Platform.isAndroid) return;
    try {
      final prefs = await SharedPreferences.getInstance();
      if (prefs.getBool(_kPrompted) ?? false) return; // only ever ask once
      await _channel.invokeMethod('pinToHomeScreen');
      await prefs.setBool(_kPrompted, true);
    } catch (_) {
      // Best-effort — never block startup if the launcher doesn't support it.
    }
  }
}
