-- Staff tags, and the first feature one switches on: POS boards made up.
--
-- A TAG is not a role. It is a label put on a Staff user (Admin > Staff > Edit > Tags) that
-- switches on one extra feature for that person. The tags themselves are a fixed list in code
-- (Data/Entities/UserTagEntity.cs StaffTags); this table only holds who has which.
--
-- "PosCreator": in the mobile app the person gets POS Boards, the open orders that carry a POS
-- board (DispatchOrder.HasPos). They open an order, tick its products and press POS made up:
-- one row here per order + product, saying a POS board is made up for that product. Once every
-- product on the order has a row, the order leaves their list. Keyed by product, not by
-- DispatchOrderItem id, because saving an order in the office rewrites its item rows.
--
-- Run once against GbEvergreen. Idempotent. Additive only.
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.GlobeweighUserTag', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.GlobeweighUserTag (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_GlobeweighUserTag PRIMARY KEY,
        UserId      INT           NOT NULL,
        Tag         NVARCHAR(50)  NOT NULL,   -- a StaffTags key, e.g. PosCreator
        DateCreated DATETIME      NOT NULL CONSTRAINT DF_GlobeweighUserTag_DateCreated DEFAULT (GETDATE()),
        CONSTRAINT UQ_GlobeweighUserTag UNIQUE (UserId, Tag)
    );
END
GO
IF OBJECT_ID('dbo.DispatchOrderPosMade', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DispatchOrderPosMade (
        id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DispatchOrderPosMade PRIMARY KEY,
        OrderId   INT       NOT NULL,   -- DispatchOrder.id
        ProductId INT       NOT NULL,
        MadeById  INT       NOT NULL,   -- GlobeweighUser.id
        DateMade  DATETIME  NOT NULL CONSTRAINT DF_DispatchOrderPosMade_DateMade DEFAULT (GETDATE()),
        CONSTRAINT UQ_DispatchOrderPosMade UNIQUE (OrderId, ProductId)
    );
END
GO
