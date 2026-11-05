import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shorebird_code_push/shorebird_code_push.dart';

/// Where the Shorebird patch download has got to. Drives the banner and the
/// restart prompt in [PatchGate].
enum PatchPhase {
  /// Nothing to do — either up to date, or the updater isn't available
  /// (debug/profile builds, or a release not built with `shorebird release`).
  idle,

  /// Asking Shorebird whether a newer patch exists. Silent — no UI.
  checking,

  /// Pulling the patch down. Shorebird exposes no byte progress, so the UI
  /// shows an indeterminate bar.
  downloading,

  /// The patch is on disk and takes effect on the next app launch.
  readyToRestart,

  /// The check or download failed. Background checks stay silent; a manual
  /// check surfaces [PatchState.error].
  failed,
}

@immutable
class PatchState {
  /// Patch number the running code came from — null when running the plain
  /// release with no patch applied.
  final int? currentPatch;

  /// Patch number waiting to be picked up on the next launch.
  final int? readyPatch;

  final PatchPhase phase;
  final String? error;

  /// Set once the user has dismissed the "restart to finish" dialog, so it
  /// doesn't reopen on every resume. The banner keeps the prompt visible.
  final bool restartPromptDismissed;

  const PatchState({
    this.currentPatch,
    this.readyPatch,
    this.phase = PatchPhase.idle,
    this.error,
    this.restartPromptDismissed = false,
  });

  bool get isBusy => phase == PatchPhase.checking || phase == PatchPhase.downloading;

  PatchState _with({
    int? currentPatch,
    int? readyPatch,
    PatchPhase? phase,
    String? error,
    bool? restartPromptDismissed,
  }) => PatchState(
    currentPatch: currentPatch ?? this.currentPatch,
    readyPatch: readyPatch ?? this.readyPatch,
    phase: phase ?? this.phase,
    // error is only ever meaningful for the phase that set it, so it is
    // cleared on every transition unless explicitly passed.
    error: error,
    restartPromptDismissed: restartPromptDismissed ?? this.restartPromptDismissed,
  );
}

final patchControllerProvider = NotifierProvider<PatchController, PatchState>(PatchController.new);

/// Drives Shorebird code-push downloads by hand.
///
/// `auto_update: false` in shorebird.yaml means the engine never fetches a
/// patch on its own — this controller is the only thing that does, which is
/// what lets the UI show the download happening and prompt for a restart
/// afterwards rather than silently swapping code in on some future launch.
class PatchController extends Notifier<PatchState> {
  final ShorebirdUpdater _updater = ShorebirdUpdater();
  bool _running = false;

  /// False in debug/profile and in any release not produced by
  /// `shorebird release` — every entry point no-ops in that case.
  bool get isAvailable => _updater.isAvailable;

  @override
  PatchState build() {
    if (_updater.isAvailable) {
      // Best effort, off the critical path — the patch number is only ever
      // shown as diagnostics on the account screen.
      _updater
          .readCurrentPatch()
          .then((patch) {
            if (patch != null) state = state._with(currentPatch: patch.number);
          })
          .catchError((Object e) {
            debugPrint('[patch] readCurrentPatch failed: $e');
          });
    }
    return const PatchState();
  }

  /// Checks for a patch and, if one exists, downloads it.
  ///
  /// Returns the status Shorebird reported, or null when the updater isn't
  /// available or a check was already in flight. Safe to call repeatedly —
  /// resume, login and the manual button all funnel through here.
  Future<UpdateStatus?> check() async {
    if (!_updater.isAvailable) return null;
    // A patch already on disk can't be superseded until the app restarts, so
    // re-checking would just re-report the same thing.
    if (_running || state.phase == PatchPhase.readyToRestart) return null;

    _running = true;
    state = state._with(phase: PatchPhase.checking);
    try {
      final status = await _updater.checkForUpdate();
      switch (status) {
        case UpdateStatus.upToDate:
        case UpdateStatus.unavailable:
          state = state._with(phase: PatchPhase.idle);
        case UpdateStatus.restartRequired:
          // Downloaded during an earlier run of this process.
          state = state._with(
            phase: PatchPhase.readyToRestart,
            readyPatch: (await _readNextPatchNumber()) ?? state.readyPatch,
          );
        case UpdateStatus.outdated:
          state = state._with(phase: PatchPhase.downloading);
          await _updater.update();
          state = state._with(phase: PatchPhase.readyToRestart, readyPatch: await _readNextPatchNumber());
      }
      return status;
    } on UpdateException catch (e) {
      debugPrint('[patch] update failed: ${e.message}');
      state = state._with(phase: PatchPhase.failed, error: e.message);
      return null;
    } catch (e) {
      debugPrint('[patch] check failed: $e');
      state = state._with(phase: PatchPhase.failed, error: '$e');
      return null;
    } finally {
      _running = false;
    }
  }

  /// Hides the restart dialog for the rest of the session; the banner stays.
  void dismissRestartPrompt() => state = state._with(restartPromptDismissed: true);

  /// Lets the banner reopen the dialog after a "Later".
  void reopenRestartPrompt() => state = state._with(restartPromptDismissed: false);

  Future<int?> _readNextPatchNumber() async {
    try {
      return (await _updater.readNextPatch())?.number;
    } catch (e) {
      debugPrint('[patch] readNextPatch failed: $e');
      return null;
    }
  }
}
