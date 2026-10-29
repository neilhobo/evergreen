-- Server-side print queue. The API enqueues (PickingController.CompleteLoad); the
-- PrintWorker in GlobeweighService renders each row with headless Chrome and sends the
-- PDF to the Weighbridge Printer. Also adds the two Setting columns that let the copy
-- count and the printer name be changed without a rebuild.
-- Run once against GbEvergreen. Idempotent.

IF OBJECT_ID('dbo.PrintJob', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PrintJob (
        id                  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PrintJob PRIMARY KEY,
        DocType             NVARCHAR(40)  NOT NULL CONSTRAINT DF_PrintJob_DocType DEFAULT('DeliveryNote'),
        DispatchOrderId     INT           NOT NULL,
        WeighbridgeRecordId INT           NOT NULL,
        Copies              INT           NOT NULL CONSTRAINT DF_PrintJob_Copies DEFAULT(2),
        PrinterName         NVARCHAR(200) NULL,
        Status              NVARCHAR(20)  NOT NULL CONSTRAINT DF_PrintJob_Status DEFAULT('Queued'),
        Attempts            INT           NOT NULL CONSTRAINT DF_PrintJob_Attempts DEFAULT(0),
        LastError           NVARCHAR(1000) NULL,
        Reason              NVARCHAR(40)  NOT NULL CONSTRAINT DF_PrintJob_Reason DEFAULT('LoadComplete'),
        CreatedUtc          DATETIME      NOT NULL,
        StartedUtc          DATETIME      NULL,
        CompletedUtc        DATETIME      NULL
    );

    -- The worker's claim query: oldest queued job first.
    CREATE INDEX IX_PrintJob_Status ON dbo.PrintJob (Status, id);

    -- Idempotency: at most ONE automatic delivery note per order, so re-completing a load
    -- (or a retried POST) can never reprint it. Deliberately a filtered UNIQUE index rather
    -- than app-side checking — the guarantee belongs next to the data. Manual reprints use
    -- Reason='Reprint' and are not covered by it.
    CREATE UNIQUE INDEX UX_PrintJob_AutoPerOrder
        ON dbo.PrintJob (DocType, DispatchOrderId)
        WHERE Reason = 'LoadComplete';
END
GO

-- How many copies of each delivery note to print, and where. Defaults match what the
-- client asked for: 2 copies to the Ricoh on the yard-office network.
IF COL_LENGTH('dbo.Setting', 'DeliveryNoteCopies') IS NULL
    ALTER TABLE dbo.Setting ADD DeliveryNoteCopies INT NOT NULL
        CONSTRAINT DF_Setting_DeliveryNoteCopies DEFAULT(2);
GO

IF COL_LENGTH('dbo.Setting', 'DeliveryNotePrinter') IS NULL
    ALTER TABLE dbo.Setting ADD DeliveryNotePrinter NVARCHAR(200) NULL
        CONSTRAINT DF_Setting_DeliveryNotePrinter DEFAULT('Weighbridge Printer');
GO

-- The DEFAULT only applies to new rows; the existing settings row needs filling in.
UPDATE dbo.Setting SET DeliveryNotePrinter = 'Weighbridge Printer' WHERE DeliveryNotePrinter IS NULL;
GO
