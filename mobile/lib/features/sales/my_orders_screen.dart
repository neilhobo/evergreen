import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../data/sales_models.dart';
import '../account/account_sheet.dart';
import 'edit_order_screen.dart';
import 'order_screen.dart';
import 'sales_format.dart';
import 'sales_providers.dart';

enum _Filter { all, drafts, office, live }

extension on _Filter {
  String get label => switch (this) {
    _Filter.all => 'All',
    _Filter.drafts => 'Drafts',
    _Filter.office => 'With office',
    _Filter.live => 'Live',
  };

  bool matches(SalesOrder o) => switch (this) {
    _Filter.all => true,
    _Filter.drafts => o.status.isDraft,
    _Filter.office => o.status == SalesOrderStatus.pending,
    _Filter.live => o.status == SalesOrderStatus.approved || o.status == SalesOrderStatus.planned,
  };
}

bool _finished(SalesOrder o) => o.status == SalesOrderStatus.dispatched || o.status == SalesOrderStatus.cancelled;

/// My Orders (or, with [completed], the finished ones): the TauriAdmin list
/// shrunk to a phone — filter chips, a row per order with its pill.
class MyOrdersScreen extends ConsumerStatefulWidget {
  final bool completed;
  const MyOrdersScreen({super.key, this.completed = false});

  @override
  ConsumerState<MyOrdersScreen> createState() => _MyOrdersScreenState();
}

class _MyOrdersScreenState extends ConsumerState<MyOrdersScreen> {
  _Filter _filter = _Filter.all;
  bool _searching = false;
  final _search = TextEditingController();

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  bool _matchesSearch(SalesOrder o) {
    final term = _search.text.trim().toLowerCase();
    if (term.isEmpty) return true;
    return [
      o.customerName,
      o.poNumber,
      o.ref,
      o.orderNo?.toString(),
    ].any((v) => v?.toLowerCase().contains(term) ?? false);
  }

  @override
  Widget build(BuildContext context) {
    final orders = ref.watch(salesOrdersProvider);
    final products = ref.watch(productsProvider).valueOrNull ?? const {};

    return Column(
      children: [
        SalesHeader(
          overline: widget.completed ? 'Dispatched and cancelled' : 'Sales',
          title: widget.completed ? 'Completed' : 'My Orders',
          actions: [
            HeaderIconButton(
              icon: _searching ? Icons.close : Icons.search,
              tooltip: _searching ? 'Close search' : 'Search',
              onPressed: () => setState(() {
                _searching = !_searching;
                if (!_searching) _search.clear();
              }),
            ),
            HeaderIconButton(
              icon: Icons.account_circle_outlined,
              tooltip: 'Account',
              onPressed: () => showAccountSheet(context),
            ),
          ],
          bottom: _searching
              ? SalesSearchField(
                  controller: _search,
                  hint: 'Customer, PO or order number',
                  onChanged: (_) => setState(() {}),
                  autofocus: true,
                )
              : null,
        ),
        Expanded(
          child: orders.when(
            loading: () => const Center(child: CircularProgressIndicator()),
            error: (e, _) => ErrorRetry(message: apiMessage(e), onRetry: () => ref.invalidate(salesOrdersProvider)),
            data: (all) {
              final scope = all.where((o) => widget.completed ? _finished(o) : true).where(_matchesSearch).toList();
              final visible = widget.completed ? scope : scope.where(_filter.matches).toList();
              return Stack(
                children: [
                  Column(
                    children: [
                      if (!widget.completed)
                        Container(
                          color: Colors.white,
                          height: 58,
                          child: ListView(
                            scrollDirection: Axis.horizontal,
                            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 11),
                            children: [
                              for (final f in _Filter.values)
                                Padding(
                                  padding: const EdgeInsets.only(right: 8),
                                  child: _Chip(
                                    label: '${f.label} ${scope.where(f.matches).length}',
                                    selected: _filter == f,
                                    onTap: () => setState(() => _filter = f),
                                  ),
                                ),
                            ],
                          ),
                        ),
                      const Divider(height: 1, color: Color(0xFFE5E9EF)),
                      Expanded(
                        child: RefreshIndicator(
                          onRefresh: () => ref.refresh(salesOrdersProvider.future),
                          child: visible.isEmpty
                              ? ListView(
                                  children: [
                                    Padding(
                                      padding: const EdgeInsets.all(40),
                                      child: Text(
                                        widget.completed ? 'No finished orders yet.' : 'No orders here.',
                                        textAlign: TextAlign.center,
                                        style: const TextStyle(color: Color(0xFF556070)),
                                      ),
                                    ),
                                  ],
                                )
                              : ListView.separated(
                                  padding: const EdgeInsets.only(bottom: 96),
                                  itemCount: visible.length,
                                  separatorBuilder: (_, _) => const Divider(height: 1, color: Color(0xFFE5E9EF)),
                                  itemBuilder: (_, i) =>
                                      _OrderRow(order: visible[i], total: orderTotal(visible[i].items, products)),
                                ),
                        ),
                      ),
                    ],
                  ),
                  if (!widget.completed)
                    Positioned(
                      right: 18,
                      bottom: 18,
                      child: FloatingActionButton(
                        heroTag: 'new-order',
                        tooltip: 'New order',
                        backgroundColor: SalesHeader.navy,
                        foregroundColor: Colors.white,
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
                        onPressed: () =>
                            Navigator.of(context).push(MaterialPageRoute(builder: (_) => const EditOrderScreen())),
                        child: const Icon(Icons.add, size: 28),
                      ),
                    ),
                ],
              );
            },
          ),
        ),
      ],
    );
  }
}

class _OrderRow extends StatelessWidget {
  final SalesOrder order;
  final double total;
  const _OrderRow({required this.order, required this.total});

  @override
  Widget build(BuildContext context) {
    final o = order;
    final pillLabel = o.status == SalesOrderStatus.returned && (o.officeNote?.trim().isNotEmpty ?? false)
        ? 'Returned — ${o.officeNote!.trim()}'
        : null;
    return Material(
      color: Colors.white,
      child: InkWell(
        onTap: () => Navigator.of(context).push(MaterialPageRoute(builder: (_) => OrderScreen(orderId: o.id))),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(
                      o.customerName ?? 'Customer',
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14, color: Color(0xFF0F172A)),
                    ),
                  ),
                  const SizedBox(width: 8),
                  Text(o.number, style: const TextStyle(fontSize: 12, color: Color(0xFF556070))),
                ],
              ),
              const SizedBox(height: 3),
              Text(
                '${dayLabel(o.deliveryDate)} · ${qty(o.pallets)} pallets · ${gbp(total)}',
                style: const TextStyle(fontSize: 12, color: Color(0xFF556070)),
              ),
              const SizedBox(height: 6),
              StatusPill(o.status, label: pillLabel),
            ],
          ),
        ),
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  final String label;
  final bool selected;
  final VoidCallback onTap;
  const _Chip({required this.label, required this.selected, required this.onTap});

  @override
  Widget build(BuildContext context) {
    return Material(
      color: selected ? SalesHeader.navy : Colors.white,
      shape: StadiumBorder(side: BorderSide(color: selected ? SalesHeader.navy : const Color(0xFFCBD2DD))),
      child: InkWell(
        customBorder: const StadiumBorder(),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
          child: Text(
            label,
            style: TextStyle(
              fontSize: 13,
              fontWeight: FontWeight.w500,
              color: selected ? Colors.white : const Color(0xFF334155),
            ),
          ),
        ),
      ),
    );
  }
}

/// The white search box in the navy header.
class SalesSearchField extends StatelessWidget {
  final TextEditingController controller;
  final String hint;
  final ValueChanged<String> onChanged;
  final bool autofocus;
  const SalesSearchField({
    super.key,
    required this.controller,
    required this.hint,
    required this.onChanged,
    this.autofocus = false,
  });

  @override
  Widget build(BuildContext context) {
    return TextField(
      controller: controller,
      autofocus: autofocus,
      onChanged: onChanged,
      decoration: InputDecoration(
        hintText: hint,
        prefixIcon: const Icon(Icons.search, size: 20),
        fillColor: Colors.white,
        isDense: true,
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: BorderSide.none),
        enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: BorderSide.none),
        focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(12), borderSide: BorderSide.none),
      ),
    );
  }
}

class ErrorRetry extends StatelessWidget {
  final String message;
  final VoidCallback onRetry;
  const ErrorRetry({super.key, required this.message, required this.onRetry});

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              message,
              textAlign: TextAlign.center,
              style: const TextStyle(color: Color(0xFF556070)),
            ),
            const SizedBox(height: 14),
            OutlinedButton(onPressed: onRetry, child: const Text('Try again')),
          ],
        ),
      ),
    );
  }
}
