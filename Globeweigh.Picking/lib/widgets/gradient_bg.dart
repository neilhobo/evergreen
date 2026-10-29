import 'package:flutter/material.dart';

import '../theme/app_colors.dart';

/// Navy gradient background with ambient green (top-right) and blue
/// (bottom-left) glows. Mirrors the SpecGauge login aesthetic — used for the
/// immersive entry screens.
class GradientBg extends StatelessWidget {
  final Widget child;

  const GradientBg({super.key, required this.child});

  @override
  Widget build(BuildContext context) {
    return DecoratedBox(
      decoration: const BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [
            Color(0xFF152338),
            Color(0xFF0D1624),
            Color(0xFF070D16),
          ],
          stops: [0.0, 0.55, 1.0],
        ),
      ),
      child: Stack(
        children: [
          // Green glow (top-right)
          Positioned(
            top: -250,
            right: -220,
            child: IgnorePointer(
              child: Container(
                width: 650,
                height: 650,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  gradient: RadialGradient(
                    colors: [
                      AppColors.accent.withValues(alpha: 0.22),
                      AppColors.accent.withValues(alpha: 0.0),
                    ],
                    stops: const [0.0, 0.65],
                  ),
                ),
              ),
            ),
          ),
          // Blue glow (bottom-left)
          Positioned(
            bottom: -200,
            left: -180,
            child: IgnorePointer(
              child: Container(
                width: 550,
                height: 550,
                decoration: const BoxDecoration(
                  shape: BoxShape.circle,
                  gradient: RadialGradient(
                    colors: [Color(0x262563EB), Color(0x002563EB)],
                    stops: [0.0, 0.65],
                  ),
                ),
              ),
            ),
          ),
          Positioned.fill(child: child),
        ],
      ),
    );
  }
}
