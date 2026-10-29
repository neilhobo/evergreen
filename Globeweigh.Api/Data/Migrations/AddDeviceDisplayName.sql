-- Adds Device.DisplayName ("PC Name" in Screen Settings): a friendly name shown in big
-- writing above the Home menu buttons. Null/blank = show nothing. Run once against
-- GbEvergreen (local AND prod).
IF COL_LENGTH('dbo.Device', 'DisplayName') IS NULL
    ALTER TABLE dbo.Device ADD DisplayName nvarchar(100) NULL;
GO
