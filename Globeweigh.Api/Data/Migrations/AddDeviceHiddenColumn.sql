-- Hidden flag for the client-facing Devices overview: hidden devices are filtered out
-- of api/devices/all unless includeHidden=true (dev-mode "Show hidden"). Additive and
-- idempotent — legacy apps are unaffected. Run once against GbEvergreen (local + prod).
IF COL_LENGTH('dbo.Device', 'Hidden') IS NULL
BEGIN
    ALTER TABLE dbo.Device ADD Hidden BIT NOT NULL CONSTRAINT DF_Device_Hidden DEFAULT(0);
END
GO
