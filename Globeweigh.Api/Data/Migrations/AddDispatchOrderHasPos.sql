-- Adds the POS (point-of-sale board) flag to dbo.DispatchOrder. A POS board is not a
-- product: no stock, no picking, no price - an order either has one or it doesn't, so
-- it is a bit on the order rather than a DispatchOrderItem.
-- Run once against GbEvergreen. Idempotent.
IF COL_LENGTH('dbo.DispatchOrder', 'HasPos') IS NULL
    ALTER TABLE dbo.DispatchOrder ADD HasPos bit NOT NULL
        CONSTRAINT DF_DispatchOrder_HasPos DEFAULT (0);
GO
