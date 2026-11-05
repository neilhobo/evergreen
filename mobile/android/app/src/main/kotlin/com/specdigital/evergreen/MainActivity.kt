package com.specdigital.evergreen

import android.content.Intent
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "evergreen/app")
            .setMethodCallHandler { call, result ->
                when (call.method) {
                    // Shorebird only applies a downloaded patch when a new
                    // process starts, so finishing the activity is not enough:
                    // queue a fresh launch of the app, then end this process.
                    "restart" -> {
                        val component = packageManager
                            .getLaunchIntentForPackage(packageName)?.component
                        if (component == null) {
                            result.error("no_launch_intent", null, null)
                        } else {
                            startActivity(Intent.makeRestartActivityTask(component))
                            Runtime.getRuntime().exit(0)
                        }
                    }
                    else -> result.notImplemented()
                }
            }
    }
}
