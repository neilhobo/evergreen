using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Globeweigh.Api.Data.Entities;

// Keyed entity for the Device table. Each physical touch screen has one row, keyed by
// DeviceName (the OS hostname). The screen-mode flags drive what the TauriTouch app
// shows on boot, exactly as the WPF MainWindowViewModel branched on them:
//   IsWeighIn -> Weigh-In screen, IsWeighOut -> Weigh-Out screen, else Home.
// Dispatch / Production were added (see Migrations/AddDeviceModeColumns.sql).
// All NOT NULL columns are mapped so a create-on-miss insert succeeds.
public class Device
{
    public int Id { get; set; }
    public string? DeviceName { get; set; }
    public string? IpAddress { get; set; }
    public string? ApplicationName { get; set; }
    public string? CurrentVersion { get; set; }
    // The Shorebird patch the Picking app is running on top of CurrentVersion; null on the bare
    // release, or from an app too old to report it (see Migrations/AddDeviceCurrentPatch.sql).
    public int? CurrentPatch { get; set; }
    // When CurrentVersion/CurrentPatch last changed (UTC); Device Manager hides finished pushes
    // older than this (see Migrations/AddDeviceDateOfLastUpdate.sql).
    public DateTime? DateOfLastUpdateUtc { get; set; }
    public string? VersionOnServer { get; set; }
    public bool IsBoxLabeler { get; set; }
    public bool AutoStartConfigured { get; set; }
    public bool RecycleRequested { get; set; }
    public bool IsWeighIn { get; set; }
    public bool IsWeighOut { get; set; }
    public bool Factory { get; set; }
    public bool Dispatch { get; set; } // legacy, no longer used by TauriTouch
    public bool Production { get; set; }
    public bool ProductionSummary { get; set; }
    public bool SampleWeight { get; set; }
    public bool Camera { get; set; }
    public bool ManualStock { get; set; } // see Migrations/AddDeviceManualStockColumn.sql
    public bool Weighscale { get; set; } // Factory: COM weighscale attached to this PC (see Migrations/AddDeviceWeighscaleColumn.sql)
    public bool Printer { get; set; } // Factory: label printer attached to this PC (see Migrations/AddDevicePrinterColumn.sql)
    public bool LoginEnabled { get; set; } = true; // Factory: show the sign-in screen on startup; off = straight to Home (see Migrations/AddDeviceLoginRelayColumns.sql)
    public bool RelayCard { get; set; } // Factory: relay card attached to this PC (see Migrations/AddDeviceLoginRelayColumns.sql)
    public bool ProductionAlarm { get; set; } // show the missed-scan alarm popup on this PC (see Migrations/AddProductionAlarm.sql)
    // Weigh In: ask for the haulier on a screen of its own, then Product/Supplier/Bog
    // together (see Migrations/AddDeviceHaulierOwnScreen.sql). Off = all four lists at once.
    public bool HaulierOwnScreen { get; set; }

    public string? DisplayName { get; set; } // "PC Name": shown big above the Home menu buttons (see Migrations/AddDeviceDisplayName.sql)

    // Per-PC hardware setup, moved out of WebView localStorage so shell reinstalls can't
    // lose it (see Migrations/AddDeviceHardwareConfig.sql). JSON blobs in the app's shapes.
    public string? WeighscaleConfig { get; set; }
    public string? RelayConfig { get; set; }
    public string? PrinterName { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateOfLastPing { get; set; }
    public DateTime? DateOfLastRestart { get; set; }

    // Hidden from the client-facing Devices overview (see Migrations/AddDeviceHiddenColumn.sql).
    public bool Hidden { get; set; }

    // Remote-control (scrcpy / wireless-ADB) setup — see Migrations/AddDeviceRemoteColumns.sql.
    public string? RemoteHost { get; set; }   // ADB host override; blank => fall back to IpAddress
    public int? RemotePort { get; set; }      // ADB connect port (wireless-debugging port, or 5555)
    public string? RemoteNotes { get; set; }  // free-text setup notes

    public class Config : IEntityTypeConfiguration<Device>
    {
        public void Configure(EntityTypeBuilder<Device> b)
        {
            b.ToTable("Device");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).HasColumnName("id");
        }
    }
}
