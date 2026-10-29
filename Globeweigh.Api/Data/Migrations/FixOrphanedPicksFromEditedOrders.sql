-- @manual - one-off data correction; run it deliberately, not from a deploy.
-- One-off data correction for picks orphaned by the old UpdateOrder behaviour.
--
-- UpdateOrder used to delete every DispatchOrderItem row and re-add it, so picked pallets
-- were left pointing at a line id that no longer existed. Where the product stayed on the
-- order the totals still resolve by (OrderId, ProductId) and nothing is wrong; where the
-- EDIT REMOVED THE LINE the picks were stranded — still flagged picked, on no order line,
-- and never shipped (confirmed with the client for load 4761 / order 83: 24 shipped, not 29).
--
-- This does exactly what the fixed UpdateOrder now does at the moment a line is dropped:
--   1. soft-delete the stranded picks (the pallets go back on the yard — stock is only
--      touched at dispatch, so there is nothing to restore), and
--   2. clear CodicoPallet.PickedOrderId so those pallets can be picked again.
--
-- Scope is deliberately narrow: ONLY picks whose line id is gone AND whose product is no
-- longer on the order. The 133 picks whose product survived the edit are left untouched.
-- Run once against GbEvergreen.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

BEGIN TRANSACTION;

-- The stranded picks, captured before anything changes.
SELECT p.id, p.DispatchOrderId, p.ProductId, p.PalletId
INTO   #orphaned
FROM   dbo.DispatchOrder_Picked p
WHERE  p.DateDeleted IS NULL
   AND NOT EXISTS (SELECT 1 FROM dbo.DispatchOrderItem i WHERE i.id = p.DispatchOrderItemId)
   AND NOT EXISTS (SELECT 1 FROM dbo.DispatchOrderItem i
                   WHERE i.OrderId = p.DispatchOrderId AND i.ProductId = p.ProductId);

UPDATE p
SET    p.DateDeleted = GETDATE(), p.DeletedById = 0
FROM   dbo.DispatchOrder_Picked p
JOIN   #orphaned o ON o.id = p.id;

-- Free the master pallet records, but only where the order now holds no live pick for
-- that code (mirrors the unpick path in PickingController). No-scan picks carry no code.
UPDATE c
SET    c.PickedOrderId = NULL
FROM   dbo.CodicoPallet c
JOIN   #orphaned o ON o.PalletId = c.UniqueCode AND o.DispatchOrderId = c.PickedOrderId
WHERE  o.PalletId <> ''
   AND NOT EXISTS (SELECT 1 FROM dbo.DispatchOrder_Picked p2
                   WHERE p2.DateDeleted IS NULL
                     AND p2.PalletId = c.UniqueCode
                     AND p2.DispatchOrderId = o.DispatchOrderId);

DROP TABLE #orphaned;

COMMIT;
GO
