import 'dart:async';

import 'package:flutter/foundation.dart' show kDebugMode;
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../api/api_client.dart';
import '../format.dart';
import '../models/pending_order.dart';
import '../models/pick_item.dart';
import '../models/pick_result.dart';
import '../models/scanned_pallet.dart';
import '../services/scanner.dart';
import '../services/session.dart';
import '../theme/app_colors.dart';
import 'product_pallets_screen.dart';

/// Picking List for a load — a thin rail of the load's orders on the left, and the
/// selected order's product lines (pallet count each) beside it, plus picking progress
/// (overall % and per line). A load with a single order selects it automatically.
/// Pallets are scanned (DataMatrix → ProdCd/Weight/UniqueCode); each scan matches a
/// product line by GTIN and increments its picked count. Re-scanning the same pallet
/// (UniqueCode) pops a warning.
class PickingListScreen extends StatefulWidget {
  final int recordId;
  final String? haulierName;

  /// The trailer's weigh-in (Kg) off the weighbridge record — the base the picked pallets
  /// sit on top of for the load's gross weight.
  final int weightIn;

  const PickingListScreen({
    super.key,
    required this.recordId,
    this.haulierName,
    this.weightIn = 0,
  });

  @override
  State<PickingListScreen> createState() => _PickingListScreenState();
}

enum _ScanStatus {
  ok,
  duplicate,
  notOnLoad,
  notOnOrder,
  orderComplete,
  readOnly,
  alreadyComplete,
  unreadable,
  error,
}

class _PickingListScreenState extends State<PickingListScreen> {
  final _api = ApiClient();
  List<PickItem>? _rows;
  String? _error;
  bool _loading = false;

  // The load's orders (left rail) and which one's lines are listed. Null selection means
  // "nothing chosen yet" — only possible on a multi-order load.
  List<PendingOrder>? _orders;
  int? _selectedOrderId;

  // Picked pallets per line (key = order:product) and the set of UniqueCodes seen
  // (for duplicate detection). Session-only for now.
  final Map<String, List<ScannedPallet>> _picked = {};
  final Set<String> _scanned = {};
  // Bumped on each successful pick so an open product-detail page refreshes live.
  final ValueNotifier<int> _tick = ValueNotifier<int>(0);

  // Scans arrive from the native DataWedge intent receiver (primary). A KeyboardListener
  // is kept as a fallback for devices/profiles that emit keystrokes; both route through
  // _handleScan, which drops an identical repeat within 800ms so the two can't double-fire.
  StreamSubscription<String>? _scanSub;
  String? _lastRaw;
  DateTime? _lastRawAt;
  bool _dialogOpen = false;
  // The per-product detail page is on top — it owns the scanner while open (scans there
  // remove a pallet from the pick rather than adding one).
  bool _detailOpen = false;
  // A pick POST is in flight — drop further scans until it returns (single source of truth
  // is the server, and the scanner can't physically read two pallets within a LAN round-trip).
  bool _busy = false;
  // A dev Auto Pick round-trip is in flight (fetch a pallet, then scan it).
  bool _autoPicking = false;

  final _scanFocus = FocusNode();
  final StringBuffer _scanBuf = StringBuffer();

  // Last scan outcome, shown on the right panel.
  _ScanStatus? _status;
  String? _statusTitle;
  String? _statusSub;
  PickItem? _statusLine;

  String _key(PickItem r) => '${r.orderId}:${r.productId}';
  List<ScannedPallet> _palletsFor(PickItem r) => _picked[_key(r)] ?? const [];
  int _pickedFor(PickItem r) => _picked[_key(r)]?.length ?? 0;
  bool _isComplete(PickItem r) => _pickedFor(r) >= r.palletCount;

  int _totalTarget() => _rows?.fold<int>(0, (s, r) => s + r.palletCount) ?? 0;
  int _totalPicked() => _rows?.fold<int>(0, (s, r) => s + _pickedFor(r)) ?? 0;

  // Lines of the selected order only — the list panel shows nothing until one is picked.
  List<PickItem> _visibleRows() {
    final sel = _selectedOrderId;
    if (sel == null) return const [];
    return (_rows ?? const <PickItem>[])
        .where((r) => r.orderId == sel)
        .toList();
  }

  Iterable<PickItem> _rowsOf(int orderId) =>
      (_rows ?? const <PickItem>[]).where((r) => r.orderId == orderId);
  int _orderTarget(int orderId) =>
      _rowsOf(orderId).fold<int>(0, (s, r) => s + r.palletCount);
  int _orderPicked(int orderId) =>
      _rowsOf(orderId).fold<int>(0, (s, r) => s + _pickedFor(r));

  // An order is finished only when the picker has ended it — with every pallet on
  // ("Complete order") or short ("Complete early"). Reaching the pallet count is not the
  // end in itself: the order stays open so it can still be over-picked, and it holds the
  // picker until they close it.
  bool _isOrderComplete(PendingOrder o) => o.pickingComplete;

  // Everything ordered is on (or more) — the point at which ending the order is the
  // ordinary last step rather than an early one.
  bool _isOrderFull(PendingOrder o) {
    final target = _orderTarget(o.id);
    return target > 0 && _orderPicked(o.id) >= target;
  }

  // Mirror a completion decision the server has made into the rail.
  // [early] left null keeps whatever the order already recorded — used when the server
  // refuses a pick because the order is complete, where the app knows it is finished but
  // not whether it went short.
  void _setOrderComplete(int orderId, bool complete, {bool? early}) {
    final orders = _orders;
    if (orders == null) return;
    final i = orders.indexWhere((o) => o.id == orderId);
    if (i < 0) return;
    if (complete == orders[i].pickingComplete && early == null) return;
    setState(() {
      orders[i] = complete
          ? orders[i].copyWith(
              datePickingComplete: DateTime.now(),
              completedEarly: early,
            )
          : orders[i].copyWith(clearPickingComplete: true);
    });
  }

  static const Color _green = Color(0xFF16A34A);
  // Order rail — wide enough for "#12345" and a wrapped customer name, no wider.
  static const double _railWidth = 172;

  @override
  void initState() {
    super.initState();
    _load();
    _scanSub = Scanner.instance.scans.listen(_handleScan);
  }

  @override
  void dispose() {
    _scanSub?.cancel();
    _scanFocus.dispose();
    _tick.dispose();
    super.dispose();
  }

  // Single entry point for scans from either source; ignores an identical repeat within
  // 800ms (in case a device emits both an intent and a keystroke for the same scan).
  void _handleScan(String raw) {
    // The product detail page is open and handling scans itself (remove-a-pallet).
    if (_detailOpen) return;
    final text = raw.trim();
    if (text.isEmpty) return;
    final now = DateTime.now();
    if (_lastRaw == text &&
        _lastRawAt != null &&
        now.difference(_lastRawAt!) < const Duration(milliseconds: 800)) {
      return;
    }
    _lastRaw = text;
    _lastRawAt = now;

    // A blocking popup is up (e.g. duplicate pallet) — ignore the scan but re-sound
    // the error so the operator clears the popup before continuing.
    if (_dialogOpen) {
      Scanner.instance.playError();
      HapticFeedback.heavyImpact();
      return;
    }

    _processScan(text);
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final rows = await _api.getPickList(widget.recordId);
      if (!mounted) return;
      setState(() {
        _rows = rows;
        _error = null;
        _loading = false;
      });
      await _loadOrders(rows);
      await _loadPicked();
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = '$e';
        _loading = false;
      });
    }
    _focusScanner();
  }

  // The load's orders, for the left rail — the server returns them in loading order.
  // If the detail call fails we fall back to the orders the pick lines reference (order
  // number only, no customer) so the rail — and with it the whole screen — still works.
  Future<void> _loadOrders(List<PickItem> rows) async {
    List<PendingOrder> orders;
    try {
      final detail = await _api.getPendingDetail(widget.recordId);
      orders = detail.orders;
    } catch (_) {
      final byId = <int, int>{};
      for (final r in rows) {
        byId.putIfAbsent(r.orderId, () => r.orderNo);
      }
      // No detail call, so no loading order to show — number them as they arrive.
      final entries = byId.entries.toList();
      orders = [
        for (var i = 0; i < entries.length; i++)
          PendingOrder(
            id: entries[i].key,
            orderNo: entries[i].value,
            pickedQuantity: 0,
            totalQuantityOrdered: 0,
            loadingOrder: i + 1,
          ),
      ];
    }
    if (!mounted) return;
    setState(() {
      _orders = orders;
      // One order means there's nothing to choose — select it. Otherwise keep the current
      // selection across a refresh, and drop it if that order is no longer on the load.
      if (orders.length == 1) {
        _selectedOrderId = orders.first.id;
      } else if (!orders.any((o) => o.id == _selectedOrderId)) {
        _selectedOrderId = null;
      }
    });
  }

  // What the selected order's picks weigh, and how many of those weights are estimates —
  // read off the local progress, so both follow a pick or an un-pick immediately. With no
  // order chosen they cover the whole load, matching the header's stand-in totals.
  double _weightShown() {
    final selected = _selectedOrder();
    final pallets = selected == null
        ? _picked.values.expand((l) => l)
        : _rowsOf(selected.id).expand(_palletsFor);
    return pallets.fold<double>(0, (s, p) => s + p.weight);
  }

  int _estimatedShown() {
    final selected = _selectedOrder();
    final pallets = selected == null
        ? _picked.values.expand((l) => l)
        : _rowsOf(selected.id).expand(_palletsFor);
    return pallets.where((p) => p.isWeightEstimated).length;
  }

  // Every pick on the whole load (all orders), estimates included — the pallets' share
  // of the load's gross weight, whichever order is open.
  double _loadPickedWeight() =>
      _picked.values.expand((l) => l).fold<double>(0, (s, p) => s + p.weight);

  // Restore progress from the server: the pallets already picked onto this load. Makes the
  // counts, per-line progress and the duplicate set survive app restarts / other devices.
  Future<void> _loadPicked() async {
    try {
      final picked = await _api.getPicked(widget.recordId);
      if (!mounted) return;
      _picked.clear();
      _scanned.clear();
      // GET is newest-first; insert oldest-first so the detail page (which reverses) shows
      // newest at the top, matching live scans.
      for (final p in picked.reversed) {
        final line = _lineFor(p.orderId, p.productId);
        _picked
            .putIfAbsent('${p.orderId}:${p.productId}', () => [])
            .add(
              ScannedPallet(
                pickedId: p.id,
                uniqueCode: p.palletId,
                productCode: p.isNoScan ? '' : _stripZeros(line?.gtin),
                weight: p.weight,
                pickedAt: p.pickedAt,
                isNoScan: p.isNoScan,
                isWeightEstimated: p.isWeightEstimated,
              ),
            );
        // Empty PalletId = no-scan; don't add it to the duplicate set.
        if (p.palletId.isNotEmpty) _scanned.add(p.palletId);
      }
      setState(() {});
      _tick.value++;
    } catch (_) {
      // Non-fatal: progress just starts from zero if the picked list can't load.
    }
  }

  PickItem? _lineFor(int? orderId, int? productId) {
    if (orderId == null || productId == null) return null;
    for (final r in _rows ?? const <PickItem>[]) {
      if (r.orderId == orderId && r.productId == productId) return r;
    }
    return null;
  }

  static String _stripZeros(String? code) =>
      (code ?? '').trim().replaceFirst(RegExp(r'^0+'), '');

  void _focusScanner() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _scanFocus.requestFocus();
    });
  }

  // Accumulate scanned characters; the trailing Enter ends a scan.
  void _onKey(KeyEvent e) {
    if (e is! KeyDownEvent) return;
    if (e.logicalKey == LogicalKeyboardKey.enter ||
        e.logicalKey == LogicalKeyboardKey.numpadEnter) {
      final raw = _scanBuf.toString().trim();
      _scanBuf.clear();
      if (raw.isNotEmpty) _handleScan(raw);
      return;
    }
    final ch = e.character;
    if (ch != null && ch.isNotEmpty) _scanBuf.write(ch);
  }

  Future<void> _processScan(String raw, {bool allowOverPick = false}) async {
    final prodCd = Scanner.field(raw, const ['ProdCd:', 'ERRORd:']);
    final uniqueCode = Scanner.field(raw, const ['UniqueCode:']);

    // Bad/old label format the device couldn't parse — never reaches the server.
    if (uniqueCode == null ||
        uniqueCode.isEmpty ||
        prodCd == null ||
        prodCd.isEmpty) {
      _setStatus(_ScanStatus.unreadable, 'Label not recognised', raw, null);
      return;
    }

    // One order at a time: nothing can be scanned until the picker has chosen which
    // order they are picking, and an order closed by hand takes no more pallets. An order
    // that is merely fully picked still accepts a scan — the server answers
    // 'alreadyComplete' and the picker is asked whether to over-pick the item.
    final selected = _selectedOrder();
    if (selected == null) {
      _setStatus(
        _ScanStatus.error,
        'Select an order first',
        'Choose the order you are picking',
        null,
      );
      return;
    }
    if (_isReadOnly(selected)) {
      final active = _activeOrder();
      _setStatus(
        _ScanStatus.readOnly,
        'Order #${selected.orderNo} is read only',
        'Finish order #${active?.orderNo ?? ''} first',
        null,
      );
      return;
    }
    if (_isOrderComplete(selected)) {
      _setStatus(
        _ScanStatus.orderComplete,
        'Order #${selected.orderNo} is completed',
        'Reopen it to pick against it again',
        null,
      );
      return;
    }

    if (_busy) return;
    _busy = true;
    try {
      final result = await _api.pickPallet(
        recordId: widget.recordId,
        prodCode: prodCd,
        uniqueCode: uniqueCode,
        orderId: selected.id,
        userId: Session.userId,
        allowOverPick: allowOverPick,
      );
      if (!mounted) return;
      _applyResult(result, prodCd, uniqueCode, raw);
    } on ApiException catch (e) {
      if (mounted) {
        _setStatus(_ScanStatus.error, 'Not recorded', e.message, null);
      }
    } finally {
      _busy = false;
    }
  }

  // Apply the server's decision. The pick is already persisted (DispatchOrder_Picked) when
  // status is 'ok'; we just mirror it into the in-memory progress + scanned set.
  void _applyResult(
    PickResult r,
    String prodCd,
    String uniqueCode,
    String raw,
  ) {
    final pallet = r.palletId ?? uniqueCode;
    switch (r.status) {
      case 'ok':
        final line = _lineFor(r.orderId, r.productId);
        setState(() {
          _picked
              .putIfAbsent('${r.orderId}:${r.productId}', () => [])
              .add(
                ScannedPallet(
                  pickedId: r.pickedId,
                  uniqueCode: pallet,
                  productCode: r.productCode ?? prodCd,
                  weight: r.weight,
                  pickedAt: r.pickedAt ?? DateTime.now(),
                  isWeightEstimated: r.weightEstimated,
                ),
              );
          _scanned.add(pallet);
          _status = _ScanStatus.ok;
          _statusTitle = r.productDescription ?? line?.productDescription;
          // Flag the allowed duplicate / deliberate over-pick so the picker can see what
          // was recorded, not just that something was.
          final order = 'Order #${r.orderNo ?? line?.orderNo ?? ''}';
          _statusSub = r.wasOverPick
              ? '$order  ·  over-picked'
              : r.wasDuplicate
              ? '$order  ·  duplicate code recorded'
              : order;
          _statusLine = line;
        });
        _tick.value++; // refresh any open product-detail page
        break;
      case 'duplicate':
        _setStatus(
          _ScanStatus.duplicate,
          'Pallet already scanned',
          'UniqueCode $pallet',
          null,
        );
        _showDuplicateDialog(pallet);
        break;
      case 'notOnLoad':
        _setStatus(
          _ScanStatus.notOnLoad,
          'Product not on this load',
          'ProdCd ${r.productCode ?? prodCd}',
          null,
        );
        break;
      case 'notOnOrder':
        // The pallet is on the load, just not on the order being picked — say which order
        // is open rather than implying the pallet is wrong.
        _setStatus(
          _ScanStatus.notOnOrder,
          'Not on order #${_selectedOrder()?.orderNo ?? ''}',
          r.productDescription ?? 'ProdCd ${r.productCode ?? prodCd}',
          null,
        );
        break;
      case 'orderComplete':
        // The server refuses any completed order; whether it went short is its own record.
        if (r.orderId != null) _setOrderComplete(r.orderId!, true);
        _setStatus(
          _ScanStatus.orderComplete,
          'Order #${r.orderNo ?? _selectedOrder()?.orderNo ?? ''} is complete',
          'Select the next order to carry on',
          null,
        );
        break;
      case 'alreadyComplete':
        final complete = _lineFor(r.orderId, r.productId);
        _setStatus(
          _ScanStatus.alreadyComplete,
          'All pallets already picked',
          r.productDescription,
          complete,
        );
        // Ask whether to take it anyway rather than leaving the picker holding a pallet.
        _showOverPickDialog(r, complete, raw);
        break;
      default:
        _setStatus(
          _ScanStatus.unreadable,
          'Label not recognised',
          pallet,
          null,
        );
    }
  }

  // Called for every bad scan (duplicate, wrong product, etc.) — error tone + buzz.
  void _setStatus(_ScanStatus s, String? title, String? sub, PickItem? line) {
    setState(() {
      _status = s;
      _statusTitle = title;
      _statusSub = sub;
      _statusLine = line;
    });
    Scanner.instance.playError();
    HapticFeedback.heavyImpact();
  }

  void _showDuplicateDialog(String uniqueCode) {
    _dialogOpen = true;
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        icon: const Icon(
          Icons.error_outline,
          color: AppColors.warning,
          size: 40,
        ),
        title: const Text(
          'Pallet already scanned',
          style: TextStyle(fontFamily: 'Poppins', fontWeight: FontWeight.w700),
        ),
        content: Text(
          'This pallet (UniqueCode $uniqueCode) has already been picked.',
          textAlign: TextAlign.center,
          style: const TextStyle(fontFamily: 'Poppins'),
        ),
        actions: [
          TextButton(
            onPressed: () {
              Navigator.of(ctx).pop();
              _focusScanner();
            },
            child: const Text('OK'),
          ),
        ],
      ),
    ).then((_) => _dialogOpen = false);
  }

  // The scanned pallet's line is already fully picked. Rather than just refusing it —
  // which leaves a picker holding a pallet with nowhere to put it — take over the whole
  // screen and ask. Confirming repeats the same scan with the over-pick allowed, so the
  // pallet is recorded and the line reads over its ordered count.
  void _showOverPickDialog(PickResult r, PickItem? line, String raw) {
    _showOverPickConfirm(
      product: r.productDescription ?? line?.productDescription ?? 'This product',
      picked: line != null ? _pickedFor(line) : r.pickedForLine,
      target: line?.palletCount ?? r.targetForLine,
      orderNo: r.orderNo ?? line?.orderNo,
      // Same label, this time with the picker's go-ahead.
      onConfirm: () => _processScan(raw, allowOverPick: true),
    );
  }

  // Full-screen "this item is already full — take it anyway?" question. Both ways of
  // adding a pallet come through here, so a scan and a no-scan label ask the same thing.
  void _showOverPickConfirm({
    required String product,
    required int picked,
    required int target,
    required int? orderNo,
    required VoidCallback onConfirm,
  }) {
    _dialogOpen = true;
    showDialog<void>(
      context: context,
      barrierDismissible: false,
      builder: (ctx) => Dialog.fullscreen(
        backgroundColor: AppColors.background,
        child: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                const Icon(
                  Icons.warning_amber_rounded,
                  color: AppColors.warning,
                  size: 84,
                ),
                const SizedBox(height: 18),
                const Text(
                  'Order item fully picked',
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 28,
                    fontWeight: FontWeight.w700,
                    color: AppColors.textDark,
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  product,
                  textAlign: TextAlign.center,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 19,
                    fontWeight: FontWeight.w600,
                    color: AppColors.textDark,
                  ),
                ),
                const SizedBox(height: 8),
                Text(
                  '$picked of $target pallets already picked'
                  '${orderNo != null ? '  ·  Order #$orderNo' : ''}',
                  textAlign: TextAlign.center,
                  style: const TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 15,
                    fontWeight: FontWeight.w500,
                    color: AppColors.textLight,
                  ),
                ),
                const SizedBox(height: 26),
                const Text(
                  'Do you want to over-pick this item and add this pallet anyway?',
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 17,
                    fontWeight: FontWeight.w600,
                    color: AppColors.textDark,
                    height: 1.35,
                  ),
                ),
                const SizedBox(height: 30),
                Row(
                  children: [
                    Expanded(
                      child: OutlinedButton(
                        onPressed: () {
                          Navigator.of(ctx).pop();
                          _focusScanner();
                        },
                        style: OutlinedButton.styleFrom(
                          foregroundColor: AppColors.textDark,
                          side: const BorderSide(color: AppColors.borderLight),
                          minimumSize: const Size.fromHeight(58),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(12),
                          ),
                          textStyle: const TextStyle(
                            fontFamily: 'Poppins',
                            fontSize: 17,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        child: const Text('No, cancel'),
                      ),
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: FilledButton(
                        onPressed: () {
                          Navigator.of(ctx).pop();
                          onConfirm();
                        },
                        style: FilledButton.styleFrom(
                          backgroundColor: AppColors.warning,
                          minimumSize: const Size.fromHeight(58),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(12),
                          ),
                          textStyle: const TextStyle(
                            fontFamily: 'Poppins',
                            fontSize: 17,
                            fontWeight: FontWeight.w700,
                          ),
                        ),
                        child: const Text('Yes, over-pick'),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    ).then((_) => _dialogOpen = false);
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      // No app bar — the screen is tight for rows, so Back and Refresh live in the pick
      // list panel's own header instead and the whole height goes to the lists.
      body: SafeArea(
        child: KeyboardListener(
          focusNode: _scanFocus,
          autofocus: true,
          onKeyEvent: _onKey,
          child: Padding(
            padding: const EdgeInsets.all(12),
            // The order rail stays live down the left; everything right of it is one
            // region, so a completed order is covered by a single overlay.
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SizedBox(width: _railWidth, child: _orderRail()),
                const SizedBox(width: 12),
                Expanded(
                  child: Stack(
                    children: [
                      Positioned.fill(child: _workArea()),
                      // A finished order: cover its items, comments and scan panel —
                      // the whole working area — leaving the rail to move on with.
                      if (_selectedOrderComplete())
                        Positioned.fill(child: _completedOverlay()),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  // Everything the picker works in: the selected order's lines, the load's gross weight,
  // its picking comments and the scan panel. Landscape puts the gross weight/comments/scan
  // in a column beside the lines; portrait stacks them underneath.
  Widget _workArea() {
    return MediaQuery.of(context).orientation == Orientation.landscape
        ? Row(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Expanded(child: _listPanel()),
              const SizedBox(width: 12),
              SizedBox(
                width: 280,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    _grossWeightPanel(),
                    const SizedBox(height: 12),
                    Expanded(flex: 2, child: _commentsPanel()),
                    const SizedBox(height: 12),
                    Expanded(flex: 3, child: _scanPanel(vertical: true)),
                  ],
                ),
              ),
            ],
          )
        : Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Expanded(child: _listPanel()),
              const SizedBox(height: 12),
              _grossWeightPanel(),
              const SizedBox(height: 12),
              SizedBox(height: 92, child: _commentsPanel()),
              const SizedBox(height: 12),
              SizedBox(height: 132, child: _scanPanel()),
            ],
          );
  }

  // Thin left rail of the load's orders — order number + customer, with that order's
  // picking progress underneath. Tapping one lists its product lines beside it.
  Widget _orderRail() {
    final orders = _orders ?? const <PendingOrder>[];
    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.borderLight),
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(6, 8, 12, 12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // Back sits at the very top-left of the screen, where the app bar's used
                // to be, just left of the Orders heading.
                Row(
                  children: [
                    _headerButton(
                      icon: Icons.arrow_back,
                      tooltip: 'Back',
                      onPressed: () => Navigator.of(context).maybePop(),
                    ),
                    const SizedBox(width: 2),
                    Expanded(
                      child: Text(
                        orders.length > 1
                            ? 'Orders  ·  ${orders.length}'
                            : 'Order',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          fontFamily: 'Poppins',
                          fontSize: 15,
                          fontWeight: FontWeight.w700,
                          color: AppColors.textDark,
                        ),
                      ),
                    ),
                  ],
                ),
                // The rail is in loading order: 1 goes on the trailer first, so the
                // first delivery drop ends up last on and nearest the doors.
                if (orders.length > 1) ...[
                  const SizedBox(height: 2),
                  // Indented to line up under the heading, not under the Back button.
                  const Padding(
                    padding: EdgeInsets.only(left: 44),
                    child: Text(
                      'In loading order',
                      style: TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 11.5,
                        fontWeight: FontWeight.w600,
                        color: AppColors.warning,
                      ),
                    ),
                  ),
                ],
              ],
            ),
          ),
          const Divider(height: 1),
          Expanded(
            child: orders.isEmpty
                ? _centered(
                    _rows == null ? 'Loading…' : 'No orders',
                    AppColors.textLight,
                  )
                : ListView.separated(
                    padding: EdgeInsets.zero,
                    itemCount: orders.length,
                    separatorBuilder: (_, _) => const Divider(height: 1),
                    itemBuilder: (_, i) {
                      final o = orders[i];
                      return _OrderTile(
                        order: o,
                        selected: o.id == _selectedOrderId,
                        picked: _orderPicked(o.id),
                        target: _orderTarget(o.id),
                        complete: _isOrderComplete(o),
                        // Dimmed with a padlock when it can only be viewed — another
                        // order is under way. It still opens; it just takes no pallets.
                        locked: _isReadOnly(o),
                        completeColor: _green,
                        onTap: () => _selectOrder(o),
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }

  Widget _listPanel() {
    // The panel below lists one order, so its header counts that order too — the load's
    // own total would disagree with everything under it. Before an order is chosen there
    // is nothing else to show, so the load's figures stand in.
    final selected = _selectedOrder();
    final target = selected == null ? _totalTarget() : _orderTarget(selected.id);
    final picked = selected == null ? _totalPicked() : _orderPicked(selected.id);
    // Over-picked lines can push this past 1 — the bar is a bar, so cap it there.
    final pct = target > 0 ? (picked / target).clamp(0.0, 1.0) : 0.0;
    final done = target > 0 && picked >= target;
    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.borderLight),
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
                padding: const EdgeInsets.fromLTRB(16, 10, 8, 14),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    // Refresh lives on the title row now the app bar is gone (Back sits at
                    // the top of the order rail, the screen's true top-left).
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          child: _headerLines(picked, target, pct, done),
                        ),
                        const SizedBox(width: 6),
                        Column(
                          crossAxisAlignment: CrossAxisAlignment.end,
                          children: [
                            _headerButton(
                              icon: Icons.refresh,
                              tooltip: 'Refresh',
                              onPressed: _loading ? null : _load,
                            ),
                            // Dev-only: pick a pallet without a scanner to hand.
                            if (kDebugMode) _autoPickButton(),
                          ],
                        ),
                      ],
                    ),
                    if (_rows != null) ...[
                      const SizedBox(height: 10),
                      ClipRRect(
                        borderRadius: BorderRadius.circular(6),
                        child: LinearProgressIndicator(
                          value: pct,
                          minHeight: 8,
                          backgroundColor: AppColors.borderLight,
                          valueColor: AlwaysStoppedAnimation(
                            done ? _green : AppColors.primary,
                          ),
                        ),
                      ),
                    ],
                  ],
                ),
              ),
          const Divider(height: 1),
          Expanded(child: _body()),
        ],
      ),
    );
  }

  Widget _completedOverlay() {
    final order = _selectedOrder();
    final picked = order == null ? 0 : _orderPicked(order.id);
    final target = order == null ? 0 : _orderTarget(order.id);
    final early = order?.completedEarly ?? false;
    return Container(
      // Its own card: the overlay spans several panels with gutters between them, so it
      // needs to read as one sheet laid over them rather than a grey rectangle.
      decoration: BoxDecoration(
        color: AppColors.surface.withValues(alpha: 0.96),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.borderLight),
      ),
      padding: const EdgeInsets.all(24),
      child: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              early ? Icons.flag : Icons.check_circle,
              size: 56,
              color: early ? AppColors.warning : _green,
            ),
            const SizedBox(height: 14),
            Text(
              'Order #${order?.orderNo ?? ''} completed',
              textAlign: TextAlign.center,
              style: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 22,
                fontWeight: FontWeight.w700,
                color: AppColors.textDark,
              ),
            ),
            const SizedBox(height: 6),
            Text(
              early
                  ? 'Completed early  ·  $picked of $target pallets picked'
                  : '$picked of $target pallets picked',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontFamily: 'Poppins',
                fontSize: 14,
                fontWeight: FontWeight.w600,
                color: early ? AppColors.warning : AppColors.textLight,
              ),
            ),
            const SizedBox(height: 8),
            const Text(
              'No more pallets can be picked onto it.',
              textAlign: TextAlign.center,
              style: TextStyle(
                fontFamily: 'Poppins',
                fontSize: 13,
                fontWeight: FontWeight.w500,
                color: AppColors.textLight,
              ),
            ),
            const SizedBox(height: 22),
            FilledButton.icon(
              onPressed: _busy || order == null
                  ? null
                  : () => _reopenOrder(order),
              icon: const Icon(Icons.lock_open, size: 19),
              label: const Text('Reopen order'),
              style: FilledButton.styleFrom(
                backgroundColor: AppColors.primary,
                padding: const EdgeInsets.symmetric(
                  horizontal: 26,
                  vertical: 14,
                ),
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(12),
                ),
                textStyle: const TextStyle(
                  fontFamily: 'Poppins',
                  fontSize: 16,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  // Undo a completion so the order takes pallets again.
  Future<void> _reopenOrder(PendingOrder order) async {
    if (_busy) return;
    _busy = true;
    try {
      await _api.reopenOrderPicking(
        recordId: widget.recordId,
        orderId: order.id,
        userId: Session.userId,
      );
      if (!mounted) return;
      // Open again at whatever it is picked to — including a full one, which can then be
      // over-picked or corrected before being completed a second time.
      _setOrderComplete(order.id, false);
      setState(() {
        _status = null;
        _statusTitle = null;
        _statusSub = null;
        _statusLine = null;
      });
      HapticFeedback.selectionClick();
    } on ApiException catch (e) {
      if (mounted) {
        _setStatus(_ScanStatus.error, 'Not reopened', e.message, null);
      }
    } finally {
      _busy = false;
      _focusScanner();
    }
  }

  // DEV ONLY — "Auto Pick" picks the next outstanding pallet on the selected order
  // without a scanner: the server hands back a real unpicked pallet for the line and the
  // app feeds it through the ordinary scan path, so the whole flow is exercised.
  Widget _autoPickButton() {
    return Padding(
      padding: const EdgeInsets.only(top: 2),
      child: OutlinedButton.icon(
        onPressed: _busy || _autoPicking ? null : _autoPick,
        icon: const Icon(Icons.bolt, size: 15),
        label: const Text('Auto Pick'),
        style: OutlinedButton.styleFrom(
          foregroundColor: const Color(0xFF7C3AED),
          backgroundColor: const Color(0xFFF3E8FF),
          side: const BorderSide(color: Color(0xFF7C3AED)),
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
          minimumSize: const Size(0, 30),
          tapTargetSize: MaterialTapTargetSize.shrinkWrap,
          shape: RoundedRectangleBorder(
            borderRadius: BorderRadius.circular(10),
          ),
          textStyle: const TextStyle(
            fontFamily: 'Poppins',
            fontSize: 11.5,
            fontWeight: FontWeight.w600,
          ),
        ),
      ),
    );
  }

  // One press = one pallet, onto the first line of the selected order that still needs
  // one (so it works down the lines in the order they are listed). With every line full it
  // keeps behaving like a scan — it presents a pallet for the first item, which the server
  // answers with 'alreadyComplete' and the picker gets the over-pick prompt.
  Future<void> _autoPick() async {
    final order = _selectedOrder();
    if (order == null) {
      _setStatus(
        _ScanStatus.error,
        'Select an order first',
        'Auto Pick works on the open order',
        null,
      );
      return;
    }
    if (_isReadOnly(order)) {
      final active = _activeOrder();
      _setStatus(
        _ScanStatus.readOnly,
        'Order #${order.orderNo} is read only',
        'Finish order #${active?.orderNo ?? ''} first',
        null,
      );
      return;
    }
    if (_isOrderComplete(order)) {
      _setStatus(
        _ScanStatus.orderComplete,
        'Order #${order.orderNo} is completed',
        'Reopen it to pick against it again',
        null,
      );
      return;
    }
    final rows = _visibleRows();
    if (rows.isEmpty) {
      _setStatus(
        _ScanStatus.error,
        'Nothing to pick',
        'This order has no items',
        null,
      );
      return;
    }
    // The first line still short of its count; failing that the first line of the order,
    // so a press on a full order over-picks the first item just as scanning it would.
    final line = rows.firstWhere((r) => !_isComplete(r), orElse: () => rows.first);

    setState(() => _autoPicking = true);
    try {
      final pallet = await _api.devNextPallet(
        recordId: widget.recordId,
        orderId: line.orderId,
        productId: line.productId,
      );
      if (!mounted) return;
      if (pallet == null) {
        _setStatus(
          _ScanStatus.error,
          'No pallet to auto pick',
          'Nothing unpicked in stock for ${line.productDescription ?? 'this product'}',
          null,
        );
        return;
      }
      // Same label the DataWedge scanner emits, so this runs the real scan path.
      _lastRaw = null; // bypass the identical-repeat guard
      _handleScan(
        'ProdCd:${pallet['prodCode']}'
        'Weight:${pallet['weight']} kg'
        'UniqueCode:${pallet['uniqueCode']}',
      );
    } finally {
      if (mounted) setState(() => _autoPicking = false);
    }
  }

  // Back / Refresh, sized for a gloved finger now that they carry the app bar's job.
  Widget _headerButton({
    required IconData icon,
    required String tooltip,
    required VoidCallback? onPressed,
  }) => IconButton(
    icon: Icon(icon, size: 24),
    tooltip: tooltip,
    onPressed: onPressed,
    color: AppColors.textDark,
    disabledColor: AppColors.placeholder,
    constraints: const BoxConstraints.tightFor(width: 42, height: 42),
    padding: EdgeInsets.zero,
    splashRadius: 24,
  );

  // Amber flag for picks whose weight is an estimate (a no-scan label, or a code with no
  // pallet record) — the server fills those from the product's other pallets, so the
  // calculated weight is close but not measured, and this says so.
  Widget _estimatedFlag() {
    final estimated = _estimatedShown();
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        const Icon(Icons.flag, size: 15, color: AppColors.warning),
        const SizedBox(width: 5),
        Text(
          'incl. $estimated estimated',
          maxLines: 1,
          softWrap: false,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            fontFamily: 'Poppins',
            fontSize: 13,
            fontWeight: FontWeight.w700,
            color: AppColors.warning,
          ),
        ),
      ],
    );
  }

  // Which order is on screen (with the haulier after it), its picking progress, and what
  // has been picked onto it so far.
  Widget _headerLines(int picked, int target, double pct, bool done) {
    final selected = _selectedOrder();
    final haulier = widget.haulierName;
    final title = selected == null
        ? haulier ?? 'Pick List'
        : 'Order #${selected.orderNo}${haulier == null ? '' : '  ·  $haulier'}';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          title,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(
            fontFamily: 'Poppins',
            fontSize: 18,
            fontWeight: FontWeight.w700,
            color: AppColors.textDark,
          ),
        ),
        const SizedBox(height: 2),
        Text(
          _rows == null
              ? 'Pallets to pick'
              : '$picked / $target pallets picked  ·  ${(pct * 100).round()}%',
          style: TextStyle(
            fontFamily: 'Poppins',
            fontSize: 13,
            fontWeight: FontWeight.w600,
            color: done ? _green : AppColors.textLight,
          ),
        ),
        // Weight of what's been picked onto the order so far, with any estimated weights
        // flagged beside it — the total is then close to true, but not measured.
        const SizedBox(height: 3),
        Row(
          children: [
            Flexible(
              child: Text.rich(
                maxLines: 1,
                softWrap: false,
                overflow: TextOverflow.ellipsis,
                TextSpan(
                  children: [
                    const TextSpan(
                      text: 'Weight (Calc) : ',
                      style: TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 13,
                        fontWeight: FontWeight.w500,
                        color: AppColors.textLight,
                      ),
                    ),
                    TextSpan(
                      text: _rows == null ? '…' : '${fmtKg(_weightShown())} Kg',
                      style: const TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 13,
                        fontWeight: FontWeight.w700,
                        color: AppColors.textDark,
                      ),
                    ),
                  ],
                ),
              ),
            ),
            if (_estimatedShown() > 0) ...[
              const SizedBox(width: 12),
              _estimatedFlag(),
            ],
          ],
        ),
      ],
    );
  }

  // Move to another order — only allowed once the current one is finished. This is the
  // rule the whole screen turns on: a pallet is matched to the order that is open, so an
  // order has to be closed off (fully picked, or "Complete Early") before the next starts.
  // Any order can be opened — a picker needs to be able to look at what else is on the
  // load. Only one can be *picked* onto at a time (see [_isReadOnly]); the others open
  // read-only, which the scan panel says plainly.
  void _selectOrder(PendingOrder o) {
    if (o.id == _selectedOrderId) return;
    setState(() {
      _selectedOrderId = o.id;
      // The previous order's scan result doesn't belong to this one.
      _status = null;
      _statusTitle = null;
      _statusSub = null;
      _statusLine = null;
    });
    _focusScanner();
  }

  // The order to be picked now: the first one on the rail — that is, in loading order —
  // that isn't finished. Orders go on the trailer in that sequence, so that is the one the
  // picker should be working, and it is what holds them to one order at a time.
  PendingOrder? _activeOrder() {
    for (final o in _orders ?? const <PendingOrder>[]) {
      if (!_isOrderComplete(o)) return o;
    }
    return null;
  }

  // View-only: a later order on the load, still waiting its turn. It opens so the picker
  // can see what's coming, but takes no pallets until the ones before it are finished.
  // A finished order is never read-only — it can still be over-picked, or reopened.
  bool _isReadOnly(PendingOrder o) {
    if (_isOrderComplete(o)) return false;
    final active = _activeOrder();
    return active != null && active.id != o.id;
  }

  bool _selectedOrderReadOnly() {
    final o = _selectedOrder();
    return o != null && _isReadOnly(o);
  }

  bool _selectedOrderComplete() {
    final o = _selectedOrder();
    return o != null && _isOrderComplete(o);
  }

  bool _selectedOrderFull() {
    final o = _selectedOrder();
    return o != null && _isOrderFull(o);
  }

  PendingOrder? _selectedOrder() {
    final id = _selectedOrderId;
    if (id == null) return null;
    for (final o in _orders ?? const <PendingOrder>[]) {
      if (o.id == id) return o;
    }
    return null;
  }

  // The load's gross weight as it stands: the trailer's weigh-in plus every pallet picked
  // onto the load so far (all orders, estimates included) — what the weighbridge should
  // read on the way out. Read off the local progress, so it moves with each pick. Kept to
  // one short strip above the comments: a title row with the figure flush right, and the
  // sum it came from underneath.
  Widget _grossWeightPanel() {
    final pallets = _rows == null ? null : _loadPickedWeight();
    final gross = pallets == null ? null : widget.weightIn + pallets;
    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.borderLight),
      ),
      padding: const EdgeInsets.fromLTRB(14, 8, 14, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            children: [
              const Icon(
                Icons.scale_outlined,
                size: 17,
                color: AppColors.textLight,
              ),
              const SizedBox(width: 7),
              const Expanded(
                child: Text(
                  'Gross Weight',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 13.5,
                    fontWeight: FontWeight.w700,
                    color: AppColors.textDark,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              Text(
                gross == null ? '…' : '${fmtKg(gross)} Kg',
                maxLines: 1,
                softWrap: false,
                style: const TextStyle(
                  fontFamily: 'Poppins',
                  fontSize: 16,
                  fontWeight: FontWeight.w700,
                  color: AppColors.textDark,
                ),
              ),
            ],
          ),
          const SizedBox(height: 1),
          Text(
            'Weigh in ${fmtKg(widget.weightIn.toDouble())} Kg'
            '  +  pallets ${pallets == null ? '…' : fmtKg(pallets)} Kg',
            maxLines: 1,
            softWrap: false,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              fontFamily: 'Poppins',
              fontSize: 11.5,
              fontWeight: FontWeight.w500,
              color: AppColors.textLight,
            ),
          ),
        ],
      ),
    );
  }

  // The selected order's picking instructions (typed on the order in Admin). Tinted
  // amber whenever there is something to read, so it can't be scrolled past.
  Widget _commentsPanel() {
    final order = _selectedOrder();
    final comments = (order?.pickingComments ?? '').trim();
    final has = comments.isNotEmpty;
    return Container(
      decoration: BoxDecoration(
        color: has
            ? AppColors.warning.withValues(alpha: 0.08)
            : AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: has ? AppColors.warning : AppColors.borderLight,
        ),
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(14, 9, 14, 9),
            child: Row(
              children: [
                Icon(
                  has ? Icons.sticky_note_2 : Icons.sticky_note_2_outlined,
                  size: 17,
                  color: has ? AppColors.warning : AppColors.textLight,
                ),
                const SizedBox(width: 7),
                const Expanded(
                  child: Text(
                    'Picking Comments',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 13.5,
                      fontWeight: FontWeight.w700,
                      color: AppColors.textDark,
                    ),
                  ),
                ),
                if (order != null)
                  Text(
                    '#${order.orderNo}',
                    style: const TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 12.5,
                      fontWeight: FontWeight.w600,
                      color: AppColors.textLight,
                    ),
                  ),
              ],
            ),
          ),
          const Divider(height: 1),
          Expanded(
            child: SingleChildScrollView(
              padding: const EdgeInsets.fromLTRB(14, 9, 14, 11),
              child: Text(
                order == null
                    ? 'Select an order'
                    : has
                    ? comments
                    : 'No picking comments',
                style: TextStyle(
                  fontFamily: 'Poppins',
                  fontSize: has ? 14.5 : 13,
                  fontWeight: has ? FontWeight.w600 : FontWeight.w500,
                  color: has ? AppColors.textDark : AppColors.textLight,
                  height: 1.3,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _scanPanel({bool vertical = false}) {
    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.borderLight),
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Expanded(
            child: _selectedOrderReadOnly()
                // Another order is under way — this one is open to look at only, and the
                // panel says so instead of inviting a scan it would refuse.
                ? _readOnlyPrompt(vertical: vertical)
                : vertical
                ? (_status == null
                      ? _scanPromptVertical()
                      : _scanResultVertical())
                : (_status == null ? _scanPrompt() : _scanResult()),
          ),
          // Fixed row along the bottom of the scan panel — the manual fallback for a pallet
          // whose label won't read, and the way out of an order that can't be finished.
          // No-scan stays available on a full order (an over-pick can be a no-scan pallet
          // too); completing early only makes sense while the order is still short. Both
          // go once the order is closed by hand.
          if (_visibleRows().isNotEmpty &&
              !_selectedOrderComplete() &&
              !_selectedOrderReadOnly()) ...[
            const Divider(height: 1),
            IntrinsicHeight(
              child: Row(
                children: [
                  Expanded(
                    child: TextButton.icon(
                      onPressed: _busy ? null : _openNoScanPicker,
                      icon: const Icon(Icons.note_add_outlined, size: 18),
                      // Two buttons share the narrow landscape panel — scale the label
                      // down rather than overflow it.
                      label: const FittedBox(
                        fit: BoxFit.scaleDown,
                        child: Text('Add no-scan label'),
                      ),
                      style: _panelButtonStyle(AppColors.primary),
                    ),
                  ),
                  const VerticalDivider(width: 1),
                  Expanded(
                    child: TextButton.icon(
                      onPressed: _busy ? null : _confirmComplete,
                      // Green once everything ordered is on: ending the order is then the
                      // ordinary last step, not the exception the amber flag warns of.
                      icon: Icon(
                        _selectedOrderFull()
                            ? Icons.check_circle_outline
                            : Icons.flag_outlined,
                        size: 18,
                      ),
                      label: FittedBox(
                        fit: BoxFit.scaleDown,
                        child: Text(
                          _selectedOrderFull()
                              ? 'Complete order'
                              : 'Complete early',
                        ),
                      ),
                      style: _panelButtonStyle(
                        _selectedOrderFull() ? _green : AppColors.warning,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }

  // Shown in place of the scan prompt while the selected order is only being viewed.
  Widget _readOnlyPrompt({required bool vertical}) {
    final order = _selectedOrder();
    final active = _activeOrder();
    final sub = active == null
        ? 'This order cannot be picked at the moment'
        : 'Order #${order?.orderNo ?? ''} can be viewed, not picked.\n'
              'Finish order #${active.orderNo} first.';
    final icon = Container(
      width: vertical ? 84 : 60,
      height: vertical ? 84 : 60,
      decoration: BoxDecoration(
        color: AppColors.textLight.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(vertical ? 22 : 16),
      ),
      child: Icon(
        Icons.visibility_outlined,
        color: AppColors.textLight,
        size: vertical ? 44 : 32,
      ),
    );
    final titleText = Text(
      'Read only',
      textAlign: vertical ? TextAlign.center : TextAlign.start,
      style: TextStyle(
        fontFamily: 'Poppins',
        fontSize: vertical ? 19 : 18,
        fontWeight: FontWeight.w700,
        color: AppColors.textDark,
      ),
    );
    final subText = Text(
      sub,
      textAlign: vertical ? TextAlign.center : TextAlign.start,
      style: const TextStyle(
        fontFamily: 'Poppins',
        fontSize: 13.5,
        fontWeight: FontWeight.w500,
        color: AppColors.textLight,
        height: 1.3,
      ),
    );

    return Padding(
      padding: EdgeInsets.all(vertical ? 20 : 16),
      child: vertical
          ? Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                icon,
                const SizedBox(height: 18),
                titleText,
                const SizedBox(height: 6),
                subText,
              ],
            )
          : Row(
              children: [
                icon,
                const SizedBox(width: 16),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: [titleText, const SizedBox(height: 4), subText],
                  ),
                ),
              ],
            ),
    );
  }

  // Full-height variant for the landscape right column — content stacked and centred.
  Widget _scanPromptVertical() {
    return Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Container(
            width: 84,
            height: 84,
            decoration: BoxDecoration(
              color: AppColors.primary.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(22),
            ),
            child: const Icon(
              Icons.qr_code_scanner,
              color: AppColors.primary,
              size: 44,
            ),
          ),
          const SizedBox(height: 18),
          const Text(
            'Scan a pallet',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 19,
              fontWeight: FontWeight.w700,
              color: AppColors.textDark,
            ),
          ),
          const SizedBox(height: 6),
          const Text(
            'Scan a pallet barcode to pick it',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 14,
              fontWeight: FontWeight.w500,
              color: AppColors.textLight,
            ),
          ),
        ],
      ),
    );
  }

  Widget _scanResultVertical() {
    final ok = _status == _ScanStatus.ok;
    final color = ok ? _green : AppColors.error;
    final icon = ok ? Icons.check_circle : Icons.error;
    return Padding(
      padding: const EdgeInsets.all(20),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Icon(icon, color: color, size: 72),
          const SizedBox(height: 16),
          Text(
            _statusTitle ?? '',
            textAlign: TextAlign.center,
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 18,
              fontWeight: FontWeight.w700,
              color: ok ? AppColors.textDark : color,
              height: 1.25,
            ),
          ),
          if (_statusSub != null) ...[
            const SizedBox(height: 6),
            Text(
              _statusSub!,
              textAlign: TextAlign.center,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 13.5,
                fontWeight: FontWeight.w500,
                color: AppColors.textLight,
              ),
            ),
          ],
          if (ok && _statusLine != null) ...[
            const SizedBox(height: 16),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 10),
              decoration: BoxDecoration(
                color: _green.withValues(alpha: 0.10),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Text(
                '${_pickedFor(_statusLine!)} / ${_statusLine!.palletCount}',
                style: const TextStyle(
                  fontFamily: 'Poppins',
                  fontSize: 20,
                  fontWeight: FontWeight.w700,
                  color: _green,
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _scanPrompt() {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 16),
      child: Row(
        children: [
          Container(
            width: 60,
            height: 60,
            decoration: BoxDecoration(
              color: AppColors.primary.withValues(alpha: 0.12),
              borderRadius: BorderRadius.circular(16),
            ),
            child: const Icon(
              Icons.qr_code_scanner,
              color: AppColors.primary,
              size: 32,
            ),
          ),
          const SizedBox(width: 16),
          const Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text(
                  'Scan a pallet',
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 18,
                    fontWeight: FontWeight.w700,
                    color: AppColors.textDark,
                  ),
                ),
                SizedBox(height: 4),
                Text(
                  'Scan a pallet barcode to pick it',
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 14,
                    fontWeight: FontWeight.w500,
                    color: AppColors.textLight,
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _scanResult() {
    final ok = _status == _ScanStatus.ok;
    final color = ok ? _green : AppColors.error;
    final icon = ok ? Icons.check_circle : Icons.error;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 14),
      child: Row(
        children: [
          Icon(icon, color: color, size: 52),
          const SizedBox(width: 16),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                Text(
                  _statusTitle ?? '',
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 18,
                    fontWeight: FontWeight.w700,
                    color: ok ? AppColors.textDark : color,
                    height: 1.2,
                  ),
                ),
                if (_statusSub != null) ...[
                  const SizedBox(height: 3),
                  Text(
                    _statusSub!,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 13.5,
                      fontWeight: FontWeight.w500,
                      color: AppColors.textLight,
                    ),
                  ),
                ],
              ],
            ),
          ),
          if (ok && _statusLine != null) ...[
            const SizedBox(width: 12),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
              decoration: BoxDecoration(
                color: _green.withValues(alpha: 0.10),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Text(
                '${_pickedFor(_statusLine!)} / ${_statusLine!.palletCount}',
                style: const TextStyle(
                  fontFamily: 'Poppins',
                  fontSize: 16,
                  fontWeight: FontWeight.w700,
                  color: _green,
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _body() {
    if (_error != null) return _centered(_error!, AppColors.error);
    if (_rows == null) return _centered('Loading…', AppColors.textLight);
    if (_rows!.isEmpty) {
      return _centered('Nothing to pick', AppColors.textLight);
    }
    if (_selectedOrderId == null) {
      return _centered('Select an order to see its items', AppColors.textLight);
    }
    final rows = _visibleRows();
    if (rows.isEmpty) {
      return _centered('Nothing to pick on this order', AppColors.textLight);
    }
    return ListView.separated(
      padding: EdgeInsets.zero,
      itemCount: rows.length,
      separatorBuilder: (_, _) => const Divider(height: 1),
      itemBuilder: (_, i) {
        final r = rows[i];
        return _PickRow(
          item: r,
          picked: _pickedFor(r),
          complete: _isComplete(r),
          completeColor: _green,
          // A cubic meter line has no pallets to open: tapping marks it picked (or offers
          // to undo that).
          onTap: () => r.isPos
              ? _onPosTap(r)
              : r.isCubicMeter
              ? _onCubicMeterTap(r)
              : _openProduct(r),
        );
      },
    );
  }

  // The order's POS board: a tap picks it; tapping a picked one asks before un-picking.
  Future<void> _onPosTap(PickItem line) async {
    final picked = _pickedFor(line) > 0;
    if (picked) {
      _dialogOpen = true;
      final yes = await showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: const Text(
            'Unpick the POS board?',
            textAlign: TextAlign.center,
            style: TextStyle(fontFamily: 'Poppins', fontWeight: FontWeight.w700),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(ctx).pop(false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.of(ctx).pop(true),
              style: FilledButton.styleFrom(backgroundColor: AppColors.warning),
              child: const Text('Unpick'),
            ),
          ],
        ),
      );
      _dialogOpen = false;
      if (yes != true || !mounted) {
        _focusScanner();
        return;
      }
    }
    if (_busy) return;
    _busy = true;
    try {
      final r = await _api.pickPos(
        recordId: widget.recordId,
        orderId: line.orderId,
        userId: Session.userId,
        undo: picked,
      );
      if (!mounted) return;
      if (r.isOk) {
        setState(() {
          if (picked) {
            _picked.remove(_key(line));
          } else {
            _picked[_key(line)] = [
              ScannedPallet(
                uniqueCode: '',
                productCode: '',
                pickedAt: r.pickedAt ?? DateTime.now(),
              ),
            ];
          }
          _status = _ScanStatus.ok;
          _statusTitle = picked ? 'POS board unpicked' : 'POS board picked';
          _statusSub = 'Order #${line.orderNo}';
          _statusLine = line;
        });
        HapticFeedback.selectionClick();
        _tick.value++;
      } else if (r.status == 'orderComplete') {
        _setOrderComplete(line.orderId, true);
        _setStatus(
          _ScanStatus.orderComplete,
          'Order #${line.orderNo} is complete',
          'Select the next order to carry on',
          null,
        );
      } else {
        _setStatus(_ScanStatus.error, 'POS board not updated', r.status, null);
      }
    } on ApiException catch (e) {
      if (mounted) _setStatus(_ScanStatus.error, 'POS board not updated', e.message, null);
    } finally {
      _busy = false;
      _focusScanner();
    }
  }

  // Raw peat, sold by the cubic meter: nothing is scanned, the picker just marks the line
  // picked. Tapping a marked line asks before undoing it.
  void _onCubicMeterTap(PickItem line) {
    final marks = _palletsFor(line);
    if (marks.isEmpty) {
      // The density goes on the order line first; saving it is what marks the line picked.
      _dialogOpen = true;
      showDialog<double>(
        context: context,
        barrierDismissible: false,
        builder: (_) => _DensityDialog(line: line),
      ).then((density) {
        _dialogOpen = false;
        if (density != null) _markPicked(line, density);
        _focusScanner();
      });
      return;
    }
    _dialogOpen = true;
    showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text(
          'Unmark as picked?',
          textAlign: TextAlign.center,
          style: TextStyle(fontFamily: 'Poppins', fontWeight: FontWeight.w700),
        ),
        content: Text(
          '${line.productDescription ?? 'This line'} (${line.quantity} m³) will go back to not picked.',
          textAlign: TextAlign.center,
          style: const TextStyle(fontFamily: 'Poppins'),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(ctx).pop(false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.of(ctx).pop(true),
            style: FilledButton.styleFrom(backgroundColor: AppColors.warning),
            child: const Text('Unmark'),
          ),
        ],
      ),
    ).then((yes) async {
      _dialogOpen = false;
      if (yes == true) {
        final err = await _deletePick(line, marks.first);
        if (err != null && mounted) {
          _setStatus(_ScanStatus.error, 'Not unmarked', err, null);
        } else if (mounted) {
          setState(() {
            _statusTitle = 'Unmarked as picked';
            _statusSub = 'Order #${line.orderNo}';
          });
        }
      }
      _focusScanner();
    });
  }

  Future<void> _markPicked(PickItem line, double density) async {
    if (_busy) return;
    _busy = true;
    try {
      final r = await _api.markPicked(
        recordId: widget.recordId,
        orderId: line.orderId,
        productId: line.productId,
        density: density,
        userId: Session.userId,
      );
      if (!mounted) return;
      if (r.isOk || r.status == 'alreadyComplete') {
        setState(() {
          final marks = _picked.putIfAbsent(_key(line), () => []);
          if (marks.isEmpty) {
            marks.add(
              ScannedPallet(
                pickedId: r.pickedId,
                uniqueCode: '',
                productCode: '',
                weight: 0,
                pickedAt: r.pickedAt ?? DateTime.now(),
              ),
            );
          }
          _status = _ScanStatus.ok;
          _statusTitle = line.productDescription;
          _statusSub = 'Marked as picked  ·  ${line.quantity} m³  ·  density ${density.toStringAsFixed(2)}';
          _statusLine = line;
        });
        HapticFeedback.selectionClick();
        _tick.value++;
      } else if (r.status == 'orderComplete') {
        _setOrderComplete(line.orderId, true);
        _setStatus(
          _ScanStatus.orderComplete,
          'Order #${line.orderNo} is complete',
          'Select the next order to carry on',
          null,
        );
      } else {
        _setStatus(_ScanStatus.error, 'Not marked', r.status, null);
      }
    } on ApiException catch (e) {
      if (mounted) _setStatus(_ScanStatus.error, 'Not marked', e.message, null);
    } finally {
      _busy = false;
    }
  }

  void _openProduct(PickItem line) {
    _detailOpen = true;
    Navigator.of(context)
        .push(
          MaterialPageRoute(
            builder: (_) => ProductPalletsScreen(
              line: line,
              refresh: _tick,
              pallets: () => _palletsFor(line),
              onDelete: (p) => _deletePick(line, p),
            ),
          ),
        )
        .then((_) {
          _detailOpen = false;
          _focusScanner();
        });
  }

  // Un-pick one pallet (from the product detail page). The server soft-deletes the pick row;
  // on success it's dropped from this line's progress here too. Returns null when removed,
  // otherwise the message to show on the detail page.
  Future<String?> _deletePick(PickItem line, ScannedPallet p) async {
    if (_busy) return 'Busy — try again';
    _busy = true;
    try {
      final r = await _api.deletePick(
        recordId: widget.recordId,
        orderId: line.orderId,
        productId: line.productId,
        uniqueCode: p.uniqueCode,
        pickedId: p.pickedId,
      );
      if (!r.isOk) {
        return r.status == 'notFound'
            ? 'That pallet is not picked on this line'
            : 'Could not remove pallet';
      }
      if (!mounted) return null;
      setState(() {
        _picked[_key(line)]?.remove(p);
        // The code stays "scanned" while any other line still holds a pick for it.
        final stillPicked = _picked.values.any(
          (l) => l.any((x) => x.uniqueCode == p.uniqueCode),
        );
        if (!stillPicked) _scanned.remove(p.uniqueCode);
        _status = _ScanStatus.ok;
        _statusTitle = p.isNoScan ? 'No-scan label removed' : 'Pallet removed';
        _statusSub = p.isNoScan
            ? 'Order #${line.orderNo}'
            : '${p.uniqueCode}  ·  Order #${line.orderNo}';
        _statusLine = line;
      });
      _tick.value++;
      return null;
    } on ApiException catch (e) {
      return e.message;
    } finally {
      _busy = false;
    }
  }

  ButtonStyle _panelButtonStyle(Color colour) => TextButton.styleFrom(
    foregroundColor: colour,
    minimumSize: const Size.fromHeight(42),
    padding: const EdgeInsets.symmetric(horizontal: 6),
    shape: const RoundedRectangleBorder(),
    textStyle: const TextStyle(
      fontFamily: 'Poppins',
      fontSize: 13,
      fontWeight: FontWeight.w600,
    ),
  );

  // End the order. Every order is finished by hand, so this is the ordinary last step
  // when everything is on — and the way out when it isn't, with the shortfall flagged for
  // reporting. Either way it is confirmed first, because it closes the order to pallets.
  void _confirmComplete() {
    final order = _selectedOrder();
    if (order == null) return;
    final picked = _orderPicked(order.id);
    final target = _orderTarget(order.id);
    final remaining = target - picked;
    final full = remaining <= 0;
    _dialogOpen = true;
    showDialog<void>(
      context: context,
      builder: (ctx) => AlertDialog(
        icon: Icon(
          full ? Icons.check_circle_outline : Icons.flag_outlined,
          color: full ? _green : AppColors.warning,
          size: 40,
        ),
        title: Text(
          full
              ? 'Complete order #${order.orderNo}?'
              : 'Complete order #${order.orderNo} early?',
          textAlign: TextAlign.center,
          style: const TextStyle(
            fontFamily: 'Poppins',
            fontWeight: FontWeight.w700,
          ),
        ),
        content: Text(
          full
              ? '$picked of $target pallets picked.\n'
                    'The order will be closed for picking.'
              : '$picked of $target pallets picked — $remaining short.\n'
                    'The order will be closed for picking and the shortfall recorded.',
          textAlign: TextAlign.center,
          style: const TextStyle(fontFamily: 'Poppins'),
        ),
        actions: [
          TextButton(
            onPressed: () {
              Navigator.of(ctx).pop();
              _focusScanner();
            },
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () {
              Navigator.of(ctx).pop();
              _completeOrder(order, early: !full);
            },
            style: FilledButton.styleFrom(
              backgroundColor: full ? _green : AppColors.warning,
            ),
            child: const Text('Complete order'),
          ),
        ],
      ),
    ).then((_) => _dialogOpen = false);
  }

  Future<void> _completeOrder(PendingOrder order, {required bool early}) async {
    if (_busy) return;
    _busy = true;
    try {
      await _api.completeOrderPicking(
        recordId: widget.recordId,
        orderId: order.id,
        userId: Session.userId,
      );
      if (!mounted) return;
      _setOrderComplete(order.id, true, early: early);
      setState(() {
        _status = _ScanStatus.orderComplete;
        _statusTitle = 'Order #${order.orderNo} completed';
        _statusSub = early
            ? 'Completed early — select the next order'
            : 'Select the next order to carry on';
        _statusLine = null;
      });
      HapticFeedback.selectionClick();
    } on ApiException catch (e) {
      if (mounted) {
        _setStatus(_ScanStatus.error, 'Not completed', e.message, null);
      }
    } finally {
      _busy = false;
      _focusScanner();
    }
  }

  // "Add no-scan label" — choose which product the unreadable pallet is for, then record a
  // manual pick against it (so the order can still complete; flagged for reporting).
  void _openNoScanPicker() {
    // Scoped to the order on screen — the picker is working one order at a time. Lines
    // that are already full are listed too: a pallet over and above the ordered count can
    // just as easily be one whose label won't read. Choosing one asks first.
    // Cubic meter lines have no labels to miss — they are marked picked from the list.
    final lines = _visibleRows().where((r) => !r.isTapToPick).toList();
    // Rows that fit the card's list area without scrolling: the card is 640 tall (the
    // ET40's landscape screen caps it a touch under that), less the title and Cancel
    // strips, leaves ~515 for the list, and a two-line ListTile plus its hairline is 73.
    // Past that the lines split into two columns and the card widens to suit.
    const perColumn = 7;
    final twoCols = lines.length > perColumn;
    // A centred popup, not a sheet sliding up from the bottom: it reads as a question the
    // picker answers, and it sits where the other prompts on this screen do. While it is
    // up a stray scan is rejected like any other blocking popup (see _onScan).
    _dialogOpen = true;
    // The chosen line comes back as the dialog's result; the pick (or the over-pick
    // question) is started only once the popup has fully closed, so the dialog flag is
    // handed over cleanly rather than cleared underneath the next popup.
    showDialog<PickItem>(
      context: context,
      builder: (ctx) => Dialog(
        backgroundColor: AppColors.surface,
        insetPadding: const EdgeInsets.symmetric(horizontal: 24, vertical: 24),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
        clipBehavior: Clip.antiAlias,
        child: ConstrainedBox(
          constraints: BoxConstraints(
            maxWidth: twoCols ? 960 : 680,
            maxHeight: 640,
          ),
          // The list is Expanded, so the card takes its full height whatever the order
          // holds — a two-line order gets the same popup as a ten-line one.
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              const Padding(
                padding: EdgeInsets.fromLTRB(20, 18, 20, 14),
                child: Text(
                  'Add no-scan label',
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 18,
                    fontWeight: FontWeight.w700,
                    color: AppColors.textDark,
                  ),
                ),
              ),
              const Divider(height: 1),
              if (lines.isEmpty)
                const Expanded(
                  child: Center(
                    child: Text(
                      'Nothing to pick on this order',
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 14,
                        color: AppColors.textLight,
                      ),
                    ),
                  ),
                )
              else
                Expanded(
                  // Up to perColumn rows sit in one column. Beyond that the lines split
                  // into two columns read down the first and then the second — a
                  // balanced split, so 8 lines is 4 + 4 — and only past two columns'
                  // worth does it scroll. The picker sees the whole order at a glance
                  // and taps; no scrolling for a hand in a glove.
                  child: twoCols
                      ? SingleChildScrollView(
                          child: IntrinsicHeight(
                            child: Row(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: [
                                Expanded(
                                  child: _noScanColumn(
                                    ctx,
                                    lines.sublist(0, (lines.length + 1) ~/ 2),
                                  ),
                                ),
                                const VerticalDivider(width: 1),
                                Expanded(
                                  child: _noScanColumn(
                                    ctx,
                                    lines.sublist((lines.length + 1) ~/ 2),
                                  ),
                                ),
                              ],
                            ),
                          ),
                        )
                      : ListView.separated(
                          padding: EdgeInsets.zero,
                          itemCount: lines.length,
                          separatorBuilder: (_, _) => const Divider(height: 1),
                          itemBuilder: (_, i) => _noScanTile(ctx, lines[i]),
                        ),
                ),
              const Divider(height: 1),
              // An explicit way out for a gloved hand — the barrier still works too.
              Padding(
                padding: const EdgeInsets.fromLTRB(12, 6, 12, 6),
                child: Align(
                  alignment: Alignment.centerRight,
                  child: TextButton(
                    onPressed: () => Navigator.of(ctx).pop(),
                    child: const Text('Cancel'),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    ).then((r) {
      _dialogOpen = false;
      if (r == null) {
        _focusScanner();
        return;
      }
      if (_isComplete(r)) {
        // The same question the scanner asks before going over count.
        _showOverPickConfirm(
          product: r.productDescription ?? 'This product',
          picked: _pickedFor(r),
          target: r.palletCount,
          orderNo: r.orderNo,
          onConfirm: () => _addNoScan(r, allowOverPick: true),
        );
      } else {
        _addNoScan(r);
      }
    });
  }

  // One column of the no-scan chooser: its lines top to bottom with hairlines between,
  // sized to its content (the two-column layout scrolls as a whole if it ever must).
  Widget _noScanColumn(BuildContext ctx, List<PickItem> items) => Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (var i = 0; i < items.length; i++) ...[
            if (i > 0) const Divider(height: 1),
            _noScanTile(ctx, items[i]),
          ],
        ],
      );

  // A product line in the no-scan chooser: description and progress, with a full line
  // flagged amber (choosing one asks about the over-pick first — see _openNoScanPicker).
  Widget _noScanTile(BuildContext ctx, PickItem r) {
    final full = _isComplete(r);
    return ListTile(
      title: Text(
        r.productDescription ?? 'Product ${r.productId}',
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: const TextStyle(
          fontFamily: 'Poppins',
          fontSize: 15,
          fontWeight: FontWeight.w600,
          color: AppColors.textDark,
        ),
      ),
      subtitle: Text(
        '${_pickedFor(r)} / ${r.palletCount}'
        '${full ? '  ·  fully picked' : ''}',
        style: TextStyle(
          fontFamily: 'Poppins',
          fontSize: 13,
          color: full ? AppColors.warning : AppColors.textLight,
        ),
      ),
      trailing: Icon(
        full ? Icons.warning_amber_rounded : Icons.add_circle_outline,
        color: full ? AppColors.warning : AppColors.primary,
      ),
      onTap: () => Navigator.of(ctx).pop(r),
    );
  }

  Future<void> _addNoScan(PickItem line, {bool allowOverPick = false}) async {
    if (_busy) return;
    _busy = true;
    try {
      final r = await _api.addNoScanLabel(
        recordId: widget.recordId,
        orderId: line.orderId,
        productId: line.productId,
        userId: Session.userId,
        allowOverPick: allowOverPick,
      );
      if (!mounted) return;
      if (r.isOk) {
        setState(() {
          _picked
              .putIfAbsent(_key(line), () => [])
              .add(
                ScannedPallet(
                  pickedId: r.pickedId,
                  uniqueCode: '',
                  productCode: '',
                  // The server's estimate from the product's other pallets.
                  weight: r.weight,
                  pickedAt: r.pickedAt ?? DateTime.now(),
                  isNoScan: true,
                  isWeightEstimated: true,
                ),
              );
          _status = _ScanStatus.ok;
          _statusTitle = line.productDescription;
          _statusSub = r.wasOverPick
              ? 'No-scan label added  ·  over-picked'
              : 'No-scan label added  ·  Order #${line.orderNo}';
          _statusLine = line;
        });
        HapticFeedback.selectionClick();
        _tick.value++;
        } else if (r.status == 'orderComplete') {
        _setOrderComplete(line.orderId, true);
        _setStatus(
          _ScanStatus.orderComplete,
          'Order #${line.orderNo} is complete',
          'Select the next order to carry on',
          null,
        );
      } else if (r.status == 'alreadyComplete') {
        _setStatus(
          _ScanStatus.alreadyComplete,
          'All pallets already picked',
          line.productDescription,
          line,
        );
      } else {
        _setStatus(_ScanStatus.error, 'Not added', r.status, null);
      }
    } on ApiException catch (e) {
      if (mounted) _setStatus(_ScanStatus.error, 'Not added', e.message, null);
    } finally {
      _busy = false;
    }
  }

  Widget _centered(String text, Color color) => Center(
    child: Padding(
      padding: const EdgeInsets.all(24),
      child: Text(
        text,
        textAlign: TextAlign.center,
        style: TextStyle(fontFamily: 'Poppins', fontSize: 14, color: color),
      ),
    ),
  );
}

/// One order in the left rail — number, customer, and how much of it is picked. The
/// selected order is tinted and carries an accent bar down its left edge.
class _OrderTile extends StatelessWidget {
  final PendingOrder order;
  final bool selected;
  final int picked;
  final int target;

  /// Picking finished — every pallet picked, or ended short via "Complete early".
  final bool complete;

  /// Can't be started yet: another order is still being picked.
  final bool locked;
  final Color completeColor;
  final VoidCallback onTap;

  const _OrderTile({
    required this.order,
    required this.selected,
    required this.picked,
    required this.target,
    required this.complete,
    required this.locked,
    required this.completeColor,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final done = complete;
    final accent = done ? completeColor : AppColors.primary;
    return InkWell(
      onTap: onTap,
      child: Opacity(
        // Locked orders stay readable but visibly out of reach.
        opacity: locked ? 0.45 : 1,
        child: Container(
          decoration: BoxDecoration(
            color: selected ? AppColors.primary.withValues(alpha: 0.08) : null,
            // Transparent when unselected so the text never shifts sideways.
            border: Border(
              left: BorderSide(
                color: selected ? accent : Colors.transparent,
                width: 3,
              ),
            ),
          ),
          padding: const EdgeInsets.fromLTRB(11, 12, 14, 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.center,
                children: [
                  // Loading order — the sequence this order goes on the trailer. Always
                  // orange, including on a completed order: it's a fixed instruction, not
                  // progress, so it must not read as a status colour.
                  if (order.loadingOrder > 0) ...[
                    Container(
                      width: 26,
                      height: 26,
                      alignment: Alignment.center,
                      decoration: BoxDecoration(
                        color: AppColors.warning,
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Text(
                        '${order.loadingOrder}',
                        style: const TextStyle(
                          fontFamily: 'Poppins',
                          fontSize: 14,
                          fontWeight: FontWeight.w700,
                          color: Colors.white,
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                  ],
                  Expanded(
                    child: Text(
                      '#${order.orderNo}',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 17,
                        fontWeight: FontWeight.w700,
                        color: selected ? accent : AppColors.textDark,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 2),
              Text(
                order.customerName ?? '—',
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(
                  fontFamily: 'Poppins',
                  fontSize: 12.5,
                  fontWeight: FontWeight.w500,
                  color: AppColors.textLight,
                  height: 1.25,
                ),
              ),
              if (target > 0) ...[
                const SizedBox(height: 9),
                ClipRRect(
                  borderRadius: BorderRadius.circular(5),
                  child: LinearProgressIndicator(
                    value: (picked / target).clamp(0.0, 1.0),
                    minHeight: 5,
                    backgroundColor: AppColors.borderLight,
                    valueColor: AlwaysStoppedAnimation(accent),
                  ),
                ),
                const SizedBox(height: 5),
                Row(
                  children: [
                    Text(
                      '$picked / $target',
                      style: TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 11.5,
                        fontWeight: FontWeight.w700,
                        color: done ? completeColor : AppColors.textMuted,
                      ),
                    ),
                    const Spacer(),
                    if (locked)
                      const Icon(
                        Icons.lock_outline,
                        size: 13,
                        color: AppColors.textMuted,
                      )
                    else if (done)
                      Icon(Icons.check_circle, size: 14, color: completeColor),
                  ],
                ),
                // An order ended short says so — the pallets weren't all picked and the
                // count alone would read as a half-finished order.
                if (order.completedEarly) ...[
                  const SizedBox(height: 3),
                  const Text(
                    'Completed early',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 10.5,
                      fontWeight: FontWeight.w700,
                      color: AppColors.warning,
                    ),
                  ),
                ],
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// One grouped pick line — product, order, and a picked/target progress bar. The
/// leading tile shows the target pallet count, turning green with a tick when done.
class _PickRow extends StatelessWidget {
  final PickItem item;
  final int picked;
  final bool complete;
  final Color completeColor;
  final VoidCallback onTap;

  const _PickRow({
    required this.item,
    required this.picked,
    required this.complete,
    required this.completeColor,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final target = item.palletCount;
    // Over-picked lines can push this past 1 — the bar is a bar, so cap it there.
    final pct = target > 0 ? (picked / target).clamp(0.0, 1.0) : 0.0;
    final accent = complete ? completeColor : AppColors.primary;

    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
        child: Row(
          children: [
            Container(
              width: 46,
              height: 46,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: accent.withValues(alpha: 0.12),
                borderRadius: BorderRadius.circular(12),
              ),
              child: complete
                  ? Icon(Icons.check, color: completeColor, size: 24)
                  : item.isTapToPick
                  ? const Icon(Icons.check_circle_outline, color: AppColors.primary, size: 24)
                  : Text(
                      '$target',
                      style: const TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 19,
                        fontWeight: FontWeight.w700,
                        color: AppColors.primary,
                      ),
                    ),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    item.productDescription ?? 'Product ${item.productId}',
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 15,
                      fontWeight: FontWeight.w600,
                      color: AppColors.textDark,
                      height: 1.2,
                    ),
                  ),
                  const SizedBox(height: 3),
                  Text(
                    // The order is already named by the rail selection — the line only needs
                    // its own detail.
                    item.isPos
                        ? 'Point-of-sale board  ·  no scan needed'
                        : item.isCubicMeter
                        ? '${item.quantity} m³  ·  no scan needed'
                        : [
                            if (item.size != null) 'Size ${item.size}',
                            if (item.upp > 0) 'UPP ${item.upp}',
                          ].join('  ·  '),
                    style: const TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 13,
                      fontWeight: FontWeight.w500,
                      color: AppColors.textLight,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Row(
                    children: [
                      Expanded(
                        child: ClipRRect(
                          borderRadius: BorderRadius.circular(5),
                          child: LinearProgressIndicator(
                            value: pct,
                            minHeight: 6,
                            backgroundColor: AppColors.borderLight,
                            valueColor: AlwaysStoppedAnimation(accent),
                          ),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Text(
                        item.isTapToPick
                            ? (complete ? 'Picked' : 'Tap to mark picked')
                            : '$picked / $target',
                        style: TextStyle(
                          fontFamily: 'Poppins',
                          fontSize: 13,
                          fontWeight: FontWeight.w700,
                          color: complete ? completeColor : AppColors.textMuted,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(width: 6),
            // No detail page behind a cubic meter line, so no chevron.
            if (!item.isTapToPick)
              const Icon(
                Icons.chevron_right,
                size: 22,
                color: AppColors.placeholder,
              )
            else
              const SizedBox(width: 22),
          ],
        ),
      ),
    );
  }
}

/// Tapping an unpicked cubic meter line (raw peat): the picker enters the load's density
/// (kg/m³, up to 2 decimals). Returns it on Save — which then marks the line picked — or
/// null on Cancel.
class _DensityDialog extends StatefulWidget {
  final PickItem line;
  const _DensityDialog({required this.line});

  @override
  State<_DensityDialog> createState() => _DensityDialogState();
}

class _DensityDialogState extends State<_DensityDialog> {
  final _ctrl = TextEditingController();

  @override
  void dispose() {
    _ctrl.dispose();
    super.dispose();
  }

  double? get _value {
    final v = double.tryParse(_ctrl.text.trim());
    return v != null && v > 0 ? v : null;
  }

  void _save() {
    final v = _value;
    if (v == null) return;
    Navigator.of(context).pop(double.parse(v.toStringAsFixed(2)));
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text(
        'Enter Density',
        textAlign: TextAlign.center,
        style: TextStyle(fontFamily: 'Poppins', fontWeight: FontWeight.w700),
      ),
      content: SizedBox(
        width: 360,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              '${widget.line.productDescription ?? 'This line'}  ·  ${widget.line.quantity} m³\n'
              'Saving the density marks the line as picked.',
              textAlign: TextAlign.center,
              style: const TextStyle(fontFamily: 'Poppins', color: AppColors.textLight),
            ),
            const SizedBox(height: 16),
            TextField(
              controller: _ctrl,
              autofocus: true,
              textAlign: TextAlign.center,
              keyboardType: const TextInputType.numberWithOptions(decimal: true),
              // Digits with at most 2 decimals.
              inputFormatters: [
                FilteringTextInputFormatter.allow(RegExp(r'^\d{0,7}(\.\d{0,2})?')),
              ],
              textInputAction: TextInputAction.done,
              style: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 24,
                fontWeight: FontWeight.w700,
              ),
              decoration: const InputDecoration(
                hintText: '0.00',
                suffixText: 'kg/m³',
                border: OutlineInputBorder(),
              ),
              onChanged: (_) => setState(() {}),
              onSubmitted: (_) => _save(),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: _value == null ? null : _save,
          style: FilledButton.styleFrom(backgroundColor: const Color(0xFF16A34A)),
          child: const Text('Save'),
        ),
      ],
    );
  }
}
