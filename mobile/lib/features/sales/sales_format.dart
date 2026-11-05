import 'package:flutter/material.dart';

import '../../data/sales_models.dart';

const _dow = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const _mon = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

/// "Wed 8 Oct", or ASAP for no date — the way the office talks about a delivery.
String dayLabel(DateTime? d) => d == null ? 'ASAP' : '${_dow[d.weekday - 1]} ${d.day} ${_mon[d.month - 1]}';

String dateTimeLabel(DateTime d) {
  final hh = d.hour.toString().padLeft(2, '0');
  final mm = d.minute.toString().padLeft(2, '0');
  return '${d.day} ${_mon[d.month - 1]} $hh:$mm';
}

String ago(DateTime d) {
  final mins = DateTime.now().difference(d).inMinutes;
  if (mins < 1) return 'just now';
  if (mins < 60) return '$mins min ago';
  final h = (mins / 60).round();
  if (h < 24) return '$h h ago';
  final days = (h / 24).round();
  return days == 1 ? 'yesterday' : '$days days ago';
}

String gbp(double n) {
  final fixed = n.toStringAsFixed(2);
  final parts = fixed.split('.');
  final whole = parts[0].replaceAllMapped(RegExp(r'\B(?=(\d{3})+(?!\d))'), (_) => ',');
  return '£$whole.${parts[1]}';
}

String qty(double n) => n == n.roundToDouble() ? n.toInt().toString() : n.toStringAsFixed(2);

String titleCase(String? s) =>
    (s ?? '').toLowerCase().replaceAllMapped(RegExp(r'\b\w'), (m) => m.group(0)!.toUpperCase());

/// What happens next, in a line (TauriAdmin's nextStep).
String nextStep(SalesOrder o) => switch (o.status) {
  SalesOrderStatus.draft => 'Only you can see this order until you send it to the office.',
  SalesOrderStatus.returned => 'The office sent this back. Change it and resend.',
  SalesOrderStatus.pending => o.dateSent != null ? 'With the office since ${ago(o.dateSent!)}.' : 'With the office.',
  SalesOrderStatus.approved => 'Approved. Waiting for transport to be allocated.',
  SalesOrderStatus.planned =>
    'Transport allocated for ${dayLabel(o.loadDate)}${o.loadHaulier != null ? ' with ${titleCase(o.loadHaulier)}' : ''}.',
  SalesOrderStatus.dispatched => 'Dispatched ${dayLabel(o.dateDispatched)}.',
  SalesOrderStatus.cancelled => 'The office cancelled this order.',
};

/// The pill colours from design A (background, foreground).
(Color, Color) statusColors(SalesOrderStatus s) => switch (s) {
  SalesOrderStatus.draft => (const Color(0xFFEEF1F5), const Color(0xFF475569)),
  SalesOrderStatus.returned => (const Color(0xFFFDECDC), const Color(0xFF9A3D06)),
  SalesOrderStatus.pending => (const Color(0xFFE5EEFC), const Color(0xFF1D4ED8)),
  SalesOrderStatus.approved => (const Color(0xFFE2F4EA), const Color(0xFF166534)),
  SalesOrderStatus.planned => (const Color(0xFFEFE7FB), const Color(0xFF6D28D9)),
  SalesOrderStatus.dispatched => (const Color(0xFFE8ECEF), const Color(0xFF334155)),
  SalesOrderStatus.cancelled => (const Color(0xFFFDE8E8), const Color(0xFF991B1B)),
};

class StatusPill extends StatelessWidget {
  final SalesOrderStatus status;
  final String? label;
  const StatusPill(this.status, {super.key, this.label});

  @override
  Widget build(BuildContext context) {
    final (bg, fg) = statusColors(status);
    return Container(
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
          Flexible(
            child: Text(
              label ?? status.label,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 11, fontWeight: FontWeight.w600, color: fg),
            ),
          ),
        ],
      ),
    );
  }
}

/// Design A's navy header band.
class SalesHeader extends StatelessWidget {
  final String? overline;
  final String title;
  final Widget? leading;
  final List<Widget> actions;
  final Widget? bottom;
  const SalesHeader({
    super.key,
    this.overline,
    required this.title,
    this.leading,
    this.actions = const [],
    this.bottom,
  });

  static const navy = Color(0xFF1F2A44);

  @override
  Widget build(BuildContext context) {
    return Container(
      color: navy,
      padding: EdgeInsets.fromLTRB(18, MediaQuery.of(context).padding.top + 14, 18, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              if (leading != null) ...[leading!, const SizedBox(width: 10)],
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (overline != null)
                      Text(overline!, style: TextStyle(fontSize: 12, color: Colors.white.withValues(alpha: 0.75))),
                    Text(
                      title,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(fontSize: 21, fontWeight: FontWeight.w600, color: Colors.white),
                    ),
                  ],
                ),
              ),
              ...actions,
            ],
          ),
          if (bottom != null) ...[const SizedBox(height: 12), bottom!],
        ],
      ),
    );
  }
}

class HeaderIconButton extends StatelessWidget {
  final IconData icon;
  final String tooltip;
  final VoidCallback? onPressed;
  const HeaderIconButton({super.key, required this.icon, required this.tooltip, this.onPressed});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(left: 8),
      child: Material(
        color: Colors.white.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(12),
        child: IconButton(
          tooltip: tooltip,
          onPressed: onPressed,
          icon: Icon(icon, color: Colors.white, size: 21),
          constraints: const BoxConstraints.tightFor(width: 44, height: 44),
        ),
      ),
    );
  }
}

class SectionLabel extends StatelessWidget {
  final String text;
  const SectionLabel(this.text, {super.key});
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 6),
    child: Text(
      text.toUpperCase(),
      style: const TextStyle(fontSize: 11, fontWeight: FontWeight.w600, color: Color(0xFF556070), letterSpacing: 0.6),
    ),
  );
}

Future<bool> confirm(BuildContext context, String message, {String ok = 'OK'}) async {
  final r = await showDialog<bool>(
    context: context,
    builder: (c) => AlertDialog(
      content: Text(message),
      actions: [
        TextButton(onPressed: () => Navigator.pop(c, false), child: const Text('Cancel')),
        FilledButton(
          style: FilledButton.styleFrom(backgroundColor: SalesHeader.navy),
          onPressed: () => Navigator.pop(c, true),
          child: Text(ok),
        ),
      ],
    ),
  );
  return r ?? false;
}

void showError(BuildContext context, String message) {
  ScaffoldMessenger.of(context)
    ..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));
}
