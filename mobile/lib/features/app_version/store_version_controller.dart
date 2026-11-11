import 'dart:io' show Platform;

import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';
import 'package:url_launcher/url_launcher.dart';

import '../auth/auth_controller.dart';

/// What GET /api/mobile/version says about a new store build (TestFlight on iOS),
/// set against the installed one.
@immutable
class StoreVersionState {
  final String installed;
  final String latest;
  final String minimum;
  final String url;

  /// Installed build number is below [latest]: offer the update.
  final bool updateAvailable;

  /// Installed build number is below [minimum]: block until updated.
  final bool updateRequired;

  /// "Later" was tapped for this [latest]; the bar stays, the dialog doesn't reopen.
  final bool promptDismissed;

  const StoreVersionState({
    this.installed = '',
    this.latest = '',
    this.minimum = '',
    this.url = '',
    this.updateAvailable = false,
    this.updateRequired = false,
    this.promptDismissed = false,
  });

  StoreVersionState copyWith({bool? promptDismissed}) => StoreVersionState(
    installed: installed,
    latest: latest,
    minimum: minimum,
    url: url,
    updateAvailable: updateAvailable,
    updateRequired: updateRequired,
    promptDismissed: promptDismissed ?? this.promptDismissed,
  );
}

final storeVersionControllerProvider = NotifierProvider<StoreVersionController, StoreVersionState>(
  StoreVersionController.new,
);

/// Compares the installed build number (the "+N" in pubspec's version) with the
/// manifest mobile/tools/release-ios.ps1 writes on the server after an upload.
///
/// Fails open: offline or an API error simply means no prompt. Skipped in debug
/// builds (the dev pubspec rarely matches prod) unless run with
/// `--dart-define=GW_FAKE_STORE_UPDATE=true`, which fakes a newer build to preview
/// the prompt.
class StoreVersionController extends Notifier<StoreVersionState> {
  static const _fake = bool.fromEnvironment('GW_FAKE_STORE_UPDATE');
  bool _running = false;

  @override
  StoreVersionState build() => const StoreVersionState();

  String get _platform => Platform.isIOS ? 'ios' : 'android';

  Future<void> check() async {
    if (_running) return;
    if (kDebugMode && !_fake) return;
    _running = true;
    try {
      final pkg = await PackageInfo.fromPlatform();
      final installedBuild = int.tryParse(pkg.buildNumber) ?? 0;
      final installed = '${pkg.version}+${pkg.buildNumber}';

      String latest, minimum, url;
      if (_fake) {
        latest = '${pkg.version}+${installedBuild + 1}';
        minimum = '';
        url = 'itms-beta://beta.itunes.apple.com/v1/app/6830162362';
      } else {
        final res = await ref
            .read(apiClientProvider)
            .dio
            .get<Map<String, dynamic>>('/api/mobile/version', queryParameters: {'platform': _platform});
        final data = res.data ?? const {};
        latest = data['latest'] as String? ?? '';
        minimum = data['minimum'] as String? ?? '';
        url = data['url'] as String? ?? '';
      }

      final available = _build(latest) > installedBuild;
      // A "Later" only holds for the build it was given for; a newer one asks again.
      final dismissed = state.promptDismissed && state.latest == latest;
      state = StoreVersionState(
        installed: installed,
        latest: latest,
        minimum: minimum,
        url: url,
        updateAvailable: available,
        updateRequired: _build(minimum) > installedBuild,
        promptDismissed: available && dismissed,
      );
    } catch (e) {
      debugPrint('[store-version] check skipped: $e');
    } finally {
      _running = false;
    }
  }

  void dismissPrompt() => state = state.copyWith(promptDismissed: true);

  /// Opens TestFlight (or whatever store link the manifest gives) at the app.
  Future<void> openStore() async {
    final uri = Uri.tryParse(state.url);
    var opened = false;
    if (uri != null && state.url.isNotEmpty) {
      try {
        opened = await launchUrl(uri, mode: LaunchMode.externalApplication);
      } catch (e) {
        debugPrint('[store-version] launch failed: $e');
      }
    }
    // itms-beta:// fails when TestFlight itself isn't installed: send them to get it.
    if (!opened && Platform.isIOS) {
      await launchUrl(
        Uri.parse('https://apps.apple.com/app/testflight/id899247664'),
        mode: LaunchMode.externalApplication,
      );
    }
  }

  // "1.0.0+7" -> 7; no "+N" -> 0, so an empty manifest never prompts.
  static int _build(String version) {
    final i = version.indexOf('+');
    return i < 0 ? 0 : int.tryParse(version.substring(i + 1)) ?? 0;
  }
}
