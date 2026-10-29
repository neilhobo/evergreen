-- @manual - one-off data backfill, not part of the schema line a deploy applies.
-- Manual pallets are "born scanned". Run once against GbEvergreen (local AND prod).
--
-- CodicoPallet has the persisted computed column Code13, so any UPDATE needs
-- QUOTED_IDENTIFIER ON (sqlcmd defaults it OFF — msg 1934 without this).
SET QUOTED_IDENTIFIER ON;
GO

-- Manually-created pallets (Touch Manual Stock, Admin Stock manual add, stock-take
-- placeholders) never pass the conveyor scanner, so an unscanned manual pallet dated
-- today registers as a missed scan: a red row on the Production screen and the alarm
-- siren sounding with nothing that can ever clear it. New manual pallets are now
-- inserted with IsScanned = 1 (see ManualStockController.AddPallet and StockController);
-- this backfills the ones already in the table.
UPDATE dbo.CodicoPallet
SET IsScanned  = 1,
    DateScanned = ISNULL(DateScanned, DateCreated)
WHERE IsManual = 1
  AND IsScanned = 0;
GO

-- Drop the alarm acks that only existed to silence those false missed scans, so the
-- ack table stays a record of REAL missed pallets.
DELETE a
FROM dbo.ProductionAlarmAck a
JOIN dbo.CodicoPallet cp ON cp.id = a.PalletId
WHERE cp.IsManual = 1;
GO
