import '../core/api/api_client.dart';
import 'sales_models.dart';

/// The calls TauriAdmin's My Orders and Customers make, on the same API
/// (SalesOrdersController, CustomersSuppliersController, ProductsController,
/// CostingController). The server scopes everything to the signed-in rep.
class SalesApi {
  final ApiClient _client;
  SalesApi(this._client);

  Future<List<SalesOrder>> orders() async {
    final res = await _client.dio.get<List<dynamic>>('/api/sales-orders');
    return res.data!.map((e) => SalesOrder.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<SalesOrderDetail> order(int id) async {
    final res = await _client.dio.get<Map<String, dynamic>>('/api/sales-orders/$id');
    return SalesOrderDetail.fromJson(res.data!);
  }

  Future<SalesOrder> create(SalesOrderInput input) async {
    final res = await _client.dio.post<Map<String, dynamic>>('/api/sales-orders', data: input.toJson());
    return SalesOrder.fromJson(res.data!);
  }

  Future<SalesOrder> update(int id, SalesOrderInput input) async {
    final res = await _client.dio.put<Map<String, dynamic>>('/api/sales-orders/$id', data: input.toJson());
    return SalesOrder.fromJson(res.data!);
  }

  Future<void> send(int id) => _client.dio.post<void>('/api/sales-orders/$id/send', data: {});
  Future<void> recall(int id) => _client.dio.post<void>('/api/sales-orders/$id/recall', data: {});
  Future<void> delete(int id) => _client.dio.delete<void>('/api/sales-orders/$id');

  // ---- The Sales overview (super-admin + Colin; the server checks) ----

  /// Every rep's sent orders, newest first, each with the rep's name.
  Future<List<SalesOrder>> overviewOrders() async {
    final res = await _client.dio.get<List<dynamic>>('/api/sales-orders/overview');
    return res.data!.map((e) => SalesOrder.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<SalesOrderDetail> overviewOrder(int id) async {
    final res = await _client.dio.get<Map<String, dynamic>>('/api/sales-orders/overview/$id');
    return SalesOrderDetail.fromJson(res.data!);
  }

  /// Approve an order as the rep sent it (TauriAdmin's order dialog saved
  /// unchanged): no haulier, vehicle or trailer, which the office adds in Open
  /// Orders. Returns the new order number.
  Future<int> approve(SalesOrder o) => approveWith(
    o.id,
    SalesOrderInput(
      customerId: o.customerId,
      addressId: o.addressId,
      deliveryDate: o.deliveryDate,
      poNumber: o.poNumber,
      comments: o.comments,
      pickingComments: o.pickingComments,
      hasPos: o.hasPos,
      items: o.items,
    ),
  );

  /// Approve with the office's changes (Edit before approving): the server
  /// brings the sales order into line and lists the changes in its history.
  Future<int> approveWith(int id, SalesOrderInput input) async {
    final body = input.toJson()..addAll({'haulierId': 0});
    final res = await _client.dio.post<Map<String, dynamic>>('/api/sales-orders/$id/approve', data: body);
    return res.data!['orderNo'] as int;
  }

  /// A new open order straight away (TauriAdmin's Open Orders > New): the next
  /// order number, no approval. [salesRepId] is the customer's rep, or null.
  /// No haulier, which the office adds in Open Orders. Returns the order number.
  Future<int> createOfficeOrder(SalesOrderInput input, {int? salesRepId}) async {
    final body = input.toJson()..addAll({'haulierId': 0, 'salesRepId': salesRepId});
    final res = await _client.dio.post<Map<String, dynamic>>('/api/dispatch/orders', data: body);
    return res.data!['orderNo'] as int;
  }

  /// Send an order back to its rep with what to change.
  Future<void> returnToRep(int id, String note) =>
      _client.dio.post<void>('/api/sales-orders/$id/return', data: {'note': note});

  /// Every customer, with its rep's name.
  Future<List<Customer>> allCustomers() async {
    final res = await _client.dio.get<List<dynamic>>('/api/customers-suppliers', queryParameters: {'typeId': 1});
    return res.data!.map((e) => Customer.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// The rep's own customers (Sales Rep = them).
  Future<List<Customer>> myCustomers() async {
    final res = await _client.dio.get<List<dynamic>>(
      '/api/customers-suppliers',
      queryParameters: {'typeId': 1, 'mine': true},
    );
    return res.data!.map((e) => Customer.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<List<CustomerAddress>> addresses(int customerId) async {
    final res = await _client.dio.get<List<dynamic>>('/api/customers-suppliers/$customerId/addresses');
    return res.data!.map((e) => CustomerAddress.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// Add an address to one of the rep's customers; returns its id.
  Future<int> createAddress(int customerId, AddressInput input) async {
    final res = await _client.dio.post<Map<String, dynamic>>(
      '/api/customers-suppliers/$customerId/addresses',
      data: input.toJson(),
    );
    return res.data!['id'] as int;
  }

  Future<void> updateAddress(int addressId, AddressInput input) =>
      _client.dio.put<void>('/api/customers-suppliers/addresses/$addressId', data: input.toJson());

  Future<void> deleteAddress(int addressId) =>
      _client.dio.delete<void>('/api/customers-suppliers/addresses/$addressId');

  /// The address's Delivery Note Info (goes onto an order's Comments when the
  /// address is picked); empty clears it.
  Future<void> setDeliveryNote(int addressId, String text) =>
      _client.dio.put<void>('/api/customers-suppliers/addresses/$addressId/delivery-note', data: {'text': text});

  Future<List<CostingZone>> costingZones() async {
    final res = await _client.dio.get<List<dynamic>>('/api/customers-suppliers/costing-zones');
    return res.data!.map((e) => CostingZone.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// A new customer with the caller as its sales rep (assignToMe), as
  /// My Orders > Customers adds them.
  Future<Customer> createCustomer({
    required String name,
    String? code,
    required int costingZoneId,
    List<String> emails = const [],
  }) async {
    final res = await _client.dio.post<Map<String, dynamic>>(
      '/api/customers-suppliers',
      data: {
        'typeId': 1,
        'code': code,
        'name': name,
        'costingZoneId': costingZoneId,
        'isBoth': false,
        'inactive': false,
        'emails': emails,
        'assignToMe': true,
      },
    );
    return Customer.fromJson(res.data!);
  }

  Future<List<Product>> products() async {
    final res = await _client.dio.get<List<dynamic>>('/api/products');
    return res.data!.map((e) => Product.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// productId → the zone's price (a product marked N/A is left out).
  Future<Map<int, double>> zonePrices(int zoneId) async {
    if (zoneId <= 0) return {};
    final res = await _client.dio.get<List<dynamic>>('/api/costing/zones/$zoneId/prices');
    return {
      for (final e in res.data!.cast<Map<String, dynamic>>())
        if (e['na'] != true) e['productId'] as int: (e['price'] as num).toDouble(),
    };
  }

  /// productId → the customer's own price.
  Future<Map<int, double>> customerPrices(int customerId) async {
    final res = await _client.dio.get<List<dynamic>>('/api/costing/customers/$customerId/prices');
    return {
      for (final e in res.data!.cast<Map<String, dynamic>>()) e['productId'] as int: (e['price'] as num).toDouble(),
    };
  }
}
