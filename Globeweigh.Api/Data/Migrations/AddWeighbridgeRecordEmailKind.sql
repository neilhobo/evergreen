-- Which email a WeighbridgeRecordEmail row records, now that a load is emailed twice from the
-- Dispatch page: 'Ready' (the "ready for collection" message sent from the Loads grid) or
-- 'Dispatched' (the dispatch confirmation sent from the Completed Loads grid once the load has
-- weighed out). Every row before this column existed was a 'Ready' email, which the default
-- gives it. Run once against GbEvergreen. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.WeighbridgeRecordEmail', 'Kind') IS NULL
    ALTER TABLE dbo.WeighbridgeRecordEmail
        ADD Kind NVARCHAR(20) NOT NULL CONSTRAINT DF_WeighbridgeRecordEmail_Kind DEFAULT ('Ready');
GO
