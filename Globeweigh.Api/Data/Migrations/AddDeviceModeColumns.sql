-- Adds the Dispatch and Production screen-mode flags to dbo.Device, mirroring the
-- existing IsWeighIn / IsWeighOut columns. Run once against GbEvergreen.
-- (The WPF Globeweigh.Touch app only had IsWeighIn/IsWeighOut/IsBoxLabeler; the
-- TauriTouch Super Admin config adds Dispatch and Production modes.)
IF COL_LENGTH('dbo.Device', 'Dispatch') IS NULL
    ALTER TABLE dbo.Device ADD Dispatch bit NOT NULL CONSTRAINT DF_Device_Dispatch DEFAULT(0);
GO
IF COL_LENGTH('dbo.Device', 'Production') IS NULL
    ALTER TABLE dbo.Device ADD Production bit NOT NULL CONSTRAINT DF_Device_Production DEFAULT(0);
GO
