-- Stock transfers (TauriAdmin Stock module): move counted stock between locations.
-- A transfer reuses dbo.StockTake (IsTransfer=1) + dbo.StockTakeItem for the moved
-- quantities: LocationId = destination, FromLocationId = source. Counts-only — the
-- pallet in/out-of-stock state is untouched; only per-location standing counts move.
-- Run once against GbEvergreen. Requires AddStockTakeTables.sql / AddStockTakeLocation.sql.
IF COL_LENGTH('dbo.StockTake', 'IsTransfer') IS NULL
    ALTER TABLE dbo.StockTake ADD IsTransfer bit NOT NULL CONSTRAINT DF_StockTake_IsTransfer DEFAULT (0);
GO
IF COL_LENGTH('dbo.StockTake', 'FromLocationId') IS NULL
    ALTER TABLE dbo.StockTake ADD FromLocationId int NULL;
GO
