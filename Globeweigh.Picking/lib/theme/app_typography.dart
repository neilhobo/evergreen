import 'package:flutter/material.dart';

import 'app_colors.dart';

/// Poppins type scale mirrored from the SpecGauge app styling.
class AppTypography {
  AppTypography._();

  static const String fontFamily = 'Poppins';

  static TextStyle _t({
    required double fontSize,
    required FontWeight fontWeight,
    Color color = AppColors.textDark,
    double height = 1.3,
    double? letterSpacing,
  }) =>
      TextStyle(
        fontFamily: fontFamily,
        fontSize: fontSize,
        fontWeight: fontWeight,
        color: color,
        height: height,
        letterSpacing: letterSpacing,
      );

  static TextTheme textTheme = TextTheme(
    displayLarge: _t(fontSize: 28, fontWeight: FontWeight.w700, height: 1.1),
    displayMedium: _t(fontSize: 22, fontWeight: FontWeight.w700, height: 1.2),
    headlineLarge: _t(fontSize: 18, fontWeight: FontWeight.w700, height: 1.2),
    headlineMedium: _t(fontSize: 16, fontWeight: FontWeight.w600, height: 1.2),
    titleLarge: _t(fontSize: 16, fontWeight: FontWeight.w600),
    titleMedium: _t(fontSize: 14, fontWeight: FontWeight.w600),
    titleSmall: _t(fontSize: 13, fontWeight: FontWeight.w500),
    bodyLarge: _t(fontSize: 14, fontWeight: FontWeight.w400, height: 1.4),
    bodyMedium: _t(fontSize: 13, fontWeight: FontWeight.w400, height: 1.3),
    bodySmall: _t(
        fontSize: 12,
        fontWeight: FontWeight.w400,
        color: AppColors.textMuted,
        height: 1.3),
    labelLarge: _t(fontSize: 13, fontWeight: FontWeight.w600, color: Colors.white),
    labelMedium: _t(
        fontSize: 12,
        fontWeight: FontWeight.w500,
        color: AppColors.textLight,
        letterSpacing: 0.4),
    labelSmall: _t(
        fontSize: 11, fontWeight: FontWeight.w500, color: AppColors.textLight),
  );

  static TextStyle cardLabel = _t(
    fontSize: 12,
    fontWeight: FontWeight.w500,
    color: AppColors.textLight,
    letterSpacing: 0.4,
  );

  static TextStyle cardValue = _t(
    fontSize: 28,
    fontWeight: FontWeight.w700,
    height: 1.1,
  );
}
