-- Customer-specific pricing: a price for one product agreed with one customer, set from the
-- Product detail panel (Products > View > Customer Pricing). One row per customer per product;
-- removing a price deletes the row. Every change is kept in CustomerProductPriceHistory, the
-- CostingZonePriceHistory twin. Run once against GbEvergreen. Idempotent.
--
-- See Data/Entities/CustomerProductPrice.cs and CostingController (customer-prices).
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.CustomerProductPrice', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerProductPrice (
        id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerProductPrice PRIMARY KEY,
        CustomerSupplierId INT            NOT NULL
            CONSTRAINT FK_CustomerProductPrice_CustomerSuppliers REFERENCES dbo.CustomerSuppliers (id),
        ProductId          INT            NOT NULL
            CONSTRAINT FK_CustomerProductPrice_Product REFERENCES dbo.Product (id),
        Price              DECIMAL(18,2)  NOT NULL,
        DateCreated        DATETIME       NOT NULL CONSTRAINT DF_CustomerProductPrice_DateCreated DEFAULT (GETDATE()),
        CreatedBy          NVARCHAR(100)  NULL,
        DateModified       DATETIME       NULL,
        ModifiedBy         NVARCHAR(100)  NULL
    );
    CREATE UNIQUE INDEX UX_CustomerProductPrice_Customer_Product
        ON dbo.CustomerProductPrice (CustomerSupplierId, ProductId);
    CREATE INDEX IX_CustomerProductPrice_Product ON dbo.CustomerProductPrice (ProductId);
END
GO
-- No FK to CustomerProductPrice: a removed price keeps its history.
IF OBJECT_ID('dbo.CustomerProductPriceHistory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerProductPriceHistory (
        id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerProductPriceHistory PRIMARY KEY,
        CustomerSupplierId INT            NOT NULL,
        ProductId          INT            NOT NULL,
        -- NULL = the price was added / removed.
        OldPrice           DECIMAL(18,2)  NULL,
        NewPrice           DECIMAL(18,2)  NULL,
        ChangedBy          NVARCHAR(100)  NULL,
        DateCreated        DATETIME       NOT NULL CONSTRAINT DF_CustomerProductPriceHistory_DateCreated DEFAULT (GETDATE())
    );
    CREATE INDEX IX_CustomerProductPriceHistory_Product
        ON dbo.CustomerProductPriceHistory (ProductId, DateCreated);
END
GO
