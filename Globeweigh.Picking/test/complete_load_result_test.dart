import 'package:flutter_test/flutter_test.dart';
import 'package:globeweigh_picking/models/complete_load_result.dart';

// The wording here is what a picker reads before handing paperwork to a driver, so the
// pluralisation and the "nothing was queued" case are worth pinning down.
void main() {
  group('message', () {
    test('several notes, several copies', () {
      const r = CompleteLoadResult(
          ordersDispatched: 3, deliveryNotesQueued: 3, copies: 2);
      expect(r.message,
          'Load is complete — 3 delivery notes sent to the printer (2 copies each)');
    });

    test('a single note is not pluralised', () {
      const r = CompleteLoadResult(
          ordersDispatched: 1, deliveryNotesQueued: 1, copies: 2);
      expect(r.message,
          'Load is complete — 1 delivery note sent to the printer (2 copies each)');
    });

    test('one copy drops the copies suffix', () {
      const r = CompleteLoadResult(
          ordersDispatched: 1, deliveryNotesQueued: 1, copies: 1);
      expect(r.message, 'Load is complete — 1 delivery note sent to the printer');
    });

    test('nothing queued must not claim anything was printed', () {
      // Re-completing a load: the notes went the first time, so the filtered unique
      // index queues nothing. Claiming "sent to the printer" here would send a picker
      // looking for paper that is not coming.
      const r = CompleteLoadResult(
          ordersDispatched: 2, deliveryNotesQueued: 0, copies: 2);
      expect(r.message, 'Load is complete');
      expect(r.message.contains('printer'), isFalse);
    });
  });

  group('parse', () {
    test('reads the server payload', () {
      final r = CompleteLoadResult.parse(
          '{"ordersDispatched":3,"deliveryNotesQueued":3,"copies":2}');
      expect(r.ordersDispatched, 3);
      expect(r.deliveryNotesQueued, 3);
      expect(r.copies, 2);
    });

    test('an empty body still completes the load quietly', () {
      // What an older server returns (204, no content). The load is still complete;
      // the app just must not crash or invent a printing claim.
      final r = CompleteLoadResult.parse('');
      expect(r.deliveryNotesQueued, 0);
      expect(r.message, 'Load is complete');
    });

    test('junk body is tolerated', () {
      expect(CompleteLoadResult.parse('not json').message, 'Load is complete');
      expect(CompleteLoadResult.parse('[1,2,3]').message, 'Load is complete');
    });
  });
}
