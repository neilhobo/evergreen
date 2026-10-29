-- Staged features: work that is finished and deployed but NOT switched on yet.
--
-- A table rather than a column per feature on dbo.Setting, because the point of it is that the
-- next staged feature costs nothing: the flag is a row, created the first time somebody switches
-- it, and a name nobody has ever switched is simply OFF. So a feature can be built, deployed and
-- left dark without a schema change, and switched on from Super Admin > Staging when the client
-- says so.
--
-- Nothing is seeded. A missing row means off, which is the safe answer for a feature that has
-- never been turned on, and it means this script does not have to know what will be staged next.
--
-- Applied automatically by Globeweigh.Api\deploy\migrate-db.ps1 (npm run migrate:local / :prod).

IF OBJECT_ID('dbo.FeatureFlag', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.FeatureFlag (
        id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_FeatureFlag PRIMARY KEY,
        -- The name the code asks for (TauriAdmin src/api/features.ts FEATURE). Stored as given,
        -- matched case-insensitively by the collation.
        Name         nvarchar(100) NOT NULL CONSTRAINT UQ_FeatureFlag_Name UNIQUE,
        Enabled      bit           NOT NULL CONSTRAINT DF_FeatureFlag_Enabled DEFAULT (0),
        -- Who last flipped it and when. A staged feature going live is exactly the sort of change
        -- somebody asks about afterwards.
        DateChanged  datetime      NULL,
        ChangedById  int           NULL
    );
END
GO
