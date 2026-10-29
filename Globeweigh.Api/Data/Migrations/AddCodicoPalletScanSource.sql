-- How a pallet came to be marked scanned. Needed so the missed-scan alarm can tell a real
-- conveyor-scanner read from an operator clearing the row by hand: only a scanner read is
-- evidence that the scanner moved past an earlier pallet, and only a scanner read proves the
-- scanner can still see. See ProductionController.UnackedMissedTodayAsync.
--
--   NULL       the conveyor scanner (GlobeweighService BarcodeWorker, which does not write
--              this column) — and every row that predates the column
--   'Scanner'  the conveyor scanner, written explicitly
--   'Reprint'  marked scanned because its label was reprinted on a Touch PC
--   'Manual'   marked scanned by hand ("Mark as Passed" on the Production screen)
--
-- Run once against GbEvergreen (local AND prod).
-- QUOTED_IDENTIFIER must be ON to alter CodicoPallet (it has filtered/indexed
-- dependencies); sqlcmd defaults it OFF.
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.CodicoPallet', 'ScanSource') IS NULL
    ALTER TABLE dbo.CodicoPallet ADD ScanSource nvarchar(20) NULL;
GO
