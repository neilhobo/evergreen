-- Trailer confirmation (Picking app): before working a newly-created pending load,
-- the picker must confirm the trailer number is correct. Nullable columns on
-- WeighbridgeRecord — DateTrailerConfirmed set when confirmed; TrailerConfirmedById
-- is the confirming user (0/null until the app gets a login, populated later).
-- Run once against GbEvergreen (idempotent). Additive — legacy WPF unaffected.
IF COL_LENGTH('dbo.WeighbridgeRecord', 'DateTrailerConfirmed') IS NULL
    ALTER TABLE dbo.WeighbridgeRecord ADD DateTrailerConfirmed datetime NULL;
GO
IF COL_LENGTH('dbo.WeighbridgeRecord', 'TrailerConfirmedById') IS NULL
    ALTER TABLE dbo.WeighbridgeRecord ADD TrailerConfirmedById int NULL;
GO
