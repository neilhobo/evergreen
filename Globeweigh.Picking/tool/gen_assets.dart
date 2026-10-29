// Generates launcher-icon and splash source art from the brand globe (white disc with
// navy continents) so flutter_launcher_icons / flutter_native_splash can consume them.
// Run:  dart run tool/gen_assets.dart
import 'dart:io';
import 'package:image/image.dart' as img;

const scratch =
    r'C:\Users\neilh\AppData\Local\Temp\claude\d--Git-Globeweigh-Evergreen\5dbb22d1-a3b6-4a9d-96a6-22867893592e\scratchpad';

// Brand palette (matches the in-app Wordmark tile + navy home background).
final blueTop = img.ColorRgb8(0x08, 0x7E, 0xEF); // AppColors.primary
final blueBot = img.ColorRgb8(0x0A, 0x6F, 0xD1); // primaryHover
final navy = img.ColorRgb8(0x15, 0x23, 0x38); // AppColors.navy

img.Image loadGlobe() => img.decodePng(
    File(r'D:\Git-Globeweigh\Evergreen\Globeweigh.Api\wwwroot\admin\images\Globe-96.png')
        .readAsBytesSync())!;

img.Image verticalGradient(int size, img.Color top, img.Color bot) {
  final out = img.Image(width: size, height: size, numChannels: 4);
  for (var y = 0; y < size; y++) {
    final t = y / (size - 1);
    final r = (top.r + (bot.r - top.r) * t).round();
    final g = (top.g + (bot.g - top.g) * t).round();
    final b = (top.b + (bot.b - top.b) * t).round();
    for (var x = 0; x < size; x++) {
      out.setPixelRgba(x, y, r, g, b, 255);
    }
  }
  return out;
}

// White globe scaled onto a transparent square canvas of [canvas] px, globe ~[frac].
img.Image globeOn(int canvas, double frac, {img.Image? base}) {
  final globe = loadGlobe();
  final g = (canvas * frac).round();
  final scaled = img.copyResize(globe, width: g, height: g, interpolation: img.Interpolation.cubic);
  final dst = base ?? img.Image(width: canvas, height: canvas, numChannels: 4);
  final off = ((canvas - g) / 2).round();
  return img.compositeImage(dst, scaled, dstX: off, dstY: off);
}

void write(String path, img.Image im) {
  final f = File(path)..parent.createSync(recursive: true);
  f.writeAsBytesSync(img.encodePng(im));
  print('wrote $path (${im.width}x${im.height})');
}

void main() {
  const root = r'D:\Git-Globeweigh\Evergreen\Globeweigh.Picking';

  // Adaptive background: brand-blue gradient tile.
  final bg = verticalGradient(1024, blueTop, blueBot);
  write('$root\\assets\\icon\\background.png', bg);

  // Adaptive foreground: white globe, ~62% so it sits inside the adaptive safe zone.
  write('$root\\assets\\icon\\foreground.png', globeOn(1024, 0.62));

  // Legacy / round icon: gradient tile + globe (~64%).
  final legacy = globeOn(1024, 0.64, base: verticalGradient(1024, blueTop, blueBot));
  write('$root\\assets\\icon\\icon.png', legacy);

  // Native splash centre: white globe on transparent (shown on the navy splash bg).
  // Sized for the Android-12+ splash circle mask (content kept within the centre safe
  // zone of a 1152px canvas) since the ET40 (Android 13) uses that splash API.
  write('$root\\assets\\splash\\globe.png', globeOn(1152, 0.45));

  // Previews so the look can be checked before generating.
  write('$scratch\\_preview_icon.png', legacy);
  final fgNavy = img.compositeImage(verticalGradient(1024, navy, navy), globeOn(1024, 0.62));
  write('$scratch\\_preview_splash.png', fgNavy);
}
