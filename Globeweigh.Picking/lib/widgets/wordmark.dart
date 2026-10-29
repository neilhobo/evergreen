import 'package:flutter/material.dart';

import '../theme/app_colors.dart';

/// "Globe" (white) / "weigh" (green) wordmark with a rounded icon tile and
/// a green glow — mirrors the SpecGauge wordmark structure.
class Wordmark extends StatelessWidget {
  final double iconSize;
  final double fontSize;

  const Wordmark({super.key, this.iconSize = 56, this.fontSize = 30});

  @override
  Widget build(BuildContext context) {
    final radius = iconSize / 4;
    return Row(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        Container(
          width: iconSize,
          height: iconSize,
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(radius),
            gradient: const LinearGradient(
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
              colors: [AppColors.primary, Color(0xFF0A6FD1)],
            ),
            boxShadow: [
              BoxShadow(
                color: AppColors.accent.withValues(alpha: 0.55),
                blurRadius: iconSize * 0.22,
              ),
            ],
          ),
          child: Icon(Icons.public, color: Colors.white, size: iconSize * 0.6),
        ),
        SizedBox(width: iconSize * 0.28),
        Text.rich(
          TextSpan(
            style: TextStyle(
              fontFamily: 'Poppins',
              fontSize: fontSize,
              fontWeight: FontWeight.w700,
              letterSpacing: -0.3,
              height: 1,
            ),
            children: const [
              TextSpan(text: 'Globe', style: TextStyle(color: Colors.white)),
              TextSpan(text: 'weigh', style: TextStyle(color: AppColors.accent)),
            ],
          ),
        ),
      ],
    );
  }
}
