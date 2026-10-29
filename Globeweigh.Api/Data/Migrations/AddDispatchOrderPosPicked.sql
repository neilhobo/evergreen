-- Records that an order's POS board (DispatchOrder.HasPos) was picked. The board is not an
-- order line, so the Picking app shows it as an extra "POS Board" line on the order that the
-- picker taps to pick (nothing scanned). Picked = DatePosPicked set; PosPickedById = the
-- Picking app's signed-in user. It counts as one pick towards the order being full.
-- Run once against GbEvergreen. Idempotent. Additive only.
IF COL_LENGTH('dbo.DispatchOrder', 'DatePosPicked') IS NULL
    ALTER TABLE dbo.DispatchOrder ADD DatePosPicked datetime NULL;
GO

IF COL_LENGTH('dbo.DispatchOrder', 'PosPickedById') IS NULL
    ALTER TABLE dbo.DispatchOrder ADD PosPickedById int NULL;
GO
