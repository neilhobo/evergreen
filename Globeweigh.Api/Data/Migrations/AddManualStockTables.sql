-- Manual Stock module (TauriTouch): batches of manually-created pallets.
-- 1) dbo.PalletBatch — one row per "New Batch" (product + pallet count + order number).
-- 2) CodicoPallet.IsManual — distinguishes manually-created pallets from the 3rd-party feed
--    (the batch's pallets are inserted into CodicoPallet in a later phase).
-- Run once against GbEvergreen.
-- QUOTED_IDENTIFIER must be ON to alter CodicoPallet (it has filtered/indexed dependencies);
-- sqlcmd defaults it OFF.
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.PalletBatch', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PalletBatch (
        id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_PalletBatch PRIMARY KEY,
        OrderNumber nvarchar(50) NULL,
        ProductId int NOT NULL,
        PalletCount int NOT NULL,
        CreatedBy nvarchar(100) NULL,
        DateCreated datetime NOT NULL CONSTRAINT DF_PalletBatch_DateCreated DEFAULT (GETDATE())
    );
END
GO
IF COL_LENGTH('dbo.CodicoPallet', 'IsManual') IS NULL
    ALTER TABLE dbo.CodicoPallet ADD IsManual bit NOT NULL CONSTRAINT DF_CodicoPallet_IsManual DEFAULT(0);
GO
-- Which batch a manual pallet belongs to (null for 3rd-party pallets).
IF COL_LENGTH('dbo.CodicoPallet', 'PalletBatchId') IS NULL
    ALTER TABLE dbo.CodicoPallet ADD PalletBatchId int NULL;
GO
