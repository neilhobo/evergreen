-- Manual Stock: batches that use the product's Average Weight instead of weighing each pallet,
-- and a printed flag per pallet (the tick on the batch's pallet list).
-- 1) PalletBatch.RequireIndividualWeight — 1 = weigh each pallet on the scale (the original
--    flow, so every existing batch is 1); 0 = all pallets created at once at PalletWeight.
-- 2) PalletBatch.PalletWeight — kg per pallet for an average-weight batch (NULL otherwise).
-- 3) CodicoPallet.DateLabelPrinted — when the pallet's label was last sent to a printer.
--    Manual pallets so far auto-printed on capture, so they are backfilled as printed.
-- Run once against GbEvergreen.
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.PalletBatch', 'RequireIndividualWeight') IS NULL
    ALTER TABLE dbo.PalletBatch ADD RequireIndividualWeight bit NOT NULL
        CONSTRAINT DF_PalletBatch_RequireIndividualWeight DEFAULT(1);
GO
IF COL_LENGTH('dbo.PalletBatch', 'PalletWeight') IS NULL
    ALTER TABLE dbo.PalletBatch ADD PalletWeight decimal(10,2) NULL;
GO
IF COL_LENGTH('dbo.CodicoPallet', 'DateLabelPrinted') IS NULL
BEGIN
    ALTER TABLE dbo.CodicoPallet ADD DateLabelPrinted datetime NULL;
    EXEC('UPDATE dbo.CodicoPallet SET DateLabelPrinted = DateCreated WHERE IsManual = 1');
END
GO
