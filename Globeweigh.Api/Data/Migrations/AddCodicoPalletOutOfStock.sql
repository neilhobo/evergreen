-- Out-of-stock marking on the CodicoPallet master list, driven by "Make Current Stock"
-- on a stock take: the newest N pallets per product (N = the summed stock-take counts
-- across locations) stay in stock, everything else is marked out with the method
-- recorded. NULL DateOutOfStock = in stock. Pallets can be brought BACK into stock by a
-- later stock take (the columns are simply cleared), so this is a state, not a log.
-- Run once against GbEvergreen.
-- QUOTED_IDENTIFIER must be ON to alter CodicoPallet (it has filtered/indexed
-- dependencies); sqlcmd defaults it OFF.
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.CodicoPallet', 'DateOutOfStock') IS NULL
    ALTER TABLE dbo.CodicoPallet ADD DateOutOfStock datetime NULL;
GO
IF COL_LENGTH('dbo.CodicoPallet', 'OutOfStockMethod') IS NULL
    ALTER TABLE dbo.CodicoPallet ADD OutOfStockMethod nvarchar(50) NULL;
GO
-- The stock take whose "Make Current Stock" marked this pallet out (when the method is
-- StockTake).
IF COL_LENGTH('dbo.CodicoPallet', 'OutOfStockStockTakeId') IS NULL
    ALTER TABLE dbo.CodicoPallet ADD OutOfStockStockTakeId int NULL;
GO
