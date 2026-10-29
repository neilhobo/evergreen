-- Per-customer pallet-label configuration for the TauriAdmin Customers page.
-- One row per customer; currently just an optional logo override for the master
-- label template (no row / NULL LogoImage = default Evergreen logo).
-- Run once against GbEvergreen. Idempotent.
IF OBJECT_ID('dbo.CustomerLabelConfig', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerLabelConfig (
        CustomerSupplierId INT            NOT NULL CONSTRAINT PK_CustomerLabelConfig PRIMARY KEY,
        LogoImage          VARBINARY(MAX) NULL,
        LogoContentType    NVARCHAR(100)  NULL,
        LogoFileName       NVARCHAR(260)  NULL,
        DateLastModified   DATETIME       NOT NULL
    );
END
GO
