import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../data/models.dart';
import '../../data/sales_models.dart';
import '../account/account_sheet.dart';
import '../gate/gate_screen.dart';
import '../sales/approval.dart';
import '../sales/order_screen.dart';
import '../sales/sales_format.dart';
import '../sales/sales_providers.dart';
import '../sales/sales_shell.dart';

const _ground = Color(0xFFF2F4F7);
const _ink = Color(0xFF13201A);
const _muted = Color(0xFF556070);
const _line = Color(0xFFE5E9EF);

/// How many waiting orders the card lists before "See all".
const _cardRows = 3;

/// Home for the super-admin and Colin (design 3, "Today"): the orders waiting
/// for approval on top, each approvable from here, then the modules — Sales,
/// and Gate & Barrier (live cameras, open / close). A sales rep never sees this: with one module
/// they go straight into it.
class TodayScreen extends ConsumerStatefulWidget {
  final User user;
  const TodayScreen({super.key, required this.user});

  @override
  ConsumerState<TodayScreen> createState() => _TodayScreenState();
}

class _TodayScreenState extends ConsumerState<TodayScreen> with WidgetsBindingObserver {
  Timer? _poll;
  final _approving = <int>{};
  bool _refreshing = false;

  Future<void> _refresh() async {
    if (_refreshing) return;
    setState(() => _refreshing = true);
    try {
      // At least a beat of spinning, even when the server answers at once: a refresh that
      // finishes before the eye sees it looks like a button that does nothing.
      await Future.wait([
        ref.refresh(overviewOrdersProvider.future),
        Future<void>.delayed(const Duration(milliseconds: 700)),
      ]);
    } catch (_) {
      // The card shows the error with its own Try again.
    } finally {
      if (mounted) setState(() => _refreshing = false);
    }
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _poll = Timer.periodic(const Duration(seconds: 60), (_) => ref.invalidate(overviewOrdersProvider));
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) ref.invalidate(overviewOrdersProvider);
  }

  @override
  void dispose() {
    _poll?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  void _openSales() => Navigator.of(context).push(MaterialPageRoute(builder: (_) => const SalesShell(overview: true)));

  void _openOrder(SalesOrder o) =>
      Navigator.of(context).push(MaterialPageRoute(builder: (_) => OrderScreen(orderId: o.id, overview: true)));

  Future<void> _approve(SalesOrder o, double total) async {
    if (!await confirmApprove(context, o, total) || !mounted) return;
    setState(() => _approving.add(o.id));
    try {
      final no = await ref.read(salesApiProvider).approve(o);
      ref.invalidate(overviewOrdersProvider);
      if (mounted) ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text('Approved as order $no')));
    } catch (e) {
      if (mounted) showError(context, apiMessage(e));
    } finally {
      if (mounted) setState(() => _approving.remove(o.id));
    }
  }

  @override
  Widget build(BuildContext context) {
    final orders = ref.watch(overviewOrdersProvider);
    final products = ref.watch(productsProvider).valueOrNull ?? const {};
    final all = orders.valueOrNull ?? const <SalesOrder>[];
    // Longest waiting first, as the office's pile reads.
    final waiting = all.where((o) => o.status == SalesOrderStatus.pending).toList()
      ..sort((a, b) => (a.dateSent ?? a.dateCreated).compareTo(b.dateSent ?? b.dateCreated));
    final live = all.where((o) => o.status == SalesOrderStatus.approved || o.status == SalesOrderStatus.planned).length;

    return Scaffold(
      backgroundColor: _ground,
      // No pull-to-refresh, and no bounce: dragging the page down showed a white gap above the
      // navy header (the user's call, 2026-11-06). The card's own button refreshes instead.
      body: ScrollConfiguration(
        behavior: ScrollConfiguration.of(context).copyWith(overscroll: false),
        child: ListView(
          physics: const ClampingScrollPhysics(),
          padding: EdgeInsets.zero,
          children: [
            // The card overlaps the navy band by drawing the band BEHIND it, not by shifting the
            // card up: a shifted (Transform.translate) card is hit-tested where it used to be, so
            // its top — the refresh button — barely took a tap (2026-11-06).
            Stack(
              children: [
                Positioned(
                  top: 0,
                  left: 0,
                  right: 0,
                  height: MediaQuery.of(context).padding.top + _Header.height + _cardOverlap,
                  child: const ColoredBox(color: SalesHeader.navy),
                ),
                Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    _Header(user: widget.user),
                    Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 14),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          _WaitingCard(
                            orders: orders,
                            waiting: waiting,
                            products: products,
                            approving: _approving,
                            onOpen: _openOrder,
                            onApprove: _approve,
                            onSeeAll: _openSales,
                            onRetry: () => ref.invalidate(overviewOrdersProvider),
                            refreshing: _refreshing,
                            onRefresh: _refresh,
                          ),
                          const SizedBox(height: 22),
                          const Padding(padding: EdgeInsets.only(left: 4, bottom: 10), child: SectionLabel('Modules')),
                          Row(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Expanded(
                                child: _ModuleTile(
                                  icon: Icons.description_outlined,
                                  title: 'Sales',
                                  subtitle: orders.hasValue ? '$live live · ${waiting.length} waiting' : 'Every rep',
                                  onTap: _openSales,
                                ),
                              ),
                              const SizedBox(width: 12),
                              Expanded(
                                child: _ModuleTile(
                                  icon: Icons.fence_outlined,
                                  title: 'Gate & Barrier',
                                  subtitle: widget.user.canUseGateBarrier ? 'Cameras · open / close' : 'Not available',
                                  onTap: widget.user.canUseGateBarrier
                                      ? () => Navigator.of(
                                          context,
                                        ).push(MaterialPageRoute(builder: (_) => const GateScreen()))
                                      : null,
                                ),
                              ),
                            ],
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// How far the navy band runs down behind the top of the Waiting card.
const _cardOverlap = 46.0;

class _Header extends StatelessWidget {
  final User user;
  const _Header({required this.user});

  /// Below the status bar: 18 top padding, a fixed 56 row, 24 down to the card.
  static const height = 18.0 + 56 + 24;

  static const _dow = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
  static const _mon = [
    'January',
    'February',
    'March',
    'April',
    'May',
    'June',
    'July',
    'August',
    'September',
    'October',
    'November',
    'December',
  ];

  String get _initials {
    final s =
        '${(user.firstName ?? '').trim().characters.firstOrNull ?? ''}'
        '${(user.surname ?? '').trim().characters.firstOrNull ?? ''}';
    return s.isEmpty ? user.displayName.characters.first.toUpperCase() : s.toUpperCase();
  }

  @override
  Widget build(BuildContext context) {
    final now = DateTime.now();
    // No colour of its own: the band behind it (TodayScreen) is the navy.
    return Container(
      height: MediaQuery.of(context).padding.top + height,
      padding: EdgeInsets.fromLTRB(20, MediaQuery.of(context).padding.top + 18, 16, 24),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '${_dow[now.weekday - 1]} ${now.day} ${_mon[now.month - 1]}',
                  style: TextStyle(fontSize: 13, color: Colors.white.withValues(alpha: 0.75)),
                ),
                const Text(
                  'Today',
                  style: TextStyle(fontSize: 26, fontWeight: FontWeight.w700, color: Colors.white),
                ),
              ],
            ),
          ),
          Tooltip(
            message: 'Account',
            child: Material(
              color: Colors.transparent,
              shape: CircleBorder(side: BorderSide(color: Colors.white.withValues(alpha: 0.45), width: 2)),
              child: InkWell(
                customBorder: const CircleBorder(),
                onTap: () => showAccountSheet(context),
                child: SizedBox(
                  width: 44,
                  height: 44,
                  child: Center(
                    child: Text(
                      _initials,
                      style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w700, fontSize: 15),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// "Waiting for you": the first few orders with the office, each with Approve.
class _WaitingCard extends StatelessWidget {
  final AsyncValue<List<SalesOrder>> orders;
  final List<SalesOrder> waiting;
  final Map<int, Product> products;
  final Set<int> approving;
  final void Function(SalesOrder) onOpen;
  final Future<void> Function(SalesOrder, double) onApprove;
  final VoidCallback onSeeAll;
  final VoidCallback onRetry;
  final bool refreshing;
  final VoidCallback onRefresh;

  const _WaitingCard({
    required this.orders,
    required this.waiting,
    required this.products,
    required this.approving,
    required this.onOpen,
    required this.onApprove,
    required this.onSeeAll,
    required this.onRetry,
    required this.refreshing,
    required this.onRefresh,
  });

  @override
  Widget build(BuildContext context) {
    final Widget body;
    // A refresh blanks the list for its spinner, so it plainly reloads.
    if ((!orders.hasValue && orders.isLoading) || refreshing) {
      body = const Padding(
        padding: EdgeInsets.all(24),
        child: Center(child: CircularProgressIndicator()),
      );
    } else if (!orders.hasValue && orders.hasError) {
      body = Padding(
        padding: const EdgeInsets.symmetric(vertical: 12),
        child: Column(
          children: [
            Text(
              apiMessage(orders.error!),
              textAlign: TextAlign.center,
              style: const TextStyle(color: _muted),
            ),
            TextButton(onPressed: onRetry, child: const Text('Try again')),
          ],
        ),
      );
    } else if (waiting.isEmpty) {
      body = const Padding(
        padding: EdgeInsets.fromLTRB(0, 4, 0, 14),
        child: Text('Nothing waiting. All caught up.', style: TextStyle(fontSize: 14, color: _muted)),
      );
    } else {
      body = Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          for (final o in waiting.take(_cardRows))
            _WaitingRow(
              order: o,
              total: orderTotal(o.items, products),
              busy: approving.contains(o.id),
              onOpen: () => onOpen(o),
              onApprove: () => onApprove(o, orderTotal(o.items, products)),
            ),
          const Divider(height: 1, color: _line),
          TextButton(
            onPressed: onSeeAll,
            style: TextButton.styleFrom(
              minimumSize: const Size.fromHeight(48),
              foregroundColor: SalesHeader.navy,
              textStyle: const TextStyle(fontFamily: 'Poppins', fontSize: 14, fontWeight: FontWeight.w600),
            ),
            child: Text(waiting.length > _cardRows ? 'See all ${waiting.length} in Sales' : 'Open Sales'),
          ),
        ],
      );
    }

    return Container(
      padding: const EdgeInsets.fromLTRB(18, 16, 18, 4),
      decoration: BoxDecoration(
        color: Colors.white,
        borderRadius: BorderRadius.circular(20),
        boxShadow: const [BoxShadow(color: Color(0x1F101828), blurRadius: 24, offset: Offset(0, 8))],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const Expanded(
                child: Text(
                  'Waiting for you',
                  style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700, color: _ink),
                ),
              ),
              // The count and the ↻ are ONE target, at least 48 square, so a tap anywhere near
              // either refreshes. (No Transform here: a shifted widget is hit-tested unshifted.)
              Padding(
                padding: EdgeInsets.zero,
                child: Semantics(
                  button: true,
                  label: 'Refresh',
                  child: InkWell(
                    onTap: refreshing ? null : onRefresh,
                    borderRadius: BorderRadius.circular(12),
                    child: ConstrainedBox(
                      constraints: const BoxConstraints(minHeight: 48, minWidth: 48),
                      child: Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 8),
                        child: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            if (waiting.isNotEmpty) ...[
                              Text(
                                '${waiting.length} ${waiting.length == 1 ? 'order' : 'orders'}',
                                style: const TextStyle(
                                  fontSize: 13,
                                  fontWeight: FontWeight.w700,
                                  color: Color(0xFFB45309),
                                ),
                              ),
                              const SizedBox(width: 8),
                            ],
                            _SpinningRefresh(spinning: refreshing),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          body,
        ],
      ),
    );
  }
}

class _WaitingRow extends StatelessWidget {
  final SalesOrder order;
  final double total;
  final bool busy;
  final VoidCallback onOpen;
  final VoidCallback onApprove;
  const _WaitingRow({
    required this.order,
    required this.total,
    required this.busy,
    required this.onOpen,
    required this.onApprove,
  });

  @override
  Widget build(BuildContext context) {
    final o = order;
    return InkWell(
      onTap: onOpen,
      child: Container(
        padding: const EdgeInsets.symmetric(vertical: 12),
        decoration: const BoxDecoration(
          border: Border(top: BorderSide(color: _line)),
        ),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    o.customerName ?? 'Customer',
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600, color: _ink),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    '${o.salesRepName ?? 'No rep'} · ${dayLabel(o.deliveryDate)} · ${gbp(total)}',
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(fontSize: 13, color: _muted),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 10),
            busy
                ? const SizedBox(
                    width: 38,
                    height: 38,
                    child: Padding(padding: EdgeInsets.all(9), child: CircularProgressIndicator(strokeWidth: 2.5)),
                  )
                : ApproveButton(compact: true, onPressed: o.items.isEmpty ? null : onApprove),
          ],
        ),
      ),
    );
  }
}

class _ModuleTile extends StatelessWidget {
  final IconData icon;
  final String title;
  final String subtitle;
  final VoidCallback? onTap;
  const _ModuleTile({required this.icon, required this.title, required this.subtitle, this.onTap});

  @override
  Widget build(BuildContext context) {
    final enabled = onTap != null;
    return Material(
      color: Colors.white,
      borderRadius: BorderRadius.circular(18),
      elevation: 0,
      shadowColor: const Color(0x14101A23),
      child: InkWell(
        borderRadius: BorderRadius.circular(18),
        onTap: onTap,
        child: Container(
          height: 128,
          padding: const EdgeInsets.all(16),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(18),
            boxShadow: const [BoxShadow(color: Color(0x0F101828), blurRadius: 8, offset: Offset(0, 2))],
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Icon(icon, size: 28, color: enabled ? SalesHeader.navy : const Color(0xFF6B7280)),
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    title,
                    style: TextStyle(
                      fontSize: 16,
                      fontWeight: FontWeight.w700,
                      color: enabled ? _ink : const Color(0xFF374151),
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(subtitle, style: const TextStyle(fontSize: 13, color: _muted)),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The ↻ refresh glyph, turning while a refresh runs. A character, not an icon: build 6's iOS
/// icon font has no refresh glyph (restart_alt is Android-only code, so the iOS build shook it
/// out), and a patch cannot add one. The system font draws ↻.
class _SpinningRefresh extends StatefulWidget {
  final bool spinning;
  const _SpinningRefresh({required this.spinning});

  @override
  State<_SpinningRefresh> createState() => _SpinningRefreshState();
}

class _SpinningRefreshState extends State<_SpinningRefresh> with SingleTickerProviderStateMixin {
  late final AnimationController _turn = AnimationController(vsync: this, duration: const Duration(milliseconds: 800));

  @override
  void initState() {
    super.initState();
    if (widget.spinning) _turn.repeat();
  }

  @override
  void didUpdateWidget(_SpinningRefresh old) {
    super.didUpdateWidget(old);
    if (widget.spinning && !_turn.isAnimating) {
      _turn.repeat();
    } else if (!widget.spinning && _turn.isAnimating) {
      // Finish the turn it is on rather than stopping at an odd angle.
      _turn.forward().then((_) {
        if (mounted && !widget.spinning) _turn.value = 0;
      });
    }
  }

  @override
  void dispose() {
    _turn.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => RotationTransition(
    turns: _turn,
    child: SizedBox(
      width: 24,
      height: 24,
      child: Center(
        child: Text(
          '↻',
          textAlign: TextAlign.center,
          textHeightBehavior: const TextHeightBehavior(
            applyHeightToFirstAscent: false,
            applyHeightToLastDescent: false,
          ),
          style: TextStyle(
            fontSize: 21,
            height: 1,
            color: widget.spinning ? SalesHeader.navy : _muted,
            fontFamily: '.SF UI Text',
          ),
        ),
      ),
    ),
  );
}
