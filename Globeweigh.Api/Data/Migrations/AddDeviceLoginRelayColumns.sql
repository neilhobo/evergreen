-- Adds two Factory-mode flags to dbo.Device. Run once against GbEvergreen.
--   LoginEnabled: show the sign-in screen on startup (default ON = existing behaviour);
--                 off means the app goes straight to the Home menu with no login.
--   RelayCard:    a relay card is attached to this PC (Hardware section, like Weighscale/Printer).
IF COL_LENGTH('dbo.Device', 'LoginEnabled') IS NULL
    ALTER TABLE dbo.Device ADD LoginEnabled bit NOT NULL CONSTRAINT DF_Device_LoginEnabled DEFAULT(1);
GO
IF COL_LENGTH('dbo.Device', 'RelayCard') IS NULL
    ALTER TABLE dbo.Device ADD RelayCard bit NOT NULL CONSTRAINT DF_Device_RelayCard DEFAULT(0);
GO
