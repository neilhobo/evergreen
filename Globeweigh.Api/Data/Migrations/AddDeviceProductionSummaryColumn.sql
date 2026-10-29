-- Adds the ProductionSummary Home-button toggle to dbo.Device (TauriTouch Factory mode),
-- alongside SampleWeight / Production / Camera. Run once against GbEvergreen.
IF COL_LENGTH('dbo.Device', 'ProductionSummary') IS NULL
    ALTER TABLE dbo.Device ADD ProductionSummary bit NOT NULL CONSTRAINT DF_Device_ProductionSummary DEFAULT(0);
GO
