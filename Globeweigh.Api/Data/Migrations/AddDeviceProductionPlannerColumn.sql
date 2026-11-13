-- Adds the ProductionPlanner Home-button toggle to dbo.Device (TauriTouch Factory mode),
-- alongside SampleWeight / Production / ProductionSummary / Camera / ManualStock. It also
-- gates GET /api/production/planning/touch/{deviceName}: only a device with this on can
-- read the planner without a login. Run once against GbEvergreen.
IF COL_LENGTH('dbo.Device', 'ProductionPlanner') IS NULL
    ALTER TABLE dbo.Device ADD ProductionPlanner bit NOT NULL CONSTRAINT DF_Device_ProductionPlanner DEFAULT(0);
GO
