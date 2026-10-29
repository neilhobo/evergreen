-- Adds the Weighscale toggle to dbo.Device (TauriTouch Factory mode). When on, the Home
-- screen shows a weighscale status in the bottom bar and the COM weighscale setup screen
-- is available on that PC. Run once against GbEvergreen.
IF COL_LENGTH('dbo.Device', 'Weighscale') IS NULL
    ALTER TABLE dbo.Device ADD Weighscale bit NOT NULL CONSTRAINT DF_Device_Weighscale DEFAULT(0);
GO
