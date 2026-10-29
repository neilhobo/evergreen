-- Outbox for the sales orders exported to Sage 50 Accounts. The API enqueues one row per
-- order when a load is completed (PickingController.CompleteLoad, DispatchController.
-- WeighOut) or when an admin asks for one (SageController.ExportOrder); Globeweigh.
-- SageSidecar, on the machine with Sage installed, claims rows over the API, posts them
-- with Sage's SDO and writes the Sage order number back. See Data/Entities/SageExportJob.cs.
-- Run once against GbEvergreen. Idempotent.

-- The filtered unique index below will not create under sqlcmd's default settings
-- (QUOTED_IDENTIFIER OFF -> error 1934), and the deploy runner (migrate-run.ps1) does
-- not pass -I. Set them here so the script does not depend on how it is invoked.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF OBJECT_ID('dbo.SageExportJob', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SageExportJob (
        id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SageExportJob PRIMARY KEY,
        DocType         NVARCHAR(40)   NOT NULL CONSTRAINT DF_SageExportJob_DocType DEFAULT('SalesOrder'),
        DispatchOrderId INT            NOT NULL,
        OrderNo         INT            NOT NULL,
        Payload         NVARCHAR(MAX)  NOT NULL,
        Status          NVARCHAR(20)   NOT NULL CONSTRAINT DF_SageExportJob_Status DEFAULT('Queued'),
        Attempts        INT            NOT NULL CONSTRAINT DF_SageExportJob_Attempts DEFAULT(0),
        LastError       NVARCHAR(2000) NULL,
        Reason          NVARCHAR(40)   NOT NULL CONSTRAINT DF_SageExportJob_Reason DEFAULT('Dispatch'),
        SageOrderNo     INT            NULL,
        ClaimedBy       NVARCHAR(100)  NULL,
        CreatedUtc      DATETIME       NOT NULL,
        StartedUtc      DATETIME       NULL,
        CompletedUtc    DATETIME       NULL
    );

    -- The sidecar's claim query: oldest queued job first.
    CREATE INDEX IX_SageExportJob_Status ON dbo.SageExportJob (Status, id);

    -- The ops view and the per-order lookups.
    CREATE INDEX IX_SageExportJob_Order ON dbo.SageExportJob (DispatchOrderId, id);
END
GO

-- Idempotency: an order can be on its way to Sage, or in Sage, at most once. A filtered
-- UNIQUE index over every row that is not Failed, so a re-completed load or a repeated
-- POST cannot queue a duplicate, while a Failed job can still be superseded by a fresh
-- export once whatever broke it is fixed. In the index rather than app code for the same
-- reason as PrintJob: the guarantee belongs next to the data.
-- Its own block, so a database where the table exists but this index is missing (the
-- QUOTED_IDENTIFIER failure above, on a run before the SET was added) gets it on re-run.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
                WHERE name = 'UX_SageExportJob_LiveOrder'
                  AND object_id = OBJECT_ID('dbo.SageExportJob'))
    CREATE UNIQUE INDEX UX_SageExportJob_LiveOrder
        ON dbo.SageExportJob (DocType, DispatchOrderId)
        WHERE Status <> 'Failed';
GO
