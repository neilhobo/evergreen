package com.globeweigh.globeweigh_picking

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.media.AudioManager
import android.media.ToneGenerator
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.provider.Settings
import androidx.core.content.pm.ShortcutInfoCompat
import androidx.core.content.pm.ShortcutManagerCompat
import androidx.core.graphics.drawable.IconCompat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel

class MainActivity : FlutterActivity() {
    private val shortcutChannelName = "globeweigh/shortcut"
    private val scannerChannelName = "globeweigh/scanner"
    private var scanChannel: MethodChannel? = null
    private var toneGen: ToneGenerator? = null

    // Receives the DataWedge scan broadcast and forwards the decoded data to Flutter.
    private val scanReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            val data = intent?.getStringExtra(DataWedge.DATA_EXTRA) ?: return
            if (data.isEmpty()) return
            runOnUiThread { scanChannel?.invokeMethod("onScan", data) }
        }
    }

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        val messenger = flutterEngine.dartExecutor.binaryMessenger

        MethodChannel(messenger, shortcutChannelName).setMethodCallHandler { call, result ->
            when (call.method) {
                "pinToHomeScreen" -> result.success(pinToHomeScreen())
                "getDeviceName" -> result.success(userDeviceName())
                else -> result.notImplemented()
            }
        }

        MethodChannel(messenger, "globeweigh/vnc").setMethodCallHandler { call, result ->
            when (call.method) {
                "isInstalled" -> result.success(VncCompanion.isInstalled(this))
                "start" -> result.success(VncCompanion.start(this))
                else -> result.notImplemented()
            }
        }

        MethodChannel(messenger, "globeweigh/app").setMethodCallHandler { call, result ->
            when (call.method) {
                "restart" -> {
                    result.success(null)
                    restartApp()
                }
                else -> result.notImplemented()
            }
        }

        scanChannel = MethodChannel(messenger, scannerChannelName)
        scanChannel?.setMethodCallHandler { call, result ->
            when (call.method) {
                "playError" -> {
                    playErrorTone()
                    result.success(null)
                }
                else -> result.notImplemented()
            }
        }

        // Provision our DataWedge profile so a fresh device scans with no manual setup.
        DataWedge.configure(this)
    }

    // Relaunch the app in a brand-new process. Shorebird loads a downloaded patch only when
    // the Flutter engine starts, so "restart to update" has to kill the process — a Flutter
    // hot restart would keep the old code. The restart task is handed to the system first;
    // it survives our exit because ActivityManager owns it, and starts us afresh.
    private fun restartApp() {
        val launch = packageManager.getLaunchIntentForPackage(packageName)
            ?: Intent(this, MainActivity::class.java)
        val component = launch.component ?: return
        startActivity(Intent.makeRestartActivityTask(component))
        // A beat for the start request to leave the process before it dies.
        Handler(Looper.getMainLooper()).postDelayed({ Runtime.getRuntime().exit(0) }, 150)
    }

    // Distinct error tone for a bad scan (duplicate pallet, wrong product, etc.).
    private fun playErrorTone() {
        try {
            if (toneGen == null) toneGen = ToneGenerator(AudioManager.STREAM_MUSIC, 100)
            toneGen?.startTone(ToneGenerator.TONE_SUP_ERROR, 500)
        } catch (e: Exception) {
            // Ignore — audio is best-effort.
        }
    }

    override fun onDestroy() {
        try {
            toneGen?.release()
        } catch (_: Exception) {
        }
        toneGen = null
        super.onDestroy()
    }

    override fun onResume() {
        super.onResume()
        // Re-provision each time we come to the foreground (idempotent, self-healing).
        DataWedge.configure(this)
        val filter = IntentFilter(DataWedge.INTENT_ACTION)
        // DataWedge broadcasts from its own process, so the receiver must be exported (API 33+).
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            registerReceiver(scanReceiver, filter, Context.RECEIVER_EXPORTED)
        } else {
            @Suppress("UnspecifiedRegisterReceiverFlag")
            registerReceiver(scanReceiver, filter)
        }
    }

    override fun onPause() {
        try {
            unregisterReceiver(scanReceiver)
        } catch (_: IllegalArgumentException) {
            // not registered — ignore
        }
        super.onPause()
    }

    // The name the user set in Settings > About phone > Device name. Zebra/Android stores it
    // in Settings.Global.device_name; fall back to the Bluetooth name, then null (the Dart
    // side then uses model + suffix). Reading these settings needs no permission.
    private fun userDeviceName(): String? {
        val sources = listOf(
            { Settings.Global.getString(contentResolver, Settings.Global.DEVICE_NAME) },
            { Settings.Secure.getString(contentResolver, "bluetooth_name") },
            { Settings.System.getString(contentResolver, "device_name") },
        )
        for (get in sources) {
            val v = try { get() } catch (_: Exception) { null }?.trim()
            if (!v.isNullOrBlank() && !isFactoryName(v)) return v
        }
        return null
    }

    // A never-renamed device reports its factory name — on a Zebra ET40 that's the bare model
    // "ET401", identical on every unit. Registration upserts on device name, so two such
    // tablets would share one Device row (and one would vanish from Device Manager). Treat a
    // factory name as "not set" so the Dart side generates a unique model + suffix instead.
    private fun isFactoryName(name: String): Boolean =
        listOf(Build.MODEL, Build.DEVICE, Build.PRODUCT)
            .any { it != null && it.isNotBlank() && it.trim().equals(name, ignoreCase = true) }

    // Asks the launcher to pin the app's icon to the home screen.
    private fun pinToHomeScreen(): Boolean {
        if (!ShortcutManagerCompat.isRequestPinShortcutSupported(this)) return false

        val launchIntent = (packageManager.getLaunchIntentForPackage(packageName)
            ?: Intent(this, MainActivity::class.java)).apply {
            action = Intent.ACTION_MAIN
        }

        val shortcut = ShortcutInfoCompat.Builder(this, "globeweigh_picking_home")
            .setShortLabel("Globeweigh Picking")
            .setIcon(IconCompat.createWithResource(this, R.mipmap.ic_launcher))
            .setIntent(launchIntent)
            .build()

        return ShortcutManagerCompat.requestPinShortcut(this, shortcut, null)
    }
}
