-- Adds per-device remote-control (scrcpy / wireless-ADB) setup fields to dbo.Device.
-- Backs the TauriAdmin Devices page detail panel: the admin stores each Android
-- device's ADB host/port so the "View" button can pair/connect and launch scrcpy.
-- RemoteHost  - ADB host override; blank falls back to the device's registered IpAddress.
-- RemotePort  - ADB connect port (wireless-debugging connect port, or 5555 in tcpip mode).
-- RemoteNotes - free-text setup notes (e.g. "tcpip 5555 enabled", network name).
-- Run once against GbEvergreen.
IF COL_LENGTH('dbo.Device', 'RemoteHost') IS NULL
    ALTER TABLE dbo.Device ADD RemoteHost nvarchar(64) NULL;
GO
IF COL_LENGTH('dbo.Device', 'RemotePort') IS NULL
    ALTER TABLE dbo.Device ADD RemotePort int NULL;
GO
IF COL_LENGTH('dbo.Device', 'RemoteNotes') IS NULL
    ALTER TABLE dbo.Device ADD RemoteNotes nvarchar(256) NULL;
GO
