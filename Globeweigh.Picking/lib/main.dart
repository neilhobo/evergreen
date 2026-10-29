import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'config.dart';
import 'screens/splash_screen.dart';
import 'theme/app_theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  // Hide the Android navigation bar; a swipe from the edge reveals it briefly,
  // then it auto-collapses again (sticky immersive).
  await SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky);
  // Decide which API server to talk to (local vs production) before anything calls it.
  await AppConfig.resolve();
  runApp(const GlobeweighPickingApp());
}

class GlobeweighPickingApp extends StatefulWidget {
  const GlobeweighPickingApp({super.key});

  @override
  State<GlobeweighPickingApp> createState() => _GlobeweighPickingAppState();
}

class _GlobeweighPickingAppState extends State<GlobeweighPickingApp>
    with WidgetsBindingObserver {
  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    // Android can restore the system bars after the app is backgrounded;
    // re-hide them whenever we come back to the foreground.
    if (state == AppLifecycleState.resumed) {
      SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky);
    }
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Globeweigh Picking',
      debugShowCheckedModeBanner: false,
      theme: buildAppTheme(),
      home: const SplashScreen(),
    );
  }
}
