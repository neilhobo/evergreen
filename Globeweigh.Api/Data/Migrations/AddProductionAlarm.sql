-- Production missed-scan alarm. Run once against GbEvergreen (local AND prod).
--
-- Device.ProductionAlarm: this PC shows the app-wide alarm popup (and can silence the
-- alarm). Off by default so kiosk PCs (e.g. lorry-driver weigh-in) never see it.
IF COL_LENGTH('dbo.Device', 'ProductionAlarm') IS NULL
    ALTER TABLE dbo.Device ADD ProductionAlarm bit NOT NULL CONSTRAINT DF_Device_ProductionAlarm DEFAULT(0);
GO

-- One row per missed-scan pallet that has been acknowledged (alarm silenced). A missed
-- pallet with no row here is an ACTIVE alarm; suppressing inserts rows for all currently
-- missed pallets, so a NEW missed pallet re-fires the alarm.
IF OBJECT_ID('dbo.ProductionAlarmAck', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProductionAlarmAck (
        PalletId   int          NOT NULL CONSTRAINT PK_ProductionAlarmAck PRIMARY KEY,
        AckedAtUtc datetime2(0) NOT NULL CONSTRAINT DF_ProductionAlarmAck_AckedAtUtc DEFAULT (SYSUTCDATETIME()),
        AckedBy    nvarchar(100) NULL
    );
END
GO
