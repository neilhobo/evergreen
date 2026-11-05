import 'package:evergreen/data/models.dart';
import 'package:flutter_test/flutter_test.dart';

// AdminUserDto as Globeweigh.Api serialises it (camelCase).
Map<String, dynamic> _dto({String expiresUtc = '2026-11-03T20:00:00Z'}) => {
  'id': 7,
  'userTypeId': 1,
  'firstName': 'Pat',
  'surname': 'Murphy',
  'username': 'pat@example.com',
  'roleId': 0,
  'roleIds': [2],
  'costingAccess': false,
  'disabled': false,
  'impersonatedByUserId': null,
  'impersonatedByName': null,
  'token': 'abc',
  'expiresUtc': expiresUtc,
};

void main() {
  test('reads a session from admin-login', () {
    final s = Session.fromJson(_dto());
    expect(s.token, 'abc');
    expect(s.expiresUtc, DateTime.utc(2026, 11, 3, 20));
    expect(s.user.displayName, 'Pat Murphy');
    expect(s.user.roleIds, [2]);
  });

  test('a non-expiring token (DateTime.MaxValue) parses as far future', () {
    final s = Session.fromJson(_dto(expiresUtc: '9999-12-31T23:59:59.9999999Z'));
    expect(s.expiresUtc.year, 9999);
    expect(s.expiresUtc.isAfter(DateTime.now().toUtc().add(const Duration(days: 365))), isTrue);
  });

  test('display name falls back to the username', () {
    final u = User.fromJson({..._dto(), 'firstName': null, 'surname': ' '});
    expect(u.displayName, 'pat@example.com');
  });

  test('a stored user round-trips', () {
    final u = User.fromJson(_dto());
    final back = User.fromJson(u.toJson());
    expect(back.id, 7);
    expect(back.username, 'pat@example.com');
  });

  test('switch user: who may switch, and a switched session', () {
    expect(User.fromJson(_dto()).canSwitchUser, isTrue); // Office Admin
    expect(
      User.fromJson({
        ..._dto(),
        'roleIds': [1],
      }).canSwitchUser,
      isTrue,
    );
    expect(
      User.fromJson({
        ..._dto(),
        'roleIds': [3, 6],
      }).canSwitchUser,
      isFalse,
    );

    final switched = User.fromJson({..._dto(), 'impersonatedByUserId': 1, 'impersonatedByName': 'Neil Hobson'});
    expect(switched.isSwitched, isTrue);
    expect(switched.canSwitchUser, isFalse); // no switching from inside a switch
    expect(switched.sessionKey, '7:1');
    final back = User.fromJson(switched.toJson());
    expect(back.impersonatedByUserId, 1);
    expect(back.impersonatedByName, 'Neil Hobson');
  });
}
