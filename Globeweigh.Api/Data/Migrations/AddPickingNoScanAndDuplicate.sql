-- Picking app: support messy 3rd-party pallet data without blocking the picker.
--
--  * CodicoPallet.PickedOrderId — when a pallet is picked, the master record(s) for that
--    UniqueCode are linked to the dispatch order. Some UniqueCodes are duplicated by the
--    3rd party (2 CodicoPallet rows, same UniqueCode, different id); when that pallet is
--    scanned twice both rows get linked, so we can report exactly what was dispatched.
--
--  * DispatchOrder_Picked.IsNoScan — a pick added manually via "Add no-scan label" (a
--    label that won't read because it isn't in the expected 2D DataMatrix format). It
--    still updates the count so the order can be completed, and is flagged for reporting.
--
-- Run once against GbEvergreen. Idempotent. Additive only — the legacy WPF app is unaffected.

IF COL_LENGTH('dbo.CodicoPallet', 'PickedOrderId') IS NULL
BEGIN
    ALTER TABLE dbo.CodicoPallet ADD PickedOrderId INT NULL;
END
GO

IF COL_LENGTH('dbo.DispatchOrder_Picked', 'IsNoScan') IS NULL
BEGIN
    ALTER TABLE dbo.DispatchOrder_Picked
        ADD IsNoScan BIT NOT NULL CONSTRAINT DF_DispatchOrderPicked_IsNoScan DEFAULT(0);
END
GO
