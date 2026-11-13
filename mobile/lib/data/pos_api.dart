import '../core/api/api_client.dart';

/// One product of a POS order: a POS board is wanted for it (PosLineDto).
class PosLine {
  final int productId;
  final String name;
  final int? size;
  final int upp;
  final String? soldBy;
  final double quantity;

  /// When its board was marked made up, and by whom; null = still to make.
  final DateTime? madeAt;
  final String? madeBy;

  const PosLine({
    required this.productId,
    required this.name,
    this.size,
    this.upp = 1,
    this.soldBy,
    this.quantity = 0,
    this.madeAt,
    this.madeBy,
  });

  bool get made => madeAt != null;

  bool get _cubic => soldBy == 'CubicMeter';

  /// "4 pallets · 50L · 60 UPP", as an order line reads in Sales.
  String get detail {
    final q = quantity == quantity.roundToDouble() ? quantity.toInt().toString() : quantity.toStringAsFixed(2);
    return [
      if (_cubic) '$q m³' else '$q ${quantity == 1 ? 'pallet' : 'pallets'}',
      if (size != null && size! > 0) '${size}L',
      if (!_cubic) '$upp UPP',
    ].join(' · ');
  }

  factory PosLine.fromJson(Map<String, dynamic> j) => PosLine(
    productId: j['productId'] as int,
    name: j['name'] as String? ?? '',
    size: (j['size'] as num?)?.toInt(),
    upp: (j['upp'] as num?)?.toInt() ?? 1,
    soldBy: j['soldBy'] as String?,
    quantity: (j['quantity'] as num?)?.toDouble() ?? 0,
    madeAt: j['madeAt'] == null ? null : DateTime.parse(j['madeAt'] as String),
    madeBy: j['madeBy'] as String?,
  );
}

/// An open order that carries a POS board (PosOrderDto).
class PosOrder {
  final int id;
  final int orderNo;
  final String? customerName;
  final DateTime? deliveryDate;
  final List<PosLine> lines;

  /// Finished by hand (Complete), whatever was ticked.
  final bool completed;

  /// null = not on a load; 'Load' = on a load not yet weighed in; 'Pending' =
  /// on a load that is weighed in (the lorry is on site, being loaded).
  final String? loadState;
  final DateTime? loadDate;
  final String? loadHaulier;

  bool get onLoad => loadState != null;
  bool get onPending => loadState == 'Pending';

  const PosOrder({
    required this.id,
    required this.orderNo,
    this.customerName,
    this.deliveryDate,
    this.loadState,
    this.loadDate,
    this.loadHaulier,
    this.lines = const [],
    this.completed = false,
  });

  int get total => lines.length;
  int get made => lines.where((l) => l.made).length;
  bool get allMade => total > 0 && made == total;

  /// Off the POS Boards list: every board made, or completed by hand.
  bool get done => completed || allMade;

  factory PosOrder.fromJson(Map<String, dynamic> j) => PosOrder(
    id: j['id'] as int,
    orderNo: j['orderNo'] as int,
    customerName: j['customerName'] as String?,
    deliveryDate: j['deliveryDate'] == null ? null : DateTime.parse(j['deliveryDate'] as String),
    lines: (j['lines'] as List? ?? const []).map((e) => PosLine.fromJson(e as Map<String, dynamic>)).toList(),
    completed: j['completed'] as bool? ?? false,
    loadState: j['loadState'] as String?,
    loadDate: j['loadDate'] == null ? null : DateTime.parse(j['loadDate'] as String),
    loadHaulier: j['loadHaulier'] as String?,
  );
}

/// POS Boards (PosBoardsController), for whoever holds the POS creator tag.
class PosApi {
  final ApiClient _client;
  PosApi(this._client);

  /// The signed-in user's Staff tags as they are now.
  Future<List<String>> myTags() async {
    final res = await _client.dio.get<List<dynamic>>('/api/auth/tags');
    return (res.data ?? const []).cast<String>();
  }

  /// The open orders still owed a board, soonest delivery first.
  Future<List<PosOrder>> orders() async {
    final res = await _client.dio.get<List<dynamic>>('/api/mobile/pos-boards');
    return res.data!.map((e) => PosOrder.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// POS made up for these products of the order; returns it as it now stands.
  Future<PosOrder> made(int orderId, List<int> productIds) async {
    final res = await _client.dio.post<Map<String, dynamic>>(
      '/api/mobile/pos-boards/$orderId/made',
      data: {'productIds': productIds},
    );
    return PosOrder.fromJson(res.data!);
  }

  /// Finish the order as it stands (not every product gets a board): it leaves
  /// the list. [productIds], ticked but not yet saved, are marked made up first.
  Future<PosOrder> complete(int orderId, List<int> productIds) async {
    final res = await _client.dio.post<Map<String, dynamic>>(
      '/api/mobile/pos-boards/$orderId/complete',
      data: {'productIds': productIds},
    );
    return PosOrder.fromJson(res.data!);
  }
}
