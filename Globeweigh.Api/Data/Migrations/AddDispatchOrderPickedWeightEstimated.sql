-- Picking app: a pick with no measured weight now carries an estimate instead of 0.
--
--  * DispatchOrder_Picked.IsWeightEstimated — Weight was NOT read from the pallet's own
--    CodicoPallet record (a no-scan label, or a scanned code with no/zero master row) but
--    estimated by the API from the product's other pallets: the median of the ones still in
--    stock, else of the ones already scanned onto the same order line, else of the product's
--    last dispatched pallets. Before this the row held Weight 0, which understated every
--    total downstream (the app's calculated weight, vwDispatchOrder/Item PickedWeight, the
--    delivery note, the Sage export). Flagged so the figure is never mistaken for measured.
--
--    Historic rows are left as they are (Weight 0, flag 0): re-weighting dispatched orders
--    would change delivery notes and Sage figures already issued.
--
-- Run once against GbEvergreen. Idempotent. Additive only — the legacy WPF app is unaffected.

IF COL_LENGTH('dbo.DispatchOrder_Picked', 'IsWeightEstimated') IS NULL
BEGIN
    ALTER TABLE dbo.DispatchOrder_Picked
        ADD IsWeightEstimated BIT NOT NULL CONSTRAINT DF_DispatchOrderPicked_IsWeightEstimated DEFAULT(0);
END
GO
