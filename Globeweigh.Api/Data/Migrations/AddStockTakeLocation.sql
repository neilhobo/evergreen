-- Stock take locations: a stock take covers exactly one location/area.
-- 1) StockTake.LocationId — nullable (takes saved before locations existed have none).
-- 2) Refresh the legacy dbo.StockLocation table (reused) to the current areas:
--    Main Yard, McSloys Yard, Tyrone Trailers, Dmack.
--    "McSloys" is renamed in place; the other legacy rows are soft-deleted.
-- Run once against GbEvergreen.
IF COL_LENGTH('dbo.StockTake', 'LocationId') IS NULL
    ALTER TABLE dbo.StockTake ADD LocationId int NULL;
GO
UPDATE dbo.StockLocation SET LocationName = 'McSloys Yard' WHERE LocationName = 'McSloys';
UPDATE dbo.StockLocation SET DateDeleted = GETDATE()
    WHERE LocationName IN ('Morgans', 'Main Factory') AND DateDeleted IS NULL;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.StockLocation WHERE LocationName = 'Main Yard')
    INSERT dbo.StockLocation (LocationName, DateCreated) VALUES ('Main Yard', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.StockLocation WHERE LocationName = 'Tyrone Trailers')
    INSERT dbo.StockLocation (LocationName, DateCreated) VALUES ('Tyrone Trailers', GETDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.StockLocation WHERE LocationName = 'Dmack')
    INSERT dbo.StockLocation (LocationName, DateCreated) VALUES ('Dmack', GETDATE());
GO
