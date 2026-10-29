-- Adds the ManualStock Home-button toggle to dbo.Device (TauriTouch Factory mode),
-- alongside SampleWeight / Production / ProductionSummary / Camera. Run once against GbEvergreen.
IF COL_LENGTH('dbo.Device', 'ManualStock') IS NULL
    ALTER TABLE dbo.Device ADD ManualStock bit NOT NULL CONSTRAINT DF_Device_ManualStock DEFAULT(0);
GO
