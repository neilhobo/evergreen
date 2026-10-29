-- The Sage customer mirror behind Admin > Sage Link > Customers: Globeweigh.SageSidecar
-- reads the Sage 50 customer ledger and its address book and PUTs them to the API, which
-- replaces these tables as a whole; the page reads them. SageSyncState carries the
-- freshness and the "refresh requested" flag. See Data/Entities/SageCustomer.cs.
-- Run once against GbEvergreen. Idempotent.

IF OBJECT_ID('dbo.SageCustomer', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SageCustomer (
        id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SageCustomer PRIMARY KEY,
        AccountRef   NVARCHAR(10)  NOT NULL,
        Name         NVARCHAR(60)  NULL,
        Address1     NVARCHAR(60)  NULL,
        Address2     NVARCHAR(60)  NULL,
        Address3     NVARCHAR(60)  NULL,
        Address4     NVARCHAR(60)  NULL,
        Address5     NVARCHAR(60)  NULL,
        Contact      NVARCHAR(30)  NULL,
        Telephone    NVARCHAR(30)  NULL,
        Telephone2   NVARCHAR(30)  NULL,
        Email        NVARCHAR(255) NULL,
        VatNumber    NVARCHAR(30)  NULL,
        DelName      NVARCHAR(60)  NULL,
        DefTaxCode   SMALLINT      NOT NULL CONSTRAINT DF_SageCustomer_DefTaxCode DEFAULT(0),
        Balance      DECIMAL(18,2) NOT NULL CONSTRAINT DF_SageCustomer_Balance DEFAULT(0),
        OnHold       BIT           NOT NULL CONSTRAINT DF_SageCustomer_OnHold DEFAULT(0),
        Inactive     BIT           NOT NULL CONSTRAINT DF_SageCustomer_Inactive DEFAULT(0),
        FirstSeenUtc DATETIME      NOT NULL,
        LastSeenUtc  DATETIME      NOT NULL
    );
    CREATE UNIQUE INDEX UX_SageCustomer_AccountRef ON dbo.SageCustomer (AccountRef);
END
GO

IF OBJECT_ID('dbo.SageCustomerAddress', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SageCustomerAddress (
        id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SageCustomerAddress PRIMARY KEY,
        AccountRef    NVARCHAR(10)  NOT NULL,
        AddressNumber INT           NOT NULL,
        Name          NVARCHAR(60)  NULL,
        Description   NVARCHAR(60)  NULL,
        AddressType   NVARCHAR(30)  NULL,
        Address1      NVARCHAR(60)  NULL,
        Address2      NVARCHAR(60)  NULL,
        Address3      NVARCHAR(60)  NULL,
        Address4      NVARCHAR(60)  NULL,
        Address5      NVARCHAR(60)  NULL,
        Telephone     NVARCHAR(30)  NULL,
        Contact       NVARCHAR(30)  NULL,
        Email         NVARCHAR(255) NULL,
        LastSeenUtc   DATETIME      NOT NULL
    );
    CREATE INDEX IX_SageCustomerAddress_Account ON dbo.SageCustomerAddress (AccountRef, AddressNumber);
END
GO

IF OBJECT_ID('dbo.SageSyncState', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SageSyncState (
        Entity         NVARCHAR(40)   NOT NULL CONSTRAINT PK_SageSyncState PRIMARY KEY,
        LastRunUtc     DATETIME       NULL,
        LastSuccessUtc DATETIME       NULL,
        LastError      NVARCHAR(2000) NULL,
        [RowCount]     INT            NOT NULL CONSTRAINT DF_SageSyncState_RowCount DEFAULT(0),  -- reserved word, hence the brackets
        DetailCount    INT            NOT NULL CONSTRAINT DF_SageSyncState_DetailCount DEFAULT(0),
        DurationMs     INT            NOT NULL CONSTRAINT DF_SageSyncState_DurationMs DEFAULT(0),
        Machine        NVARCHAR(100)  NULL,
        RequestedUtc   DATETIME       NULL,
        RequestedBy    NVARCHAR(100)  NULL
    );
END
GO

-- The page expects the row to exist even before the first sync ("not synced yet").
IF NOT EXISTS (SELECT 1 FROM dbo.SageSyncState WHERE Entity = 'Customers')
    INSERT INTO dbo.SageSyncState (Entity) VALUES ('Customers');
GO
