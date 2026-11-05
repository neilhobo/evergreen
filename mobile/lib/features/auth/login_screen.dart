import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../theme/colors.dart';
import 'auth_controller.dart';

class LoginScreen extends ConsumerStatefulWidget {
  const LoginScreen({super.key});

  @override
  ConsumerState<LoginScreen> createState() => _LoginScreenState();
}

class _LoginScreenState extends ConsumerState<LoginScreen> {
  final _formKey = GlobalKey<FormState>();
  final _username = TextEditingController();
  final _password = TextEditingController();
  bool _obscure = true;
  // On by default: the form is filled in next time, and a lapsed or refused
  // token signs straight back in.
  bool _remember = true;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) async {
      final saved = await ref.read(authStorageProvider).readSavedCredentials();
      if (!mounted || saved == null) return;
      setState(() {
        _username.text = saved.username;
        _password.text = saved.password;
        _remember = true;
      });
    });
  }

  @override
  void dispose() {
    _username.dispose();
    _password.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (!_formKey.currentState!.validate()) return;
    FocusScope.of(context).unfocus();
    await ref.read(authControllerProvider.notifier).login(_username.text.trim(), _password.text, remember: _remember);
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(authControllerProvider);
    final loading = state is AuthLoading;
    final error = state is AuthUnauthenticated ? state.message : null;

    return Scaffold(
      backgroundColor: AppColors.navy,
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            // 200 extra at the foot lifts the centred form 100 px: dead centre
            // reads as sitting low. Padding rather than a Transform, so it
            // still scrolls clear on a short screen or with the keyboard up.
            padding: const EdgeInsets.fromLTRB(24, 32, 24, 232),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 400),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  // The client's logo; the white-glow cut, as the plain one's black
                  // "Horticulture" script disappears on navy. Its glow pads the edges,
                  // hence the smaller gap below.
                  Center(
                    child: Image.asset(
                      'assets/images/evergreen_logo_glow_white.png',
                      width: 240,
                      semanticLabel: 'Evergreen Horticulture',
                    ),
                  ),
                  const SizedBox(height: 20),
                  Container(
                    padding: const EdgeInsets.fromLTRB(24, 28, 24, 24),
                    decoration: BoxDecoration(
                      color: Colors.white,
                      borderRadius: BorderRadius.circular(12),
                      boxShadow: [
                        BoxShadow(
                          color: Colors.black.withValues(alpha: 0.25),
                          blurRadius: 32,
                          offset: const Offset(0, 12),
                        ),
                      ],
                    ),
                    child: Theme(
                      data: _compactTheme(Theme.of(context)),
                      child: AutofillGroup(
                        child: Form(
                          key: _formKey,
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.stretch,
                            children: [
                              const Text(
                                'Sign in',
                                style: TextStyle(
                                  fontSize: 18,
                                  fontWeight: FontWeight.w700,
                                  height: 1.2,
                                  color: AppColors.text,
                                ),
                              ),
                              const SizedBox(height: 4),
                              const Text(
                                'Warehouse management',
                                style: TextStyle(fontSize: 13, color: AppColors.textMuted),
                              ),
                              const SizedBox(height: 24),
                              TextFormField(
                                controller: _username,
                                keyboardType: TextInputType.emailAddress,
                                autocorrect: false,
                                autofillHints: const [AutofillHints.username],
                                textInputAction: TextInputAction.next,
                                decoration: const InputDecoration(
                                  labelText: 'Email address',
                                  prefixIcon: Icon(Icons.mail_outline, size: 18),
                                ),
                                validator: (v) => (v == null || v.trim().isEmpty) ? 'Email address is required' : null,
                              ),
                              const SizedBox(height: 14),
                              TextFormField(
                                controller: _password,
                                obscureText: _obscure,
                                autofillHints: const [AutofillHints.password],
                                textInputAction: TextInputAction.done,
                                onFieldSubmitted: (_) => _submit(),
                                decoration: InputDecoration(
                                  labelText: 'Password',
                                  prefixIcon: const Icon(Icons.lock_outline, size: 18),
                                  suffixIcon: IconButton(
                                    icon: Icon(
                                      _obscure ? Icons.visibility_outlined : Icons.visibility_off_outlined,
                                      size: 18,
                                      color: AppColors.textMuted,
                                    ),
                                    tooltip: _obscure ? 'Show password' : 'Hide password',
                                    onPressed: () => setState(() => _obscure = !_obscure),
                                  ),
                                ),
                              ),
                              if (error != null) ...[
                                const SizedBox(height: 14),
                                Container(
                                  padding: const EdgeInsets.all(10),
                                  decoration: BoxDecoration(
                                    color: AppColors.error.withValues(alpha: 0.08),
                                    border: Border.all(color: AppColors.error.withValues(alpha: 0.3)),
                                    borderRadius: BorderRadius.circular(4),
                                  ),
                                  child: Row(
                                    children: [
                                      const Icon(Icons.error_outline, size: 18, color: AppColors.error),
                                      const SizedBox(width: 8),
                                      Expanded(
                                        child: Text(
                                          error,
                                          style: const TextStyle(fontSize: 12, color: AppColors.error),
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              ],
                              const SizedBox(height: 14),
                              Align(
                                alignment: Alignment.centerLeft,
                                child: _SaveDetails(value: _remember, onChanged: (v) => setState(() => _remember = v)),
                              ),
                              const SizedBox(height: 18),
                              SizedBox(
                                height: 48,
                                child: FilledButton(
                                  onPressed: loading ? null : _submit,
                                  child: loading
                                      ? const SizedBox(
                                          width: 18,
                                          height: 18,
                                          child: CircularProgressIndicator(color: Colors.white, strokeWidth: 2),
                                        )
                                      : const Text('Sign in'),
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 24),
                  Text(
                    '© ${DateTime.now().year} Specdigital Solutions Limited',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontSize: 11,
                      fontWeight: FontWeight.w500,
                      color: Colors.white.withValues(alpha: 0.4),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// The SpecGauge login's sizing (14 px input text, 13 px labels and button,
/// 12 px field corners, 14 px button corners), scoped to the login card so the
/// rest of the app keeps its own theme.
ThemeData _compactTheme(ThemeData base) {
  const label = TextStyle(fontFamily: 'Poppins', fontSize: 13, height: 1.3, color: AppColors.textMuted);
  OutlineInputBorder border(Color c, [double w = 1]) => OutlineInputBorder(
    borderRadius: BorderRadius.circular(12),
    borderSide: BorderSide(color: c, width: w),
  );
  return base.copyWith(
    textTheme: base.textTheme.copyWith(
      bodyLarge: const TextStyle(fontFamily: 'Poppins', fontSize: 14, height: 1.4, color: AppColors.text),
      bodySmall: const TextStyle(fontFamily: 'Poppins', fontSize: 12, height: 1.3, color: AppColors.textMuted),
    ),
    inputDecorationTheme: base.inputDecorationTheme.copyWith(
      contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 14),
      border: border(AppColors.border),
      enabledBorder: border(AppColors.border),
      focusedBorder: border(AppColors.primary, 1.5),
      errorBorder: border(AppColors.error),
      focusedErrorBorder: border(AppColors.error, 1.5),
      labelStyle: label,
      hintStyle: label,
    ),
    filledButtonTheme: FilledButtonThemeData(
      style: FilledButton.styleFrom(
        backgroundColor: AppColors.primary,
        foregroundColor: Colors.white,
        textStyle: const TextStyle(fontFamily: 'Poppins', fontSize: 13, fontWeight: FontWeight.w600),
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
        elevation: 0,
      ),
    ),
  );
}

class _SaveDetails extends StatelessWidget {
  final bool value;
  final ValueChanged<bool> onChanged;
  const _SaveDetails({required this.value, required this.onChanged});

  @override
  Widget build(BuildContext context) {
    return InkWell(
      borderRadius: BorderRadius.circular(6),
      onTap: () => onChanged(!value),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 4, horizontal: 2),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            SizedBox(
              width: 18,
              height: 18,
              child: Checkbox(
                value: value,
                onChanged: (v) => onChanged(v ?? false),
                materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
                visualDensity: VisualDensity.compact,
                activeColor: AppColors.primary,
                checkColor: Colors.white,
                side: const BorderSide(color: Color(0xFFCBD5E1), width: 1.5),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(4)),
              ),
            ),
            const SizedBox(width: 10),
            const Text(
              'Save details',
              style: TextStyle(fontFamily: 'Poppins', fontSize: 14, color: AppColors.text),
            ),
          ],
        ),
      ),
    );
  }
}
