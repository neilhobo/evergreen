-- Sage Link > Comparison: Sage accounts the office has said will never be copied into
-- Evergreen ("Ignore" on Missing in Evergreen). An ignored account leaves that list and
-- "Copy to Evergreen", and is shown under the Ignored category instead, where "Stop ignoring"
-- deletes its row. Read and written by SageComparison / SageCustomersController.
-- Run once against GbEvergreen. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.SageIgnoredAccount', 'U') IS NULL
    CREATE TABLE dbo.SageIgnoredAccount (
        AccountRef   NVARCHAR(20) NOT NULL CONSTRAINT PK_SageIgnoredAccount PRIMARY KEY,
        IgnoredById  INT          NOT NULL,
        DateIgnored  DATETIME     NOT NULL
    );
GO
