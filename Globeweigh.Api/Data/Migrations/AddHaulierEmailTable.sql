-- Haulier email addresses (TauriAdmin Hauliers > Edit Haulier > Emails tab, and the Emails
-- panel beside the Hauliers list): one row per address, so a haulier can have several.
-- Supersedes the single Haulier.Email column: its values are moved across here, one row
-- each, and the column is cleared (it stays in the table, but the API no longer reads or
-- writes it). Run once against GbEvergreen. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.HaulierEmail', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HaulierEmail (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_HaulierEmail PRIMARY KEY,
        HaulierId   INT           NOT NULL
            CONSTRAINT FK_HaulierEmail_Haulier REFERENCES dbo.Haulier (id) ON DELETE CASCADE,
        Email       NVARCHAR(200) NOT NULL,
        DateCreated DATETIME      NOT NULL CONSTRAINT DF_HaulierEmail_DateCreated DEFAULT (GETDATE())
    );
    CREATE INDEX IX_HaulierEmail_HaulierId ON dbo.HaulierEmail (HaulierId);
END
GO
-- Move the old single-column values across (skipping any already there), then clear them.
INSERT INTO dbo.HaulierEmail (HaulierId, Email)
SELECT h.id, LTRIM(RTRIM(h.Email))
FROM dbo.Haulier h
WHERE LTRIM(RTRIM(ISNULL(h.Email, ''))) <> ''
  AND NOT EXISTS (
      SELECT 1 FROM dbo.HaulierEmail e
      WHERE e.HaulierId = h.id AND e.Email = LTRIM(RTRIM(h.Email)));

UPDATE dbo.Haulier SET Email = NULL WHERE Email IS NOT NULL;
GO
