package com.globeweigh.globeweigh_picking

import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager

/**
 * Bridge to the droidVNC-NG remote-support companion app. The Picking app can't install
 * it silently (Android forbids that without an MDM), but it can detect it and nudge its
 * VNC server started through droidVNC-NG's documented Intent API.
 *
 * The access key here must match the one preseeded by scripts/provision-vnc.ps1
 * (defaults.json). On a provisioned device (PROJECT_MEDIA appop granted) the start is
 * fully headless; on an unprovisioned device droidVNC-NG rejects the unknown key and
 * nothing happens — so calling this on every launch is safe.
 */
object VncCompanion {
    const val PACKAGE = "net.christianbeier.droidvnc_ng"
    private const val PREFIX = "net.christianbeier.droidvnc_ng"

    // Must match scripts/provision-vnc.ps1 (defaults.json accessKey/password/port).
    private const val ACCESS_KEY = "ec275e75ab76cb8708f4993aa4c57b47"
    private const val VNC_PORT = 5900
    private const val VNC_PASSWORD = "2wb7wkrb"

    fun isInstalled(context: Context): Boolean = try {
        context.packageManager.getPackageInfo(PACKAGE, 0)
        true
    } catch (_: PackageManager.NameNotFoundException) {
        false
    }

    /** Ask droidVNC-NG to start its server (idempotent; no-op if it rejects the key). */
    fun start(context: Context): Boolean = try {
        val intent = Intent().apply {
            component = ComponentName(PACKAGE, "$PREFIX.MainService")
            action = "$PREFIX.ACTION_START"
            putExtra("$PREFIX.EXTRA_ACCESS_KEY", ACCESS_KEY)
            putExtra("$PREFIX.EXTRA_PORT", VNC_PORT)
            putExtra("$PREFIX.EXTRA_PASSWORD", VNC_PASSWORD)
        }
        context.startForegroundService(intent)
        true
    } catch (_: Exception) {
        false
    }
}
