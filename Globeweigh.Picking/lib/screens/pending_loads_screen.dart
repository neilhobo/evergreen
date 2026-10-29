import 'dart:async';

import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../format.dart';
import '../models/pending_load.dart';
import '../models/pending_load_detail.dart';
import '../models/pending_order.dart';
import '../services/session.dart';
import '../theme/app_colors.dart';
import 'load_photos_screen.dart';
import 'picking_list_screen.dart';

/// Pending Loads — split into two equal columns. The left column is the dispatch
/// "Pending" list (weighbridge records weighed in, awaiting weigh-out), mirroring the
/// TauriAdmin Orders/Dispatch "Pending" panel. The right column shows the selected
/// record's detail.
class PendingLoadsScreen extends StatefulWidget {
  const PendingLoadsScreen({super.key});

  @override
  State<PendingLoadsScreen> createState() => _PendingLoadsScreenState();
}

class _PendingLoadsScreenState extends State<PendingLoadsScreen> {
  final _api = ApiClient();
  List<PendingLoad>? _rows;
  String? _error;
  bool _loading = false;
  int? _selectedId;
  PendingLoadDetail? _detail;
  String? _detailError;
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    _load();
    // Keep the list live with a quiet background poll.
    _poll = Timer.periodic(
      const Duration(seconds: 15),
      (_) => _load(silent: true),
    );
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  Future<void> _load({bool silent = false}) async {
    if (!silent) setState(() => _loading = true);
    try {
      final rows = await _api.getPending();
      if (!mounted) return;
      setState(() {
        _rows = rows;
        _error = null;
        _loading = false;
        if (_selectedId != null && !rows.any((r) => r.id == _selectedId)) {
          _selectedId = null;
        }
      });
      // The selected load's orders move on too — one of them may have just been completed
      // on the picking list, which is what decides whether this load can be completed.
      final selected = _selectedId;
      if (selected != null) unawaited(_loadDetail(selected, silent: true));
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _error = '$e';
        _loading = false;
      });
    }
  }

  PendingLoad? get _selected =>
      _rows?.where((r) => r.id == _selectedId).firstOrNull;

  // Loads confirmed this session — switches the panel instantly on YES without
  // waiting for the next list refresh to bring back trailerConfirmed=true.
  final Set<int> _confirmedLocal = {};

  bool _needsTrailerConfirm(PendingLoad r) =>
      !r.trailerConfirmed && !_confirmedLocal.contains(r.id);

  // Trailer numbers corrected this session — the panel and list show the new number at
  // once, without waiting for the next list refresh.
  final Map<int, String> _trailerLocal = {};

  String? _trailerOf(PendingLoad r) => _trailerLocal[r.id] ?? r.trailerNo;

  void _showError(String message) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          message,
          style: const TextStyle(fontFamily: 'Poppins', fontSize: 14),
        ),
      ),
    );
  }

  Future<void> _confirmTrailer() async {
    final sel = _selected;
    if (sel == null) return;
    try {
      await _api.confirmTrailer(recordId: sel.id, userId: Session.userId);
      if (!mounted) return;
      setState(() => _confirmedLocal.add(sel.id));
      _load(silent: true);
    } on ApiException catch (e) {
      if (mounted) _showError(e.message);
    }
  }

  // NO — wrong trailer: ask for the trailer actually there. Submitting saves it to the load
  // and puts it back on the confirm panel, where the picker still answers YES / NO; Cancel
  // leaves the panel as it was.
  Future<void> _rejectTrailer() async {
    final sel = _selected;
    if (sel == null) return;
    final entered = await showDialog<String>(
      context: context,
      barrierDismissible: false,
      builder: (_) => _ChangeTrailerDialog(current: _trailerOf(sel)),
    );
    if (entered == null || !mounted) return;
    try {
      await _api.changeTrailer(
        recordId: sel.id,
        trailerNo: entered,
        userId: Session.userId,
      );
      if (!mounted) return;
      setState(() => _trailerLocal[sel.id] = entered);
      _load(silent: true);
    } on ApiException catch (e) {
      if (mounted) _showError(e.message);
    }
  }

  void selectRow(int id) {
    setState(() {
      _selectedId = id;
      _detail = null;
      _detailError = null;
    });
    _loadDetail(id);
  }

  Future<void> _openPickingList() async {
    final sel = _selected;
    if (sel == null) return;
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) =>
            PickingListScreen(
              recordId: sel.id,
              haulierName: sel.haulierName,
              weightIn: sel.weightIn,
            ),
      ),
    );
    // Reflect any pallets just picked in the progress rings.
    if (mounted) _load(silent: true);
  }

  Future<void> _completeLoad() async {
    final sel = _selected;
    if (sel == null) return;
    await Navigator.of(context).push(
      MaterialPageRoute(
        builder: (_) =>
            LoadPhotosScreen(
              recordId: sel.id,
              haulierName: sel.haulierName,
              hasPos: _detail?.hasPos ?? false,
            ),
      ),
    );
    if (mounted) _load(silent: true);
  }

  // Orders on the selected load that the picker hasn't completed yet. The load can't be
  // completed while any remain — completing dispatches every order and writes stock
  // history, so it has to wait until each one has been signed off on the picking list.
  List<int> _openOrderNumbers() {
    final orders = _detail?.orders;
    if (orders == null) return const [];
    return orders
        .where((o) => !o.pickingComplete)
        .map((o) => o.orderNo)
        .toList();
  }

  // [silent] is for the background refresh: keep showing what we have rather than
  // replacing the panel with an error over one dropped request.
  Future<void> _loadDetail(int recordId, {bool silent = false}) async {
    try {
      final detail = await _api.getPendingDetail(recordId);
      if (!mounted || _selectedId != recordId) return;
      setState(() {
        _detail = detail;
        _detailError = null;
      });
    } catch (e) {
      if (silent || !mounted || _selectedId != recordId) return;
      setState(() => _detailError = '$e');
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.background,
      appBar: AppBar(
        backgroundColor: AppColors.navy,
        foregroundColor: Colors.white,
        surfaceTintColor: Colors.transparent,
        title: const Text(
          'Pending Loads',
          style: TextStyle(
            fontFamily: 'Poppins',
            fontWeight: FontWeight.w700,
            color: Colors.white,
          ),
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh',
            onPressed: _loading ? null : () => _load(),
          ),
        ],
      ),
      body: _body(),
    );
  }

  Widget _body() {
    // No loads to pick → take over the whole screen with an empty state rather than
    // showing the two-panel split with a tiny "no records" note.
    if (_rows != null && _rows!.isEmpty && _error == null) {
      return const _EmptyLoads();
    }
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Expanded(child: _ListPanel(this)),
          const SizedBox(width: 12),
          Expanded(child: _DetailPanel(this)),
        ],
      ),
    );
  }
}

/// Full-screen empty state shown when there are no pending loads to pick.
class _EmptyLoads extends StatelessWidget {
  const _EmptyLoads();

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 120,
            height: 120,
            decoration: BoxDecoration(
              color: AppColors.primary.withValues(alpha: 0.10),
              shape: BoxShape.circle,
            ),
            child: const Icon(
              Icons.local_shipping_outlined,
              color: AppColors.primary,
              size: 60,
            ),
          ),
          const SizedBox(height: 24),
          const Text(
            'No pending loads',
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 22,
              fontWeight: FontWeight.w700,
              color: AppColors.textDark,
            ),
          ),
          const SizedBox(height: 8),
          const Text(
            'Loads ready to pick will appear here',
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
}

/// Left column — the pending list.
class _ListPanel extends StatelessWidget {
  final _PendingLoadsScreenState s;
  const _ListPanel(this.s);

  @override
  Widget build(BuildContext context) {
    final rows = s._rows;
    if (s._error != null) return _Centered(s._error!, color: AppColors.error);
    if (rows == null) return const _Centered('Loading…');
    if (rows.isEmpty) return const _Centered('No pending records');
    // No header card — the list sits directly on the light-grey page background and
    // each record is its own bordered, shadowed card.
    return ListView.builder(
      padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 4),
      clipBehavior: Clip.none,
      itemCount: rows.length,
      itemBuilder: (_, i) {
        final r = rows[i];
        return _PendingItem(
          row: r,
          selected: r.id == s._selectedId,
          onTap: () => s.selectRow(r.id),
        );
      },
    );
  }
}

/// A single pending record rendered as a mobile list item — the haulier name on top
/// with the rest of the fields stacked on their own rows beneath.
class _PendingItem extends StatelessWidget {
  final PendingLoad row;
  final bool selected;
  final VoidCallback onTap;
  const _PendingItem({
    required this.row,
    required this.selected,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: selected ? AppColors.primary : AppColors.borderLight,
          width: selected ? 1.5 : 1,
        ),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: 0.05),
            blurRadius: 12,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          borderRadius: BorderRadius.circular(16),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 9),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.center,
              children: [
                Container(
                  width: 40,
                  height: 40,
                  decoration: BoxDecoration(
                    color: AppColors.primary.withValues(alpha: 0.12),
                    borderRadius: BorderRadius.circular(11),
                  ),
                  child: const Icon(
                    Icons.pending_actions,
                    color: AppColors.primary,
                    size: 22,
                  ),
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        row.haulierName ?? '—',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          fontFamily: 'Poppins',
                          fontSize: 17,
                          fontWeight: FontWeight.w700,
                          color: AppColors.textDark,
                          height: 1.2,
                        ),
                      ),
                      const SizedBox(height: 5),
                      Text(
                        '${fmtDateTime(row.pendingWeighDate)}   ·   Trailer ${row.trailerNo ?? '—'}   ·   ${row.orderCount ?? 0} order${(row.orderCount ?? 0) == 1 ? '' : 's'}',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(
                          fontFamily: 'Poppins',
                          fontSize: 14,
                          fontWeight: FontWeight.w500,
                          color: AppColors.textLight,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 8),
                _LoadProgress(
                  picked: row.pickedPallets,
                  total: row.totalPallets,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Overall picking progress for a load — a circular percentage ring in a fixed ~100px
/// column on the right of the list item, with the picked/total count beneath.
/// Why "Complete" is greyed out: the orders still to be finished on the picking list.
class _CompleteBlockedNote extends StatelessWidget {
  final List<int> orderNos;

  const _CompleteBlockedNote({required this.orderNos});

  @override
  Widget build(BuildContext context) {
    final list = orderNos.map((n) => '#$n').join(', ');
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 9),
      decoration: BoxDecoration(
        color: AppColors.warning.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(10),
        border: Border.all(color: AppColors.warning.withValues(alpha: 0.55)),
      ),
      child: Row(
        children: [
          const Icon(Icons.info_outline, size: 17, color: AppColors.warning),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              orderNos.length == 1
                  ? 'Order $list still to complete on the picking list'
                  : 'Orders still to complete on the picking list: $list',
              style: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 12.5,
                fontWeight: FontWeight.w600,
                color: AppColors.textDark,
                height: 1.25,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _LoadProgress extends StatelessWidget {
  final int picked;
  final int total;
  const _LoadProgress({required this.picked, required this.total});

  static const Color _green = Color(0xFF16A34A);

  @override
  Widget build(BuildContext context) {
    final pct = total > 0 ? (picked / total).clamp(0.0, 1.0) : 0.0;
    final done = total > 0 && picked >= total;
    final color = done
        ? _green
        : (picked > 0 ? AppColors.primary : AppColors.textMuted);
    return SizedBox(
      width: 68,
      child: Center(
        child: SizedBox(
          width: 48,
          height: 48,
          child: Stack(
            alignment: Alignment.center,
            children: [
              SizedBox(
                width: 48,
                height: 48,
                child: CircularProgressIndicator(
                  value: pct,
                  strokeWidth: 5,
                  backgroundColor: AppColors.borderLight,
                  valueColor: AlwaysStoppedAnimation(color),
                ),
              ),
              done
                  ? const Icon(Icons.check, color: _green, size: 22)
                  : Text(
                      '${(pct * 100).round()}%',
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
    );
  }
}

/// Right column — the selected load: pending icon + haulier as the header, then a
/// list of the orders on that load.
class _DetailPanel extends StatelessWidget {
  final _PendingLoadsScreenState s;
  const _DetailPanel(this.s);

  @override
  Widget build(BuildContext context) {
    final sel = s._selected;
    return Container(
      decoration: BoxDecoration(
        color: AppColors.surface,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: AppColors.borderLight),
      ),
      clipBehavior: Clip.antiAlias,
      child: sel == null
          ? const _Centered('Select a load to view its orders')
          : s._needsTrailerConfirm(sel)
          ? _ConfirmTrailerPanel(
              haulierName: sel.haulierName,
              trailerNo: s._trailerOf(sel),
              onYes: s._confirmTrailer,
              onNo: s._rejectTrailer,
            )
          : Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // Header: pending icon + haulier (the main header).
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 16, 16, 14),
                  child: Row(
                    children: [
                      Container(
                        width: 52,
                        height: 52,
                        decoration: BoxDecoration(
                          color: AppColors.primary.withValues(alpha: 0.12),
                          borderRadius: BorderRadius.circular(14),
                        ),
                        child: const Icon(
                          Icons.pending_actions,
                          color: AppColors.primary,
                          size: 28,
                        ),
                      ),
                      const SizedBox(width: 14),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              sel.haulierName ?? '—',
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(
                                fontFamily: 'Poppins',
                                fontSize: 20,
                                fontWeight: FontWeight.w700,
                                color: AppColors.textDark,
                                height: 1.15,
                              ),
                            ),
                            const SizedBox(height: 0),
                            // Products on the left, the scanned weight flush right on the
                            // same row — label and value stay on one line.
                            Row(
                              children: [
                                _stat('Products', s._detail?.totalProducts),
                                const SizedBox(width: 12),
                                // Expanded (not Spacer + Flexible — that splits the free
                                // space in two and clips the value off the end).
                                Expanded(
                                  child: Align(
                                    alignment: Alignment.centerRight,
                                    child: _stat(
                                      'Weight (Calculated)',
                                      s._detail == null
                                          ? null
                                          : '${fmtKg(s._detail!.totalWeight)} Kg',
                                      noWrap: true,
                                    ),
                                  ),
                                ),
                              ],
                            ),
                            // Pallets on the left; any estimated weights flagged flush
                            // right under the weight — a no-scan label (or a code with no
                            // pallet record) carries the server's best estimate from the
                            // product's other pallets, so the total is close but not
                            // measured.
                            Row(
                              children: [
                                _stat('Pallets', s._detail?.totalPallets),
                                const SizedBox(width: 12),
                                if ((s._detail?.estimatedPallets ?? 0) > 0)
                                  Expanded(
                                    child: Padding(
                                      padding: const EdgeInsets.only(top: 3),
                                      child: Row(
                                        mainAxisAlignment:
                                            MainAxisAlignment.end,
                                        mainAxisSize: MainAxisSize.min,
                                        children: [
                                          const Icon(
                                            Icons.flag,
                                            size: 16,
                                            color: AppColors.warning,
                                          ),
                                          const SizedBox(width: 5),
                                          Flexible(
                                            child: Text(
                                              'incl. ${s._detail!.estimatedPallets} estimated',
                                              maxLines: 1,
                                              softWrap: false,
                                              overflow: TextOverflow.ellipsis,
                                              style: const TextStyle(
                                                fontFamily: 'Poppins',
                                                fontSize: 13.5,
                                                fontWeight: FontWeight.w700,
                                                color: AppColors.warning,
                                              ),
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ),
                              ],
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
                const Divider(height: 1),
                const Padding(
                  padding: EdgeInsets.fromLTRB(16, 12, 16, 8),
                  child: Text.rich(
                    TextSpan(
                      style: TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 11,
                        fontWeight: FontWeight.w700,
                        color: AppColors.textLight,
                        letterSpacing: 0.6,
                      ),
                      children: [
                        TextSpan(text: 'ORDERS  ·  '),
                        // Orange, matching the loading-order badges below.
                        TextSpan(
                          text: 'IN LOADING ORDER',
                          style: TextStyle(color: AppColors.warning),
                        ),
                      ],
                    ),
                  ),
                ),
                Expanded(child: _ordersBody(context)),
                const Divider(height: 1),
                Padding(
                  padding: const EdgeInsets.all(12),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      if (s._openOrderNumbers().isNotEmpty) ...[
                        _CompleteBlockedNote(orderNos: s._openOrderNumbers()),
                        const SizedBox(height: 10),
                      ],
                      Row(
                        children: [
                          Expanded(
                            child: ElevatedButton.icon(
                              icon: const Icon(Icons.checklist, size: 20),
                              label: const Text('Picking List'),
                              style: ElevatedButton.styleFrom(
                                minimumSize: const Size.fromHeight(52),
                                textStyle: const TextStyle(
                                  fontFamily: 'Poppins',
                                  fontSize: 15,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                              onPressed: () => s._openPickingList(),
                            ),
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: ElevatedButton.icon(
                              icon: const Icon(
                                Icons.check_circle_outline,
                                size: 20,
                              ),
                              label: const Text('Complete'),
                              style: ElevatedButton.styleFrom(
                                backgroundColor: const Color(0xFF16A34A),
                                foregroundColor: Colors.white,
                                minimumSize: const Size.fromHeight(52),
                                textStyle: const TextStyle(
                                  fontFamily: 'Poppins',
                                  fontSize: 15,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                              // Greyed out until every order on the load is completed (and
                              // while the detail is still loading, so it can't be beaten to it).
                              onPressed:
                                  s._detail == null ||
                                      s._openOrderNumbers().isNotEmpty
                                  ? null
                                  : () => s._completeLoad(),
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  // "Products : X" / "Pallets : X" — value shows '…' until the detail loads.
  // noWrap keeps a stat's label and value together on one line (ellipsised if the row
  // is ever too tight) rather than letting the value drop under its label.
  Widget _stat(String label, Object? value, {bool noWrap = false}) => Padding(
    padding: const EdgeInsets.only(top: 3),
    child: Text.rich(
      maxLines: noWrap ? 1 : null,
      softWrap: !noWrap,
      overflow: noWrap ? TextOverflow.ellipsis : TextOverflow.clip,
      TextSpan(
        children: [
          TextSpan(
            text: '$label : ',
            style: const TextStyle(
              fontFamily: 'Poppins',
              fontSize: 13.5,
              fontWeight: FontWeight.w500,
              color: AppColors.textLight,
            ),
          ),
          TextSpan(
            text: value?.toString() ?? '…',
            style: const TextStyle(
              fontFamily: 'Poppins',
              fontSize: 13.5,
              fontWeight: FontWeight.w700,
              color: AppColors.textDark,
            ),
          ),
        ],
      ),
    ),
  );

  Widget _ordersBody(BuildContext context) {
    if (s._detailError != null) {
      return _Centered(s._detailError!, color: AppColors.error);
    }
    final detail = s._detail;
    if (detail == null) return const _Centered('Loading…');
    if (detail.orders.isEmpty) return const _Centered('No orders on this load');
    // Order No column best-fits the widest number on the load (one line, never wrapped),
    // so every row's customer starts at the same place.
    final orderNoWidth = detail.orders
        .map((o) => _OrderTile.measureOrderNo(context, o.orderNo))
        .reduce((a, b) => a > b ? a : b);
    return ListView.separated(
      padding: EdgeInsets.zero,
      itemCount: detail.orders.length,
      separatorBuilder: (_, _) => const Divider(height: 1),
      itemBuilder: (_, i) => _OrderTile(detail.orders[i], orderNoWidth: orderNoWidth),
    );
  }
}

/// Confirmation gate shown for a newly-created load: the picker must confirm the
/// trailer number before the load's orders (and its actions) are shown.
class _ConfirmTrailerPanel extends StatelessWidget {
  final String? haulierName;
  final String? trailerNo;
  final VoidCallback onYes;
  final VoidCallback onNo;

  const _ConfirmTrailerPanel({
    required this.haulierName,
    required this.trailerNo,
    required this.onYes,
    required this.onNo,
  });

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.all(24),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          // Header: haulier, with the trailer number beneath in large type.
          Text(
            haulierName ?? '—',
            textAlign: TextAlign.center,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              fontFamily: 'Poppins',
              fontSize: 20,
              fontWeight: FontWeight.w700,
              color: AppColors.textDark,
              height: 1.15,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            'Trailer ${trailerNo ?? '—'}',
            textAlign: TextAlign.center,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              fontFamily: 'Poppins',
              fontSize: 30,
              fontWeight: FontWeight.w800,
              color: AppColors.primary,
            ),
          ),
          const SizedBox(height: 28),
          const Text(
            'Confirm correct Trailer No',
            textAlign: TextAlign.center,
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: 16,
              fontWeight: FontWeight.w600,
              color: AppColors.textLight,
            ),
          ),
          const SizedBox(height: 16),
          Row(
            children: [
              Expanded(
                child: ElevatedButton.icon(
                  icon: const Icon(Icons.close, size: 20),
                  label: const Text('NO'),
                  style: ElevatedButton.styleFrom(
                    backgroundColor: AppColors.error,
                    foregroundColor: Colors.white,
                    minimumSize: const Size.fromHeight(56),
                    textStyle: const TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 16,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  onPressed: onNo,
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: ElevatedButton.icon(
                  icon: const Icon(Icons.check, size: 20),
                  label: const Text('YES'),
                  style: ElevatedButton.styleFrom(
                    backgroundColor: const Color(0xFF16A34A),
                    foregroundColor: Colors.white,
                    minimumSize: const Size.fromHeight(56),
                    textStyle: const TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 16,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                  onPressed: onYes,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// A single order on a load — loading order, order no, customer (+ PO), and picked/total
/// quantity. Listed in loading order: 1 goes on the trailer first (delivered last).
class _OrderTile extends StatelessWidget {
  final PendingOrder o;
  // Width of the Order No column — the widest order number on the load (see _ordersBody).
  final double orderNoWidth;
  const _OrderTile(this.o, {required this.orderNoWidth});

  static const _orderNoStyle = TextStyle(
    fontFamily: 'Poppins',
    fontSize: 15,
    fontWeight: FontWeight.w700,
    color: AppColors.primary,
  );

  // The laid-out width of "#<orderNo>" plus a gap before the customer; never below the
  // old fixed 56.
  static double measureOrderNo(BuildContext context, int orderNo) {
    final tp = TextPainter(
      text: TextSpan(text: '#$orderNo', style: _orderNoStyle),
      maxLines: 1,
      textDirection: TextDirection.ltr,
      textScaler: MediaQuery.textScalerOf(context),
    )..layout();
    final w = tp.width + 12;
    return w < 56 ? 56 : w;
  }

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
      child: Row(
        children: [
          if (o.loadingOrder > 0) ...[
            Container(
              width: 28,
              height: 28,
              alignment: Alignment.center,
              decoration: BoxDecoration(
                color: AppColors.warning,
                borderRadius: BorderRadius.circular(8),
              ),
              child: Text(
                '${o.loadingOrder}',
                style: const TextStyle(
                  fontFamily: 'Poppins',
                  fontSize: 14,
                  fontWeight: FontWeight.w700,
                  color: Colors.white,
                ),
              ),
            ),
            const SizedBox(width: 10),
          ],
          SizedBox(
            width: orderNoWidth,
            child: Text(
              '#${o.orderNo}',
              maxLines: 1,
              softWrap: false,
              style: _orderNoStyle,
            ),
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  o.customerName ?? '—',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 15,
                    fontWeight: FontWeight.w600,
                    color: AppColors.textDark,
                  ),
                ),
                if ((o.poNumber ?? '').isNotEmpty) ...[
                  const SizedBox(height: 2),
                  Text(
                    'PO ${o.poNumber}',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontFamily: 'Poppins',
                      fontSize: 12,
                      color: AppColors.textLight,
                    ),
                  ),
                ],
              ],
            ),
          ),
          const SizedBox(width: 10),
          Text(
            '${o.pickedQuantity}/${o.totalQuantityOrdered}',
            style: const TextStyle(
              fontFamily: 'Poppins',
              fontSize: 14,
              fontWeight: FontWeight.w600,
              color: AppColors.textMuted,
            ),
          ),
        ],
      ),
    );
  }
}

// ---- shared bits ------------------------------------------------------------

class _Centered extends StatelessWidget {
  final String text;
  final Color color;
  const _Centered(this.text, {this.color = AppColors.textLight});

  @override
  Widget build(BuildContext context) => Center(
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

// ---- formatting -------------------------------------------------------------

String pad2(int n) => n.toString().padLeft(2, '0');

String fmtDateTime(DateTime? d) {
  if (d == null) return '—';
  final l = d.toLocal();
  return '${pad2(l.day)}/${pad2(l.month)}/${l.year % 100} ${pad2(l.hour)}:${pad2(l.minute)}';
}

/// The picker answered NO to the trailer number: type the trailer actually there. Returns
/// the new number (trimmed) on Submit, null on Cancel.
class _ChangeTrailerDialog extends StatefulWidget {
  final String? current;
  const _ChangeTrailerDialog({required this.current});

  @override
  State<_ChangeTrailerDialog> createState() => _ChangeTrailerDialogState();
}

class _ChangeTrailerDialogState extends State<_ChangeTrailerDialog> {
  final _ctrl = TextEditingController();

  @override
  void dispose() {
    _ctrl.dispose();
    super.dispose();
  }

  String get _value => _ctrl.text.trim();

  void _submit() {
    if (_value.isEmpty) return;
    Navigator.of(context).pop(_value);
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text(
        'Change Trailer No',
        textAlign: TextAlign.center,
        style: TextStyle(fontFamily: 'Poppins', fontWeight: FontWeight.w700),
      ),
      content: SizedBox(
        width: 360,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              'The load says Trailer ${widget.current ?? '—'}.\nEnter the trailer that is actually there.',
              textAlign: TextAlign.center,
              style: const TextStyle(fontFamily: 'Poppins', color: AppColors.textLight),
            ),
            const SizedBox(height: 16),
            TextField(
              controller: _ctrl,
              autofocus: true,
              maxLength: 50,
              textAlign: TextAlign.center,
              textCapitalization: TextCapitalization.characters,
              textInputAction: TextInputAction.done,
              style: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 24,
                fontWeight: FontWeight.w700,
              ),
              decoration: const InputDecoration(
                hintText: 'Trailer No',
                counterText: '',
                border: OutlineInputBorder(),
              ),
              onChanged: (_) => setState(() {}),
              onSubmitted: (_) => _submit(),
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
          onPressed: _value.isEmpty ? null : _submit,
          style: FilledButton.styleFrom(backgroundColor: const Color(0xFF16A34A)),
          child: const Text('Submit'),
        ),
      ],
    );
  }
}
