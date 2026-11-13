import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../data/models.dart';
import '../../data/pos_api.dart';
import '../account/account_sheet.dart';
import '../sales/my_orders_screen.dart' show ErrorRetry;
import '../sales/sales_format.dart';
import 'pos_order_screen.dart';
import 'pos_providers.dart';

const _ground = Color(0xFFF2F4F7);
const _ink = Color(0xFF13201A);
const _muted = Color(0xFF556070);
const _line = Color(0xFFE5E9EF);
const _green = Color(0xFF15803D);

/// Home for whoever holds the POS creator tag (design A): the open orders that
/// carry a POS board, each with how many of its boards are made up. Tapping one
/// opens its checklist; an order with every board made leaves the list.
class PosBoardsScreen extends ConsumerStatefulWidget {
  final User user;
  const PosBoardsScreen({super.key, required this.user});

  @override
  ConsumerState<PosBoardsScreen> createState() => _PosBoardsScreenState();
}

class _PosBoardsScreenState extends ConsumerState<PosBoardsScreen> with WidgetsBindingObserver {
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    // The office adds and dispatches orders through the day.
    _poll = Timer.periodic(const Duration(seconds: 60), (_) => ref.invalidate(posOrdersProvider));
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) ref.invalidate(posOrdersProvider);
  }

  @override
  void dispose() {
    _poll?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  Future<void> _open(PosOrder o) async {
    await Navigator.of(context).push(MaterialPageRoute(builder: (_) => PosOrderScreen(order: o)));
    ref.invalidate(posOrdersProvider);
  }

  @override
  Widget build(BuildContext context) {
    final orders = ref.watch(posOrdersProvider);
    final count = orders.valueOrNull?.length;
    return Scaffold(
      backgroundColor: _ground,
      body: Column(
        children: [
          SalesHeader(
            overline: widget.user.displayName,
            title: 'POS Boards',
            actions: [
              HeaderIconButton(
                icon: Icons.account_circle_outlined,
                tooltip: 'Account',
                onPressed: () => showAccountSheet(context),
              ),
            ],
            bottom: Text(
              count == null
                  ? 'Open orders with a POS board'
                  : count == 0
                  ? 'No orders need boards'
                  : '$count open ${count == 1 ? 'order needs' : 'orders need'} boards',
              style: TextStyle(fontSize: 13, color: Colors.white.withValues(alpha: 0.75)),
            ),
          ),
          Expanded(
            child: orders.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (e, _) => ErrorRetry(message: apiMessage(e), onRetry: () => ref.invalidate(posOrdersProvider)),
              data: (rows) => RefreshIndicator(
                onRefresh: () => ref.refresh(posOrdersProvider.future),
                child: rows.isEmpty
                    ? ListView(
                        children: const [
                          Padding(
                            padding: EdgeInsets.fromLTRB(40, 80, 40, 40),
                            child: Column(
                              children: [
                                Icon(Icons.task_alt, size: 56, color: _green),
                                SizedBox(height: 14),
                                Text(
                                  'Every board is made up',
                                  style: TextStyle(fontSize: 18, fontWeight: FontWeight.w600, color: _ink),
                                ),
                                SizedBox(height: 8),
                                Text(
                                  'New open orders with a POS board show here as the office enters them.',
                                  textAlign: TextAlign.center,
                                  style: TextStyle(fontSize: 14, height: 1.4, color: _muted),
                                ),
                              ],
                            ),
                          ),
                        ],
                      )
                    : ListView.separated(
                        padding: const EdgeInsets.fromLTRB(14, 14, 14, 24),
                        itemCount: rows.length,
                        separatorBuilder: (_, _) => const SizedBox(height: 10),
                        itemBuilder: (_, i) => _OrderCard(order: rows[i], onTap: () => _open(rows[i])),
                      ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _OrderCard extends StatelessWidget {
  final PosOrder order;
  final VoidCallback onTap;
  const _OrderCard({required this.order, required this.onTap});

  @override
  Widget build(BuildContext context) {
    final o = order;
    return Material(
      color: Colors.white,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: const BorderSide(color: _line),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      'Order ${o.orderNo}',
                      style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: _ink),
                    ),
                  ),
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
                    decoration: BoxDecoration(color: const Color(0xFFFEF3C7), borderRadius: BorderRadius.circular(999)),
                    child: Text(
                      o.deliveryDate == null ? 'ASAP' : 'Goes ${dayLabel(o.deliveryDate)}',
                      style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: Color(0xFF92400E)),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 6),
              Text(
                o.customerName ?? 'Customer',
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 14, color: _muted),
              ),
              const SizedBox(height: 8),
              _LoadPill(order: o),
              const SizedBox(height: 10),
              Row(
                children: [
                  Expanded(
                    child: ClipRRect(
                      borderRadius: BorderRadius.circular(3),
                      child: LinearProgressIndicator(
                        value: o.total == 0 ? 0 : o.made / o.total,
                        minHeight: 6,
                        backgroundColor: _line,
                        color: _green,
                      ),
                    ),
                  ),
                  const SizedBox(width: 10),
                  Text(
                    '${o.made} of ${o.total} made',
                    style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600, color: Color(0xFF334155)),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// Where the order is in dispatch: not on a load yet (grey), on a load (blue),
/// or on a Pending load (red: the lorry is weighed in and being loaded, so its
/// boards are wanted now). A load names its haulier and collection day.
class _LoadPill extends StatelessWidget {
  final PosOrder order;
  const _LoadPill({required this.order});

  @override
  Widget build(BuildContext context) {
    final o = order;
    final (bg, fg, label) = o.onPending
        ? (const Color(0xFFFDE8E8), const Color(0xFF991B1B), 'On Pending · loading now')
        : o.onLoad
        ? (const Color(0xFFE5EEFC), const Color(0xFF1D4ED8), 'On a load')
        : (const Color(0xFFEEF1F5), const Color(0xFF475569), 'Not on a load');
    final detail = [
      if (o.onLoad && (o.loadHaulier?.trim().isNotEmpty ?? false)) titleCase(o.loadHaulier!.trim()),
      if (o.onLoad && !o.onPending && o.loadDate != null) dayLabel(o.loadDate),
    ].join(' · ');
    return Row(
      children: [
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 3),
          decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(999)),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 7,
                height: 7,
                decoration: BoxDecoration(color: fg, shape: BoxShape.circle),
              ),
              const SizedBox(width: 6),
              Text(
                label,
                style: TextStyle(fontSize: 11, fontWeight: FontWeight.w600, color: fg),
              ),
            ],
          ),
        ),
        if (detail.isNotEmpty) ...[
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              detail,
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontSize: 12, color: _muted),
            ),
          ),
        ],
      ],
    );
  }
}
