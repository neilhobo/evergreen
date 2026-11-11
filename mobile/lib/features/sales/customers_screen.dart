import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../data/sales_models.dart';
import '../account/account_sheet.dart';
import 'address_form.dart';
import 'edit_order_screen.dart';
import 'my_orders_screen.dart' show ErrorRetry, SalesSearchField;
import 'order_screen.dart' show OrderScreen, SalesButton;
import 'sales_format.dart';
import 'sales_providers.dart';

const _tints = [
  (Color(0xFFE5EEFC), Color(0xFF1D4ED8)),
  (Color(0xFFE2F4EA), Color(0xFF166534)),
  (Color(0xFFEFE7FB), Color(0xFF6D28D9)),
  (Color(0xFFFDECDC), Color(0xFF9A3D06)),
  (Color(0xFFEEF1F5), Color(0xFF334155)),
];

String _initials(String name) {
  final words = name.split(RegExp(r'\s+')).where((w) => w.isNotEmpty && RegExp(r'[A-Za-z0-9]').hasMatch(w[0])).toList();
  if (words.isEmpty) return '?';
  return (words.length == 1 ? words[0].substring(0, words[0].length.clamp(1, 2)) : '${words[0][0]}${words[1][0]}')
      .toUpperCase();
}

/// The rep's own customers (Sales Rep = them), searchable, with Add new.
///
/// With [overview]: every customer, each naming its rep, read-only (the
/// super-admin and Colin).
class CustomersScreen extends ConsumerStatefulWidget {
  final bool overview;
  const CustomersScreen({super.key, this.overview = false});
  @override
  ConsumerState<CustomersScreen> createState() => _CustomersScreenState();
}

class _CustomersScreenState extends ConsumerState<CustomersScreen> {
  final _search = TextEditingController();

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final overview = widget.overview;
    final customersProvider = overview ? allCustomersProvider : myCustomersProvider;
    final customers = ref.watch(customersProvider);
    final orders = ref.watch(ordersFor(overview: overview)).valueOrNull ?? const [];
    final term = _search.text.trim().toLowerCase();

    // The last order each customer had from this rep.
    final lastOrder = <int, DateTime>{};
    for (final o in orders) {
      final d = o.dateCreated;
      if (lastOrder[o.customerId] == null || d.isAfter(lastOrder[o.customerId]!)) lastOrder[o.customerId] = d;
    }

    return Column(
      children: [
        SalesHeader(
          overline: overview ? 'Sales · every rep' : null,
          title: overview ? 'All customers' : 'Customers',
          leading: overview
              ? HeaderIconButton(icon: Icons.arrow_back, tooltip: 'Home', onPressed: () => Navigator.of(context).pop())
              : null,
          actions: [
            if (!overview)
              HeaderIconButton(
                icon: Icons.account_circle_outlined,
                tooltip: 'Account',
                onPressed: () => showAccountSheet(context),
              ),
          ],
          bottom: SalesSearchField(controller: _search, hint: 'Search name or code', onChanged: (_) => setState(() {})),
        ),
        Expanded(
          child: customers.when(
            loading: () => const Center(child: CircularProgressIndicator()),
            error: (e, _) => ErrorRetry(message: apiMessage(e), onRetry: () => ref.invalidate(customersProvider)),
            data: (all) {
              final rows = all
                  .where(
                    (c) =>
                        term.isEmpty ||
                        [c.name, c.code, c.salesRepName].any((v) => v?.toLowerCase().contains(term) ?? false),
                  )
                  .toList();
              return Stack(
                children: [
                  RefreshIndicator(
                    onRefresh: () => ref.refresh(customersProvider.future),
                    child: ListView(
                      // Room under the last row for the New customer button.
                      padding: const EdgeInsets.only(bottom: 96),
                      children: [
                        Padding(
                          padding: const EdgeInsets.fromLTRB(18, 12, 18, 6),
                          child: SectionLabel('${overview ? 'All customers' : 'Your customers'} · ${all.length}'),
                        ),
                        for (final (k, c) in rows.indexed) ...[
                          if (k > 0) const Divider(height: 1, color: Color(0xFFE5E9EF)),
                          _CustomerRow(
                            customer: c,
                            tint: _tints[c.id % _tints.length],
                            lastOrder: lastOrder[c.id],
                            overview: overview,
                          ),
                        ],
                        if (rows.isEmpty)
                          Padding(
                            padding: const EdgeInsets.all(32),
                            child: Text(
                              all.isEmpty
                                  ? (overview
                                        ? 'No customers yet.'
                                        : 'No customers yet. Add your first with the + button.')
                                  : 'No customers match.',
                              textAlign: TextAlign.center,
                              style: const TextStyle(color: Color(0xFF556070)),
                            ),
                          ),
                      ],
                    ),
                  ),
                  // Always on screen, however long the list (it used to sit under the last row).
                  if (!overview)
                    Positioned(
                      right: 18,
                      bottom: 18,
                      // The same button as My Orders' new order.
                      child: FloatingActionButton(
                        heroTag: 'new-customer',
                        tooltip: 'New customer',
                        backgroundColor: SalesHeader.navy,
                        foregroundColor: Colors.white,
                        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(18)),
                        onPressed: () => _addCustomer(context, ref),
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

class _CustomerRow extends StatelessWidget {
  final Customer customer;
  final (Color, Color) tint;
  final DateTime? lastOrder;
  final bool overview;
  const _CustomerRow({required this.customer, required this.tint, this.lastOrder, this.overview = false});

  @override
  Widget build(BuildContext context) {
    final c = customer;
    final sub = [
      if (overview) c.salesRepName ?? 'No rep',
      if (c.code?.isNotEmpty ?? false) c.code!,
      if (c.costingZone?.isNotEmpty ?? false) c.costingZone!,
      if (lastOrder != null) 'last order ${dayLabel(lastOrder)}',
    ].join(' · ');
    return Material(
      color: Colors.white,
      child: InkWell(
        onTap: () => Navigator.of(context).push(
          MaterialPageRoute(
            builder: (_) => CustomerScreen(customer: c, overview: overview),
          ),
        ),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 18, vertical: 12),
          child: Row(
            children: [
              Container(
                width: 40,
                height: 40,
                alignment: Alignment.center,
                decoration: BoxDecoration(color: tint.$1, borderRadius: BorderRadius.circular(10)),
                child: Text(
                  _initials(c.displayName),
                  style: TextStyle(color: tint.$2, fontWeight: FontWeight.w600, fontSize: 14),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(c.displayName, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14)),
                    if (sub.isNotEmpty) Text(sub, style: const TextStyle(fontSize: 12, color: Color(0xFF556070))),
                  ],
                ),
              ),
              const Icon(Icons.chevron_right, color: Color(0xFF94A3B8)),
            ],
          ),
        ),
      ),
    );
  }
}

/// One customer: their addresses (add, change, delete) and contacts, their
/// orders from this rep, and New order.
///
/// With [overview]: read-only, every rep's orders for them, no New order.
class CustomerScreen extends ConsumerWidget {
  final Customer customer;
  final bool overview;
  const CustomerScreen({super.key, required this.customer, this.overview = false});

  Future<void> _addAddress(BuildContext context, WidgetRef ref) async {
    final api = ref.read(salesApiProvider);
    final saved = await openAddressForm(
      context,
      onSave: (d) async {
        final id = await api.createAddress(customer.id, d.input);
        if (d.deliveryNote.isNotEmpty) await api.setDeliveryNote(id, d.deliveryNote);
      },
    );
    if (saved) ref.invalidate(addressesProvider(customer.id));
  }

  Future<void> _editAddress(BuildContext context, WidgetRef ref, CustomerAddress a) async {
    final api = ref.read(salesApiProvider);
    final saved = await openAddressForm(
      context,
      title: 'Edit address',
      initial: a.fields,
      initialNote: a.deliveryNote,
      onSave: (d) async {
        await api.updateAddress(a.id, d.input);
        if (d.deliveryNote != (a.deliveryNote ?? '').trim()) await api.setDeliveryNote(a.id, d.deliveryNote);
      },
      onDelete: () => api.deleteAddress(a.id),
    );
    if (saved) ref.invalidate(addressesProvider(customer.id));
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final addresses = ref.watch(addressesProvider(customer.id));
    final orders = (ref.watch(ordersFor(overview: overview)).valueOrNull ?? const <SalesOrder>[])
        .where((o) => o.customerId == customer.id)
        .toList();
    final products = ref.watch(productsProvider).valueOrNull ?? const {};

    return Scaffold(
      backgroundColor: const Color(0xFFF4F6F9),
      body: Column(
        children: [
          SalesHeader(
            overline: [
              customer.code,
              customer.costingZone,
              if (overview) customer.salesRepName,
            ].where((s) => s != null && s.isNotEmpty).join(' · '),
            title: customer.displayName,
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
                if (customer.costingZoneId <= 0) ...[
                  Container(
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(color: const Color(0xFFFDECDC), borderRadius: BorderRadius.circular(12)),
                    child: const Text(
                      'No costing zone yet, so orders can\'t be priced. Ask the office to set one.',
                      style: TextStyle(fontSize: 13, color: Color(0xFF7C2D06)),
                    ),
                  ),
                  const SizedBox(height: 14),
                ],
                const SectionLabel('Delivery addresses'),
                addresses.when(
                  loading: () => const Padding(
                    padding: EdgeInsets.all(16),
                    child: Center(child: CircularProgressIndicator()),
                  ),
                  error: (e, _) => Text(apiMessage(e)),
                  data: (rows) => Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      if (rows.isEmpty)
                        const Padding(
                          padding: EdgeInsets.only(bottom: 8),
                          child: Text('None on file yet.', style: TextStyle(color: Color(0xFF556070))),
                        ),
                      for (final a in rows)
                        AddressCard(
                          name: a.addressName,
                          lines: a.lines,
                          contact: [a.contactName, a.telephone, a.email],
                          note: a.deliveryNote,
                          onTap: overview ? null : () => _editAddress(context, ref, a),
                        ),
                    ],
                  ),
                ),
                if (!overview) _AddButton(label: 'Add address', onPressed: () => _addAddress(context, ref)),
                if (customer.emails.isNotEmpty) ...[
                  const SizedBox(height: 14),
                  const SectionLabel('Emails'),
                  for (final e in customer.emails) SelectableText(e, style: const TextStyle(fontSize: 14)),
                ],
                const SizedBox(height: 16),
                SectionLabel('${overview ? 'Orders' : 'Your orders'} · ${orders.length}'),
                if (orders.isEmpty) const Text('None yet.', style: TextStyle(color: Color(0xFF556070))),
                for (final o in orders.take(10))
                  Card(
                    margin: const EdgeInsets.only(bottom: 8),
                    child: ListTile(
                      title: Text(
                        '${o.number} · ${dayLabel(o.deliveryDate)}',
                        style: const TextStyle(fontSize: 14, fontWeight: FontWeight.w600),
                      ),
                      subtitle: Padding(
                        padding: const EdgeInsets.only(top: 4),
                        child: Row(
                          children: [
                            StatusPill(o.status),
                            const SizedBox(width: 8),
                            Text(gbp(orderTotal(o.items, products)), style: const TextStyle(fontSize: 12)),
                          ],
                        ),
                      ),
                      onTap: () => Navigator.of(context).push(
                        MaterialPageRoute(
                          builder: (_) => OrderScreen(orderId: o.id, overview: overview),
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
          Container(
            color: Colors.white,
            padding: EdgeInsets.fromLTRB(18, 12, 18, 12 + MediaQuery.of(context).padding.bottom),
            child: SalesButton(
              label: 'New order for ${customer.displayName}',
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute(
                  builder: (_) =>
                      EditOrderScreen(customer: customer, mode: overview ? OrderEditMode.officeNew : OrderEditMode.rep),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// One address as a tappable card (tap to change it).
class AddressCard extends StatelessWidget {
  final String? name;
  final List<String> lines;
  final List<String?> contact;
  final String? note;
  final VoidCallback? onTap;
  const AddressCard({super.key, this.name, this.lines = const [], this.contact = const [], this.note, this.onTap});

  @override
  Widget build(BuildContext context) {
    final who = contact.where((s) => s != null && s.trim().isNotEmpty).join(' · ');
    return Card(
      margin: const EdgeInsets.only(bottom: 8),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.all(12),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (name?.trim().isNotEmpty ?? false)
                      Text(name!, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14)),
                    if (lines.isNotEmpty) Text(lines.join(', '), style: const TextStyle(fontSize: 13)),
                    if (who.isNotEmpty) ...[
                      const SizedBox(height: 4),
                      Text(who, style: const TextStyle(fontSize: 12, color: Color(0xFF556070))),
                    ],
                    if (note?.trim().isNotEmpty ?? false) ...[
                      const SizedBox(height: 4),
                      Text(
                        'Delivery note: ${note!.trim()}',
                        style: const TextStyle(fontSize: 12, fontStyle: FontStyle.italic, color: Color(0xFF556070)),
                      ),
                    ],
                  ],
                ),
              ),
              if (onTap != null) const Icon(Icons.edit_outlined, size: 18, color: Color(0xFF94A3B8)),
            ],
          ),
        ),
      ),
    );
  }
}

class _AddButton extends StatelessWidget {
  final String label;
  final VoidCallback? onPressed;
  const _AddButton({required this.label, this.onPressed});

  @override
  Widget build(BuildContext context) => OutlinedButton.icon(
    onPressed: onPressed,
    icon: const Icon(Icons.add, size: 18),
    label: Text(label),
    style: OutlinedButton.styleFrom(
      minimumSize: const Size.fromHeight(46),
      backgroundColor: Colors.white,
      foregroundColor: SalesHeader.navy,
      side: const BorderSide(color: Color(0xFF9AA6B8)),
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
    ),
  );
}

Future<void> _addCustomer(BuildContext context, WidgetRef ref) async {
  final created = await Navigator.of(
    context,
  ).push<Customer>(MaterialPageRoute(fullscreenDialog: true, builder: (_) => const NewCustomerScreen()));
  if (created == null || !context.mounted) return;
  // Straight onto the new customer, ready for a first order.
  Navigator.of(context).push(MaterialPageRoute(builder: (_) => CustomerScreen(customer: created)));
}

/// A new customer with this rep as its Sales Rep, and as many delivery
/// addresses as they have. The customer is created first, then each address;
/// if an address fails the customer still exists, and the ones not saved stay
/// on screen to retry.
class NewCustomerScreen extends ConsumerStatefulWidget {
  const NewCustomerScreen({super.key});
  @override
  ConsumerState<NewCustomerScreen> createState() => _NewCustomerScreenState();
}

class _NewCustomerScreenState extends ConsumerState<NewCustomerScreen> {
  final _name = TextEditingController();
  final _code = TextEditingController();
  final _email = TextEditingController();
  List<CostingZone>? _zones;
  int _zoneId = 0;
  final List<AddressDraft> _drafts = [];
  Customer? _created; // set once the customer itself is saved
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    ref
        .read(salesApiProvider)
        .costingZones()
        .then((z) {
          if (mounted) setState(() => _zones = z);
        })
        .catchError((Object e) {
          if (mounted) setState(() => _error = apiMessage(e));
        });
  }

  @override
  void dispose() {
    _name.dispose();
    _code.dispose();
    _email.dispose();
    super.dispose();
  }

  Future<void> _addDraft() async {
    await openAddressForm(context, onSave: (d) async => setState(() => _drafts.add(d)));
  }

  Future<void> _editDraft(int i) async {
    await openAddressForm(
      context,
      title: 'Edit address',
      initial: _drafts[i].input,
      initialNote: _drafts[i].deliveryNote,
      onSave: (d) async => setState(() => _drafts[i] = d),
      onDelete: () async => setState(() => _drafts.removeAt(i)),
    );
  }

  Future<void> _save() async {
    if (_name.text.trim().isEmpty) return setState(() => _error = 'Enter the customer name.');
    final email = _email.text.trim();
    if (email.isNotEmpty && !RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$').hasMatch(email)) {
      return setState(() => _error = 'That email address doesn\'t look right.');
    }
    if (_drafts.isEmpty &&
        !await confirm(context, 'Add the customer without a delivery address? You can add one later.', ok: 'Add')) {
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    final api = ref.read(salesApiProvider);
    try {
      _created ??= await api.createCustomer(
        name: _name.text.trim(),
        code: _code.text.trim().isEmpty ? null : _code.text.trim(),
        costingZoneId: _zoneId,
        emails: [if (email.isNotEmpty) email],
      );
      ref.invalidate(myCustomersProvider);
      // One at a time, dropping each from the list once it is saved, so a
      // failure part-way leaves just the unsaved ones to retry.
      while (_drafts.isNotEmpty) {
        final d = _drafts.first;
        final id = await api.createAddress(_created!.id, d.input);
        if (d.deliveryNote.isNotEmpty) await api.setDeliveryNote(id, d.deliveryNote);
        setState(() => _drafts.removeAt(0));
      }
      if (!mounted) return;
      ScaffoldMessenger.of(
        context,
      ).showSnackBar(SnackBar(content: Text('${_created!.displayName} added to your customers.')));
      Navigator.of(context).pop(_created);
    } catch (e) {
      if (!mounted) return;
      setState(
        () => _error = _created == null
            ? apiMessage(e)
            : '${_created!.displayName} was added, but an address wasn\'t saved: ${apiMessage(e)}\nPress Save to try the rest again.',
      );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final locked = _created != null; // the customer's own fields are saved; only addresses remain
    return Scaffold(
      backgroundColor: Colors.white,
      body: Column(
        children: [
          SalesHeader(
            title: 'New customer',
            leading: HeaderIconButton(
              icon: Icons.close,
              tooltip: 'Close',
              onPressed: () => Navigator.of(context).pop(_created),
            ),
          ),
          Expanded(
            child: ListView(
              padding: const EdgeInsets.fromLTRB(18, 16, 18, 24),
              children: [
                TextField(
                  controller: _name,
                  enabled: !locked,
                  autofocus: true,
                  textCapitalization: TextCapitalization.words,
                  decoration: const InputDecoration(labelText: 'Name'),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: _code,
                  enabled: !locked,
                  textCapitalization: TextCapitalization.characters,
                  decoration: const InputDecoration(labelText: 'Code (optional)'),
                ),
                const SizedBox(height: 10),
                TextField(
                  controller: _email,
                  enabled: !locked,
                  keyboardType: TextInputType.emailAddress,
                  decoration: const InputDecoration(labelText: 'Email (optional)'),
                ),
                const SizedBox(height: 10),
                DropdownButtonFormField<int>(
                  initialValue: _zoneId,
                  decoration: const InputDecoration(labelText: 'Costing zone'),
                  items: [
                    const DropdownMenuItem(value: 0, child: Text('None yet — the office sets it')),
                    for (final z in _zones ?? const <CostingZone>[])
                      DropdownMenuItem(value: z.id, child: Text(z.description ?? 'Zone ${z.id}')),
                  ],
                  onChanged: locked ? null : (v) => setState(() => _zoneId = v ?? 0),
                ),
                const SizedBox(height: 20),
                SectionLabel('Delivery addresses · ${_drafts.length}'),
                for (final (i, d) in _drafts.indexed)
                  AddressCard(
                    name: d.input.addressName,
                    lines: d.input.lines.where((l) => l.trim().isNotEmpty).toList(),
                    contact: [d.input.contactName, d.input.telephone, d.input.email],
                    note: d.deliveryNote,
                    onTap: _busy ? null : () => _editDraft(i),
                  ),
                _AddButton(label: 'Add address', onPressed: _busy ? null : _addDraft),
                if (_error != null) ...[
                  const SizedBox(height: 12),
                  Text(_error!, style: const TextStyle(color: Color(0xFFDC2626), fontSize: 13)),
                ],
              ],
            ),
          ),
          Container(
            decoration: const BoxDecoration(
              border: Border(top: BorderSide(color: Color(0xFFE5E9EF))),
            ),
            padding: EdgeInsets.fromLTRB(18, 12, 18, 12 + MediaQuery.of(context).padding.bottom),
            child: SalesButton(
              label: _busy ? 'Saving…' : (locked ? 'Save the remaining addresses' : 'Add customer'),
              onPressed: _busy ? null : _save,
            ),
          ),
        ],
      ),
    );
  }
}
