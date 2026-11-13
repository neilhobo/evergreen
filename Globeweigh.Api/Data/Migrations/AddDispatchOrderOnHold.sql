-- Adds the On Hold flag to dbo.DispatchOrder (Open Orders > row menu > Put on hold). An order
-- on hold cannot be put on a load, and only an order that is not on a load can be put on hold.
-- Read and written by DispatchController (Orders / SetOrderHold / LinkOrders); vwDispatchOrder
-- is left as it is.
-- Run once against GbEvergreen. Idempotent.
IF COL_LENGTH('dbo.DispatchOrder', 'OnHold') IS NULL
    ALTER TABLE dbo.DispatchOrder ADD OnHold bit NOT NULL
        CONSTRAINT DF_DispatchOrder_OnHold DEFAULT (0);
GO
