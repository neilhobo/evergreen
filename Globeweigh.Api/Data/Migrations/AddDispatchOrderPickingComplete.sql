-- Picking app: one order at a time. A picker selects an order on the load and may only
-- scan pallets onto that order; it must be finished before another order can be started —
-- either fully picked, or ended short via "Complete Early" (a confirmed, deliberate act).
--
--  * DispatchOrder.DatePickingComplete — already on the table, previously unused by the
--    API. Now set when an order's pallets are all picked, or when the picker completes it
--    early. Cleared again if a pallet is un-picked from a fully-picked (not early) order.
--  * DispatchOrder.PickingCompletedById — the picker who completed it (Session user).
--  * DispatchOrder.PickingCompletedEarly — 1 when completed short of the ordered pallet
--    count, so the exception can be reported on (like DispatchOrder_Picked.IsNoScan).
--
-- Run once against GbEvergreen. Idempotent. Additive only — the legacy WPF app is unaffected.

IF COL_LENGTH('dbo.DispatchOrder', 'PickingCompletedById') IS NULL
BEGIN
    ALTER TABLE dbo.DispatchOrder ADD PickingCompletedById INT NULL;
END
GO

IF COL_LENGTH('dbo.DispatchOrder', 'PickingCompletedEarly') IS NULL
BEGIN
    ALTER TABLE dbo.DispatchOrder
        ADD PickingCompletedEarly BIT NOT NULL
            CONSTRAINT DF_DispatchOrder_PickingCompletedEarly DEFAULT(0);
END
GO
