/// UserRole in Globeweigh.TauriAdmin/src/types.ts.
abstract final class UserRole {
  static const globeweighAdmin = 1;
  static const officeAdmin = 2;
  static const officeUser = 3;
  static const factory = 4;
  static const weighbridge = 5; // retired
  static const sales = 6;

  /// roleLabel in Globeweigh.TauriAdmin/src/api/staff.ts.
  static String label(int id) => switch (id) {
    globeweighAdmin => 'Globeweigh Admin',
    officeAdmin => 'Office Admin',
    officeUser => 'Office User',
    factory => 'Factory User',
    weighbridge => 'Weighbridge User',
    sales => 'Sales',
    _ => 'Role $id',
  };
}

/// The signed-in user: AdminUserDto from POST /api/auth/admin-login, less
/// the token.
class User {
  final int id;
  final String? firstName;
  final String? surname;
  final String username;
  final List<int> roleIds;
  final bool disabled;

  /// Set while an admin is signed in AS this user (Switch user): the admin's
  /// id and name, from the signed token.
  final int? impersonatedByUserId;
  final String? impersonatedByName;

  const User({
    required this.id,
    this.firstName,
    this.surname,
    required this.username,
    this.roleIds = const [],
    this.disabled = false,
    this.impersonatedByUserId,
    this.impersonatedByName,
  });

  String get displayName {
    final name = '${firstName ?? ''} ${surname ?? ''}'.trim();
    return name.isNotEmpty ? name : username;
  }

  /// An admin acting as this user.
  bool get isSwitched => impersonatedByUserId != null;

  /// May open Switch user: a Globeweigh or Office Admin on their own session,
  /// as TauriAdmin's account menu (the API re-checks). No switching from
  /// inside a switch.
  bool get canSwitchUser =>
      !isSwitched && (roleIds.contains(UserRole.globeweighAdmin) || roleIds.contains(UserRole.officeAdmin));

  /// Who is signed in and on whose behalf: changes on every switch.
  String get sessionKey => '$id:${impersonatedByUserId ?? ''}';

  factory User.fromJson(Map<String, dynamic> json) => User(
    id: json['id'] as int,
    firstName: json['firstName'] as String?,
    surname: json['surname'] as String?,
    username: json['username'] as String? ?? '',
    roleIds: (json['roleIds'] as List? ?? const []).cast<int>(),
    disabled: json['disabled'] as bool? ?? false,
    impersonatedByUserId: json['impersonatedByUserId'] as int?,
    impersonatedByName: json['impersonatedByName'] as String?,
  );

  Map<String, dynamic> toJson() => {
    'id': id,
    'firstName': firstName,
    'surname': surname,
    'username': username,
    'roleIds': roleIds,
    'disabled': disabled,
    'impersonatedByUserId': impersonatedByUserId,
    'impersonatedByName': impersonatedByName,
  };
}

class Session {
  final String token;
  final DateTime expiresUtc;
  final User user;
  const Session({required this.token, required this.expiresUtc, required this.user});

  factory Session.fromJson(Map<String, dynamic> json) => Session(
    token: json['token'] as String,
    expiresUtc: DateTime.parse(json['expiresUtc'] as String).toUtc(),
    user: User.fromJson(json),
  );
}

/// One row on the Switch user picker (GET /api/auth/switch-users).
class SwitchUserRow {
  final int id;
  final String? firstName;
  final String? surname;
  final String? username;
  final List<int> roleIds;
  final bool disabled;

  const SwitchUserRow({
    required this.id,
    this.firstName,
    this.surname,
    this.username,
    this.roleIds = const [],
    this.disabled = false,
  });

  String get name => '${firstName ?? ''} ${surname ?? ''}'.trim();
  String get displayName => name.isNotEmpty ? name : (username ?? '');

  factory SwitchUserRow.fromJson(Map<String, dynamic> json) => SwitchUserRow(
    id: json['id'] as int,
    firstName: json['firstName'] as String?,
    surname: json['surname'] as String?,
    username: json['username'] as String?,
    roleIds: (json['roleIds'] as List? ?? const []).cast<int>(),
    disabled: json['disabled'] as bool? ?? false,
  );
}
