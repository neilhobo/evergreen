import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../data/sales_models.dart';
import 'edit_order_screen.dart';
import 'my_orders_screen.dart' show ErrorRetry;
import 'sales_format.dart';
import 'sales_providers.dart';

const _steps = ['Draft', 'With office', 'Approved', 'Transport', 'Out'];

const _ground = Color(0xFFF2F4F7);
const _ink = Color(0xFF13201A);
const _muted = Color(0xFF556070);
const _line = Color(0xFFE5E9EF);

/// One order, in design B's card style: a back button and the customer as a
/// bold title on a light ground, then cards — the tracker with what happens
/// next, the delivery details, the lines with their total, and the history —
/// with the actions its status allows along the foot (Edit / Send / Delete for
/// a draft, Recall while with the office), as TauriAdmin's My Orders panel.
class OrderScreen extends ConsumerStatefulWidget {
  final int orderId;
  const OrderScreen({super.key, required this.orderId});

  @override
  ConsumerState<OrderScreen> createState() => _OrderScreenState();
}

class _OrderScreenState extends ConsumerState<OrderScreen> {
  bool _busy = false;

  Future<void> _act(Future<void> Function() run, {bool pop = false}) async {
    setState(() => _busy = true);
    try {
      await run();
      ref.invalidate(salesOrdersProvider);
      if (pop) {
        if (mounted) Navigator.of(context).pop();
      } else {
        ref.invalidate(orderDetailProvider(widget.orderId));
      }
    } catch (e) {
      if (mounted) showError(context, apiMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _send(SalesOrder o, double total) async {
    final what = '${o.customerName ?? 'this customer'} (${o.ref}): ${qty(o.pallets)} pallets, ${gbp(total)}';
    final q = o.status == SalesOrderStatus.returned
        ? 'Resend this order to the office?\n\n$what'
        : 'Send this order to the office for approval?\n\n$what\n\nYou can take it back until the office approves it.';
    if (!await confirm(context, q, ok: 'Send')) return;
    await _act(() => ref.read(salesApiProvider).send(o.id));
  }

  Future<void> _recall(SalesOrder o) async {
    if (!await confirm(context, 'Take this order back from the office? It goes back to being a draft.', ok: 'Recall')) {
      return;
    }
    await _act(() => ref.read(salesApiProvider).recall(o.id));
  }

  Future<void> _delete(SalesOrder o) async {
    if (!await confirm(context, 'Delete the draft for ${o.customerName ?? 'this customer'}?', ok: 'Delete')) return;
    await _act(() => ref.read(salesApiProvider).delete(o.id), pop: true);
  }

  Future<void> _edit(SalesOrder o) async {
    await Navigator.of(context).push(MaterialPageRoute(builder: (_) => EditOrderScreen(existing: o)));
    ref.invalidate(orderDetailProvider(widget.orderId));
  }

  @override
  Widget build(BuildContext context) {
    final detail = ref.watch(orderDetailProvider(widget.orderId));
    final products = ref.watch(productsProvider).valueOrNull ?? const {};
    final o = detail.valueOrNull?.order;
    final canEdit = o != null && o.status.isDraft && !o.fromOffice;

    return Scaffold(
      backgroundColor: _ground,
      body: Column(
        children: [
          Padding(
            padding: EdgeInsets.fromLTRB(14, MediaQuery.of(context).padding.top + 12, 8, 8),
            child: Row(
              children: [
                _SquareButton(icon: Icons.arrow_back, tooltip: 'Back', onPressed: () => Navigator.of(context).pop()),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(o?.number ?? '', style: const TextStyle(fontSize: 12, color: _muted)),
                      Text(
                        o?.customerName ?? 'Order',
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontSize: 19, fontWeight: FontWeight.w700, color: _ink, height: 1.2),
                      ),
                    ],
                  ),
                ),
                if (canEdit)
                  PopupMenuButton<String>(
                    tooltip: 'More',
                    icon: const Icon(Icons.more_vert, color: _ink),
                    onSelected: (v) {
                      if (v == 'delete') _delete(o);
                    },
                    itemBuilder: (_) => const [PopupMenuItem(value: 'delete', child: Text('Delete draft'))],
                  ),
              ],
            ),
          ),
          Expanded(
            child: detail.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (e, _) => ErrorRetry(
                message: apiMessage(e),
                onRetry: () => ref.invalidate(orderDetailProvider(widget.orderId)),
              ),
              data: (d) => RefreshIndicator(
                onRefresh: () => ref.refresh(orderDetailProvider(widget.orderId).future),
                child: _Body(detail: d, products: products),
              ),
            ),
          ),
          if (o != null) _actions(o, orderTotal(o.items, products)),
        ],
      ),
    );
  }

  Widget _actions(SalesOrder o, double total) {
    if (o.fromOffice) return const SizedBox.shrink();
    final List<Widget> buttons;
    if (o.status.isDraft) {
      buttons = [
        Expanded(flex: 10, child: _OutlineButton(label: 'Edit', onPressed: _busy ? null : () => _edit(o))),
        const SizedBox(width: 10),
        Expanded(
          flex: 14,
          child: SalesButton(
            label: o.status == SalesOrderStatus.returned ? 'Resend to office' : 'Send to office',
            onPressed: _busy || o.items.isEmpty ? null : () => _send(o, total),
          ),
        ),
      ];
    } else if (o.status == SalesOrderStatus.pending) {
      buttons = [
        Expanded(child: _OutlineButton(label: 'Recall to change it', onPressed: _busy ? null : () => _recall(o))),
      ];
    } else {
      return const SizedBox.shrink();
    }
    return Padding(
      padding: EdgeInsets.fromLTRB(14, 10, 14, 12 + MediaQuery.of(context).padding.bottom),
      child: Row(children: buttons),
    );
  }
}

class _Body extends StatelessWidget {
  final SalesOrderDetail detail;
  final Map<int, Product> products;
  const _Body({required this.detail, required this.products});

  @override
  Widget build(BuildContext context) {
    final o = detail.order;
    final total = orderTotal(o.items, products);
    final address = (o.addressText ?? '').replaceAll('\n', ', ');
    return ListView(
      padding: const EdgeInsets.fromLTRB(14, 4, 14, 20),
      children: [
        if (o.status == SalesOrderStatus.returned && (o.officeNote?.trim().isNotEmpty ?? false)) ...[
          OfficeNote(o.officeNote!.trim()),
          const SizedBox(height: 12),
        ],
        if (o.fromOffice) ...[
          const _Card(
            child: Text(
              'Entered by the office with you as the sales rep. It can only be changed there.',
              style: TextStyle(fontSize: 13, color: _ink),
            ),
          ),
          const SizedBox(height: 12),
        ],
        _Card(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _Tracker(
                step: o.status.step,
                warn: o.status == SalesOrderStatus.returned,
                cancelled: o.status == SalesOrderStatus.cancelled,
              ),
              const SizedBox(height: 12),
              Row(
                children: [
                  StatusPill(o.status),
                  const Spacer(),
                  if (o.dateSent != null) Text('Sent ${ago(o.dateSent!)}', style: const TextStyle(fontSize: 12, color: _muted)),
                ],
              ),
              const SizedBox(height: 8),
              Text(nextStep(o), style: const TextStyle(fontSize: 13, color: _ink, height: 1.4)),
            ],
          ),
        ),
        const SizedBox(height: 12),
        _Card(
          child: Column(
            children: [
              _Row(icon: Icons.event_outlined, label: 'Delivery', value: dayLabel(o.deliveryDate)),
              _Row(icon: Icons.place_outlined, label: 'Deliver to', value: address.isEmpty ? '—' : address),
              if (o.poNumber?.isNotEmpty ?? false) _Row(icon: Icons.tag, label: 'PO number', value: o.poNumber!),
              if (o.comments?.isNotEmpty ?? false) _Row(icon: Icons.notes, label: 'Comments', value: o.comments!),
              if (o.pickingComments?.isNotEmpty ?? false)
                _Row(icon: Icons.inventory_2_outlined, label: 'Picking comments', value: o.pickingComments!),
            ],
          ),
        ),
        const SizedBox(height: 12),
        _Card(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _CardTitle('Products'),
              if (o.items.isEmpty) const Text('No products yet.', style: TextStyle(fontSize: 13, color: _muted)),
              for (final i in o.items)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 6),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      SizedBox(
                        width: 34,
                        child: Text(
                          qty(i.quantity ?? 0),
                          style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w700, color: _ink),
                        ),
                      ),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              products[i.productId]?.name ?? '#${i.productId}',
                              style: const TextStyle(fontSize: 14, color: _ink),
                            ),
                            Text(
                              '${products[i.productId]?.qtyUnit ?? 'pallets'} · ${i.price != null ? gbp(i.price!) : 'no price'}',
                              style: const TextStyle(fontSize: 12, color: _muted),
                            ),
                          ],
                        ),
                      ),
                      Text(gbp(lineTotal(i, products[i.productId])), style: const TextStyle(fontSize: 13, color: _ink)),
                    ],
                  ),
                ),
              if (o.hasPos)
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: 6),
                  child: Text('+ POS board', style: TextStyle(fontSize: 13, color: _muted)),
                ),
              const Divider(height: 20, color: _line),
              Row(
                children: [
                  Expanded(
                    child: Text('${qty(o.pallets)} pallets', style: const TextStyle(fontSize: 12, color: _muted)),
                  ),
                  Text(gbp(total), style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: _ink)),
                ],
              ),
            ],
          ),
        ),
        if (detail.events.isNotEmpty) ...[
          const SizedBox(height: 12),
          _Card(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _CardTitle('History'),
                for (final (k, e) in detail.events.indexed) _Event(e, latest: k == 0, last: k == detail.events.length - 1),
              ],
            ),
          ),
        ],
      ],
    );
  }
}

/// Design B's card: white, rounded, a soft shadow.
class _Card extends StatelessWidget {
  final Widget child;
  const _Card({required this.child});

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
    decoration: BoxDecoration(
      color: Colors.white,
      borderRadius: BorderRadius.circular(18),
      boxShadow: const [BoxShadow(color: Color(0x14101A23), blurRadius: 3, offset: Offset(0, 1))],
    ),
    child: child,
  );
}

class _CardTitle extends StatelessWidget {
  final String text;
  const _CardTitle(this.text);
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 6),
    child: Text(text, style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w700, color: _ink)),
  );
}

class _Row extends StatelessWidget {
  final IconData icon;
  final String label;
  final String value;
  const _Row({required this.icon, required this.label, required this.value});

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 6),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Icon(icon, size: 18, color: _muted),
        const SizedBox(width: 12),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(label, style: const TextStyle(fontSize: 12, color: _muted)),
              Text(value, style: const TextStyle(fontSize: 14, color: _ink)),
            ],
          ),
        ),
      ],
    ),
  );
}

class OfficeNote extends StatelessWidget {
  final String note;
  const OfficeNote(this.note, {super.key});
  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
    decoration: BoxDecoration(
      color: const Color(0xFFFDECDC),
      borderRadius: BorderRadius.circular(14),
      border: Border.all(color: const Color(0xFFF3C9A5)),
    ),
    child: Text.rich(
      TextSpan(
        children: [
          const TextSpan(
            text: 'Office note: ',
            style: TextStyle(fontWeight: FontWeight.w700),
          ),
          TextSpan(text: note),
        ],
      ),
      style: const TextStyle(fontSize: 13, height: 1.4, color: Color(0xFF7C2D06)),
    ),
  );
}

/// Five segments, the current step's label in bold (design B).
class _Tracker extends StatelessWidget {
  final int step;
  final bool warn;
  final bool cancelled;
  const _Tracker({required this.step, required this.warn, required this.cancelled});

  @override
  Widget build(BuildContext context) {
    Color colorOf(int k) {
      if (k > step) return const Color(0xFFDFE3EA);
      if (k == step && warn) return const Color(0xFFC2570C);
      if (k == step && cancelled) return const Color(0xFF991B1B);
      return SalesHeader.navy;
    }

    return Column(
      children: [
        Row(
          children: [
            for (var k = 0; k < _steps.length; k++)
              Expanded(
                child: Text(
                  _steps[k],
                  textAlign: k == 0 ? TextAlign.left : (k == _steps.length - 1 ? TextAlign.right : TextAlign.center),
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: k == step ? FontWeight.w700 : FontWeight.w500,
                    color: k == step ? _ink : _muted,
                  ),
                ),
              ),
          ],
        ),
        const SizedBox(height: 7),
        Row(
          children: [
            for (var k = 0; k < _steps.length; k++) ...[
              if (k > 0) const SizedBox(width: 4),
              Expanded(
                child: Container(
                  height: 6,
                  decoration: BoxDecoration(color: colorOf(k), borderRadius: BorderRadius.circular(3)),
                ),
              ),
            ],
          ],
        ),
      ],
    );
  }
}

/// A history entry on a timeline: the newest dot solid, older ones grey.
class _Event extends StatelessWidget {
  final SalesOrderEvent e;
  final bool latest;
  final bool last;
  const _Event(this.e, {required this.latest, required this.last});

  @override
  Widget build(BuildContext context) => IntrinsicHeight(
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SizedBox(
          width: 14,
          child: Column(
            children: [
              const SizedBox(height: 4),
              Container(
                width: 12,
                height: 12,
                decoration: BoxDecoration(
                  color: latest ? SalesHeader.navy : const Color(0xFFA9B4C2),
                  shape: BoxShape.circle,
                ),
              ),
              if (!last) Expanded(child: Container(width: 2, color: _line)),
            ],
          ),
        ),
        const SizedBox(width: 12),
        Expanded(
          child: Padding(
            padding: EdgeInsets.only(bottom: last ? 0 : 14),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text.rich(
                  TextSpan(
                    children: [
                      TextSpan(
                        text: e.who,
                        style: const TextStyle(fontWeight: FontWeight.w700),
                      ),
                      TextSpan(text: ' ${e.text}'),
                    ],
                  ),
                  style: const TextStyle(fontSize: 13, color: _ink),
                ),
                for (final c in e.changes) Text('• $c', style: const TextStyle(fontSize: 12, color: _ink)),
                Text(dateTimeLabel(e.date), style: const TextStyle(fontSize: 12, color: _muted)),
              ],
            ),
          ),
        ),
      ],
    ),
  );
}

class _SquareButton extends StatelessWidget {
  final IconData icon;
  final String tooltip;
  final VoidCallback onPressed;
  const _SquareButton({required this.icon, required this.tooltip, required this.onPressed});

  @override
  Widget build(BuildContext context) => Material(
    color: Colors.white,
    borderRadius: BorderRadius.circular(12),
    child: IconButton(
      tooltip: tooltip,
      onPressed: onPressed,
      icon: Icon(icon, color: _ink, size: 21),
      constraints: const BoxConstraints.tightFor(width: 44, height: 44),
    ),
  );
}

/// Design B's secondary button: white with a thin border.
class _OutlineButton extends StatelessWidget {
  final String label;
  final VoidCallback? onPressed;
  const _OutlineButton({required this.label, this.onPressed});

  @override
  Widget build(BuildContext context) => OutlinedButton(
    onPressed: onPressed,
    style: OutlinedButton.styleFrom(
      minimumSize: const Size.fromHeight(50),
      backgroundColor: Colors.white,
      foregroundColor: _ink,
      side: const BorderSide(color: Color(0xFFCDD3DC)),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
      textStyle: const TextStyle(fontFamily: 'Poppins', fontSize: 15, fontWeight: FontWeight.w600),
    ),
    child: Text(label),
  );
}

/// Design A's 50px buttons: navy filled, or the pale secondary.
class SalesButton extends StatelessWidget {
  final String label;
  final bool primary;
  final VoidCallback? onPressed;
  const SalesButton({super.key, required this.label, this.primary = true, this.onPressed});

  @override
  Widget build(BuildContext context) {
    return FilledButton(
      onPressed: onPressed,
      style: FilledButton.styleFrom(
        minimumSize: const Size.fromHeight(50),
        backgroundColor: primary ? SalesHeader.navy : const Color(0xFFEEF1F5),
        foregroundColor: primary ? Colors.white : SalesHeader.navy,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
        textStyle: const TextStyle(fontFamily: 'Poppins', fontSize: 15, fontWeight: FontWeight.w600),
      ),
      child: Text(label),
    );
  }
}
