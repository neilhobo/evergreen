-- Adds the Printer toggle to dbo.Device (TauriTouch Factory mode). When on, the Home
-- screen shows a printer status next to the weighscale one in the bottom bar.
-- Run once against GbEvergreen. Idempotent.
IF COL_LENGTH('dbo.Device', 'Printer') IS NULL
    ALTER TABLE dbo.Device ADD Printer bit NOT NULL CONSTRAINT DF_Device_Printer DEFAULT(0);
GO
