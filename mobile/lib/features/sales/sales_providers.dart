import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../data/sales_api.dart';
import '../../data/sales_models.dart';
import '../auth/auth_controller.dart';

final salesApiProvider = Provider<SalesApi>((ref) => SalesApi(ref.watch(apiClientProvider)));

/// The rep's orders. Watches the signed-in user so a new sign-in refetches.
final salesOrdersProvider = FutureProvider<List<SalesOrder>>((ref) async {
  ref.watch(signedInUserIdProvider);
  final rows = await ref.watch(salesApiProvider).orders();
  // Newest first, as the TauriAdmin list reads.
  rows.sort((a, b) => b.dateCreated.compareTo(a.dateCreated));
  return rows;
});

final myCustomersProvider = FutureProvider<List<Customer>>((ref) async {
  ref.watch(signedInUserIdProvider);
  final rows = await ref.watch(salesApiProvider).myCustomers();
  rows.sort((a, b) => a.displayName.toLowerCase().compareTo(b.displayName.toLowerCase()));
  return rows;
});

/// Every product, by id (names and UPP for order lines).
final productsProvider = FutureProvider<Map<int, Product>>((ref) async {
  ref.watch(signedInUserIdProvider);
  final rows = await ref.watch(salesApiProvider).products();
  return {for (final p in rows) p.id: p};
});

final orderDetailProvider = FutureProvider.autoDispose.family<SalesOrderDetail, int>((ref, id) async {
  return ref.watch(salesApiProvider).order(id);
});

final addressesProvider = FutureProvider.autoDispose.family<List<CustomerAddress>, int>((ref, customerId) async {
  return ref.watch(salesApiProvider).addresses(customerId);
});

/// One line's value: quantity × units the price applies to × price.
double lineTotal(OrderItem i, Product? p) => (i.quantity ?? 0) * (p?.priceUnits ?? 1) * (i.price ?? 0);

double orderTotal(Iterable<OrderItem> items, Map<int, Product> products) =>
    items.fold(0, (n, i) => n + lineTotal(i, products[i.productId]));
