-- Stock Takes (TauriAdmin Stock module). Stock is counted per product, never per
-- individual pallet — a stock take is a list of (product, pallet count) entries.
--
-- Reuses the legacy dbo.StockTake header table (empty / never used at Evergreen; its
-- DateCompleted / DateMadeCurrentStock / DateDeleted columns fit the planned flow),
-- adding CreatedById. The legacy pallet-level dbo.StockTakePallet is left alone, but
-- it still owns the constraint name PK_StockTakeItem (an old sp_rename kept the
-- original name), so that PK is renamed to match its table before the new
-- dbo.StockTakeItem — one counted product per row — can claim the proper name.
-- Run once against GbEvergreen.
IF COL_LENGTH('dbo.StockTake', 'CreatedById') IS NULL
    ALTER TABLE dbo.StockTake ADD CreatedById int NULL;
GO
-- Free up the PK_StockTakeItem name (held by StockTakePallet since the old rename).
IF EXISTS (SELECT 1 FROM sys.key_constraints
           WHERE name = 'PK_StockTakeItem'
             AND parent_object_id = OBJECT_ID('dbo.StockTakePallet'))
    EXEC sp_rename 'dbo.PK_StockTakeItem', 'PK_StockTakePallet', 'OBJECT';
GO
IF OBJECT_ID('dbo.StockTakeItem', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StockTakeItem (
        id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_StockTakeItem PRIMARY KEY,
        StockTakeId int NOT NULL CONSTRAINT FK_StockTakeItem_StockTake
            REFERENCES dbo.StockTake(id) ON DELETE CASCADE,
        ProductId int NOT NULL,
        PalletCount int NOT NULL,
        DateCreated datetime NOT NULL CONSTRAINT DF_StockTakeItem_DateCreated DEFAULT (GETDATE())
    );
    CREATE INDEX IX_StockTakeItem_StockTakeId ON dbo.StockTakeItem (StockTakeId);
END
GO
