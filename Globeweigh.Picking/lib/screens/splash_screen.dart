import 'package:flutter/material.dart';

import '../services/device_registration.dart';
import '../services/home_shortcut.dart';
import '../theme/app_colors.dart';
import '../widgets/gradient_bg.dart';
import '../widgets/wordmark.dart';
import 'login_screen.dart';

/// Navy branded splash shown on startup. Kicks off device registration
/// (best-effort, non-blocking) then moves on to the home screen.
class SplashScreen extends StatefulWidget {
  const SplashScreen({super.key});

  @override
  State<SplashScreen> createState() => _SplashScreenState();
}

class _SplashScreenState extends State<SplashScreen> {
  @override
  void initState() {
    super.initState();
    // Register this device with the server on startup.
    DeviceRegistration.registerOnStartup();
    // Branded pause, then into the app. Long enough to stay visible after the
    // Android 12 native splash (which overlays us for ~1.5s) lifts.
    Future.delayed(const Duration(milliseconds: 2800), () {
      if (!mounted) return;
      Navigator.of(context).pushReplacement(
        MaterialPageRoute(builder: (_) => const LoginScreen()),
      );
      // First launch only: one-tap "Add to Home screen?" prompt.
      HomeShortcut.requestPinOnce();
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: AppColors.navy,
      body: GradientBg(
        child: SafeArea(
          child: Center(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                const Wordmark(iconSize: 64, fontSize: 34),
                const SizedBox(height: 44),
                const SizedBox(
                  width: 26,
                  height: 26,
                  child: CircularProgressIndicator(
                    strokeWidth: 2.5,
                    valueColor: AlwaysStoppedAnimation(AppColors.accent),
                  ),
                ),
                const SizedBox(height: 18),
                Text(
                  'Loading…',
                  style: TextStyle(
                    fontFamily: 'Poppins',
                    fontSize: 15,
                    fontWeight: FontWeight.w400,
                    letterSpacing: 0.5,
                    color: Colors.white.withValues(alpha: 0.6),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
