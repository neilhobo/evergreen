import 'package:flutter/material.dart';

/// Palette mirrored from the SpecGauge app styling.
class AppColors {
  AppColors._();

  static const Color primary = Color(0xFF087EEF);
  static const Color primaryHover = Color(0xFF0A6FD1);
  // Brand accent — green (was orange in the SpecGauge source).
  static const Color accent = Color(0xFF22C55E);

  static const Color navy = Color(0xFF152338);
  static const Color navyDeep = Color(0xFF0D1624);
  static const Color sidebar = Color(0xFF1A2332);

  static const Color success = Color(0xFF4ADE80);
  static const Color warning = Color(0xFFFB941F);
  static const Color error = Color(0xFFFD4E4E);

  static const Color textDark = Color(0xFF0F172A);
  static const Color textMuted = Color(0xFF475569);
  static const Color textLight = Color(0xFF64748B);
  static const Color placeholder = Color(0xFF94A3B8);

  static const Color borderLight = Color(0xFFE2E8F0);
  static const Color borderMedium = Color(0xFFE5E7EB);
  static const Color borderPanel = Color(0xFFCBD5E1);

  static const Color surface = Colors.white;
  // Soft periwinkle app background; white cards pop on top of it.
  static const Color background = Color(0xFFE8EAF6);
  static const Color backgroundSubtle = Color(0xFFF1F2F9);
}
