-- Emails sent for an ORDER from the Dispatch page (the load's emails are WeighbridgeRecordEmail):
-- one row per email sent, so the Open Orders grid can show that an order has been emailed, when,
-- and to whom. Kind 'Confirmation' is the order confirmation sent to the customer from Open
-- Orders, with the order confirmation PDF attached. Run once against GbEvergreen. Idempotent.
--
-- See Data/Entities/DispatchOrderEmail.cs and DispatchController (confirmation-email).
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.DispatchOrderEmail', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DispatchOrderEmail (
        id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DispatchOrderEmail PRIMARY KEY,
        DispatchOrderId INT            NOT NULL
            CONSTRAINT FK_DispatchOrderEmail_DispatchOrder REFERENCES dbo.DispatchOrder (id),
        Kind            NVARCHAR(20)   NOT NULL CONSTRAINT DF_DispatchOrderEmail_Kind DEFAULT ('Confirmation'),
        SentTo          NVARCHAR(1000) NOT NULL,
        Subject         NVARCHAR(300)  NOT NULL,
        SentById        INT            NULL,
        DateSent        DATETIME       NOT NULL CONSTRAINT DF_DispatchOrderEmail_DateSent DEFAULT (GETDATE())
    );
    CREATE INDEX IX_DispatchOrderEmail_Order ON dbo.DispatchOrderEmail (DispatchOrderId, Kind, DateSent);
END
GO
