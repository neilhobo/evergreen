-- Sales orders (the Sales role's My Orders tab): an order a salesperson builds under their own
-- login and sends to the office for approval. Kept apart from DispatchOrder on purpose: open
-- orders, loads, picking, planning and stock all read DispatchOrder, so a draft there would
-- leak into every office screen. Approval (the office side, still to come) creates the
-- DispatchOrder and links it back here through DispatchOrderId; from then on the order's
-- progress (load, dispatch) is read from that order.
--
-- Status: 0 Draft, 1 Awaiting approval, 2 Returned by the office, 3 Approved.
-- Run once against GbEvergreen. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.SalesOrder', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesOrder (
        id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrder PRIMARY KEY,
        SalesUserId      INT            NOT NULL,
        Status           INT            NOT NULL CONSTRAINT DF_SalesOrder_Status DEFAULT (0),
        CustomerId       INT            NOT NULL,
        AddressId        INT            NOT NULL CONSTRAINT DF_SalesOrder_AddressId DEFAULT (0),
        DeliveryDate     DATETIME       NULL,
        PoNumber         NVARCHAR(100)  NULL,
        Comments         NVARCHAR(MAX)  NULL,
        PickingComments  NVARCHAR(MAX)  NULL,
        HasPos           BIT            NOT NULL CONSTRAINT DF_SalesOrder_HasPos DEFAULT (0),
        OfficeNote       NVARCHAR(1000) NULL,
        DispatchOrderId  INT            NULL,
        DateCreated      DATETIME       NOT NULL CONSTRAINT DF_SalesOrder_DateCreated DEFAULT (GETDATE()),
        DateSent         DATETIME       NULL,
        DateApproved     DATETIME       NULL,
        DateDeleted      DATETIME       NULL
    );
    CREATE INDEX IX_SalesOrder_SalesUser ON dbo.SalesOrder (SalesUserId, DateDeleted);
END
GO
IF OBJECT_ID('dbo.SalesOrderItem', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesOrderItem (
        id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrderItem PRIMARY KEY,
        SalesOrderId INT            NOT NULL
            CONSTRAINT FK_SalesOrderItem_SalesOrder REFERENCES dbo.SalesOrder (id),
        ProductId    INT            NOT NULL,
        Quantity     INT            NULL,
        Price        DECIMAL(18, 4) NULL,
        SortOrder    INT            NOT NULL CONSTRAINT DF_SalesOrderItem_SortOrder DEFAULT (0)
    );
    CREATE INDEX IX_SalesOrderItem_SalesOrder ON dbo.SalesOrderItem (SalesOrderId);
END
GO
-- The order's history as the salesperson sees it: started, sent, returned (with the office's
-- note), approved. One row per step.
IF OBJECT_ID('dbo.SalesOrderEvent', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SalesOrderEvent (
        id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SalesOrderEvent PRIMARY KEY,
        SalesOrderId INT            NOT NULL
            CONSTRAINT FK_SalesOrderEvent_SalesOrder REFERENCES dbo.SalesOrder (id),
        UserId       INT            NOT NULL,
        Text         NVARCHAR(1000) NOT NULL,
        DateCreated  DATETIME       NOT NULL CONSTRAINT DF_SalesOrderEvent_DateCreated DEFAULT (GETDATE())
    );
    CREATE INDEX IX_SalesOrderEvent_SalesOrder ON dbo.SalesOrderEvent (SalesOrderId, DateCreated);
END
GO
