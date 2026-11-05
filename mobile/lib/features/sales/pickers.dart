import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_client.dart';
import '../../data/sales_models.dart';
import 'my_orders_screen.dart' show ErrorRetry, SalesSearchField;
import 'order_screen.dart' show SalesButton;
import 'sales_format.dart';
import 'sales_providers.dart';

/// One of the rep's own customers.
Future<Customer?> pickCustomer(BuildContext context) => Navigator.of(
  context,
).push<Customer>(MaterialPageRoute(fullscreenDialog: true, builder: (_) => const _CustomerPicker()));

class _CustomerPicker extends ConsumerStatefulWidget {
  const _CustomerPicker();
  @override
  ConsumerState<_CustomerPicker> createState() => _CustomerPickerState();
}

class _CustomerPickerState extends ConsumerState<_CustomerPicker> {
  final _search = TextEditingController();

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final customers = ref.watch(myCustomersProvider);
    final term = _search.text.trim().toLowerCase();
    return Scaffold(
      backgroundColor: Colors.white,
      body: Column(
        children: [
          SalesHeader(
            title: 'Pick a customer',
            leading: HeaderIconButton(
              icon: Icons.close,
              tooltip: 'Close',
              onPressed: () => Navigator.of(context).pop(),
            ),
            bottom: SalesSearchField(
              controller: _search,
              hint: 'Search name or code',
              onChanged: (_) => setState(() {}),
              autofocus: true,
            ),
          ),
          Expanded(
            child: customers.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (e, _) => ErrorRetry(message: apiMessage(e), onRetry: () => ref.invalidate(myCustomersProvider)),
              data: (all) {
                final rows = all
                    .where((c) => term.isEmpty || [c.name, c.code].any((v) => v?.toLowerCase().contains(term) ?? false))
                    .toList();
                if (rows.isEmpty) {
                  return const Center(
                    child: Text('No customers match.', style: TextStyle(color: Color(0xFF556070))),
                  );
                }
                return ListView.separated(
                  itemCount: rows.length,
                  separatorBuilder: (_, _) => const Divider(height: 1, color: Color(0xFFE5E9EF)),
                  itemBuilder: (_, i) => ListTile(
                    minTileHeight: 58,
                    title: Text(rows[i].displayName, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14)),
                    subtitle: Text(
                      [rows[i].code, rows[i].costingZone].where((s) => s != null && s.isNotEmpty).join(' · '),
                      style: const TextStyle(fontSize: 12),
                    ),
                    onTap: () => Navigator.of(context).pop(rows[i]),
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

/// What [pickAddress] returns for its "Add a new address" row.
final newAddressChoice = CustomerAddress(id: -1, fields: AddressInput());

Future<CustomerAddress?> pickAddress(BuildContext context, List<CustomerAddress> addresses, int selectedId) {
  return showModalBottomSheet<CustomerAddress>(
    context: context,
    showDragHandle: true,
    backgroundColor: Colors.white,
    isScrollControlled: true,
    builder: (c) => SafeArea(
      child: ConstrainedBox(
        constraints: BoxConstraints(maxHeight: MediaQuery.of(c).size.height * 0.7),
        child: ListView(
          shrinkWrap: true,
          children: [
            const Padding(
              padding: EdgeInsets.fromLTRB(18, 0, 18, 8),
              child: Text('Deliver to', style: TextStyle(fontSize: 17, fontWeight: FontWeight.w600)),
            ),
            for (final a in addresses)
              ListTile(
                minTileHeight: 56,
                leading: Icon(
                  a.id == selectedId ? Icons.radio_button_checked : Icons.radio_button_off,
                  color: SalesHeader.navy,
                ),
                title: Text(
                  a.addressName ?? (a.lines.firstOrNull ?? 'Address'),
                  style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14),
                ),
                subtitle: Text(a.lines.join(', '), style: const TextStyle(fontSize: 12)),
                onTap: () => Navigator.of(c).pop(a),
              ),
            ListTile(
              minTileHeight: 56,
              leading: const Icon(Icons.add, color: SalesHeader.navy),
              title: const Text(
                'Add a new address',
                style: TextStyle(fontWeight: FontWeight.w600, fontSize: 14, color: SalesHeader.navy),
              ),
              onTap: () => Navigator.of(c).pop(newAddressChoice),
            ),
          ],
        ),
      ),
    ),
  );
}

/// A product and its line (quantity + price), for the order being built.
Future<OrderItem?> pickProduct(
  BuildContext context, {
  required Map<int, double> zonePrices,
  required Map<int, double> customerPrices,
  required Set<int> taken,
}) => Navigator.of(context).push<OrderItem>(
  MaterialPageRoute(
    fullscreenDialog: true,
    builder: (_) => _ProductPicker(zonePrices: zonePrices, customerPrices: customerPrices, taken: taken),
  ),
);

class _ProductPicker extends ConsumerStatefulWidget {
  final Map<int, double> zonePrices;
  final Map<int, double> customerPrices;
  final Set<int> taken;
  const _ProductPicker({required this.zonePrices, required this.customerPrices, required this.taken});

  @override
  ConsumerState<_ProductPicker> createState() => _ProductPickerState();
}

class _ProductPickerState extends ConsumerState<_ProductPicker> {
  final _search = TextEditingController();

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _choose(Product p) async {
    final r = await editLine(
      context,
      product: p,
      item: OrderItem(productId: p.id, quantity: 1, price: widget.zonePrices[p.id]),
      zonePrice: widget.zonePrices[p.id],
      customerPrice: widget.customerPrices[p.id],
      adding: true,
    );
    if (r?.item != null && mounted) Navigator.of(context).pop(r!.item);
  }

  @override
  Widget build(BuildContext context) {
    final products = ref.watch(productsProvider);
    final term = _search.text.trim().toLowerCase();
    return Scaffold(
      backgroundColor: Colors.white,
      body: Column(
        children: [
          SalesHeader(
            title: 'Add product',
            leading: HeaderIconButton(
              icon: Icons.close,
              tooltip: 'Close',
              onPressed: () => Navigator.of(context).pop(),
            ),
            bottom: SalesSearchField(controller: _search, hint: 'Search products', onChanged: (_) => setState(() {})),
          ),
          Expanded(
            child: products.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (e, _) => ErrorRetry(message: apiMessage(e), onRetry: () => ref.invalidate(productsProvider)),
              data: (all) {
                // What the customer's zone prices (or they have a price of their own for), finished
                // products first — the products a rep sells.
                final rows =
                    all.values
                        .where((p) => !p.deleted && !widget.taken.contains(p.id))
                        .where((p) => widget.zonePrices.containsKey(p.id) || widget.customerPrices.containsKey(p.id))
                        .where(
                          (p) =>
                              term.isEmpty ||
                              [p.description, p.skuCode].any((v) => v?.toLowerCase().contains(term) ?? false),
                        )
                        .toList()
                      ..sort((a, b) {
                        final ra = a.productTypeId == Product.finishedTypeId ? 0 : 1;
                        final rb = b.productTypeId == Product.finishedTypeId ? 0 : 1;
                        return ra != rb ? ra - rb : a.name.toLowerCase().compareTo(b.name.toLowerCase());
                      });
                if (rows.isEmpty) {
                  return const Center(
                    child: Text('No priced products match.', style: TextStyle(color: Color(0xFF556070))),
                  );
                }
                return ListView.separated(
                  itemCount: rows.length,
                  separatorBuilder: (_, _) => const Divider(height: 1, color: Color(0xFFE5E9EF)),
                  itemBuilder: (_, i) {
                    final p = rows[i];
                    final own = widget.customerPrices[p.id];
                    final zone = widget.zonePrices[p.id];
                    return ListTile(
                      minTileHeight: 58,
                      title: Text(p.name, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14)),
                      subtitle: Text(
                        [
                          if (zone != null) 'Zone ${gbp(zone)}',
                          if (own != null) 'Customer ${gbp(own)}',
                          if (p.soldBy != 'CubicMeter') '${p.upp} per pallet' else 'per m³',
                        ].join(' · '),
                        style: TextStyle(fontSize: 12, color: own != null ? const Color(0xFF1D4ED8) : null),
                      ),
                      trailing: const Icon(Icons.add_circle_outline, color: SalesHeader.navy),
                      onTap: () => _choose(p),
                    );
                  },
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

class LineResult {
  final OrderItem? item;
  final bool remove;
  const LineResult.item(OrderItem this.item) : remove = false;
  const LineResult.remove() : item = null, remove = true;
}

/// Quantity and price for one line: the zone price, the customer's own
/// price where they have one, or a price typed in.
Future<LineResult?> editLine(
  BuildContext context, {
  required Product product,
  required OrderItem item,
  double? zonePrice,
  double? customerPrice,
  bool adding = false,
}) {
  return showModalBottomSheet<LineResult>(
    context: context,
    isScrollControlled: true,
    showDragHandle: true,
    backgroundColor: Colors.white,
    builder: (_) =>
        _LineSheet(product: product, item: item, zonePrice: zonePrice, customerPrice: customerPrice, adding: adding),
  );
}

class _LineSheet extends StatefulWidget {
  final Product product;
  final OrderItem item;
  final double? zonePrice;
  final double? customerPrice;
  final bool adding;
  const _LineSheet({
    required this.product,
    required this.item,
    this.zonePrice,
    this.customerPrice,
    required this.adding,
  });

  @override
  State<_LineSheet> createState() => _LineSheetState();
}

class _LineSheetState extends State<_LineSheet> {
  late final _qty = TextEditingController(text: qty(widget.item.quantity ?? 1));
  late final _price = TextEditingController(text: widget.item.price?.toStringAsFixed(2) ?? '');
  String? _error;

  bool get _whole => widget.product.soldBy != 'CubicMeter';

  @override
  void dispose() {
    _qty.dispose();
    _price.dispose();
    super.dispose();
  }

  void _usePrice(double p) => setState(() => _price.text = p.toStringAsFixed(2));

  void _ok() {
    final q = double.tryParse(_qty.text.trim());
    final p = double.tryParse(_price.text.trim());
    if (q == null || q <= 0) return setState(() => _error = 'Enter a quantity.');
    if (_whole && q != q.roundToDouble()) return setState(() => _error = 'Whole pallets only.');
    if (p == null || p < 0) return setState(() => _error = 'Enter a price.');
    Navigator.of(context).pop(
      LineResult.item(
        OrderItem(productId: widget.product.id, quantity: q, price: p, costingZoneId: widget.item.costingZoneId),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final current = double.tryParse(_price.text.trim());
    return Padding(
      padding: EdgeInsets.fromLTRB(18, 0, 18, 18 + MediaQuery.of(context).viewInsets.bottom),
      child: SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(widget.product.name, style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w600)),
            Text(
              _whole ? 'Price per unit · ${widget.product.upp} units per pallet' : 'Price per m³',
              style: const TextStyle(fontSize: 12, color: Color(0xFF556070)),
            ),
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                  child: TextField(
                    controller: _qty,
                    autofocus: widget.adding,
                    keyboardType: TextInputType.numberWithOptions(decimal: !_whole),
                    inputFormatters: [FilteringTextInputFormatter.allow(RegExp(_whole ? r'[0-9]' : r'[0-9.]'))],
                    decoration: InputDecoration(labelText: _whole ? 'Pallets' : 'm³'),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: TextField(
                    controller: _price,
                    keyboardType: const TextInputType.numberWithOptions(decimal: true),
                    inputFormatters: [FilteringTextInputFormatter.allow(RegExp(r'[0-9.]'))],
                    onChanged: (_) => setState(() {}),
                    decoration: const InputDecoration(labelText: 'Price £'),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 10),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                if (widget.zonePrice != null)
                  ChoiceChip(
                    label: Text('Zone ${gbp(widget.zonePrice!)}'),
                    selected: current == widget.zonePrice,
                    onSelected: (_) => _usePrice(widget.zonePrice!),
                  ),
                if (widget.customerPrice != null)
                  ChoiceChip(
                    label: Text('Customer ${gbp(widget.customerPrice!)}'),
                    selected: current == widget.customerPrice,
                    onSelected: (_) => _usePrice(widget.customerPrice!),
                  ),
              ],
            ),
            if (_error != null) ...[
              const SizedBox(height: 10),
              Text(_error!, style: const TextStyle(color: Color(0xFFDC2626), fontSize: 13)),
            ],
            const SizedBox(height: 16),
            Row(
              children: [
                if (!widget.adding) ...[
                  Expanded(
                    child: SalesButton(
                      label: 'Remove',
                      primary: false,
                      onPressed: () => Navigator.of(context).pop(const LineResult.remove()),
                    ),
                  ),
                  const SizedBox(width: 10),
                ],
                Expanded(
                  flex: 2,
                  child: SalesButton(label: widget.adding ? 'Add to order' : 'Done', onPressed: _ok),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
