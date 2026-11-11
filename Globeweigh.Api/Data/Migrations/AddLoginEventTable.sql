-- Login history (Super Admin > Login History): one row per sign-in attempt to TauriAdmin or the
-- mobile app (POST /api/auth/admin-login), success or not, plus each Switch user and Return to
-- my account. Who, when, from which app, and from where: Via 'Tunnel' (evergreen.specdigital.com,
-- IpAddress = Cloudflare's Cf-Connecting-IP, the caller's real address) or 'Site' (the site
-- network, IpAddress = the PC's). Run once against GbEvergreen. Idempotent.
--
-- See Data/Entities/LoginEvent.cs and Services/LoginLog.cs.
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.LoginEvent', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LoginEvent (
        id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LoginEvent PRIMARY KEY,
        DateLogged DATETIME       NOT NULL CONSTRAINT DF_LoginEvent_DateLogged DEFAULT (GETDATE()),
        Kind       NVARCHAR(20)   NOT NULL,   -- SignIn | Failed | SwitchUser | SwitchBack
        UserId     INT            NULL,       -- who it was (null: the username matched nobody)
        Username   NVARCHAR(100)  NULL,       -- as typed (Failed) or the account's
        ByUserId   INT            NULL,       -- the admin doing a Switch user
        Detail     NVARCHAR(200)  NULL,       -- why it failed, or who was switched into
        Client     NVARCHAR(30)   NULL,       -- TauriAdmin | Mobile | Unknown
        Via        NVARCHAR(10)   NULL,       -- Tunnel | Site
        IpAddress  NVARCHAR(64)   NULL,
        UserAgent  NVARCHAR(300)  NULL
    );
    CREATE INDEX IX_LoginEvent_Date ON dbo.LoginEvent (DateLogged DESC);
    CREATE INDEX IX_LoginEvent_User ON dbo.LoginEvent (UserId, DateLogged DESC);
END
GO
