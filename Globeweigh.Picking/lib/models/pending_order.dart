/// An order on a pending load — from GET /api/picking/pending/{id}/orders.
class PendingOrder {
  final int id;
  final int orderNo;
  final String? customerName;
  final String? poNumber;
  final int pickedQuantity;
  final int totalQuantityOrdered;

  /// Loading order — the reverse of the delivery order set in Admin. 1 goes on the
  /// trailer first, so the first delivery drop is the last one loaded (nearest the doors).
  final int loadingOrder;

  /// Instructions for the pickers, typed on the order in Admin (Add/Edit Order).
  final String? pickingComments;

  /// Set once this order's picking is finished — every pallet picked, or ended short via
  /// "Complete Early". Until then the picker stays on this order.
  final DateTime? datePickingComplete;

  /// True when it was completed short of the ordered pallets (flagged for reporting).
  final bool completedEarly;

  bool get pickingComplete => datePickingComplete != null;

  const PendingOrder({
    required this.id,
    required this.orderNo,
    this.customerName,
    this.poNumber,
    required this.pickedQuantity,
    required this.totalQuantityOrdered,
    this.loadingOrder = 0,
    this.pickingComments,
    this.datePickingComplete,
    this.completedEarly = false,
  });

  /// [clearPickingComplete] reopens the order (a pallet was un-picked from a fully-picked
  /// one), which a plain null argument can't express.
  PendingOrder copyWith({
    DateTime? datePickingComplete,
    bool? completedEarly,
    bool clearPickingComplete = false,
  }) => PendingOrder(
    id: id,
    orderNo: orderNo,
    customerName: customerName,
    poNumber: poNumber,
    pickedQuantity: pickedQuantity,
    totalQuantityOrdered: totalQuantityOrdered,
    loadingOrder: loadingOrder,
    pickingComments: pickingComments,
    datePickingComplete: clearPickingComplete
        ? null
        : datePickingComplete ?? this.datePickingComplete,
    completedEarly: clearPickingComplete
        ? false
        : completedEarly ?? this.completedEarly,
  );

  factory PendingOrder.fromJson(Map<String, dynamic> json) => PendingOrder(
    id: json['id'] as int,
    orderNo: (json['orderNo'] as num?)?.toInt() ?? 0,
    customerName: json['customerName'] as String?,
    poNumber: json['poNumber'] as String?,
    pickedQuantity: (json['pickedQuantity'] as num?)?.toInt() ?? 0,
    totalQuantityOrdered: (json['totalQuantityOrdered'] as num?)?.toInt() ?? 0,
    loadingOrder: (json['loadingOrder'] as num?)?.toInt() ?? 0,
    pickingComments: json['pickingComments'] as String?,
    datePickingComplete: json['datePickingComplete'] == null
        ? null
        : DateTime.tryParse(json['datePickingComplete'] as String),
    completedEarly: json['completedEarly'] as bool? ?? false,
  );
}
