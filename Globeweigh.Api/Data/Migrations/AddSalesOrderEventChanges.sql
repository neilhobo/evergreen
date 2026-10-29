-- My Orders history: what an edit changed. Each "Changed the order" event keeps the list of
-- changes it made (customer, delivery, PO, comments, lines added/removed, quantities, prices)
-- as a JSON array of plain sentences, which the page shows when the entry is expanded.
-- NULL for every other kind of event. Run once against GbEvergreen. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.SalesOrderEvent', 'Changes') IS NULL
    ALTER TABLE dbo.SalesOrderEvent ADD Changes NVARCHAR(MAX) NULL;
GO
