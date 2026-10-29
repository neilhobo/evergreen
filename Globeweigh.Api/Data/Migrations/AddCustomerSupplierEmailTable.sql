-- Customer/supplier email addresses (TauriAdmin Customers/Suppliers > Edit > Emails tab,
-- and the Emails panel beside the list): one row per address, so a record can have
-- several. Same shape as dbo.HaulierEmail.
--
-- This is the record's own email list. It does NOT replace CustomerSupplier_Address.Email,
-- which stays what it is — the contact address of one delivery address — so that column
-- is read here to seed the list and then left alone.
-- Run once against GbEvergreen. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.CustomerSupplierEmail', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CustomerSupplierEmail (
        id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CustomerSupplierEmail PRIMARY KEY,
        CustomerSupplierId INT           NOT NULL
            CONSTRAINT FK_CustomerSupplierEmail_CustomerSuppliers REFERENCES dbo.CustomerSuppliers (id) ON DELETE CASCADE,
        Email              NVARCHAR(200) NOT NULL,
        DateCreated        DATETIME      NOT NULL CONSTRAINT DF_CustomerSupplierEmail_DateCreated DEFAULT (GETDATE())
    );
    CREATE INDEX IX_CustomerSupplierEmail_CustomerSupplierId
        ON dbo.CustomerSupplierEmail (CustomerSupplierId);
END
GO
-- Seed each record's list from the addresses already captured against it, one row per
-- distinct address, so nothing has to be re-typed. Anything already listed is skipped,
-- and the addresses keep their own Email as before.
INSERT INTO dbo.CustomerSupplierEmail (CustomerSupplierId, Email)
SELECT a.CustomerSupplierId, MIN(LTRIM(RTRIM(a.Email)))
FROM dbo.CustomerSupplier_Address a
WHERE LTRIM(RTRIM(ISNULL(a.Email, ''))) <> ''
  AND CHARINDEX('@', a.Email) > 1
  AND EXISTS (SELECT 1 FROM dbo.CustomerSuppliers c WHERE c.id = a.CustomerSupplierId)
  AND NOT EXISTS (
      SELECT 1 FROM dbo.CustomerSupplierEmail e
      WHERE e.CustomerSupplierId = a.CustomerSupplierId
        AND e.Email = LTRIM(RTRIM(a.Email)))
GROUP BY a.CustomerSupplierId, LOWER(LTRIM(RTRIM(a.Email)));
GO
