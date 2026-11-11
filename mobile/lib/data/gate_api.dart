import '../core/api/api_client.dart';
import '../core/api/api_config.dart';

/// Gate & Barrier for the super-admin and Colin: api/mobile/gate-barrier
/// (MobileGateBarrierController), the signed-in twin of TauriAdmin's page.

class GateCamera {
  final String name;
  final String stream; // go2rtc stream name
  const GateCamera(this.name, this.stream);

  factory GateCamera.fromJson(Map<String, dynamic> j) =>
      GateCamera(j['name'] as String? ?? '', j['stream'] as String? ?? '');
}

class RelayState {
  final bool connected;
  final bool gateOpen;
  final bool barrierOpen;
  const RelayState({this.connected = false, this.gateOpen = false, this.barrierOpen = false});

  factory RelayState.fromJson(Map<String, dynamic> j) => RelayState(
    connected: j['connected'] as bool? ?? false,
    gateOpen: j['gateOpen'] as bool? ?? false,
    barrierOpen: j['barrierOpen'] as bool? ?? false,
  );
}

/// One weighbridge reading from GlobeweighService (WeightReading in
/// TauriAdmin's api/gateBarrier.ts).
class WeightReading {
  final String weight;
  final int ageMs;
  final bool isConnected;
  final int? dataLostMs; // -1 while readings arrive; older services omit it

  const WeightReading({this.weight = '', this.ageMs = -1, this.isConnected = false, this.dataLostMs});

  factory WeightReading.fromJson(Map<String, dynamic> j) => WeightReading(
    weight: j['weight']?.toString() ?? '',
    ageMs: (j['ageMs'] as num?)?.toInt() ?? -1,
    isConnected: j['isConnected'] as bool? ?? false,
    dataLostMs: (j['dataLostMs'] as num?)?.toInt(),
  );
}

class GateApi {
  final ApiClient _client;
  GateApi(this._client);

  static const _base = '/api/mobile/gate-barrier';

  Future<List<GateCamera>> cameras() async {
    final res = await _client.dio.get<List<dynamic>>('$_base/cameras');
    return res.data!.map((e) => GateCamera.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// The camera's HLS link (signed, good for a few hours), as a full URL.
  Future<Uri> videoUrl(String stream) async {
    final res = await _client.dio.get<Map<String, dynamic>>('$_base/cameras/${Uri.encodeComponent(stream)}/video');
    return Uri.parse(ApiConfig.baseUrl + (res.data!['url'] as String));
  }

  Future<RelayState> state() async {
    final res = await _client.dio.get<Map<String, dynamic>>('$_base/state');
    return RelayState.fromJson(res.data!);
  }

  Future<WeightReading> weight({required int bridge}) async {
    final res = await _client.dio.get<Map<String, dynamic>>('$_base/${bridge == 2 ? 'weight2' : 'weight'}');
    return WeightReading.fromJson(res.data!);
  }

  /// Open or close the gate or the barrier; the relay's state after it.
  Future<RelayState> set({required bool gate, required bool open}) async {
    final res = await _client.dio.post<Map<String, dynamic>>(
      '$_base/${gate ? 'gate' : 'barrier'}',
      data: {'open': open},
    );
    return RelayState.fromJson(res.data!);
  }
}
