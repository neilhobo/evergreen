/// Small display formatters shared by the screens.
library;

/// Whole kilos with thousands separators — "1,234". Pallet weights are never shown to
/// the gram on the handheld.
String fmtKg(double kg) {
  final digits = kg.round().abs().toString();
  final out = StringBuffer(kg < 0 ? '-' : '');
  for (var i = 0; i < digits.length; i++) {
    if (i > 0 && (digits.length - i) % 3 == 0) out.write(',');
    out.write(digits[i]);
  }
  return out.toString();
}
