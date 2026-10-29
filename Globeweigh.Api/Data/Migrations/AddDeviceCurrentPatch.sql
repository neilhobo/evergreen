-- Device.CurrentPatch: the Shorebird patch number the Picking app on the device is running,
-- reported with every registration alongside CurrentVersion, so Device Manager can show which
-- tablets have taken a patch (a patch changes nothing in CurrentVersion). NULL = on the bare
-- release, or an app too old to report it. See DeviceManagementController.Register.
IF COL_LENGTH('dbo.Device', 'CurrentPatch') IS NULL
    ALTER TABLE dbo.Device ADD CurrentPatch int NULL;
GO
