-- Annual Leave's staff come from Admin > Staff. A LeaveEmployee row is now one Staff user's leave
-- profile (category, start / leaving dates, approval email, approver) rather than a person in its
-- own right: GlobeweighUserId is required and unique, and the name is read from GlobeweighUser,
-- so Forename / Surname go. Staff who never sign in to anything (drivers, factory) are Staff users
-- of the new type 4 "No login" (eUserType.NoLogin): no username, password or passcode.
--
-- Any row from the first build that is not tied to a Staff user is matched to one by name, or
-- gets a No login Staff user made for it, before the name columns are dropped.
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.LeaveEmployee', 'Forename') IS NOT NULL
BEGIN
    EXEC(N'
        UPDATE e SET GlobeweighUserId = (
            SELECT TOP 1 u.id FROM dbo.GlobeweighUser u
            WHERE u.DateDeleted IS NULL
              AND LTRIM(RTRIM(u.FirstName)) = e.Forename AND LTRIM(RTRIM(u.Surname)) = e.Surname
            ORDER BY u.id)
        FROM dbo.LeaveEmployee e
        WHERE e.GlobeweighUserId IS NULL;

        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        SELECT DISTINCT 4, e.Forename, e.Surname, 0, 0, 0, GETDATE()
        FROM dbo.LeaveEmployee e
        WHERE e.GlobeweighUserId IS NULL;

        UPDATE e SET GlobeweighUserId = (
            SELECT TOP 1 u.id FROM dbo.GlobeweighUser u
            WHERE u.DateDeleted IS NULL
              AND LTRIM(RTRIM(u.FirstName)) = e.Forename AND LTRIM(RTRIM(u.Surname)) = e.Surname
            ORDER BY u.id)
        FROM dbo.LeaveEmployee e
        WHERE e.GlobeweighUserId IS NULL;

        ALTER TABLE dbo.LeaveEmployee DROP COLUMN Forename, Surname;
    ');
END
GO
IF EXISTS (SELECT 1 FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.LeaveEmployee') AND name = 'GlobeweighUserId' AND is_nullable = 1)
    ALTER TABLE dbo.LeaveEmployee ALTER COLUMN GlobeweighUserId INT NOT NULL;
GO
IF OBJECT_ID('dbo.FK_LeaveEmployee_GlobeweighUser', 'F') IS NULL
    ALTER TABLE dbo.LeaveEmployee ADD CONSTRAINT FK_LeaveEmployee_GlobeweighUser
        FOREIGN KEY (GlobeweighUserId) REFERENCES dbo.GlobeweighUser (id);
GO
-- One leave profile per Staff user. Taking someone out of Annual Leave soft-deletes the row and
-- putting them back restores it, history and all, so the key holds across both.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.LeaveEmployee') AND name = 'UX_LeaveEmployee_GlobeweighUser')
    CREATE UNIQUE INDEX UX_LeaveEmployee_GlobeweighUser ON dbo.LeaveEmployee (GlobeweighUserId);
GO
