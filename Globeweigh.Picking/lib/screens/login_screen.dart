import 'package:flutter/foundation.dart' show kDebugMode;
import 'package:flutter/material.dart';

import '../api/api_client.dart';
import '../models/touch_user.dart';
import '../services/session.dart';
import '../theme/app_colors.dart';
import '../widgets/gradient_bg.dart';
import '../widgets/wordmark.dart';
import 'home_screen.dart';

/// Sign In — shown on startup, mirroring the TauriTouch login: pick a factory
/// user on the left, enter the 4-digit passcode on the keypad. Auto-submits on
/// the 4th digit; a wrong code clears the entry and shows the error.
class LoginScreen extends StatefulWidget {
  const LoginScreen({super.key});

  @override
  State<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends State<LoginScreen> {
  /// Dev convenience: in a debug build the first login of the app run signs this
  /// user straight in (no passcode) so hot-reload cycles don't cost a login. Never
  /// runs in a release build. Signing out returns to the picker as normal.
  static const String _devAutoLoginName = 'Colin McCuskey';
  static bool _devAutoLoginDone = false;

  final _api = ApiClient();

  List<TouchUser>? _users;
  String? _loadError;
  TouchUser? _selected;
  String _passcode = '';
  String? _error;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    _loadUsers();
  }

  Future<void> _loadUsers() async {
    setState(() {
      _users = null;
      _loadError = null;
    });
    try {
      final users = await _api.getTouchUsers();
      if (!mounted) return;
      setState(() => _users = users);
      _devAutoLogin(users);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _loadError = e.message);
    }
  }

  /// Debug builds only: sign [_devAutoLoginName] in without the passcode, once per
  /// app run. The user list comes from whichever DB the build is pointed at, so a
  /// missing name (different dataset) just leaves the picker as it is.
  void _devAutoLogin(List<TouchUser> users) {
    if (!kDebugMode || _devAutoLoginDone) return;
    final match = users.where((u) =>
        (u.fullName ?? '').trim().toLowerCase() ==
        _devAutoLoginName.toLowerCase());
    if (match.isEmpty) return;
    _devAutoLoginDone = true;
    Session.current = match.first;
    Navigator.of(context).pushReplacement(
      MaterialPageRoute(builder: (_) => const HomeScreen()),
    );
  }

  void _pick(TouchUser u) {
    setState(() {
      _selected = u;
      _passcode = '';
      _error = null;
    });
  }

  void _key(String k) {
    if (_busy || _selected == null || _passcode.length >= 4) return;
    setState(() {
      _passcode += k;
      _error = null;
    });
    // Auto-submit once the full 4-digit passcode is in.
    if (_passcode.length == 4) _submit();
  }

  void _backspace() {
    if (_busy || _passcode.isEmpty) return;
    setState(
        () => _passcode = _passcode.substring(0, _passcode.length - 1));
  }

  void _clear() {
    if (_busy) return;
    setState(() => _passcode = '');
  }

  Future<void> _submit() async {
    final sel = _selected;
    if (sel == null || _busy) return;
    setState(() => _busy = true);
    try {
      final user = await _api.touchLogin(userId: sel.id, passcode: _passcode);
      if (!mounted) return;
      Session.current = user;
      Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => const HomeScreen()),
      );
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _error = e.message;
        _passcode = '';
        _busy = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.navy,
      body: GradientBg(
        child: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const Center(child: Wordmark(iconSize: 34, fontSize: 22)),
                const SizedBox(height: 14),
                Expanded(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Expanded(child: _userPanel()),
                      const SizedBox(width: 14),
                      SizedBox(width: 400, child: _passcodePanel()),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  BoxDecoration get _panelBox => BoxDecoration(
        color: Colors.white.withValues(alpha: 0.06),
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: Colors.white.withValues(alpha: 0.12)),
      );

  static const TextStyle _panelTitle = TextStyle(
    fontFamily: 'Poppins',
    fontSize: 12,
    fontWeight: FontWeight.w700,
    letterSpacing: 1,
    color: Colors.white54,
  );

  Widget _userPanel() {
    return Container(
      decoration: _panelBox,
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const Text('SELECT USER', style: _panelTitle),
          const SizedBox(height: 10),
          Expanded(child: _userList()),
        ],
      ),
    );
  }

  Widget _userList() {
    if (_loadError != null) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              _loadError!,
              textAlign: TextAlign.center,
              style: const TextStyle(
                  fontFamily: 'Poppins', fontSize: 14, color: Colors.white70),
            ),
            const SizedBox(height: 12),
            TextButton.icon(
              onPressed: _loadUsers,
              icon: const Icon(Icons.refresh, size: 18),
              label: const Text('Retry'),
              style: TextButton.styleFrom(foregroundColor: AppColors.accent),
            ),
          ],
        ),
      );
    }
    final users = _users;
    if (users == null) {
      return const Center(
        child: SizedBox(
          width: 24,
          height: 24,
          child: CircularProgressIndicator(
            strokeWidth: 2.5,
            valueColor: AlwaysStoppedAnimation(AppColors.accent),
          ),
        ),
      );
    }
    if (users.isEmpty) {
      return const Center(
        child: Text(
          'No users found',
          style: TextStyle(
              fontFamily: 'Poppins', fontSize: 14, color: Colors.white70),
        ),
      );
    }
    return ListView.separated(
      itemCount: users.length,
      separatorBuilder: (_, _) => const SizedBox(height: 8),
      itemBuilder: (_, i) {
        final u = users[i];
        final selected = _selected?.id == u.id;
        return InkWell(
          borderRadius: BorderRadius.circular(10),
          onTap: () => _pick(u),
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 13),
            decoration: BoxDecoration(
              color: selected
                  ? AppColors.accent.withValues(alpha: 0.18)
                  : Colors.white.withValues(alpha: 0.05),
              borderRadius: BorderRadius.circular(10),
              border: Border.all(
                color: selected
                    ? AppColors.accent
                    : Colors.white.withValues(alpha: 0.10),
                width: selected ? 1.5 : 1,
              ),
            ),
            child: Text(
              u.fullName ?? 'User ${u.id}',
              style: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 16,
                fontWeight: FontWeight.w600,
                color: Colors.white,
              ),
            ),
          ),
        );
      },
    );
  }

  Widget _passcodePanel() {
    final sel = _selected;
    return Container(
      decoration: _panelBox,
      padding: const EdgeInsets.all(14),
      child: sel == null
          ? const Center(
              child: Text(
                'Select a user to continue',
                style: TextStyle(
                    fontFamily: 'Poppins', fontSize: 16, color: Colors.white54),
              ),
            )
          : Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Text('PASSCODE  ·  ${sel.fullName ?? ''}'.toUpperCase(),
                    style: _panelTitle,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis),
                const SizedBox(height: 10),
                // Masked passcode dots.
                Container(
                  height: 48,
                  alignment: Alignment.center,
                  decoration: BoxDecoration(
                    color: Colors.black.withValues(alpha: 0.25),
                    borderRadius: BorderRadius.circular(10),
                  ),
                  child: Text(
                    '•' * _passcode.length,
                    style: const TextStyle(
                      fontSize: 26,
                      letterSpacing: 10,
                      color: Colors.white,
                      height: 1,
                    ),
                  ),
                ),
                SizedBox(
                  height: 22,
                  child: Center(
                    child: Text(
                      _error ?? '',
                      style: const TextStyle(
                        fontFamily: 'Poppins',
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                        color: Color(0xFFFF6B6B),
                      ),
                    ),
                  ),
                ),
                Expanded(child: _keypad()),
              ],
            ),
    );
  }

  Widget _keypad() {
    Widget key(String label, VoidCallback onTap, {Color? color}) {
      return Material(
        color: color ?? Colors.white.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(10),
        child: InkWell(
          borderRadius: BorderRadius.circular(10),
          onTap: _busy ? null : onTap,
          child: Center(
            child: Text(
              label,
              style: const TextStyle(
                fontFamily: 'Poppins',
                fontSize: 24,
                fontWeight: FontWeight.w600,
                color: Colors.white,
              ),
            ),
          ),
        ),
      );
    }

    const gap = 8.0;
    return LayoutBuilder(
      builder: (_, constraints) {
        final rows = <List<Widget>>[
          [key('1', () => _key('1')), key('2', () => _key('2')), key('3', () => _key('3'))],
          [key('4', () => _key('4')), key('5', () => _key('5')), key('6', () => _key('6'))],
          [key('7', () => _key('7')), key('8', () => _key('8')), key('9', () => _key('9'))],
          [key('⌫', _backspace), key('0', () => _key('0')), key('C', _clear)],
        ];
        return Column(
          children: [
            for (final row in rows) ...[
              Expanded(
                child: Row(
                  children: [
                    for (var i = 0; i < row.length; i++) ...[
                      if (i > 0) const SizedBox(width: gap),
                      Expanded(child: row[i]),
                    ],
                  ],
                ),
              ),
              if (row != rows.last) const SizedBox(height: gap),
            ],
          ],
        );
      },
    );
  }
}
