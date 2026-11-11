import 'package:flutter/material.dart';

import '../../data/sales_models.dart';
import 'sales_format.dart';

/// The office's answers to a sent order, from the phone (the Sales overview
/// and the Today screen): Approve as sent, or Return to rep with a note.

const approveGreen = Color(0xFF15803D);

/// Approve this order as the rep sent it?
Future<bool> confirmApprove(BuildContext context, SalesOrder o, double total) => confirm(
  context,
  'Approve this order?\n\n'
  '${o.customerName ?? 'Customer'} · ${o.salesRepName ?? 'No rep'}\n'
  '${dayLabel(o.deliveryDate)} · ${qty(o.pallets)} pallets · ${gbp(total)}\n\n'
  'It becomes an open order as sent. Add the haulier in Open Orders.',
  ok: 'Approve',
);

/// What the rep must change; null = cancelled. A note is required, as the
/// server asks for one.
Future<String?> askReturnNote(BuildContext context, SalesOrder o) => showDialog<String>(
  context: context,
  builder: (_) => _ReturnDialog(order: o),
);

class _ReturnDialog extends StatefulWidget {
  final SalesOrder order;
  const _ReturnDialog({required this.order});

  @override
  State<_ReturnDialog> createState() => _ReturnDialogState();
}

class _ReturnDialogState extends State<_ReturnDialog> {
  final _note = TextEditingController();

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final ready = _note.text.trim().isNotEmpty;
    return AlertDialog(
      title: const Text('Return to rep'),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Say what ${widget.order.salesRepName ?? 'the rep'} needs to change. They see this note on the order.',
            style: const TextStyle(fontSize: 13),
          ),
          const SizedBox(height: 12),
          TextField(
            controller: _note,
            autofocus: true,
            minLines: 2,
            maxLines: 5,
            textCapitalization: TextCapitalization.sentences,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(hintText: 'e.g. Delivery date is too soon'),
          ),
        ],
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(
          style: FilledButton.styleFrom(backgroundColor: SalesHeader.navy),
          onPressed: ready ? () => Navigator.pop(context, _note.text.trim()) : null,
          child: const Text('Return'),
        ),
      ],
    );
  }
}

/// The green Approve button (50px, as the order screen's others).
class ApproveButton extends StatelessWidget {
  final VoidCallback? onPressed;
  final bool compact;
  const ApproveButton({super.key, this.onPressed, this.compact = false});

  @override
  Widget build(BuildContext context) => FilledButton(
    onPressed: onPressed,
    style: FilledButton.styleFrom(
      minimumSize: compact ? const Size(0, 38) : const Size.fromHeight(50),
      padding: compact ? const EdgeInsets.symmetric(horizontal: 14) : null,
      backgroundColor: approveGreen,
      foregroundColor: Colors.white,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(compact ? 10 : 12)),
      textStyle: TextStyle(fontFamily: 'Poppins', fontSize: compact ? 13 : 15, fontWeight: FontWeight.w600),
    ),
    child: const Text('Approve'),
  );
}
