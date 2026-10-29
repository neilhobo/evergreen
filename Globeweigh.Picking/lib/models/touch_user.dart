/// A user on the Touch login picker (GET /api/auth/touch-users) — anyone whose User
/// Type is Touch or Both — and the
/// signed-in identity after POST /api/auth/touch-login.
class TouchUser {
  final int id;
  final String? fullName;

  const TouchUser({required this.id, this.fullName});

  factory TouchUser.fromJson(Map<String, dynamic> json) {
    // touch-users rows carry fullName; the touch-login response (AdminUserDto)
    // carries firstName + surname instead.
    var name = json['fullName'] as String?;
    if (name == null || name.trim().isEmpty) {
      name = '${json['firstName'] ?? ''} ${json['surname'] ?? ''}'.trim();
      if (name.isEmpty) name = null;
    }
    return TouchUser(
      id: (json['id'] as num?)?.toInt() ?? 0,
      fullName: name,
    );
  }
}
