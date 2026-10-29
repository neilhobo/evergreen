package com.globeweigh.globeweigh_picking

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.os.Parcelable
import android.util.Log

// Auto-provisions this app's Zebra DataWedge profile via the DataWedge API, so a freshly-installed
// device scans with NO manual DataWedge setup (the client never touches DataWedge). The profile is
// bound to this package and outputs each scan as a broadcast Intent (action INTENT_ACTION) carrying
// the decoded data in DATA_EXTRA; keystroke output is turned off so scans aren't also typed into a
// field. Idempotent + self-healing — safe to run on every launch.
//
// Modelled on the working SpiceOLife/Globeweigh.Maui setup. KEY detail: PLUGIN_CONFIG is sent as a
// SINGLE Bundle (putBundle), not a Bundle[] array — the array form is silently ignored by DataWedge.
object DataWedge {
    const val INTENT_ACTION = "com.globeweigh.picking.SCANNER"
    const val DATA_EXTRA = "com.symbol.datawedge.data_string"

    private const val PACKAGE_NAME = "com.globeweigh.globeweigh_picking"
    private const val PROFILE_NAME = "GlobeweighPicking"
    private const val DW_PACKAGE = "com.symbol.datawedge"
    private const val API_ACTION = "com.symbol.datawedge.api.ACTION"
    private const val LOG_TAG = "GwDataWedge"

    fun configure(context: Context) {
        try {
            // 1. Make sure DataWedge itself is enabled (kiosk images sometimes ship it disabled).
            send(context) { it.putExtra("com.symbol.datawedge.api.ENABLE_DATAWEDGE", true) }

            // 2. Create-if-missing our profile, associate this app, enable intent (broadcast) output.
            send(context) {
                it.putExtra("com.symbol.datawedge.api.SET_CONFIG", profileBundle())
                it.putExtra("SEND_RESULT", "true")
                it.putExtra("COMMAND_IDENTIFIER", "GWP_PROFILE")
            }

            // 3. Keystroke is a separate plugin; turn it off so scans don't also type into a field.
            send(context) {
                it.putExtra("com.symbol.datawedge.api.SET_CONFIG", keystrokeOffBundle())
                it.putExtra("SEND_RESULT", "true")
                it.putExtra("COMMAND_IDENTIFIER", "GWP_KS")
            }
        } catch (e: Exception) {
            // Non-Zebra device or DataWedge absent: ignore.
            Log.e(LOG_TAG, "configure failed: $e")
        }
    }

    private fun send(context: Context, fill: (Intent) -> Unit) {
        val intent = Intent().apply {
            action = API_ACTION
            setPackage(DW_PACKAGE) // explicit target (needs <queries> on Android 11+)
        }
        fill(intent)
        context.sendBroadcast(intent)
    }

    private fun profileBundle(): Bundle {
        val main = Bundle().apply {
            putString("PROFILE_NAME", PROFILE_NAME)
            putString("PROFILE_ENABLED", "true")
            putString("CONFIG_MODE", "CREATE_IF_NOT_EXIST")
        }

        val app = Bundle().apply {
            putString("PACKAGE_NAME", PACKAGE_NAME)
            putStringArray("ACTIVITY_LIST", arrayOf("*"))
        }
        main.putParcelableArray("APP_LIST", arrayOf<Parcelable>(app))

        val intentParams = Bundle().apply {
            putString("intent_output_enabled", "true")
            putString("intent_action", INTENT_ACTION)
            putString("intent_delivery", "2") // 0=StartActivity 1=StartService 2=Broadcast
        }
        val intentPlugin = Bundle().apply {
            putString("PLUGIN_NAME", "INTENT")
            putString("RESET_CONFIG", "true")
            putBundle("PARAM_LIST", intentParams)
        }
        main.putBundle("PLUGIN_CONFIG", intentPlugin) // single Bundle, not an array

        return main
    }

    private fun keystrokeOffBundle(): Bundle {
        val main = Bundle().apply {
            putString("PROFILE_NAME", PROFILE_NAME)
            putString("PROFILE_ENABLED", "true")
            putString("CONFIG_MODE", "UPDATE")
        }

        val keystrokeParams = Bundle().apply {
            putString("keystroke_output_enabled", "false")
        }
        val keystrokePlugin = Bundle().apply {
            putString("PLUGIN_NAME", "KEYSTROKE")
            putString("RESET_CONFIG", "true")
            putBundle("PARAM_LIST", keystrokeParams)
        }
        main.putBundle("PLUGIN_CONFIG", keystrokePlugin)

        return main
    }
}
