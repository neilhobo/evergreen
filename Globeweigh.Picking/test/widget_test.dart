// Basic smoke test for the Globeweigh Picking home screen.

import 'package:flutter_test/flutter_test.dart';

import 'package:globeweigh_picking/main.dart';

void main() {
  testWidgets('Home screen shows the Pending Loads button',
      (WidgetTester tester) async {
    await tester.pumpWidget(const GlobeweighPickingApp());
    await tester.pumpAndSettle();

    expect(find.text('Pending Loads'), findsOneWidget);
  });
}
