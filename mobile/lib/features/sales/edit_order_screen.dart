import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../data/sales_models.dart';
import 'order_screen.dart' show OfficeNote, SalesButton;
import 'address_form.dart';
import 'pickers.dart';
import 'sales_format.dart';
import 'sales_providers.dart';

/// New order, or a draft / returned order being changed: customer, address,
/// delivery, PO, comments and lines. Saved as a draft the office can't see,
/// or saved and sent to the office in one go (TauriAdmin's Add/Edit Order
/// dialog in sales mode).
class EditOrderScreen extends ConsumerStatefulWidget {
  final SalesOrder? existing;
  final Customer? customer;
  const EditOrderScreen({super.key, this.existing, this.customer});

  @override
  ConsumerState<EditOrderScreen> createState() => _EditOrderScreenState();
}

class _EditOrderScreenState extends ConsumerState<EditOrderScreen> {
  Customer? _customer;
  List<CustomerAddress> _addresses = [];
  int _addressId = 0;
  DateTime? _delivery; // null = ASAP
  final _po = TextEditingController();
  final _comments = TextEditingController();
  final _picking = TextEditingController();
  bool _hasPos = false;
  List<OrderItem> _items = [];
  Map<int, double> _zonePrices = {};
  Map<int, double> _customerPrices = {};
  bool _busy = false;
  bool _loadingCustomer = false;

  SalesOrder? get _existing => widget.existing;

  @override
  void initState() {
    super.initState();
    final o = _existing;
    if (o != null) {
      _addressId = o.addressId;
      _delivery = o.deliveryDate;
      _po.text = o.poNumber ?? '';
      _comments.text = o.comments ?? '';
      _picking.text = o.pickingComments ?? '';
      _hasPos = o.hasPos;
      _items = [...o.items];
      WidgetsBinding.instance.addPostFrameCallback((_) => _loadExistingCustomer(o));
    } else if (widget.customer != null) {
      WidgetsBinding.instance.addPostFrameCallback((_) => _setCustomer(widget.customer!));
    }
  }

  @override
  void dispose() {
    _po.dispose();
    _comments.dispose();
    _picking.dispose();
    super.dispose();
  }

  Future<void> _loadExistingCustomer(SalesOrder o) async {
    try {
      final mine = await ref.read(myCustomersProvider.future);
      final c = mine.where((c) => c.id == o.customerId).firstOrNull ?? Customer(id: o.customerId, name: o.customerName);
      await _setCustomer(c, keepAddress: true);
    } catch (e) {
      if (mounted) showError(context, apiMessage(e));
    }
  }

  /// The customer, their addresses and their prices. A fresh pick takes the
  /// only address there is, and that address's Delivery Note Info goes onto
  /// Comments (which print on the delivery note), as TauriAdmin does.
  Future<void> _setCustomer(Customer c, {bool keepAddress = false}) async {
    setState(() {
      _customer = c;
      _loadingCustomer = true;
      if (!keepAddress) _addressId = 0;
    });
    try {
      final api = ref.read(salesApiProvider);
      final results = await Future.wait([
        api.addresses(c.id),
        api.zonePrices(c.costingZoneId),
        api.customerPrices(c.id).catchError((_) => <int, double>{}),
      ]);
      if (!mounted) return;
      setState(() {
        _addresses = results[0] as List<CustomerAddress>;
        _zonePrices = results[1] as Map<int, double>;
        _customerPrices = results[2] as Map<int, double>;
        if (!keepAddress && _addresses.length == 1) _applyAddress(_addresses.first);
      });
    } catch (e) {
      if (mounted) showError(context, apiMessage(e));
    } finally {
      if (mounted) setState(() => _loadingCustomer = false);
    }
  }

  void _applyAddress(CustomerAddress a) {
    _addressId = a.id;
    final note = a.deliveryNote?.trim() ?? '';
    if (note.isNotEmpty && !_comments.text.contains(note)) {
      _comments.text = [note, _comments.text.trim()].where((s) => s.isNotEmpty).join('\n\n');
    }
  }

  Future<void> _pickCustomer() async {
    final c = await pickCustomer(context);
    if (c == null || c.id == _customer?.id) return;
    if (_items.isNotEmpty &&
        mounted &&
        !await confirm(
          context,
          'Change the customer? The lines keep the prices they have — check them for the new customer.',
        )) {
      return;
    }
    await _setCustomer(c);
  }

  Future<void> _pickAddress() async {
    final c = _customer;
    if (c == null) return;
    // No addresses yet: straight to adding one.
    final a = _addresses.isEmpty ? newAddressChoice : await pickAddress(context, _addresses, _addressId);
    if (a == null || !mounted) return;
    if (!identical(a, newAddressChoice)) {
      setState(() => _applyAddress(a));
      return;
    }
    final api = ref.read(salesApiProvider);
    int? newId;
    await openAddressForm(
      context,
      onSave: (d) async {
        newId = await api.createAddress(c.id, d.input);
        if (d.deliveryNote.isNotEmpty) await api.setDeliveryNote(newId!, d.deliveryNote);
      },
    );
    if (newId == null || !mounted) return;
    ref.invalidate(addressesProvider(c.id));
    try {
      final rows = await api.addresses(c.id);
      if (!mounted) return;
      setState(() {
        _addresses = rows;
        final added = rows.where((r) => r.id == newId).firstOrNull;
        if (added != null) _applyAddress(added);
      });
    } catch (e) {
      if (mounted) showError(context, apiMessage(e));
    }
  }

  Future<void> _pickDate() async {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    final initial = _delivery != null && !_delivery!.isBefore(today) ? _delivery! : today.add(const Duration(days: 1));
    final d = await showDatePicker(
      context: context,
      initialDate: initial,
      firstDate: today,
      lastDate: today.add(const Duration(days: 365)),
      helpText: 'Delivery date',
    );
    if (d != null) setState(() => _delivery = d);
  }

  Future<void> _addItem() async {
    final c = _customer;
    if (c == null) {
      showError(context, 'Pick the customer first.');
      return;
    }
    if (c.costingZoneId <= 0) {
      showError(context, '${c.displayName} has no costing zone, so nothing can be priced. Ask the office to set one.');
      return;
    }
    final item = await pickProduct(
      context,
      zonePrices: _zonePrices,
      customerPrices: _customerPrices,
      taken: _items.map((i) => i.productId).toSet(),
    );
    if (item != null) setState(() => _items = [..._items, item]);
  }

  Future<void> _editItem(int index) async {
    final products = ref.read(productsProvider).valueOrNull ?? const {};
    final i = _items[index];
    final p = products[i.productId];
    if (p == null) return;
    final r = await editLine(
      context,
      product: p,
      item: i,
      zonePrice: _zonePrices[p.id],
      customerPrice: _customerPrices[p.id],
    );
    if (r == null) return;
    setState(() {
      if (r.remove) {
        _items = [..._items]..removeAt(index);
      } else {
        _items = [..._items]..[index] = r.item!;
      }
    });
  }

  void _step(int index, int by) {
    final i = _items[index];
    final next = (i.quantity ?? 0) + by;
    if (next < 1) return;
    setState(() => _items = [..._items]..[index] = i.copyWith(quantity: next));
  }

  SalesOrderInput? _input() {
    final c = _customer;
    if (c == null) {
      showError(context, 'Pick the customer.');
      return null;
    }
    if (_addresses.isNotEmpty && !_addresses.any((a) => a.id == _addressId)) {
      showError(context, 'Pick a delivery address.');
      return null;
    }
    String? t(TextEditingController x) => x.text.trim().isEmpty ? null : x.text.trim();
    return SalesOrderInput(
      customerId: c.id,
      addressId: _addressId,
      deliveryDate: _delivery,
      poNumber: t(_po),
      comments: t(_comments),
      pickingComments: t(_picking),
      hasPos: _hasPos,
      items: _items,
    );
  }

  Future<void> _save({required bool send}) async {
    final input = _input();
    if (input == null) return;
    if (send) {
      if (_items.isEmpty) {
        showError(context, 'Add at least one product before sending.');
        return;
      }
      final products = ref.read(productsProvider).valueOrNull ?? const {};
      final what =
          '${_customer!.displayName}: ${qty(_items.fold(0.0, (n, i) => n + (i.quantity ?? 0)))} pallets, ${gbp(orderTotal(_items, products))}';
      final q = _existing?.status == SalesOrderStatus.returned
          ? 'Resend this order to the office?\n\n$what'
          : 'Send this order to the office for approval?\n\n$what\n\nYou can take it back until the office approves it.';
      if (!await confirm(context, q, ok: 'Send')) return;
    }
    setState(() => _busy = true);
    try {
      final api = ref.read(salesApiProvider);
      final saved = _existing != null ? await api.update(_existing!.id, input) : await api.create(input);
      if (send) await api.send(saved.id);
      ref.invalidate(salesOrdersProvider);
      if (_existing != null) ref.invalidate(orderDetailProvider(_existing!.id));
      if (!mounted) return;
      Navigator.of(context).pop();
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text(send ? 'Sent to the office.' : 'Draft saved. Only you can see it.')));
    } catch (e) {
      if (mounted) showError(context, apiMessage(e));
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final products = ref.watch(productsProvider).valueOrNull ?? const {};
    final o = _existing;
    final address = _addresses.where((a) => a.id == _addressId).firstOrNull;
    final pallets = _items.fold(0.0, (n, i) => n + (i.quantity ?? 0));

    return Scaffold(
      backgroundColor: Colors.white,
      body: Column(
        children: [
          SalesHeader(
            overline: o == null ? 'New order' : '${o.ref} · ${o.status.label}',
            title: o == null ? 'New order' : 'Edit order',
            leading: HeaderIconButton(
              icon: Icons.arrow_back,
              tooltip: 'Back',
              onPressed: () => Navigator.of(context).pop(),
            ),
          ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(18, 14, 18, 24),
              children: [
                if (o?.status == SalesOrderStatus.returned && (o?.officeNote?.trim().isNotEmpty ?? false)) ...[
                  OfficeNote(o!.officeNote!.trim()),
                  const SizedBox(height: 14),
                ],
                const SectionLabel('Customer'),
                _Field(
                  text: _customer?.displayName ?? 'Pick a customer',
                  muted: _customer == null,
                  onTap: _busy ? null : _pickCustomer,
                ),
                const SizedBox(height: 12),
                const SectionLabel('Deliver to'),
                _Field(
                  text: _loadingCustomer
                      ? 'Loading…'
                      : _customer == null
                      ? '—'
                      : _addresses.isEmpty
                      ? 'No addresses yet — tap to add one'
                      : address?.oneLine ?? 'Pick an address',
                  muted: address == null,
                  small: true,
                  onTap: _customer == null || _loadingCustomer || _busy ? null : _pickAddress,
                ),
                const SizedBox(height: 12),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const SectionLabel('Delivery'),
                          _Field(text: dayLabel(_delivery), onTap: _busy ? null : _pickDate),
                          if (_delivery != null)
                            TextButton(
                              onPressed: () => setState(() => _delivery = null),
                              style: TextButton.styleFrom(padding: EdgeInsets.zero, minimumSize: const Size(0, 32)),
                              child: const Text('Make it ASAP', style: TextStyle(fontSize: 12)),
                            ),
                        ],
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          const SectionLabel('PO number'),
                          TextField(
                            controller: _po,
                            decoration: _dec('Optional'),
                            style: const TextStyle(fontSize: 14),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 14),
                SectionLabel('Products · ${qty(pallets)} pallets'),
                if (_items.isNotEmpty)
                  Container(
                    decoration: BoxDecoration(
                      border: Border.all(color: const Color(0xFFE5E9EF)),
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: Column(
                      children: [
                        for (final (k, i) in _items.indexed) ...[
                          if (k > 0) const Divider(height: 1, color: Color(0xFFE5E9EF)),
                          _Line(
                            item: i,
                            product: products[i.productId],
                            onTap: () => _editItem(k),
                            onMinus: () => _step(k, -1),
                            onPlus: () => _step(k, 1),
                          ),
                        ],
                      ],
                    ),
                  ),
                const SizedBox(height: 10),
                OutlinedButton.icon(
                  onPressed: _busy || _loadingCustomer ? null : _addItem,
                  icon: const Icon(Icons.add, size: 18),
                  label: const Text('Add product'),
                  style: OutlinedButton.styleFrom(
                    minimumSize: const Size.fromHeight(46),
                    foregroundColor: SalesHeader.navy,
                    side: const BorderSide(color: Color(0xFF9AA6B8)),
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                  ),
                ),
                const SizedBox(height: 18),
                const SectionLabel('Comments (print on the delivery note)'),
                TextField(
                  controller: _comments,
                  minLines: 2,
                  maxLines: 5,
                  decoration: _dec('Optional'),
                  style: const TextStyle(fontSize: 14),
                ),
                const SizedBox(height: 12),
                const SectionLabel('Picking comments'),
                TextField(
                  controller: _picking,
                  minLines: 1,
                  maxLines: 4,
                  decoration: _dec('Optional'),
                  style: const TextStyle(fontSize: 14),
                ),
              ],
            ),
          ),
          Container(
            decoration: const BoxDecoration(
              border: Border(top: BorderSide(color: Color(0xFFE5E9EF))),
            ),
            padding: EdgeInsets.fromLTRB(18, 12, 18, 12 + MediaQuery.of(context).padding.bottom),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Row(
                  children: [
                    const Expanded(
                      child: Text('Total', style: TextStyle(color: Color(0xFF556070))),
                    ),
                    Text(
                      gbp(orderTotal(_items, products)),
                      style: const TextStyle(fontWeight: FontWeight.w700, fontSize: 15),
                    ),
                  ],
                ),
                const SizedBox(height: 10),
                Row(
                  children: [
                    Expanded(
                      flex: 10,
                      child: SalesButton(
                        label: 'Save draft',
                        primary: false,
                        onPressed: _busy ? null : () => _save(send: false),
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      flex: 14,
                      child: SalesButton(
                        label: o?.status == SalesOrderStatus.returned ? 'Resend to office' : 'Send to office',
                        onPressed: _busy ? null : () => _save(send: true),
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

  InputDecoration _dec(String hint) => InputDecoration(
    hintText: hint,
    isDense: true,
    fillColor: Colors.white,
    border: OutlineInputBorder(
      borderRadius: BorderRadius.circular(10),
      borderSide: const BorderSide(color: Color(0xFFD5DBE4)),
    ),
    enabledBorder: OutlineInputBorder(
      borderRadius: BorderRadius.circular(10),
      borderSide: const BorderSide(color: Color(0xFFD5DBE4)),
    ),
    focusedBorder: OutlineInputBorder(
      borderRadius: BorderRadius.circular(10),
      borderSide: const BorderSide(color: SalesHeader.navy, width: 1.5),
    ),
    contentPadding: const EdgeInsets.symmetric(horizontal: 12, vertical: 13),
  );
}

class _Field extends StatelessWidget {
  final String text;
  final bool muted;
  final bool small;
  final VoidCallback? onTap;
  const _Field({required this.text, this.muted = false, this.small = false, this.onTap});

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.white,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(10),
        side: const BorderSide(color: Color(0xFFD5DBE4)),
      ),
      child: InkWell(
        borderRadius: BorderRadius.circular(10),
        onTap: onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 46),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    text,
                    style: TextStyle(
                      fontSize: small ? 13 : 14,
                      fontWeight: FontWeight.w500,
                      color: muted ? const Color(0xFF64748B) : const Color(0xFF0F172A),
                    ),
                  ),
                ),
                if (onTap != null) const Icon(Icons.chevron_right, color: Color(0xFF556070), size: 20),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _Line extends StatelessWidget {
  final OrderItem item;
  final Product? product;
  final VoidCallback onTap;
  final VoidCallback onMinus;
  final VoidCallback onPlus;
  const _Line({
    required this.item,
    required this.product,
    required this.onTap,
    required this.onMinus,
    required this.onPlus,
  });

  @override
  Widget build(BuildContext context) {
    final whole = product?.soldBy != 'CubicMeter';
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
        child: Row(
          children: [
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    product?.name ?? '#${item.productId}',
                    style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w600),
                  ),
                  Text(
                    '${item.price != null ? gbp(item.price!) : 'No price'} · ${gbp(lineTotal(item, product))}',
                    style: const TextStyle(fontSize: 12, color: Color(0xFF556070)),
                  ),
                ],
              ),
            ),
            if (whole) ...[
              _StepBtn(icon: Icons.remove, label: 'Fewer', onPressed: (item.quantity ?? 0) > 1 ? onMinus : null),
              SizedBox(
                width: 34,
                child: Text(
                  qty(item.quantity ?? 0),
                  textAlign: TextAlign.center,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              _StepBtn(icon: Icons.add, label: 'More', onPressed: onPlus),
            ] else
              Text('${qty(item.quantity ?? 0)} m³', style: const TextStyle(fontWeight: FontWeight.w600)),
          ],
        ),
      ),
    );
  }
}

class _StepBtn extends StatelessWidget {
  final IconData icon;
  final String label;
  final VoidCallback? onPressed;
  const _StepBtn({required this.icon, required this.label, this.onPressed});

  @override
  Widget build(BuildContext context) => SizedBox(
    width: 40,
    height: 40,
    child: OutlinedButton(
      onPressed: onPressed,
      style: OutlinedButton.styleFrom(
        padding: EdgeInsets.zero,
        foregroundColor: SalesHeader.navy,
        side: const BorderSide(color: Color(0xFFCBD2DD)),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
      ),
      child: Icon(icon, size: 18, semanticLabel: label),
    ),
  );
}
