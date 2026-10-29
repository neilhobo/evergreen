/// Outcome of recording one scanned pallet (POST /api/picking/pick). The server is
/// authoritative: it matches the product code to an order line, detects duplicates, and
/// persists the pick. [status] is one of: ok, duplicate, notOnLoad, notOnOrder,
/// orderComplete, alreadyComplete, unreadable.
class PickResult {
  final String status;
  final int? orderId;
  final int? orderNo;
  final int? productId;
  final String? productDescription;
  final String? palletId;
  final String? productCode;

  /// The pallet's weight (Kg) from its CodicoPallet record — or, for an unknown pallet or
  /// a no-scan label, the server's estimate from the product's other pallets (see
  /// [weightEstimated]).
  final double weight;
  final DateTime? pickedAt;
  final int pickedForLine;
  final int targetForLine;
  final bool palletKnown;
  final bool wasDuplicate;

  /// True when [weight] is an estimate rather than this pallet's own measured figure.
  final bool weightEstimated;

  /// The DispatchOrder_Picked row this pick created (0 when the call didn't create one) —
  /// carried so the pallet can later be removed by row rather than by code.
  final int pickedId;

  /// True when this pallet was taken onto a line that was already full, after the picker
  /// confirmed the over-pick prompt.
  final bool wasOverPick;

  /// True when the order this pallet went onto is now fully picked (or was already
  /// complete, for an 'orderComplete' rejection) — the picker can move to the next order.
  final bool orderComplete;

  const PickResult({
    required this.status,
    this.orderId,
    this.orderNo,
    this.productId,
    this.productDescription,
    this.palletId,
    this.productCode,
    this.weight = 0,
    this.pickedAt,
    this.pickedForLine = 0,
    this.targetForLine = 0,
    this.palletKnown = false,
    this.wasDuplicate = false,
    this.pickedId = 0,
    this.orderComplete = false,
    this.wasOverPick = false,
    this.weightEstimated = false,
  });

  bool get isOk => status == 'ok';

  factory PickResult.fromJson(Map<String, dynamic> j) => PickResult(
        status: j['status'] as String? ?? 'unreadable',
        orderId: j['orderId'] as int?,
        orderNo: j['orderNo'] as int?,
        productId: j['productId'] as int?,
        productDescription: j['productDescription'] as String?,
        palletId: j['palletId'] as String?,
        productCode: j['productCode'] as String?,
        weight: (j['weight'] as num?)?.toDouble() ?? 0,
        pickedAt: j['pickedAt'] == null
            ? null
            : DateTime.tryParse(j['pickedAt'] as String),
        pickedForLine: j['pickedForLine'] as int? ?? 0,
        targetForLine: j['targetForLine'] as int? ?? 0,
        palletKnown: j['palletKnown'] as bool? ?? false,
        wasDuplicate: j['wasDuplicate'] as bool? ?? false,
        pickedId: j['pickedId'] as int? ?? 0,
        orderComplete: j['orderComplete'] as bool? ?? false,
        wasOverPick: j['wasOverPick'] as bool? ?? false,
        weightEstimated: j['weightEstimated'] as bool? ?? false,
      );
}

/// A pallet already picked onto a load (persisted DispatchOrder_Picked row), used to
/// restore progress when the pick list (re)loads.
class PickedPallet {
  /// The DispatchOrder_Picked row id — identifies this exact pick when removing it.
  final int id;
  final int orderId;
  final int productId;
  final String palletId;

  /// Weight (Kg) recorded for the pallet — the server's estimate for a no-scan label or
  /// unknown pallet (see [isWeightEstimated]).
  final double weight;
  final DateTime pickedAt;
  final bool isNoScan;

  /// True when [weight] is an estimate rather than this pallet's own measured figure.
  final bool isWeightEstimated;

  const PickedPallet({
    required this.id,
    required this.orderId,
    required this.productId,
    required this.palletId,
    this.weight = 0,
    required this.pickedAt,
    this.isNoScan = false,
    this.isWeightEstimated = false,
  });

  factory PickedPallet.fromJson(Map<String, dynamic> j) => PickedPallet(
        id: j['id'] as int? ?? 0,
        orderId: j['orderId'] as int,
        productId: j['productId'] as int,
        palletId: j['palletId'] as String,
        weight: (j['weight'] as num?)?.toDouble() ?? 0,
        pickedAt: DateTime.tryParse(j['pickedAt'] as String? ?? '') ??
            DateTime.fromMillisecondsSinceEpoch(0),
        isNoScan: j['isNoScan'] as bool? ?? false,
        isWeightEstimated: j['isWeightEstimated'] as bool? ?? false,
      );
}
