-- Newly created pallets always default to the Main Yard location. Pallets are created
-- two ways — the 3rd party's direct SQL INSERT and TauriTouch Manual Stock (API/EF) —
-- so the default lives in the DATABASE as a DEFAULT constraint: the 3rd party's insert
-- doesn't know the column exists and picks it up automatically, and EF omits the column
-- when unset so it gets the same default from the same single place.
-- The Main Yard id is resolved by name at migration time (it's 4 on dev and prod, but
-- never hard-code it blind).
-- Run once against GbEvergreen. Requires AddStockTakeLocation.sql (Main Yard row).
-- QUOTED_IDENTIFIER must be ON to alter CodicoPallet; sqlcmd defaults it OFF.
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.CodicoPallet', 'LocationId') IS NULL
BEGIN
    DECLARE @mainYard int = (SELECT TOP 1 id FROM dbo.StockLocation
                             WHERE LocationName = 'Main Yard' AND DateDeleted IS NULL);
    IF @mainYard IS NULL
        RAISERROR ('Main Yard not found in dbo.StockLocation — run AddStockTakeLocation.sql first.', 16, 1);
    ELSE
    BEGIN
        DECLARE @sql nvarchar(max) =
            N'ALTER TABLE dbo.CodicoPallet ADD LocationId int NULL '
            + N'CONSTRAINT DF_CodicoPallet_LocationId DEFAULT (' + CAST(@mainYard AS nvarchar(10)) + N')';
        EXEC sp_executesql @sql;
    END
END
GO
