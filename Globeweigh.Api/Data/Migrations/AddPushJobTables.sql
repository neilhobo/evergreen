-- Release-push queue for the Android (Picking) pull-update flow and the TauriAdmin
-- Devices page. PushJob = one "push this APK to these devices" action; PushJobDevice =
-- the per-device rows the mobile app polls via /api/devices/register. Run once against
-- GbEvergreen. Idempotent.
IF OBJECT_ID('dbo.PushJob', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PushJob (
        id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PushJob PRIMARY KEY,
        Version         NVARCHAR(50)  NULL,
        PackageFileName NVARCHAR(260) NULL,
        Status          NVARCHAR(20)  NOT NULL CONSTRAINT DF_PushJob_Status DEFAULT('Queued'),
        CreatedUtc      DATETIME      NOT NULL,
        StartedUtc      DATETIME      NULL,
        CompletedUtc    DATETIME      NULL
    );
END
GO

IF OBJECT_ID('dbo.PushJobDevice', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.PushJobDevice (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_PushJobDevice PRIMARY KEY,
        PushJobId   INT           NOT NULL,
        DeviceId    INT           NULL,
        DeviceName  NVARCHAR(200) NULL,
        Status      NVARCHAR(20)  NOT NULL CONSTRAINT DF_PushJobDevice_Status DEFAULT('Pending'),
        Detail      NVARCHAR(400) NULL,
        UpdatedUtc  DATETIME      NOT NULL
    );
    CREATE INDEX IX_PushJobDevice_DeviceId ON dbo.PushJobDevice (DeviceId);
    CREATE INDEX IX_PushJobDevice_PushJobId ON dbo.PushJobDevice (PushJobId);
END
GO
