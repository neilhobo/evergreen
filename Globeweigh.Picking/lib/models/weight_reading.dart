/// A live weighbridge reading, proxied by Globeweigh.Api from the GlobeweighService
/// (same source as TauriAdmin's Gate/Barrier and Dispatch weigh screens).
class WeightReading {
  final String weight;
  final int ageMs;
  final bool isConnected;

  WeightReading({
    required this.weight,
    required this.ageMs,
    required this.isConnected,
  });

  factory WeightReading.fromJson(Map<String, dynamic> json) => WeightReading(
        weight: (json['weight'] ?? '').toString(),
        ageMs: (json['ageMs'] as num?)?.toInt() ?? -1,
        isConnected: json['isConnected'] == true,
      );

  /// A reading is live when the service is connected and the sample is fresh
  /// (same rule as TauriAdmin's WeighModal).
  bool get isLive => isConnected && ageMs >= 0 && ageMs < 5000;

  /// The weight rounded to whole Kg, or null if unparsable.
  int? get kg => double.tryParse(weight.trim())?.round();
}
