-- Label reprint audit. Run once against GbEvergreen (local AND prod).
--
-- One row per label the TauriTouch app reprints — from the Production screen's "Reprint
-- Label" (missed scan / no label / damaged label) or from Manual Stock's. The print itself
-- is entirely client-side (the label is rendered to a canvas and spooled to the printer
-- attached to that PC), so without this table a reprinted pallet is indistinguishable from
-- one that only ever printed once.
--
-- NOTE this is NOT dbo.PrintJob. That table is a QUEUE of documents for the server to print
-- (delivery notes, picked up by GlobeweighService's PrintWorker); this one is a log of
-- prints that have already happened on a factory PC.
IF OBJECT_ID('dbo.LabelReprint', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LabelReprint (
        Id           int           IDENTITY(1,1) NOT NULL CONSTRAINT PK_LabelReprint PRIMARY KEY,
        PalletId     int           NOT NULL,          -- CodicoPallet.Id (no FK: the log outlives the pallet)
        UniqueCode   nvarchar(50)  NULL,              -- snapshot, so a deleted pallet is still identifiable
        Source       nvarchar(20)  NOT NULL,          -- 'Production' | 'ManualStock' (which screen reprinted)
        DeviceName   nvarchar(100) NULL,              -- the PC that printed it
        PrinterName  nvarchar(200) NULL,
        UserName     nvarchar(100) NULL,              -- NULL on a device with Login disabled
        PrintedAtUtc datetime2(0)  NOT NULL CONSTRAINT DF_LabelReprint_PrintedAtUtc DEFAULT (SYSUTCDATETIME())
    );

    -- "How many times has this pallet been reprinted, and when?"
    CREATE INDEX IX_LabelReprint_PalletId ON dbo.LabelReprint (PalletId) INCLUDE (PrintedAtUtc);
    -- "What was reprinted today / this week?"
    CREATE INDEX IX_LabelReprint_PrintedAtUtc ON dbo.LabelReprint (PrintedAtUtc);
END
GO
