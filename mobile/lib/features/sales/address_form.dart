import 'package:flutter/material.dart';

import '../../core/api/api_client.dart';
import '../../data/sales_models.dart';
import 'order_screen.dart' show SalesButton;
import 'sales_format.dart';

/// What the address form hands back: the fields and the delivery note info,
/// or that the address is to go.
class AddressDraft {
  final AddressInput input;
  final String deliveryNote;
  const AddressDraft(this.input, this.deliveryNote);
}

/// Add or change one delivery address. [onSave] does the saving (or just
/// keeps the draft, for a customer not yet created); if it throws, the form
/// stays open with the message so nothing typed is lost. [onDelete] shows a
/// Delete button.
Future<bool> openAddressForm(
  BuildContext context, {
  String title = 'New address',
  AddressInput? initial,
  String? initialNote,
  required Future<void> Function(AddressDraft draft) onSave,
  Future<void> Function()? onDelete,
}) async {
  final r = await Navigator.of(context).push<bool>(
    MaterialPageRoute(
      fullscreenDialog: true,
      builder: (_) => _AddressForm(title: title, initial: initial, initialNote: initialNote, onSave: onSave, onDelete: onDelete),
    ),
  );
  return r ?? false;
}

class _AddressForm extends StatefulWidget {
  final String title;
  final AddressInput? initial;
  final String? initialNote;
  final Future<void> Function(AddressDraft draft) onSave;
  final Future<void> Function()? onDelete;
  const _AddressForm({required this.title, this.initial, this.initialNote, required this.onSave, this.onDelete});

  @override
  State<_AddressForm> createState() => _AddressFormState();
}

class _AddressFormState extends State<_AddressForm> {
  late final _name = TextEditingController(text: widget.initial?.addressName ?? '');
  late final _lines = [for (var i = 0; i < 5; i++) TextEditingController(text: widget.initial?.lines[i] ?? '')];
  late final _contact = TextEditingController(text: widget.initial?.contactName ?? '');
  late final _phone = TextEditingController(text: widget.initial?.telephone ?? '');
  late final _phone2 = TextEditingController(text: widget.initial?.telephone2 ?? '');
  late final _email = TextEditingController(text: widget.initial?.email ?? '');
  late final _note = TextEditingController(text: widget.initialNote ?? '');
  bool _busy = false;
  String? _error;

  @override
  void dispose() {
    for (final c in [_name, ..._lines, _contact, _phone, _phone2, _email, _note]) {
      c.dispose();
    }
    super.dispose();
  }

  Future<void> _save() async {
    final input = AddressInput(
      addressName: _name.text,
      lines: [for (final c in _lines) c.text],
      contactName: _contact.text,
      telephone: _phone.text,
      telephone2: _phone2.text,
      email: _email.text,
    );
    if (input.lines.every((l) => l.trim().isEmpty)) {
      setState(() => _error = 'Enter at least the first line of the address.');
      return;
    }
    final email = _email.text.trim();
    if (email.isNotEmpty && !RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$').hasMatch(email)) {
      setState(() => _error = 'That email address doesn\'t look right.');
      return;
    }
    await _run(() => widget.onSave(AddressDraft(input, _note.text.trim())));
  }

  Future<void> _delete() async {
    if (!await confirm(context, 'Delete this address?', ok: 'Delete')) return;
    await _run(widget.onDelete!);
  }

  Future<void> _run(Future<void> Function() action) async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await action();
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) setState(() => _error = apiMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Widget _field(TextEditingController c, String label, {TextInputType? type, String? hint, int maxLines = 1, bool words = true}) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: TextField(
        controller: c,
        keyboardType: type,
        minLines: 1,
        maxLines: maxLines,
        textCapitalization: words ? TextCapitalization.words : TextCapitalization.none,
        decoration: InputDecoration(labelText: label, hintText: hint),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    // Free-form, as the existing (Sage-imported) addresses use them: the
    // postcode is usually on the last line that has anything.
    const lineLabels = ['Address line 1', 'Address line 2', 'Address line 3', 'Address line 4', 'Address line 5'];
    return Scaffold(
      backgroundColor: Colors.white,
      body: Column(
        children: [
          SalesHeader(
            title: widget.title,
            leading: HeaderIconButton(icon: Icons.close, tooltip: 'Close', onPressed: () => Navigator.of(context).pop(false)),
          ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(18, 16, 18, 24),
              children: [
                _field(_name, 'Address name', hint: 'e.g. Main yard, Shop'),
                for (var i = 0; i < 5; i++)
                  _field(_lines[i], lineLabels[i], hint: i == 0 ? 'Street' : (i >= 2 ? 'Town, county or postcode' : null)),
                const SizedBox(height: 8),
                const SectionLabel('Contact'),
                _field(_contact, 'Contact name'),
                _field(_phone, 'Telephone', type: TextInputType.phone, words: false),
                _field(_phone2, 'Telephone 2', type: TextInputType.phone, words: false),
                _field(_email, 'Email', type: TextInputType.emailAddress, words: false),
                const SizedBox(height: 8),
                const SectionLabel('Delivery note info'),
                _field(
                  _note,
                  'For the driver (optional)',
                  hint: 'e.g. Back gate only after 2pm',
                  maxLines: 4,
                  words: false,
                ),
                const Text(
                  'Goes onto an order\'s comments, which print on the delivery note, whenever this address is picked.',
                  style: TextStyle(fontSize: 12, color: Color(0xFF556070)),
                ),
                if (_error != null) ...[
                  const SizedBox(height: 12),
                  Text(_error!, style: const TextStyle(color: Color(0xFFDC2626), fontSize: 13)),
                ],
              ],
            ),
          ),
          Container(
            decoration: const BoxDecoration(border: Border(top: BorderSide(color: Color(0xFFE5E9EF)))),
            padding: EdgeInsets.fromLTRB(18, 12, 18, 12 + MediaQuery.of(context).padding.bottom),
            child: Row(
              children: [
                if (widget.onDelete != null) ...[
                  Expanded(
                    flex: 10,
                    child: SalesButton(label: 'Delete', primary: false, onPressed: _busy ? null : _delete),
                  ),
                  const SizedBox(width: 10),
                ],
                Expanded(
                  flex: 14,
                  child: SalesButton(label: _busy ? 'Saving…' : 'Save address', onPressed: _busy ? null : _save),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
