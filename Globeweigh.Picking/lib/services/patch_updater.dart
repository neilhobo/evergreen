import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/services.dart';
import 'package:shorebird_code_push/shorebird_code_push.dart';

/// Where the app stands with an over-the-air Dart patch (Shorebird code push).
enum PatchState {
  /// Nothing to show: up to date, or not a Shorebird build (a `flutter run` debug build).
  idle,
  checking,
  downloading,

  /// A patch is on the device and takes effect on the next launch — restart to use it.
  restartRequired,
  failed;

  bool get showsBanner =>
      this == downloading || this == restartRequired || this == failed;
}

/// Drives Shorebird patches by hand so the picker can SEE them happen.
///
/// shorebird.yaml has auto_update off. Left on, the engine fetches a patch silently in the
/// background and it only takes effect on some later launch — the tablets run for weeks,
/// so a patch could sit unused for as long, with nobody the wiser. Instead the home screen
/// calls [checkAndDownload] on load and once a minute: a patch downloads in view
/// ("Updating…") and, once it is on the device, the picker is told to restart and given the
/// button to do it. Shorebird loads a patch only when the engine starts, so that has to be
/// a real process restart ([restartApp], native) — a Flutter hot restart would carry on
/// running the old code.
///
/// Preview in a plain debug build (no Shorebird engine, so nothing real to fetch): run with
/// `--dart-define=GW_FAKE_PATCH=true` and the whole sequence is played out with delays.
class PatchUpdater {
  PatchUpdater._();
  static final PatchUpdater instance = PatchUpdater._();

  static const bool _fake =
      kDebugMode && bool.fromEnvironment('GW_FAKE_PATCH');
  static const MethodChannel _channel = MethodChannel('globeweigh/app');

  final ShorebirdUpdater _updater = ShorebirdUpdater();

  final ValueNotifier<PatchState> state = ValueNotifier(PatchState.idle);

  /// Why the last attempt failed (when [state] is failed).
  String? error;

  /// The patch just put on the device (when [state] is restartRequired), if known.
  int? nextPatch;

  /// The patch this run of the app is on — null on a bare release, or off Shorebird.
  int? currentPatch;

  bool _busy = false;

  /// Look for a patch and, if there is one, download it straight away. Safe to call as
  /// often as you like: a run in progress, or a patch already waiting on a restart, is
  /// left alone. A failed attempt is retried on the next call (the home screen's minute
  /// timer), so a dropped connection heals itself.
  Future<void> checkAndDownload() async {
    if (_busy || state.value == PatchState.restartRequired) return;
    _busy = true;
    try {
      if (_fake) {
        await _fakeRun();
        return;
      }
      if (!_updater.isAvailable) return; // `flutter run` / not a Shorebird build
      currentPatch ??= await _readNumber(_updater.readCurrentPatch);
      state.value = PatchState.checking;
      final status = await _updater.checkForUpdate();
      switch (status) {
        case UpdateStatus.outdated:
          state.value = PatchState.downloading;
          await _updater.update();
          nextPatch = await _readNumber(_updater.readNextPatch);
          state.value = PatchState.restartRequired;
        case UpdateStatus.restartRequired:
          // Downloaded on an earlier visit (or launch) and never restarted.
          nextPatch = await _readNumber(_updater.readNextPatch);
          state.value = PatchState.restartRequired;
        case UpdateStatus.upToDate:
        case UpdateStatus.unavailable:
          state.value = PatchState.idle;
      }
    } on UpdateException catch (e) {
      error = e.message;
      state.value = PatchState.failed;
    } catch (e) {
      error = '$e';
      state.value = PatchState.failed;
    } finally {
      _busy = false;
    }
  }

  static Future<int?> _readNumber(Future<Patch?> Function() read) async {
    try {
      return (await read())?.number;
    } catch (_) {
      return null;
    }
  }

  Future<void> _fakeRun() async {
    state.value = PatchState.checking;
    await Future<void>.delayed(const Duration(seconds: 1));
    state.value = PatchState.downloading;
    await Future<void>.delayed(const Duration(seconds: 5));
    nextPatch = 1;
    state.value = PatchState.restartRequired;
  }

  /// Relaunch the app in a fresh process (native) so the downloaded patch is loaded.
  static Future<void> restartApp() async {
    try {
      await _channel.invokeMethod<void>('restart');
    } on PlatformException {
      // No native side (tests, other platforms) — nothing more to be done here.
    } on MissingPluginException {
      // Same.
    }
  }
}
