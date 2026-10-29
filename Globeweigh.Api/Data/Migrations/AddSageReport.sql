-- Saved Sage financial reports: Reports > Sage Financial Reports lists these, and each one is a
-- member-sales report over its own account groups (ReportMemberGroup.ReportKey = SageReport.ReportKey).
-- Until now the one report, the Allied Merchant Buying Group, was named in code; it becomes
-- the first row here with its existing key, so its members carry straight over.
-- See Data/Entities/SageReport.cs. Run once against GbEvergreen. Idempotent.

IF OBJECT_ID('dbo.SageReport', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SageReport (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SageReport PRIMARY KEY,
        ReportKey   NVARCHAR(40)  NOT NULL,
        Name        NVARCHAR(80)  NOT NULL,
        SortOrder   INT           NOT NULL CONSTRAINT DF_SageReport_Sort DEFAULT(0),
        CreatedUtc  DATETIME      NOT NULL CONSTRAINT DF_SageReport_Created DEFAULT(GETUTCDATE()),
        CreatedBy   NVARCHAR(100) NULL
    );
    CREATE UNIQUE INDEX UX_SageReport_Key ON dbo.SageReport (ReportKey);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SageReport WHERE ReportKey = 'AlliedMerchant')
    INSERT INTO dbo.SageReport (ReportKey, Name, SortOrder) VALUES ('AlliedMerchant', 'Allied Merchant Buying Group', 1);
GO
