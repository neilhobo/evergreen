import 'package:flutter/material.dart';

import '../../theme/colors.dart';

/// Shown while the stored session is checked at startup; the router moves on
/// once AuthController knows the answer.
///
/// The logo sits exactly where the native launch screen draws it (centred,
/// 240 dp wide: flutter_native_splash in pubspec.yaml), so the hand-off
/// doesn't jump; the spinner hangs below it without moving it.
class SplashScreen extends StatelessWidget {
  const SplashScreen({super.key});

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.navy,
      body: Stack(
        alignment: Alignment.center,
        children: [
          Image.asset(
            'assets/images/evergreen_logo_glow_white.png',
            width: 240,
            semanticLabel: 'Evergreen Horticulture',
          ),
          const Positioned.fill(
            child: Align(
              alignment: Alignment(0, 0.45),
              child: SizedBox(
                width: 26,
                height: 26,
                child: CircularProgressIndicator(color: Colors.white70, strokeWidth: 2.5),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
