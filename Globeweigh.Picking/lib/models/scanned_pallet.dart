/// A pallet that has been scanned/picked against a product line. [isNoScan] is true for a
/// manually-added "no-scan label" (a pallet whose barcode wouldn't read) — it has no unique
/// or product code. [pickedId] is the server's DispatchOrder_Picked row id, which is how a
/// single pick is removed (a no-scan label has no code to identify it by).
class ScannedPallet {
  final int pickedId;
  final String uniqueCode;
  final String productCode;

  /// Weight (Kg). From the pallet's CodicoPallet record when it has one; otherwise (a
  /// no-scan label, or a pallet the label didn't match) the server's estimate from the
  /// product's other pallets, with [isWeightEstimated] set — so a line carrying those
  /// totals close to true, but not measured.
  final double weight;
  final DateTime pickedAt;
  final bool isNoScan;

  /// True when [weight] is the server's estimate rather than this pallet's own figure.
  final bool isWeightEstimated;

  const ScannedPallet({
    required this.uniqueCode,
    required this.productCode,
    required this.pickedAt,
    this.weight = 0,
    this.pickedId = 0,
    this.isNoScan = false,
    this.isWeightEstimated = false,
  });
}
