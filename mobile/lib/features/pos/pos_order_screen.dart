import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../data/pos_api.dart';
import '../sales/sales_format.dart';
import 'pos_providers.dart';

const _ground = Color(0xFFF2F4F7);
const _ink = Color(0xFF13201A);
const _muted = Color(0xFF556070);
const _line = Color(0xFFE5E9EF);
const _green = Color(0xFF15803D);

/// One POS order's checklist (design A): tick the products a POS board is made
/// up for, then POS made up. Made lines turn green and lock. The order leaves
/// the list once every line is made, or on Complete (not every product gets a
/// board), and the foot then says so.
class PosOrderScreen extends ConsumerStatefulWidget {
  final PosOrder order;
  const PosOrderScreen({super.key, required this.order});

  @override
  ConsumerState<PosOrderScreen> createState() => _PosOrderScreenState();
}

class _PosOrderScreenState extends ConsumerState<PosOrderScreen> {
  late PosOrder _order = widget.order;
  final _selected = <int>{};
  bool _busy = false;

  void _toggle(PosLine l) {
    if (l.made || _busy || _order.done) return;
    setState(() => _selected.contains(l.productId) ? _selected.remove(l.productId) : _selected.add(l.productId));
  }

  Future<void> _confirm() async {
    if (_selected.isEmpty || _busy) return;
    setState(() => _busy = true);
    try {
      final updated = await ref.read(posApiProvider).made(_order.id, _selected.toList());
      ref.invalidate(posOrdersProvider);
      if (!mounted) return;
      setState(() {
        _order = updated;
        _selected.clear();
      });
    } catch (e) {
      if (mounted) showError(context, apiMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  /// Finish the order as it stands: not every product gets a board. Anything
  /// ticked on screen is saved with it. Asks first when boards are left unmade.
  Future<void> _complete() async {
    if (_busy) return;
    final o = _order;
    final madeAfter = o.made + _selected.length;
    final left = o.total - madeAfter;
    if (left > 0) {
      final ok = await confirm(
        context,
        'Complete order ${o.orderNo} with $madeAfter of ${o.total} boards made up?\n\n'
        '$left ${left == 1 ? 'product has' : 'products have'} no board. The order will leave your list.',
        ok: 'Complete',
      );
      if (!ok || !mounted) return;
    }
    setState(() => _busy = true);
    try {
      final updated = await ref.read(posApiProvider).complete(o.id, _selected.toList());
      ref.invalidate(posOrdersProvider);
      if (!mounted) return;
      setState(() {
        _order = updated;
        _selected.clear();
      });
    } catch (e) {
      if (mounted) showError(context, apiMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final o = _order;
    final n = _selected.length;
    return Scaffold(
      backgroundColor: _ground,
      body: Column(
        children: [
          SalesHeader(
            overline: o.customerName ?? 'Customer',
            title: 'Order ${o.orderNo}',
            leading: HeaderIconButton(
              icon: Icons.arrow_back,
              tooltip: 'Back',
              onPressed: () => Navigator.of(context).pop(),
            ),
            bottom: Text(
              o.deliveryDate == null ? 'Goes ASAP' : 'Goes ${dayLabel(o.deliveryDate)}',
              style: TextStyle(fontSize: 13, color: Colors.white.withValues(alpha: 0.75)),
            ),
          ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(14, 16, 14, 16),
              children: [
                const Padding(padding: EdgeInsets.only(left: 4, bottom: 4), child: SectionLabel('Products on this order')),
                for (final l in o.lines)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: _LineTile(line: l, selected: _selected.contains(l.productId), onTap: () => _toggle(l)),
                  ),
              ],
            ),
          ),
          if (o.done)
            Container(
              width: double.infinity,
              padding: EdgeInsets.fromLTRB(16, 18, 16, 18 + MediaQuery.of(context).padding.bottom),
              decoration: const BoxDecoration(
                color: Color(0xFFF0FDF4),
                border: Border(top: BorderSide(color: Color(0xFFBBF7D0))),
              ),
              child: Column(
                children: [
                  Text(
                    !o.allMade
                        ? 'Completed with ${o.made} of ${o.total} boards made up'
                        : o.total == 1
                        ? 'The board is made up'
                        : 'All ${o.total} boards made up',
                    style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700, color: Color(0xFF14532D)),
                  ),
                  const SizedBox(height: 4),
                  const Text('This order has left your list.', style: TextStyle(fontSize: 13, color: Color(0xFF166534))),
                ],
              ),
            )
          else
            Container(
              padding: EdgeInsets.fromLTRB(16, 14, 16, 14 + MediaQuery.of(context).padding.bottom),
              decoration: const BoxDecoration(
                color: Colors.white,
                border: Border(top: BorderSide(color: _line)),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  Text(
                    n == 0 ? 'Tick the products you have a board for' : '$n selected',
                    textAlign: TextAlign.center,
                    style: const TextStyle(fontSize: 13, color: _muted),
                  ),
                  const SizedBox(height: 10),
                  // Complete finishes the order as it stands (not every product gets a
                  // board); POS made up saves the ticks and keeps the order open.
                  Row(
                    children: [
                      Expanded(
                        flex: 9,
                        child: OutlinedButton(
                          onPressed: _busy ? null : _complete,
                          style: OutlinedButton.styleFrom(
                            minimumSize: const Size.fromHeight(52),
                            backgroundColor: Colors.white,
                            foregroundColor: _ink,
                            side: const BorderSide(color: Color(0xFFCDD3DC)),
                            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                            textStyle: const TextStyle(fontFamily: 'Poppins', fontSize: 15, fontWeight: FontWeight.w600),
                          ),
                          child: const Text('Complete'),
                        ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        flex: 13,
                        child: FilledButton(
                          onPressed: n == 0 || _busy ? null : _confirm,
                          style: FilledButton.styleFrom(
                            minimumSize: const Size.fromHeight(52),
                            backgroundColor: _green,
                            foregroundColor: Colors.white,
                            disabledBackgroundColor: const Color(0xFFCBD5E1),
                            disabledForegroundColor: const Color(0xFF334155),
                            shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                            textStyle: const TextStyle(fontFamily: 'Poppins', fontSize: 15, fontWeight: FontWeight.w600),
                          ),
                          child: Text(
                            _busy
                                ? 'Saving…'
                                : n == 0
                                ? 'POS made up'
                                : 'POS made up ($n)',
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
    );
  }
}

class _LineTile extends StatelessWidget {
  final PosLine line;
  final bool selected;
  final VoidCallback onTap;
  const _LineTile({required this.line, required this.selected, required this.onTap});

  @override
  Widget build(BuildContext context) {
    final made = line.made;
    final on = made || selected;
    final boxColor = made ? _green : SalesHeader.navy;
    return Material(
      color: made
          ? const Color(0xFFF0FDF4)
          : selected
          ? const Color(0xFFEEF2FF)
          : Colors.white,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(12),
        side: const BorderSide(color: _line),
      ),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: made ? null : onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 68),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
            child: Row(
              children: [
                Container(
                  width: 26,
                  height: 26,
                  decoration: BoxDecoration(
                    color: on ? boxColor : Colors.white,
                    borderRadius: BorderRadius.circular(7),
                    border: Border.all(color: on ? boxColor : const Color(0xFF7C8A9C), width: 2),
                  ),
                  // A plain character, not Icons.check: release 1.0.0+6 never used that icon,
                  // so it is not in its tree-shaken icon font and a patch would draw it blank.
                  alignment: Alignment.center,
                  child: on
                      ? const Text(
                          '✓',
                          style: TextStyle(fontSize: 16, height: 1, fontWeight: FontWeight.w700, color: Colors.white),
                        )
                      : null,
                ),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        line.name,
                        style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: _ink),
                      ),
                      const SizedBox(height: 3),
                      Text(
                        made
                            ? [
                                'POS made up',
                                if (line.madeBy?.isNotEmpty ?? false) line.madeBy!,
                                dateTimeLabel(line.madeAt!),
                              ].join(' · ')
                            : '${line.detail} · board needed',
                        style: TextStyle(fontSize: 12, color: made ? const Color(0xFF166534) : _muted),
                      ),
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
}
