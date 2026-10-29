import 'dart:convert';
import 'dart:typed_data';

import 'package:http/http.dart' as http;
import 'package:http_parser/http_parser.dart';

import '../config.dart';
import '../models/complete_load_result.dart';
import '../models/pending_load.dart';
import '../models/pending_load_detail.dart';
import '../models/pick_item.dart';
import '../models/pick_result.dart';
import '../models/touch_user.dart';
import '../models/update_info.dart';
import '../models/weight_reading.dart';

/// Thrown for any non-success response or transport error.
class ApiException implements Exception {
  final String message;
  ApiException(this.message);
  @override
  String toString() => message;
}

/// Thin client over Globeweigh.Api.
class ApiClient {
  final http.Client _client;
  static const _timeout = Duration(seconds: 10);

  ApiClient([http.Client? client]) : _client = client ?? http.Client();

  Uri _uri(String path) => Uri.parse('${AppConfig.apiBaseUrl}$path');

  /// The `message` an API error body carries, or null if it hasn't got one.
  static String? _messageFrom(String body) {
    try {
      final json = jsonDecode(body);
      if (json is Map<String, dynamic>) {
        final message = json['message'];
        if (message is String && message.trim().isNotEmpty) return message.trim();
      }
    } catch (_) {
      // Not JSON — nothing to report beyond the status code.
    }
    return null;
  }

  /// Registers/refreshes this device on the server and returns any queued update
  /// (PendingUpdate true → offer the update on the home screen). The server records
  /// the caller's IP automatically.
  Future<UpdateInfo> registerDevice({
    required String deviceName,
    String? applicationName,
    String? currentVersion,
    // The Shorebird patch this run is on (null: bare release), so Device Manager can
    // tell which tablets have taken a patch — the version alone never changes.
    int? currentPatch,
  }) async {
    final res = await _client
        .post(
          _uri('/api/devices/register'),
          headers: {'Content-Type': 'application/json'},
          body: jsonEncode({
            'deviceName': deviceName,
            'applicationName': applicationName,
            'currentVersion': currentVersion,
            'currentPatch': currentPatch,
          }),
        )
        .timeout(_timeout);
    if (res.statusCode != 200) {
      throw ApiException('Device registration failed (HTTP ${res.statusCode})');
    }
    return UpdateInfo.fromJson(jsonDecode(res.body) as Map<String, dynamic>);
  }

  /// Loads the Pending Loads list (the dispatch "Pending" weighbridge records).
  Future<List<PendingLoad>> getPending() async {
    try {
      final res = await _client
          .get(_uri('/api/picking/pending'))
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException(
          'Could not load pending loads (HTTP ${res.statusCode})',
        );
      }
      final list = jsonDecode(res.body) as List<dynamic>;
      return list
          .map((e) => PendingLoad.fromJson(e as Map<String, dynamic>))
          .toList();
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Loads a pending load's detail (totals + orders) by weighbridge record id.
  Future<PendingLoadDetail> getPendingDetail(int recordId) async {
    try {
      final res = await _client
          .get(_uri('/api/picking/pending/$recordId/detail'))
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException('Could not load detail (HTTP ${res.statusCode})');
      }
      return PendingLoadDetail.fromJson(
        jsonDecode(res.body) as Map<String, dynamic>,
      );
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Loads a load's pick list — pallets grouped by product & order, with counts.
  Future<List<PickItem>> getPickList(int recordId) async {
    try {
      final res = await _client
          .get(_uri('/api/picking/pending/$recordId/picklist'))
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException('Could not load pick list (HTTP ${res.statusCode})');
      }
      final list = jsonDecode(res.body) as List<dynamic>;
      return list
          .map((e) => PickItem.fromJson(e as Map<String, dynamic>))
          .toList();
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Loads the pallets already picked onto a load (to restore progress on (re)open).
  Future<List<PickedPallet>> getPicked(int recordId) async {
    try {
      final res = await _client
          .get(_uri('/api/picking/pending/$recordId/picked'))
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException(
          'Could not load picked pallets (HTTP ${res.statusCode})',
        );
      }
      final list = jsonDecode(res.body) as List<dynamic>;
      return list
          .map((e) => PickedPallet.fromJson(e as Map<String, dynamic>))
          .toList();
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Records one scanned pallet against a load. The server matches the product code to an
  /// order line, detects duplicates, persists the pick (DispatchOrder_Picked), and returns
  /// the outcome. Throws [ApiException] on transport failure (caller should re-tone).
  Future<PickResult> pickPallet({
    required int recordId,
    required String prodCode,
    required String uniqueCode,
    required int orderId,
    int? userId,
    bool allowOverPick = false,
  }) async {
    try {
      final res = await _client
          .post(
            _uri('/api/picking/pick'),
            headers: {'Content-Type': 'application/json'},
            body: jsonEncode({
              'recordId': recordId,
              'prodCode': prodCode,
              'uniqueCode': uniqueCode,
              // The order the picker has open — the pallet may only land on that one.
              'orderId': orderId,
              'userId': userId,
              // Set once the picker has confirmed the "already fully picked" prompt.
              'allowOverPick': allowOverPick,
            }),
          )
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException('Pick failed (HTTP ${res.statusCode})');
      }
      return PickResult.fromJson(jsonDecode(res.body) as Map<String, dynamic>);
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Uploads one "Complete Load" photo (already compressed to JPEG) against a load.
  /// The server replaces any previous photo with the same description, so retrying a
  /// failed complete never duplicates photos.
  Future<void> uploadLoadPhoto({
    required int recordId,
    required String description,
    required Uint8List bytes,
  }) async {
    try {
      final request =
          http.MultipartRequest(
              'POST',
              _uri('/api/picking/pending/$recordId/photos'),
            )
            ..fields['description'] = description
            ..files.add(
              http.MultipartFile.fromBytes(
                'file',
                bytes,
                filename: 'photo.jpg',
                contentType: MediaType('image', 'jpeg'),
              ),
            );
      final streamed = await request.send().timeout(
        const Duration(seconds: 30),
      );
      final res = await http.Response.fromStream(streamed);
      if (res.statusCode != 200) {
        throw ApiException('Photo upload failed (HTTP ${res.statusCode})');
      }
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// The factory users shown on the login picker (same list as TauriTouch).
  Future<List<TouchUser>> getTouchUsers() async {
    try {
      final res = await _client
          .get(_uri('/api/auth/touch-users'))
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException('Could not load users (HTTP ${res.statusCode})');
      }
      final list = jsonDecode(res.body) as List<dynamic>;
      return list
          .map((e) => TouchUser.fromJson(e as Map<String, dynamic>))
          .toList();
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Touch login by user id + 4-digit passcode. Throws [ApiException] with the
  /// server's message ("Invalid passcode") on rejection.
  Future<TouchUser> touchLogin({
    required int userId,
    required String passcode,
  }) async {
    http.Response res;
    try {
      res = await _client
          .post(
            _uri('/api/auth/touch-login'),
            headers: {'Content-Type': 'application/json'},
            body: jsonEncode({'userId': userId, 'passcode': passcode}),
          )
          .timeout(_timeout);
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
    if (res.statusCode == 200) {
      return TouchUser.fromJson(jsonDecode(res.body) as Map<String, dynamic>);
    }
    String message = 'Invalid passcode';
    try {
      final body = jsonDecode(res.body) as Map<String, dynamic>;
      message = (body['message'] as String?) ?? message;
    } catch (_) {}
    throw ApiException(message);
  }

  /// Records the picker's trailer-number confirmation on a load. UserId is null
  /// until the app has a login (server stores it for later).
  Future<void> confirmTrailer({required int recordId, int? userId}) async {
    try {
      final res = await _client
          .post(
            _uri('/api/picking/pending/$recordId/confirm-trailer'),
            headers: {'Content-Type': 'application/json'},
            body: jsonEncode({'userId': userId}),
          )
          .timeout(_timeout);
      if (res.statusCode < 200 || res.statusCode >= 300) {
        throw ApiException('Confirm failed (HTTP ${res.statusCode})');
      }
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Corrects a load's trailer number — the picker answered NO and typed the trailer
  /// actually there. It does not confirm it: the picker still answers YES / NO.
  Future<void> changeTrailer({
    required int recordId,
    required String trailerNo,
    int? userId,
  }) async {
    try {
      final res = await _client
          .post(
            _uri('/api/picking/pending/$recordId/trailer'),
            headers: {'Content-Type': 'application/json'},
            body: jsonEncode({'userId': userId, 'trailerNo': trailerNo}),
          )
          .timeout(_timeout);
      if (res.statusCode < 200 || res.statusCode >= 300) {
        var message = 'Could not change trailer (HTTP ${res.statusCode})';
        try {
          final body = jsonDecode(res.body) as Map<String, dynamic>;
          message = (body['message'] as String?) ?? message;
        } catch (_) {}
        throw ApiException(message);
      }
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// The live weight from weighbridge [bridge] (1 or 2), or null when the reading
  /// can't be fetched. Callers should treat a non-live reading (see
  /// [WeightReading.isLive]) the same as null: show stale/disconnected.
  Future<WeightReading?> getLiveWeight({required int bridge}) async {
    try {
      final res = await _client
          .get(
            _uri(
              bridge == 1
                  ? '/api/gate-barrier/weight'
                  : '/api/gate-barrier/weight2',
            ),
          )
          .timeout(const Duration(seconds: 3));
      if (res.statusCode != 200) return null;
      return WeightReading.fromJson(
        jsonDecode(res.body) as Map<String, dynamic>,
      );
    } catch (_) {
      return null;
    }
  }

  /// Completes a load: captures the outbound weight, which dispatches the linked
  /// orders and moves the record Pending → Complete (same as TauriAdmin weigh-out).
  /// Completing also queues each order's delivery note for printing on the office
  /// printer; the result says how many were queued so the picker can be told.
  Future<CompleteLoadResult> completeLoad({
    required int recordId,
    required int weight,
    int? userId,
  }) async {
    try {
      final res = await _client
          .post(
            _uri('/api/picking/pending/$recordId/complete'),
            headers: {'Content-Type': 'application/json'},
            // Who completed it, for the audit trail of every order it dispatches.
            body: jsonEncode({'weight': weight, 'userId': userId}),
          )
          .timeout(_timeout);
      if (res.statusCode < 200 || res.statusCode >= 300) {
        // The server explains a refusal (e.g. an order still open) — pass that on rather
        // than a status code the picker can do nothing with.
        throw ApiException(
          _messageFrom(res.body) ?? 'Complete failed (HTTP ${res.statusCode})',
        );
      }
      return CompleteLoadResult.parse(res.body);
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Removes one already-picked pallet from an order line (the picker re-scanned it on the
  /// product detail page and confirmed, or selected a no-scan label). [pickedId] targets the
  /// exact pick row; [uniqueCode] is the fallback when it isn't known. The server soft-deletes
  /// the row; status 'ok' on success, 'notFound' when that pallet isn't picked on this line.
  Future<PickResult> deletePick({
    required int recordId,
    required int orderId,
    required int productId,
    required String uniqueCode,
    int pickedId = 0,
  }) async {
    try {
      final res = await _client
          .post(
            _uri('/api/picking/pick/delete'),
            headers: {'Content-Type': 'application/json'},
            body: jsonEncode({
              'recordId': recordId,
              'orderId': orderId,
              'productId': productId,
              'uniqueCode': uniqueCode,
              'pickedId': pickedId,
            }),
          )
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException('Could not remove pallet (HTTP ${res.statusCode})');
      }
      return PickResult.fromJson(jsonDecode(res.body) as Map<String, dynamic>);
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Records a manual "no-scan" pick against an order line — a pallet whose label wouldn't
  /// read. Increments the line's count (flagged IsNoScan server-side for reporting).
  Future<PickResult> addNoScanLabel({
    required int recordId,
    required int orderId,
    required int productId,
    int? userId,
    bool allowOverPick = false,
  }) async {
    try {
      final res = await _client
          .post(
            _uri('/api/picking/pick/noscan'),
            headers: {'Content-Type': 'application/json'},
            body: jsonEncode({
              'recordId': recordId,
              'orderId': orderId,
              'productId': productId,
              'userId': userId,
              // Confirmation to add it to a line that is already full.
              'allowOverPick': allowOverPick,
            }),
          )
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException(
          'Could not add no-scan label (HTTP ${res.statusCode})',
        );
      }
      return PickResult.fromJson(jsonDecode(res.body) as Map<String, dynamic>);
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Marks a cubic meter line (raw peat — sold by volume, never scanned) as picked, saving
  /// the [density] (kg/m³) the picker entered on the order line. The
  /// server records it as the line's one pick; undo it with [deletePick] and its pickedId.
  Future<PickResult> markPicked({
    required int recordId,
    required int orderId,
    required int productId,
    required double density,
    int? userId,
  }) async {
    try {
      final res = await _client
          .post(
            _uri('/api/picking/pick/mark'),
            headers: {'Content-Type': 'application/json'},
            body: jsonEncode({
              'recordId': recordId,
              'orderId': orderId,
              'productId': productId,
              'density': density,
              'userId': userId,
            }),
          )
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException('Could not mark as picked (HTTP ${res.statusCode})');
      }
      return PickResult.fromJson(jsonDecode(res.body) as Map<String, dynamic>);
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Picks (or, with [undo], un-picks) an order's POS board — tapped, never scanned.
  Future<PickResult> pickPos({
    required int recordId,
    required int orderId,
    int? userId,
    bool undo = false,
  }) async {
    try {
      final res = await _client
          .post(
            _uri(undo ? '/api/picking/pick/pos/undo' : '/api/picking/pick/pos'),
            headers: {'Content-Type': 'application/json'},
            body: jsonEncode({
              'recordId': recordId,
              'orderId': orderId,
              'userId': userId,
            }),
          )
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException('Could not update the POS board (HTTP ${res.statusCode})');
      }
      return PickResult.fromJson(jsonDecode(res.body) as Map<String, dynamic>);
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// Reopen an order the picker completed, so it takes pallets again — an order ended
  /// early because stock ran out, or a full one that needs a pallet corrected or added.
  Future<void> reopenOrderPicking({
    required int recordId,
    required int orderId,
    int? userId,
  }) async {
    try {
      final res = await _client
          .post(
            _uri('/api/picking/pending/$recordId/orders/$orderId/reopen-picking'),
            headers: {'Content-Type': 'application/json'},
            // Who reopened it, for the order's audit trail — the same as completing it.
            body: jsonEncode({'userId': userId}),
          )
          .timeout(_timeout);
      if (res.statusCode != 200) {
        throw ApiException('Could not reopen the order (HTTP ${res.statusCode})');
      }
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }

  /// DEV ONLY — a real, unpicked pallet for an order line, so the dev "Auto Pick" button
  /// can feed a simulated label through the ordinary scan path. Returns null when the dev
  /// endpoint isn't there (a production server 404s it) or nothing is in stock.
  Future<Map<String, dynamic>?> devNextPallet({
    required int recordId,
    required int orderId,
    required int productId,
  }) async {
    try {
      final res = await _client
          .get(_uri('/api/picking/dev/next-pallet'
              '?recordId=$recordId&orderId=$orderId&productId=$productId'))
          .timeout(_timeout);
      if (res.statusCode != 200) return null;
      return jsonDecode(res.body) as Map<String, dynamic>;
    } catch (_) {
      return null;
    }
  }

  /// "Complete Early" — finish an order's picking with pallets still outstanding. The
  /// server records who completed it and flags the shortfall, and the order then takes
  /// no more pallets, so the picker can start the next one.
  Future<void> completeOrderPicking({
    required int recordId,
    required int orderId,
    int? userId,
  }) async {
    try {
      final res = await _client
          .post(
            _uri(
              '/api/picking/pending/$recordId/orders/$orderId/complete-picking',
            ),
            headers: {'Content-Type': 'application/json'},
            body: jsonEncode({'userId': userId}),
          )
          .timeout(_timeout);
      if (res.statusCode < 200 || res.statusCode >= 300) {
        throw ApiException(
          'Could not complete the order (HTTP ${res.statusCode})',
        );
      }
    } on ApiException {
      rethrow;
    } catch (e) {
      throw ApiException('Cannot reach server. Check WiFi/connection.');
    }
  }
}
