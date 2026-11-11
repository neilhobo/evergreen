/// The Sales role's data, mirroring what TauriAdmin's My Orders uses
/// (Globeweigh.TauriAdmin/src/api/salesOrders.ts, customersSuppliers.ts,
/// products.ts). JSON is camelCase from Globeweigh.Api.
library;

enum SalesOrderStatus { draft, returned, pending, approved, planned, dispatched, cancelled }

SalesOrderStatus _status(String? s) =>
    SalesOrderStatus.values.firstWhere((v) => v.name == s, orElse: () => SalesOrderStatus.draft);

extension SalesOrderStatusX on SalesOrderStatus {
  String get label => switch (this) {
    SalesOrderStatus.draft => 'Draft',
    SalesOrderStatus.returned => 'Returned',
    SalesOrderStatus.pending => 'Awaiting approval',
    SalesOrderStatus.approved => 'Approved',
    SalesOrderStatus.planned => 'Transport allocated',
    SalesOrderStatus.dispatched => 'Dispatched',
    SalesOrderStatus.cancelled => 'Cancelled',
  };

  /// Still the rep's to change: a draft, or one the office sent back.
  bool get isDraft => this == SalesOrderStatus.draft || this == SalesOrderStatus.returned;

  /// Where it sits on the five-step tracker (Draft → Awaiting approval →
  /// Approved → Transport allocated → Dispatched), as on TauriAdmin.
  int get step => switch (this) {
    SalesOrderStatus.draft => 0,
    SalesOrderStatus.returned => 1,
    SalesOrderStatus.pending => 1,
    SalesOrderStatus.approved => 2,
    SalesOrderStatus.planned => 3,
    SalesOrderStatus.dispatched => 4,
    SalesOrderStatus.cancelled => 2,
  };
}

DateTime? _date(Object? v) => v is String && v.isNotEmpty ? DateTime.tryParse(v)?.toLocal() : null;
double? _num(Object? v) => v is num ? v.toDouble() : null;

class OrderItem {
  final int productId;
  final double? quantity;
  final double? price;

  /// Priced from another zone than the customer's (a rep's Change in
  /// TauriAdmin); null = the customer's. Kept as it came so an edit here
  /// doesn't lose it.
  final int? costingZoneId;

  const OrderItem({required this.productId, this.quantity, this.price, this.costingZoneId});

  OrderItem copyWith({double? quantity, double? price}) => OrderItem(
    productId: productId,
    quantity: quantity ?? this.quantity,
    price: price ?? this.price,
    costingZoneId: costingZoneId,
  );

  factory OrderItem.fromJson(Map<String, dynamic> j) => OrderItem(
    productId: j['productId'] as int,
    quantity: _num(j['quantity']),
    price: _num(j['price']),
    costingZoneId: j['costingZoneId'] as int?,
  );

  Map<String, dynamic> toJson() => {
    'productId': productId,
    'quantity': quantity,
    'price': price,
    'costingZoneId': costingZoneId,
  };
}

class SalesOrder {
  final int id;
  final String ref; // S-0001
  final SalesOrderStatus status;
  final int customerId;
  final String? customerName;
  final int addressId;
  final String? addressText;
  final DateTime? deliveryDate; // null = ASAP
  final String? poNumber;
  final String? comments;
  final String? pickingComments;
  final bool hasPos;
  final String? officeNote;
  final DateTime dateCreated;
  final DateTime? dateSent;
  final int? orderNo;
  final DateTime? loadDate;
  final String? loadHaulier;
  final DateTime? dateDispatched;

  /// The rep who sent it: the Sales overview's rows only.
  final String? salesRepName;

  /// Entered by the office with this rep as its Sales Rep: read-only here.
  final bool fromOffice;
  final List<OrderItem> items;

  const SalesOrder({
    required this.id,
    required this.ref,
    required this.status,
    required this.customerId,
    this.customerName,
    required this.addressId,
    this.addressText,
    this.deliveryDate,
    this.poNumber,
    this.comments,
    this.pickingComments,
    this.hasPos = false,
    this.officeNote,
    required this.dateCreated,
    this.dateSent,
    this.orderNo,
    this.loadDate,
    this.loadHaulier,
    this.dateDispatched,
    this.salesRepName,
    this.fromOffice = false,
    this.items = const [],
  });

  /// What the list shows as the number: the office's order number once
  /// approved, the sales ref before.
  String get number => orderNo != null ? '#$orderNo' : ref;

  double get pallets => items.fold(0, (n, i) => n + (i.quantity ?? 0));

  factory SalesOrder.fromJson(Map<String, dynamic> j) => SalesOrder(
    id: j['id'] as int,
    ref: j['ref'] as String? ?? '',
    status: _status(j['status'] as String?),
    customerId: j['customerId'] as int? ?? 0,
    customerName: j['customerName'] as String?,
    addressId: j['addressId'] as int? ?? 0,
    addressText: j['addressText'] as String?,
    deliveryDate: _date(j['deliveryDate']),
    poNumber: j['poNumber'] as String?,
    comments: j['comments'] as String?,
    pickingComments: j['pickingComments'] as String?,
    hasPos: j['hasPos'] as bool? ?? false,
    officeNote: j['officeNote'] as String?,
    dateCreated: _date(j['dateCreated']) ?? DateTime.now(),
    dateSent: _date(j['dateSent']),
    orderNo: j['orderNo'] as int?,
    loadDate: _date(j['loadDate']),
    loadHaulier: j['loadHaulier'] as String?,
    dateDispatched: _date(j['dateDispatched']),
    salesRepName: j['salesRepName'] as String?,
    fromOffice: j['fromOffice'] as bool? ?? false,
    items: (j['items'] as List? ?? const []).map((e) => OrderItem.fromJson(e as Map<String, dynamic>)).toList(),
  );
}

class SalesOrderEvent {
  final DateTime date;
  final String who; // "You", or the office user's name
  final String text;
  final List<String> changes;
  const SalesOrderEvent({required this.date, required this.who, required this.text, this.changes = const []});

  factory SalesOrderEvent.fromJson(Map<String, dynamic> j) => SalesOrderEvent(
    date: _date(j['date']) ?? DateTime.now(),
    who: j['who'] as String? ?? '',
    text: j['text'] as String? ?? '',
    changes: (j['changes'] as List? ?? const []).cast<String>(),
  );
}

class SalesOrderDetail {
  final SalesOrder order;
  final List<SalesOrderEvent> events;
  const SalesOrderDetail(this.order, this.events);

  factory SalesOrderDetail.fromJson(Map<String, dynamic> j) => SalesOrderDetail(
    SalesOrder.fromJson(j['order'] as Map<String, dynamic>),
    (j['events'] as List? ?? const []).map((e) => SalesOrderEvent.fromJson(e as Map<String, dynamic>)).toList(),
  );
}

/// What create / update send (SalesOrderInput).
class SalesOrderInput {
  final int customerId;
  final int addressId;
  final DateTime? deliveryDate;
  final String? poNumber;
  final String? comments;
  final String? pickingComments;
  final bool hasPos;
  final int? copiedFromId;
  final List<OrderItem> items;

  const SalesOrderInput({
    required this.customerId,
    required this.addressId,
    this.deliveryDate,
    this.poNumber,
    this.comments,
    this.pickingComments,
    this.hasPos = false,
    this.copiedFromId,
    this.items = const [],
  });

  Map<String, dynamic> toJson() => {
    'customerId': customerId,
    'addressId': addressId,
    // Local midnight, as TauriAdmin sends it (new Date(y, m, d).toISOString()).
    'deliveryDate': deliveryDate == null
        ? null
        : DateTime(deliveryDate!.year, deliveryDate!.month, deliveryDate!.day).toUtc().toIso8601String(),
    'poNumber': poNumber,
    'comments': comments,
    'pickingComments': pickingComments,
    'hasPos': hasPos,
    'copiedFromId': ?copiedFromId,
    'items': items.map((i) => i.toJson()).toList(),
  };
}

class Customer {
  final int id;
  final String? code;
  final String? name;
  final int costingZoneId;
  final String? costingZone;
  final int? salesRepId;
  final String? salesRepName;
  final int addressCount;
  final List<String> emails;

  const Customer({
    required this.id,
    this.code,
    this.name,
    this.costingZoneId = 0,
    this.costingZone,
    this.salesRepId,
    this.salesRepName,
    this.addressCount = 0,
    this.emails = const [],
  });

  String get displayName => (name ?? '').trim().isEmpty ? (code ?? 'Customer #$id') : name!.trim();

  factory Customer.fromJson(Map<String, dynamic> j) => Customer(
    id: j['id'] as int,
    code: j['code'] as String?,
    name: j['name'] as String?,
    costingZoneId: j['costingZoneId'] as int? ?? 0,
    costingZone: j['costingZone'] as String?,
    salesRepId: j['salesRepId'] as int?,
    salesRepName: j['salesRepName'] as String?,
    addressCount: j['addressCount'] as int? ?? 0,
    emails: (j['emails'] as List? ?? const []).cast<String>(),
  );
}

class CustomerAddress {
  final int id;
  final AddressInput fields;
  final String? deliveryNote;

  const CustomerAddress({required this.id, required this.fields, this.deliveryNote});

  String? get addressName => fields.addressName;
  String? get telephone => fields.telephone;
  String? get email => fields.email;
  String? get contactName => fields.contactName;

  /// The address lines that have something in them.
  List<String> get lines => fields.lines.where((s) => s.trim().isNotEmpty).map((s) => s.trim()).toList();

  String get oneLine => [addressName, ...lines].where((s) => s != null && s.trim().isNotEmpty).join(', ');

  factory CustomerAddress.fromJson(Map<String, dynamic> j) =>
      CustomerAddress(id: j['id'] as int, fields: AddressInput.fromJson(j), deliveryNote: j['deliveryNote'] as String?);
}

/// An address's writable fields (AddressInput on the API), as TauriAdmin's
/// Address dialog edits them: a name, five lines, two phones, email, contact.
class AddressInput {
  final String? addressName;
  final List<String> lines; // always five, '' for an empty one
  final String? telephone;
  final String? telephone2;
  final String? email;
  final String? contactName;

  AddressInput({
    this.addressName,
    List<String> lines = const [],
    this.telephone,
    this.telephone2,
    this.email,
    this.contactName,
  }) : lines = List.generate(5, (i) => i < lines.length ? lines[i] : '');

  bool get isEmpty =>
      [addressName, ...lines, telephone, telephone2, email, contactName].every((s) => s == null || s.trim().isEmpty);

  String get oneLine => [addressName, ...lines].where((s) => s != null && s.trim().isNotEmpty).join(', ');

  factory AddressInput.fromJson(Map<String, dynamic> j) => AddressInput(
    addressName: j['addressName'] as String?,
    lines: [for (var i = 1; i <= 5; i++) (j['address$i'] as String?) ?? ''],
    telephone: j['telephone'] as String?,
    telephone2: j['telephone2'] as String?,
    email: j['email'] as String?,
    contactName: j['contactName'] as String?,
  );

  Map<String, dynamic> toJson() {
    String? t(String? s) => s == null || s.trim().isEmpty ? null : s.trim();
    return {
      'addressName': t(addressName),
      for (var i = 0; i < 5; i++) 'address${i + 1}': t(lines[i]),
      'telephone': t(telephone),
      'telephone2': t(telephone2),
      'email': t(email),
      'contactName': t(contactName),
    };
  }
}

class CostingZone {
  final int id;
  final String? description;
  const CostingZone(this.id, this.description);
  factory CostingZone.fromJson(Map<String, dynamic> j) => CostingZone(j['id'] as int, j['description'] as String?);
}

class Product {
  final int id;
  final String? description;
  final String? skuCode;
  final int upp;
  final int? size; // litres
  final int productTypeId;
  final String soldBy; // Pallet | CubicMeter
  final bool deleted;

  const Product({
    required this.id,
    this.description,
    this.skuCode,
    this.upp = 1,
    this.size,
    this.productTypeId = 0,
    this.soldBy = 'Pallet',
    this.deleted = false,
  });

  static const finishedTypeId = 1;

  String get name => description ?? '#$id';

  /// Units a line's price applies to per unit of quantity: a pallet line's
  /// price is per unit (the pallet's UPP), a m³ line's per m³ (priceUnitsPer).
  int get priceUnits => soldBy == 'CubicMeter' ? 1 : upp;

  String get qtyUnit => soldBy == 'CubicMeter' ? 'm³' : 'pallets';

  /// Size and UPP for an order line's second line ("50L · 60 UPP"); a m³
  /// product has no UPP, so it reads "m³".
  String get sizeUpp =>
      [if (size != null && size! > 0) '${size}L', if (soldBy == 'CubicMeter') 'm³' else '$upp UPP'].join(' · ');

  factory Product.fromJson(Map<String, dynamic> j) => Product(
    id: j['id'] as int,
    description: j['description'] as String?,
    skuCode: j['skuCode'] as String?,
    upp: (j['upp'] as num?)?.toInt() ?? 1,
    size: (j['size'] as num?)?.toInt(),
    productTypeId: j['productTypeId'] as int? ?? 0,
    soldBy: j['soldBy'] as String? ?? 'Pallet',
    deleted: j['dateDeleted'] != null,
  );
}
