-- Sage export jobs get a TARGET: which Sage company the sidecar posts the order into.
--   Test = the test company (the sidecar's TestSageDataPath) - nothing reaches the client's
--          live accounts. The default, and what every existing row becomes.
--   Live = the client's real company.
-- The API stamps it from Sage:ExportTarget when a job is queued, and only hands a job to a
-- sidecar that says it can post to that target (SageController.Claim), so a sidecar from
-- before this change - which knows only the live company - is never given anything.
-- RequestedBy = who pressed Export to Sage (null for the automatic ones).
-- Run once against GbEvergreen. Idempotent.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.SageExportJob', 'Target') IS NULL
    ALTER TABLE dbo.SageExportJob
        ADD Target NVARCHAR(10) NOT NULL CONSTRAINT DF_SageExportJob_Target DEFAULT('Test');
GO

IF COL_LENGTH('dbo.SageExportJob', 'RequestedBy') IS NULL
    ALTER TABLE dbo.SageExportJob ADD RequestedBy NVARCHAR(100) NULL;
GO

-- "At most one live export per order" becomes per TARGET: an order tried out against the
-- test company must still be exportable to the live one later. Recreated only when the
-- existing index does not carry the Target column yet.
IF NOT EXISTS (SELECT 1
                 FROM sys.indexes i
                 JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
                 JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
                WHERE i.name = 'UX_SageExportJob_LiveOrder'
                  AND i.object_id = OBJECT_ID('dbo.SageExportJob')
                  AND c.name = 'Target')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = 'UX_SageExportJob_LiveOrder'
                  AND object_id = OBJECT_ID('dbo.SageExportJob'))
        DROP INDEX UX_SageExportJob_LiveOrder ON dbo.SageExportJob;

    CREATE UNIQUE INDEX UX_SageExportJob_LiveOrder
        ON dbo.SageExportJob (DocType, Target, DispatchOrderId)
        WHERE Status <> 'Failed';
END
GO
