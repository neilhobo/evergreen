/// A pending weighbridge record — mirrors the TauriAdmin Orders/Dispatch "Pending"
/// list (GET /api/picking/pending).
class PendingLoad {
  final int id;
  final bool isOutgoing;
  final DateTime? pendingWeighDate;
  final String? haulierName;
  final String? trailerNo;
  final int weightIn;
  final int? orderCount;
  // Overall picking progress for the whole load (pallets picked / pallets to pick).
  final int pickedPallets;
  final int totalPallets;
  // The picker has confirmed the trailer number — until then the detail panel
  // shows the confirmation gate instead of the orders.
  final bool trailerConfirmed;

  const PendingLoad({
    required this.id,
    required this.isOutgoing,
    this.pendingWeighDate,
    this.haulierName,
    this.trailerNo,
    required this.weightIn,
    this.orderCount,
    this.pickedPallets = 0,
    this.totalPallets = 0,
    this.trailerConfirmed = false,
  });

  /// 0.0–1.0 fraction of the load picked.
  double get progress =>
      totalPallets > 0 ? (pickedPallets / totalPallets).clamp(0.0, 1.0) : 0.0;
  bool get isComplete => totalPallets > 0 && pickedPallets >= totalPallets;

  factory PendingLoad.fromJson(Map<String, dynamic> json) => PendingLoad(
        id: json['id'] as int,
        isOutgoing: json['isOutgoing'] as bool? ?? false,
        pendingWeighDate: json['pendingWeighDate'] == null
            ? null
            : DateTime.tryParse(json['pendingWeighDate'] as String),
        haulierName: json['haulierName'] as String?,
        trailerNo: json['trailerNo'] as String?,
        weightIn: (json['weightIn'] as num?)?.toInt() ?? 0,
        orderCount: (json['orderCount'] as num?)?.toInt(),
        pickedPallets: (json['pickedPallets'] as num?)?.toInt() ?? 0,
        totalPallets: (json['totalPallets'] as num?)?.toInt() ?? 0,
        trailerConfirmed: json['trailerConfirmed'] as bool? ?? false,
      );
}
