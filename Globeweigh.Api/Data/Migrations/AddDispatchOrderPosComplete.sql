-- POS Boards (mobile, the "POS creator" tag): an order finished by hand. Not every product on a
-- POS order gets a board, so the order screen has a Complete button: it takes the order off the
-- POS Boards list with whatever boards are ticked (DispatchOrderPosMade). One row per order.
-- An order with every product ticked still leaves the list on its own, without a row here.
-- See Migrations/AddStaffTagsAndPosMade.sql and PosBoardsController.
--
-- Run once against GbEvergreen. Idempotent. Additive only.
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.DispatchOrderPosComplete', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DispatchOrderPosComplete (
        id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DispatchOrderPosComplete PRIMARY KEY,
        OrderId       INT       NOT NULL CONSTRAINT UQ_DispatchOrderPosComplete UNIQUE,   -- DispatchOrder.id
        CompletedById INT       NOT NULL,   -- GlobeweighUser.id
        DateCompleted DATETIME  NOT NULL CONSTRAINT DF_DispatchOrderPosComplete_Date DEFAULT (GETDATE())
    );
END
GO
