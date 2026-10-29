-- Emails sent for a load from the Dispatch page's Send Email dialog (the "ready for collection"
-- message to the haulier): one row per email sent, so the Loads grid can show that a load has
-- been emailed, when, and to whom. Run once against GbEvergreen. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.WeighbridgeRecordEmail', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.WeighbridgeRecordEmail (
        id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_WeighbridgeRecordEmail PRIMARY KEY,
        WeighbridgeRecordId INT            NOT NULL
            CONSTRAINT FK_WeighbridgeRecordEmail_WeighbridgeRecord REFERENCES dbo.WeighbridgeRecord (id),
        SentTo              NVARCHAR(1000) NOT NULL,
        Subject             NVARCHAR(300)  NOT NULL,
        SentById            INT            NULL,
        DateSent            DATETIME       NOT NULL CONSTRAINT DF_WeighbridgeRecordEmail_DateSent DEFAULT (GETDATE())
    );
    CREATE INDEX IX_WeighbridgeRecordEmail_Record ON dbo.WeighbridgeRecordEmail (WeighbridgeRecordId, DateSent);
END
GO
