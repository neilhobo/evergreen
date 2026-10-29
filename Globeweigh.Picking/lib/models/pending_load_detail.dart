import 'pending_order.dart';

/// A pending load's detail — totals plus its orders (GET /api/picking/pending/{id}/detail).
class PendingLoadDetail {
  final int totalProducts;
  final int totalPallets;

  /// Weight (Kg) of the picks on the load so far, estimates included — climbs as picking
  /// goes on.
  final double totalWeight;

  /// No-scan pallets picked onto the load (an exception, flagged for reporting).
  final int noScanPallets;

  /// Picks whose weight is the server's estimate rather than the pallet's own figure
  /// (every no-scan label, plus any scanned code with no pallet record). Any at all means
  /// [totalWeight] is a best estimate rather than a measured figure.
  final int estimatedPallets;
  final List<PendingOrder> orders;

  /// Some order on the load has a POS board — completing it needs a POS Boards photo too.
  final bool hasPos;

  const PendingLoadDetail({
    required this.totalProducts,
    required this.totalPallets,
    this.totalWeight = 0,
    this.noScanPallets = 0,
    this.estimatedPallets = 0,
    required this.orders,
    this.hasPos = false,
  });

  factory PendingLoadDetail.fromJson(Map<String, dynamic> json) => PendingLoadDetail(
        totalProducts: (json['totalProducts'] as num?)?.toInt() ?? 0,
        totalPallets: (json['totalPallets'] as num?)?.toInt() ?? 0,
        totalWeight: (json['totalWeight'] as num?)?.toDouble() ?? 0,
        noScanPallets: (json['noScanPallets'] as num?)?.toInt() ?? 0,
        estimatedPallets: (json['estimatedPallets'] as num?)?.toInt() ?? 0,
        orders: ((json['orders'] as List<dynamic>?) ?? [])
            .map((e) => PendingOrder.fromJson(e as Map<String, dynamic>))
            .toList(),
        hasPos: json['hasPos'] as bool? ?? false,
      );
}
