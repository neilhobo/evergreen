-- Adds Device.HaulierOwnScreen ("Show Hauliers on one screen" in Screen Settings, shown
-- when the screen is in Weigh In mode). On = the Delivery flow asks for the haulier on a
-- screen of its own and then shows Product / Supplier / Bog together, so each list is wide
-- enough to read across the weighbridge. Off (default) = all four lists on one screen, the
-- original layout. Run once against GbEvergreen (local AND prod).
IF COL_LENGTH('dbo.Device', 'HaulierOwnScreen') IS NULL
    ALTER TABLE dbo.Device ADD HaulierOwnScreen bit NOT NULL CONSTRAINT DF_Device_HaulierOwnScreen DEFAULT(0);
GO
