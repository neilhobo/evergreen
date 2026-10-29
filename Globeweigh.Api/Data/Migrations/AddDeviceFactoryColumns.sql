-- Screen-mode redesign for TauriTouch: the mode is now WeighIn / WeighOut / Factory
-- (mutually exclusive). Factory mode shows a Home menu whose buttons are toggled by
-- SampleWeight / Production / Camera. Production already exists; add the other three.
-- (Dispatch column is left in place but no longer used.) Run once against GbEvergreen.
IF COL_LENGTH('dbo.Device', 'Factory') IS NULL
    ALTER TABLE dbo.Device ADD Factory bit NOT NULL CONSTRAINT DF_Device_Factory DEFAULT(0);
GO
IF COL_LENGTH('dbo.Device', 'SampleWeight') IS NULL
    ALTER TABLE dbo.Device ADD SampleWeight bit NOT NULL CONSTRAINT DF_Device_SampleWeight DEFAULT(0);
GO
IF COL_LENGTH('dbo.Device', 'Camera') IS NULL
    ALTER TABLE dbo.Device ADD Camera bit NOT NULL CONSTRAINT DF_Device_Camera DEFAULT(0);
GO
