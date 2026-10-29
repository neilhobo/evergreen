-- Load photos (Picking app "Complete Load"): one metadata row per photo taken
-- against a weighbridge load (Trailer Number / Side of Trailer / Other Side of Trailer).
-- The image bytes live on the server file system (Picking:PhotosPath, e.g.
-- C:\Globeweigh\LoadPhotos\{WeighbridgeRecordId}\{FileName}) so they don't count
-- against the SQL Express database limit. Run once against GbEvergreen.
IF OBJECT_ID('dbo.LoadPhoto', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LoadPhoto (
        id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoadPhoto PRIMARY KEY,
        WeighbridgeRecordId int NOT NULL,
        Description nvarchar(100) NOT NULL,
        FileName nvarchar(260) NOT NULL,       -- stored file, e.g. {guid}.jpg
        ContentType nvarchar(100) NULL,        -- e.g. image/jpeg
        DateCreated datetime NOT NULL CONSTRAINT DF_LoadPhoto_DateCreated DEFAULT (GETDATE())
    );
    CREATE INDEX IX_LoadPhoto_WeighbridgeRecordId ON dbo.LoadPhoto(WeighbridgeRecordId);
END
GO
