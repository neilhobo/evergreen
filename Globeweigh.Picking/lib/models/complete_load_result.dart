import 'dart:convert';

/// What completing a load actually did (POST /api/picking/pending/{id}/complete).
///
/// Completing dispatches the orders AND queues each one's delivery note to print on the
/// office printer. The counts come back from the server rather than being assumed by the
/// app, because they are not always what you would guess: re-completing a load queues
/// nothing (the notes went the first time), so a hard-coded "notes sent to the printer"
/// would be a lie the picker acts on.
class CompleteLoadResult {
  final int ordersDispatched;
  final int deliveryNotesQueued;
  final int copies;

  const CompleteLoadResult({
    this.ordersDispatched = 0,
    this.deliveryNotesQueued = 0,
    this.copies = 0,
  });

  /// Tolerant of an empty or unparseable body: the endpoint answered 204 with no content
  /// before printing existed, and a tablet running against an older server must still
  /// complete loads — it just won't mention printing.
  static CompleteLoadResult parse(String body) {
    if (body.trim().isEmpty) return const CompleteLoadResult();
    try {
      final m = jsonDecode(body);
      if (m is! Map) return const CompleteLoadResult();
      int i(String k) => (m[k] is num) ? (m[k] as num).toInt() : 0;
      return CompleteLoadResult(
        ordersDispatched: i('ordersDispatched'),
        deliveryNotesQueued: i('deliveryNotesQueued'),
        copies: i('copies'),
      );
    } catch (_) {
      return const CompleteLoadResult();
    }
  }

  /// The line shown to the picker once the load is complete. Deliberately says the notes
  /// were SENT to the printer, not that they printed — at this point they are queued, and
  /// the actual printing happens a second or two later on the server.
  String get message {
    if (deliveryNotesQueued <= 0) return 'Load is complete';
    final notes = deliveryNotesQueued == 1
        ? '1 delivery note'
        : '$deliveryNotesQueued delivery notes';
    if (copies <= 1) return 'Load is complete — $notes sent to the printer';
    return 'Load is complete — $notes sent to the printer ($copies copies each)';
  }
}
