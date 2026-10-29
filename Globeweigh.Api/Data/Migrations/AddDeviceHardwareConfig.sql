-- Per-PC hardware setup, moved from WebView localStorage into the Device row so a shell
-- reinstall (or PC re-image) can't lose it. JSON blobs use the exact shapes the app already
-- saved locally (WeighscaleConfig / RelayConfig from serial.ts / relay.ts); PrinterName is
-- the Windows printer name. Run once against GbEvergreen (local AND prod).
IF COL_LENGTH('dbo.Device', 'WeighscaleConfig') IS NULL
    ALTER TABLE dbo.Device ADD WeighscaleConfig nvarchar(1000) NULL;
GO
IF COL_LENGTH('dbo.Device', 'RelayConfig') IS NULL
    ALTER TABLE dbo.Device ADD RelayConfig nvarchar(500) NULL;
GO
IF COL_LENGTH('dbo.Device', 'PrinterName') IS NULL
    ALTER TABLE dbo.Device ADD PrinterName nvarchar(200) NULL;
GO
