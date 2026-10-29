import 'package:flutter/services.dart';
import 'package:ota_update/ota_update.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../config.dart';

/// Remote-support companion (droidVNC-NG) glue.
///
/// The companion is a separate APK (Android can't silently bundle-install it), so:
///  - [ensureRunning] nudges its VNC server started via droidVNC-NG's Intent API on
///    every app launch. On a provisioned device (see scripts/provision-vnc.ps1) this
///    is headless; elsewhere droidVNC-NG rejects the unknown access key and nothing
///    happens, so it's always safe to call.
///  - [shouldOfferInstall]/[installCompanion] back a one-time "install remote support?"
///    prompt: the APK downloads from our own server and hands off to the system
///    installer (same REQUEST_INSTALL_PACKAGES flow the OTA updates already use).
class VncCompanion {
  static const _channel = MethodChannel('globeweigh/vnc');
  static const _kOfferedPref = 'vnc_install_offered';

  static Future<bool> isInstalled() async {
    try {
      return await _channel.invokeMethod<bool>('isInstalled') ?? false;
    } catch (_) {
      return false;
    }
  }

  /// Fire-and-forget: ask droidVNC-NG to start its server if it's installed.
  static Future<void> ensureRunning() async {
    try {
      if (await isInstalled()) await _channel.invokeMethod('start');
    } catch (_) {
      // Best-effort — remote support must never affect picking.
    }
  }

  /// True when the companion is missing and we haven't offered the install yet.
  static Future<bool> shouldOfferInstall() async {
    if (await isInstalled()) return false;
    final prefs = await SharedPreferences.getInstance();
    return !(prefs.getBool(_kOfferedPref) ?? false);
  }

  /// Remember that the operator has seen the offer (accepted or not) — never nag again.
  static Future<void> markOffered() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(_kOfferedPref, true);
  }

  /// Download the companion APK from the server and hand it to the system installer.
  /// Emits the same OtaEvent stream the release updates use (progress → installer prompt).
  static Stream<OtaEvent> installCompanion() {
    return OtaUpdate().execute(
      '${AppConfig.apiBaseUrl}/api/devices/companion/vnc',
      destinationFilename: 'droidvnc-ng.apk',
    );
  }
}
