import 'dart:async';

import 'package:flutter/services.dart';

/// Receives barcode scans from the native DataWedge intent receiver (MainActivity)
/// and exposes them as a broadcast stream. DataWedge is auto-provisioned on the
/// native side, so no per-device setup is needed.
class Scanner {
  Scanner._() {
    _channel.setMethodCallHandler((call) async {
      if (call.method == 'onScan') {
        final data = call.arguments as String?;
        if (data != null && data.isNotEmpty) _controller.add(data);
      }
    });
  }

  static final Scanner instance = Scanner._();

  static const MethodChannel _channel = MethodChannel('globeweigh/scanner');
  final StreamController<String> _controller = StreamController<String>.broadcast();

  /// Raw decoded scan strings (e.g. "ProdCd:…Weight:… kgUniqueCode:…").
  Stream<String> get scans => _controller.stream;

  /// All labelled fields a pallet barcode can carry.
  static const _fields = ['ProdCd:', 'ERRORd:', 'Weight:', 'UniqueCode:'];

  /// Pulls one labelled field out of a raw pallet scan
  /// ("ProdCd:…Weight:… kgUniqueCode:…"). [keys] are the labels accepted for that
  /// field — the product code prints as either "ProdCd:" or "ERRORd:".
  static String? field(String input, List<String> keys) {
    final lower = input.toLowerCase();
    for (final key in keys) {
      final start = lower.indexOf(key.toLowerCase());
      if (start < 0) continue;
      final from = start + key.length;
      var end = input.length;
      for (final other in _fields) {
        if (other.toLowerCase() == key.toLowerCase()) continue;
        final p = lower.indexOf(other.toLowerCase(), from);
        if (p >= 0 && p < end) end = p;
      }
      final v = input.substring(from, end).trim();
      if (v.isNotEmpty) return v;
    }
    return null;
  }

  /// Plays a distinct error tone (for a bad scan — duplicate, wrong product, etc.).
  Future<void> playError() async {
    try {
      await _channel.invokeMethod('playError');
    } catch (_) {
      // best-effort
    }
  }
}

