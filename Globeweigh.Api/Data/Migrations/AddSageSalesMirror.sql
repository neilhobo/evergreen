-- The Sage sales mirror behind Reports (Allied Merchant Buying Group report first):
-- Globeweigh.SageSidecar reads every posted sales invoice/credit off the Sage 50 audit trail
-- (SageSalesTransaction - the turnover truth) and every invoice/credit record with its item
-- lines (SageInvoice / SageInvoiceLine - the product detail) for a window of years, and PUTs
-- them to the API, which replaces the window as a whole. SageSyncState gets a 'Sales' row and
-- the window's start date. ReportMemberGroup holds the named account groups a member-sales
-- report adds up; the Allied Merchant members are seeded here.
-- See Data/Entities/SageSalesTransaction.cs, SageInvoice.cs, ReportMemberGroup.cs.
-- Run once against GbEvergreen. Idempotent.

IF OBJECT_ID('dbo.SageSalesTransaction', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SageSalesTransaction (
        id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SageSalesTransaction PRIMARY KEY,
        UniqueRef        INT           NOT NULL,
        AccountRef       NVARCHAR(10)  NOT NULL,
        Type             SMALLINT      NOT NULL,            -- 1 = SI, 2 = SC
        Date             DATETIME      NOT NULL,
        InvRef           NVARCHAR(30)  NULL,
        Details          NVARCHAR(60)  NULL,
        NetAmount        DECIMAL(18,2) NOT NULL CONSTRAINT DF_SageSalesTransaction_Net DEFAULT(0),
        TaxAmount        DECIMAL(18,2) NOT NULL CONSTRAINT DF_SageSalesTransaction_Tax DEFAULT(0),
        ForeignNetAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_SageSalesTransaction_FNet DEFAULT(0),
        Currency         SMALLINT      NOT NULL CONSTRAINT DF_SageSalesTransaction_Currency DEFAULT(0),
        UserName         NVARCHAR(32)  NULL,
        LastSeenUtc      DATETIME      NOT NULL
    );
    CREATE INDEX IX_SageSalesTransaction_Account_Date ON dbo.SageSalesTransaction (AccountRef, Date);
    CREATE INDEX IX_SageSalesTransaction_Date ON dbo.SageSalesTransaction (Date);
END
GO

IF OBJECT_ID('dbo.SageInvoice', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SageInvoice (
        id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SageInvoice PRIMARY KEY,
        Number          INT           NOT NULL,
        Date            DATETIME      NOT NULL,
        TypeCode        SMALLINT      NOT NULL,
        IsCredit        BIT           NOT NULL CONSTRAINT DF_SageInvoice_IsCredit DEFAULT(0),
        AccountRef      NVARCHAR(10)  NOT NULL,
        Name            NVARCHAR(60)  NULL,
        OrderNumber     NVARCHAR(10)  NULL,
        CustOrderNumber NVARCHAR(30)  NULL,
        Currency        SMALLINT      NOT NULL CONSTRAINT DF_SageInvoice_Currency DEFAULT(0),
        NetAmount       DECIMAL(18,2) NOT NULL CONSTRAINT DF_SageInvoice_Net DEFAULT(0),
        TaxAmount       DECIMAL(18,2) NOT NULL CONSTRAINT DF_SageInvoice_Tax DEFAULT(0),
        CarriageNet     DECIMAL(18,2) NOT NULL CONSTRAINT DF_SageInvoice_Carr DEFAULT(0),
        Discount        DECIMAL(18,2) NOT NULL CONSTRAINT DF_SageInvoice_Disc DEFAULT(0),
        Posted          BIT           NOT NULL CONSTRAINT DF_SageInvoice_Posted DEFAULT(0),
        LastSeenUtc     DATETIME      NOT NULL
    );
    CREATE UNIQUE INDEX UX_SageInvoice_Number ON dbo.SageInvoice (Number);
    CREATE INDEX IX_SageInvoice_Account_Date ON dbo.SageInvoice (AccountRef, Date);
END
GO

IF OBJECT_ID('dbo.SageInvoiceLine', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SageInvoiceLine (
        id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SageInvoiceLine PRIMARY KEY,
        InvoiceNumber INT           NOT NULL,
        ItemNumber    SMALLINT      NOT NULL CONSTRAINT DF_SageInvoiceLine_Item DEFAULT(0),
        StockCode     NVARCHAR(30)  NULL,
        Description   NVARCHAR(60)  NULL,
        UnitOfSale    NVARCHAR(10)  NULL,
        NominalCode   NVARCHAR(8)   NULL,
        Qty           DECIMAL(18,4) NOT NULL CONSTRAINT DF_SageInvoiceLine_Qty DEFAULT(0),
        UnitPrice     DECIMAL(18,4) NOT NULL CONSTRAINT DF_SageInvoiceLine_Price DEFAULT(0),
        NetAmount     DECIMAL(18,2) NOT NULL CONSTRAINT DF_SageInvoiceLine_Net DEFAULT(0),
        TaxAmount     DECIMAL(18,2) NOT NULL CONSTRAINT DF_SageInvoiceLine_Tax DEFAULT(0),
        TaxCode       SMALLINT      NOT NULL CONSTRAINT DF_SageInvoiceLine_TaxCode DEFAULT(0),
        IsService     BIT           NOT NULL CONSTRAINT DF_SageInvoiceLine_Service DEFAULT(0)
    );
    CREATE INDEX IX_SageInvoiceLine_Invoice ON dbo.SageInvoiceLine (InvoiceNumber);
    CREATE INDEX IX_SageInvoiceLine_Stock ON dbo.SageInvoiceLine (StockCode);
END
GO

-- The sync row and the window it covers.
IF COL_LENGTH('dbo.SageSyncState', 'WindowFrom') IS NULL
    ALTER TABLE dbo.SageSyncState ADD WindowFrom DATETIME NULL;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.SageSyncState WHERE Entity = 'Sales')
    INSERT INTO dbo.SageSyncState (Entity) VALUES ('Sales');
GO

-- Named account groups for member-sales reports.
IF OBJECT_ID('dbo.ReportMemberGroup', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ReportMemberGroup (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReportMemberGroup PRIMARY KEY,
        ReportKey   NVARCHAR(40)  NOT NULL,
        Name        NVARCHAR(60)  NOT NULL,
        AccountRefs NVARCHAR(400) NOT NULL,
        SortOrder   INT           NOT NULL CONSTRAINT DF_ReportMemberGroup_Sort DEFAULT(0)
    );
    CREATE INDEX IX_ReportMemberGroup_Report ON dbo.ReportMemberGroup (ReportKey, SortOrder);
END
GO

-- The Allied Merchant Buying Group members, as requested by Evergreen on 2026-10-08. The
-- Drinagh accounts are ZDRIN01-06 in Sage (the request said ZDRI01-06).
IF NOT EXISTS (SELECT 1 FROM dbo.ReportMemberGroup WHERE ReportKey = 'AlliedMerchant')
BEGIN
    INSERT INTO dbo.ReportMemberGroup (ReportKey, Name, AccountRefs, SortOrder) VALUES
        ('AlliedMerchant', 'Dairygold',     'ZDAIR01', 1),
        ('AlliedMerchant', 'Joyce & Sons',  'ZJOYC01', 2),
        ('AlliedMerchant', 'MD O''Shea',    'ZMDOS01', 3),
        ('AlliedMerchant', 'Ganly''s',      'ZGAN01',  4),
        ('AlliedMerchant', 'Stakelums',     'ZSTAK01', 5),
        ('AlliedMerchant', 'Drinagh Co-Op', 'ZDRIN01,ZDRIN02,ZDRIN03,ZDRIN04,ZDRIN05,ZDRIN06', 6);
END
GO
