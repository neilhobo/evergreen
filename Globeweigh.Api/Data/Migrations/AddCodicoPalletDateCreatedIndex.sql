-- Production pallets grid: the API now queries the CodicoPallet table directly (the old
-- vwCodicoPallet path recomputed a whole-table window function on every 10s poll, ~5s at
-- 100k rows). This index covers the date-windowed fetch so it stays flat as the table grows.
--
-- Run once against GbEvergreen. Idempotent. Additive only — the legacy WPF app is unaffected.

-- Required: the index INCLUDEs the computed Code13 column, and sqlcmd runs with
-- QUOTED_IDENTIFIER off by default (same trap as AddProductIsBulkBagColumn.sql).
SET QUOTED_IDENTIFIER ON;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
    WHERE object_id = OBJECT_ID('dbo.CodicoPallet') AND name = 'IX_CodicoPallet_DateCreated'
)
BEGIN
    CREATE NONCLUSTERED INDEX IX_CodicoPallet_DateCreated
        ON dbo.CodicoPallet (DateCreated)
        INCLUDE (UniqueCode, Code, Weight, IsScanned, DateScanned, Code13);
END
GO
