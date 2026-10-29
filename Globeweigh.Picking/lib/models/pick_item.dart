/// A pallet-pick line — a product on an order, with the pallet count to pick.
/// From GET /api/picking/pending/{id}/picklist (grouped by product & order).
class PickItem {
  final int productId;
  final String? productDescription;
  final String? gtin;
  final int orderId;
  final int orderNo;
  final int palletCount;
  // Bag size + units per pallet, from the order-item line.
  final int? size;
  final int upp;
  // Product.SoldBy. A 'CubicMeter' line (raw peat) is never scanned: it is marked picked
  // once (palletCount 1), and [quantity] carries its m³ for display.
  final String soldBy;
  final int quantity;

  const PickItem({
    required this.productId,
    this.productDescription,
    this.gtin,
    required this.orderId,
    required this.orderNo,
    required this.palletCount,
    this.size,
    this.upp = 0,
    this.soldBy = 'Pallet',
    this.quantity = 0,
  });

  bool get isCubicMeter => soldBy == 'CubicMeter';
  // The order's POS board (soldBy 'Pos', productId 0): not an order line, tapped picked.
  bool get isPos => soldBy == 'Pos';
  // Picked with a tap rather than by scanning pallets.
  bool get isTapToPick => isCubicMeter || isPos;

  factory PickItem.fromJson(Map<String, dynamic> json) => PickItem(
        productId: (json['productId'] as num?)?.toInt() ?? 0,
        productDescription: json['productDescription'] as String?,
        gtin: json['gtin'] as String?,
        orderId: (json['orderId'] as num?)?.toInt() ?? 0,
        orderNo: (json['orderNo'] as num?)?.toInt() ?? 0,
        palletCount: (json['palletCount'] as num?)?.toInt() ?? 0,
        size: (json['size'] as num?)?.toInt(),
        upp: (json['upp'] as num?)?.toInt() ?? 0,
        soldBy: json['soldBy'] as String? ?? 'Pallet',
        quantity: (json['quantity'] as num?)?.toInt() ?? 0,
      );
}
