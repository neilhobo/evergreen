-- @manual
-- Loads the client's 2026 "Annual Leave Request Log" spreadsheet into Annual Leave: every
-- employee on it with their category, and every floating day it logs as an approved request
-- (by the approver it names). Run once, deliberately, after AddAnnualLeaveTables.sql:
--   powershell -File Globeweigh.Api\deploy\migrate-db.ps1 -Target local -IncludeManual -Only SeedAnnualLeave2026Staff.sql
-- Does nothing if any employee already exists. Run after LinkLeaveEmployeeToStaff.sql.
--
-- Everyone is an Admin > Staff user (Annual Leave has no staff of its own): each name is matched
-- to an existing Staff user by first name + surname (case-insensitive), and only someone with no
-- Staff user gets one made, of type 4 "No login" (no username, password or passcode).
-- Mairead McLernon on the spreadsheet is matched to Staff "Mairead McCann" if there is no
-- McLernon - confirm they are the same person.
--
-- Corrections made on the way in (the spreadsheet is typed by hand):
--   Josh Hanlon: "20.07.2027" read as 2026-07-20
--   Wilbert Lennox: 2026-01-10 is a Saturday - kept as logged
--   Gerard McCann: 2026-06-28 is a Sunday - kept as logged
--   Paul McDonagh: "26..08.2026" read as 2026-08-26
--   Martin McGlone: "13.07,2026" read as 2026-07-13
--   Russ Tame: "08.06.2025" read as 2026-06-08
--   Russ Tame: "09.06.2025" read as 2026-06-09
--   Russ Tame: "10.06.2025" read as 2026-06-10
--   Russ Tame: "11.06.2025" read as 2026-06-11
--   Russ Tame: "12.06.2025" read as 2026-06-12
-- Start dates for the three 2026 starters are NOT in the spreadsheet; these are worked back from
-- the pro-rata totals it shows (17.5, 11.5, 7.5) and their set days (13, 12, 11). Confirm them
-- with the client: Lee Davidson 18 May, Niall Conlon 5 Aug, Adam Cushnahan 26 Sep.
-- Emmett Brady, Ronan Fitzgerald, Gerard Logue are on the spreadsheet's name list but have no row in
-- the log: added with no leave and a note to check whether they still work there.
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.LeaveEmployee)
BEGIN
    DECLARE @emp INT, @user INT;

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Rod' AND LTRIM(RTRIM(Surname)) IN (N'Bird') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Rod', N'Bird', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'UK Sales';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-06-08', '2026-06-08', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-08'),
        (@emp, '2026-06-09', '2026-06-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-09'),
        (@emp, '2026-06-10', '2026-06-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-10'),
        (@emp, '2026-06-11', '2026-06-11', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-11'),
        (@emp, '2026-06-12', '2026-06-12', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-12');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Emmett' AND LTRIM(RTRIM(Surname)) IN (N'Brady') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Emmett', N'Brady', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, N'On the 2026 spreadsheet name list but not in the log - check they still work here.'
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Niall' AND LTRIM(RTRIM(Surname)) IN (N'Conlon') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Niall', N'Conlon', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, '2026-08-05', N'Start date worked back from the 2026 spreadsheet - confirm.'
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-12-03', '2026-12-03', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-12-03'),
        (@emp, '2026-12-04', '2026-12-04', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-12-04'),
        (@emp, '2026-10-16', '2026-10-16', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-16');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Adam' AND LTRIM(RTRIM(Surname)) IN (N'Cushnahan') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Adam', N'Cushnahan', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, '2026-09-26', N'Start date worked back from the 2026 spreadsheet - confirm.'
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-11-27', '2026-11-27', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-11-27');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Lee' AND LTRIM(RTRIM(Surname)) IN (N'Davidson') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Lee', N'Davidson', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, '2026-05-18', N'Start date worked back from the 2026 spreadsheet - confirm.'
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-07-10', '2026-07-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-10'),
        (@emp, '2026-07-13', '2026-07-13', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-13'),
        (@emp, '2026-07-14', '2026-07-14', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-14'),
        (@emp, '2026-07-20', '2026-07-20', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-20'),
        (@emp, '2026-10-09', '2026-10-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-09'),
        (@emp, '2026-10-12', '2026-10-12', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-12');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Martin' AND LTRIM(RTRIM(Surname)) IN (N'Donnelly') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Martin', N'Donnelly', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-01-09', '2026-01-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-09'),
        (@emp, '2026-02-02', '2026-02-02', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-02'),
        (@emp, '2026-03-17', '2026-03-17', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-03-17'),
        (@emp, '2026-05-21', '2026-05-21', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-05-21'),
        (@emp, '2026-05-22', '2026-05-22', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-05-22'),
        (@emp, '2026-06-15', '2026-06-15', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-15'),
        (@emp, '2026-07-13', '2026-07-13', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-13'),
        (@emp, '2026-08-24', '2026-08-24', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-24'),
        (@emp, '2026-08-25', '2026-08-25', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-25');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Ronan' AND LTRIM(RTRIM(Surname)) IN (N'Fitzgerald') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Ronan', N'Fitzgerald', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, N'On the 2026 spreadsheet name list but not in the log - check they still work here.'
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Josh' AND LTRIM(RTRIM(Surname)) IN (N'Hanlon') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Josh', N'Hanlon', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-04-07', '2026-04-07', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-04-07'),
        (@emp, '2026-04-08', '2026-04-08', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-04-08'),
        (@emp, '2026-04-09', '2026-04-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-04-09'),
        (@emp, '2026-04-10', '2026-04-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-04-10'),
        (@emp, '2026-03-09', '2026-03-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-03-09'),
        (@emp, '2026-03-10', '2026-03-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-03-10'),
        (@emp, '2026-07-16', '2026-07-16', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-16'),
        (@emp, '2026-07-17', '2026-07-17', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-17'),
        (@emp, '2026-07-20', '2026-07-20', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-20'),
        (@emp, '2026-07-21', '2026-07-21', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-21'),
        (@emp, '2026-07-22', '2026-07-22', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-22'),
        (@emp, '2026-07-23', '2026-07-23', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-23'),
        (@emp, '2026-08-26', '2026-08-26', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-26'),
        (@emp, '2026-07-06', '2026-07-06', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-06');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Wilbert' AND LTRIM(RTRIM(Surname)) IN (N'Lennox') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Wilbert', N'Lennox', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-01-05', '2026-01-05', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-05'),
        (@emp, '2026-01-06', '2026-01-06', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-06'),
        (@emp, '2026-01-07', '2026-01-07', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-07'),
        (@emp, '2026-01-08', '2026-01-08', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-08'),
        (@emp, '2026-01-09', '2026-01-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-09'),
        (@emp, '2026-01-10', '2026-01-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-10'),
        (@emp, '2026-01-12', '2026-01-12', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-12'),
        (@emp, '2026-02-17', '2026-02-17', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-17'),
        (@emp, '2026-02-18', '2026-02-18', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-18'),
        (@emp, '2026-02-19', '2026-02-19', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-19'),
        (@emp, '2026-02-20', '2026-02-20', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-20'),
        (@emp, '2026-10-06', '2026-10-06', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-06'),
        (@emp, '2026-10-07', '2026-10-07', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-07'),
        (@emp, '2026-10-08', '2026-10-08', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-08'),
        (@emp, '2026-10-09', '2026-10-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-09');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Gerard' AND LTRIM(RTRIM(Surname)) IN (N'Logue') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Gerard', N'Logue', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, N'On the 2026 spreadsheet name list but not in the log - check they still work here.'
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Aidan' AND LTRIM(RTRIM(Surname)) IN (N'McCann') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Aidan', N'McCann', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-06-26', '2026-06-26', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-26'),
        (@emp, '2026-06-29', '2026-06-29', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-29'),
        (@emp, '2026-06-30', '2026-06-30', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-30'),
        (@emp, '2026-07-01', '2026-07-01', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-01'),
        (@emp, '2026-07-02', '2026-07-02', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-02'),
        (@emp, '2026-07-03', '2026-07-03', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-03'),
        (@emp, '2026-07-06', '2026-07-06', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-06'),
        (@emp, '2026-07-07', '2026-07-07', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-07'),
        (@emp, '2026-07-08', '2026-07-08', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-08'),
        (@emp, '2026-07-09', '2026-07-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-09'),
        (@emp, '2026-07-10', '2026-07-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-10'),
        (@emp, '2026-07-13', '2026-07-13', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-13');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Gerard' AND LTRIM(RTRIM(Surname)) IN (N'McCann') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Gerard', N'McCann', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-03-17', '2026-03-17', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-03-17'),
        (@emp, '2026-03-31', '2026-03-31', 0.5, 0.5, 1, N'From the 2026 spreadsheet', N'Collette', '2026-03-31'),
        (@emp, '2026-04-15', '2026-04-15', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-04-15'),
        (@emp, '2026-07-16', '2026-07-16', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-16'),
        (@emp, '2026-08-03', '2026-08-03', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-03'),
        (@emp, '2026-08-04', '2026-08-04', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-04'),
        (@emp, '2026-08-05', '2026-08-05', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-05'),
        (@emp, '2026-08-06', '2026-08-06', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-06'),
        (@emp, '2026-08-07', '2026-08-07', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-07'),
        (@emp, '2026-08-10', '2026-08-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-10'),
        (@emp, '2026-08-11', '2026-08-11', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-11'),
        (@emp, '2026-08-12', '2026-08-12', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-12'),
        (@emp, '2026-08-27', '2026-08-27', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-27'),
        (@emp, '2026-06-28', '2026-06-28', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-28'),
        (@emp, '2026-10-19', '2026-10-19', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-19'),
        (@emp, '2026-10-20', '2026-10-20', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-20');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Sean' AND LTRIM(RTRIM(Surname)) IN (N'McCann') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Sean', N'McCann', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-02-04', '2026-02-04', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-04'),
        (@emp, '2026-02-05', '2026-02-05', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-05'),
        (@emp, '2026-02-06', '2026-02-06', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-06'),
        (@emp, '2026-08-07', '2026-08-07', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-07'),
        (@emp, '2026-08-10', '2026-08-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-10'),
        (@emp, '2026-08-11', '2026-08-11', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-11'),
        (@emp, '2026-08-12', '2026-08-12', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-12'),
        (@emp, '2026-08-13', '2026-08-13', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-13'),
        (@emp, '2026-08-14', '2026-08-14', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-14'),
        (@emp, '2026-08-17', '2026-08-17', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-17'),
        (@emp, '2026-08-18', '2026-08-18', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-18');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Joe' AND LTRIM(RTRIM(Surname)) IN (N'McCourt') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Joe', N'McCourt', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-06-01', '2026-06-01', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-01'),
        (@emp, '2026-06-02', '2026-06-02', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-02'),
        (@emp, '2026-06-03', '2026-06-03', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-03'),
        (@emp, '2026-06-04', '2026-06-04', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-04'),
        (@emp, '2026-06-05', '2026-06-05', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-05'),
        (@emp, '2026-06-08', '2026-06-08', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-08'),
        (@emp, '2026-06-09', '2026-06-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-09'),
        (@emp, '2026-06-10', '2026-06-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-10'),
        (@emp, '2026-03-13', '2026-03-13', 0.5, 0.5, 1, N'From the 2026 spreadsheet', N'Collette', '2026-03-13'),
        (@emp, '2026-09-21', '2026-09-21', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-09-21'),
        (@emp, '2026-09-22', '2026-09-22', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-09-22'),
        (@emp, '2026-09-23', '2026-09-23', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-09-23'),
        (@emp, '2026-09-24', '2026-09-24', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-09-24'),
        (@emp, '2026-06-15', '2026-06-15', 0.25, 0.25, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-15'),
        (@emp, '2026-06-16', '2026-06-16', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-16'),
        (@emp, '2026-06-17', '2026-06-17', 0.25, 0.25, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-17'),
        (@emp, '2026-07-02', '2026-07-02', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-02'),
        (@emp, '2026-07-03', '2026-07-03', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-03'),
        (@emp, '2026-07-06', '2026-07-06', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-06');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Paul' AND LTRIM(RTRIM(Surname)) IN (N'McDonagh') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Paul', N'McDonagh', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'UK Sales';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-02-06', '2026-02-06', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-02-06'),
        (@emp, '2026-07-15', '2026-07-15', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-15'),
        (@emp, '2026-09-03', '2026-09-03', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-09-03'),
        (@emp, '2026-09-04', '2026-09-04', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-09-04'),
        (@emp, '2026-10-09', '2026-10-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-09'),
        (@emp, '2026-10-12', '2026-10-12', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-12'),
        (@emp, '2026-10-13', '2026-10-13', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-13'),
        (@emp, '2026-10-14', '2026-10-14', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-14'),
        (@emp, '2026-10-15', '2026-10-15', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-15'),
        (@emp, '2026-10-16', '2026-10-16', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-16'),
        (@emp, '2026-08-26', '2026-08-26', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-26'),
        (@emp, '2026-09-24', '2026-09-24', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-09-24');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Martin' AND LTRIM(RTRIM(Surname)) IN (N'McGlone') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Martin', N'McGlone', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-02-02', '2026-02-02', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-02'),
        (@emp, '2026-06-08', '2026-06-08', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-08'),
        (@emp, '2026-06-09', '2026-06-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-09'),
        (@emp, '2026-06-10', '2026-06-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-10'),
        (@emp, '2026-06-11', '2026-06-11', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-11'),
        (@emp, '2026-06-12', '2026-06-12', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-12'),
        (@emp, '2026-07-13', '2026-07-13', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-13'),
        (@emp, '2026-07-27', '2026-07-27', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-27'),
        (@emp, '2026-07-28', '2026-07-28', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-28'),
        (@emp, '2026-09-04', '2026-09-04', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-09-04'),
        (@emp, '2026-10-28', '2026-10-28', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-28');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Mairead' AND LTRIM(RTRIM(Surname)) IN (N'McLernon', N'McCann') ORDER BY CASE WHEN LTRIM(RTRIM(Surname)) = N'McLernon' THEN 0 ELSE 1 END, id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Mairead', N'McLernon', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Office';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-03-06', '2026-03-06', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-03-06'),
        (@emp, '2026-04-07', '2026-04-07', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-04-07'),
        (@emp, '2026-08-10', '2026-08-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-10'),
        (@emp, '2026-08-11', '2026-08-11', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-11'),
        (@emp, '2026-08-12', '2026-08-12', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-12'),
        (@emp, '2026-08-13', '2026-08-13', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-13'),
        (@emp, '2026-08-14', '2026-08-14', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-14'),
        (@emp, '2026-06-18', '2026-06-18', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-18'),
        (@emp, '2026-07-13', '2026-07-13', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-13'),
        (@emp, '2026-10-02', '2026-10-02', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-10-02');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Maurice' AND LTRIM(RTRIM(Surname)) IN (N'Quinn') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Maurice', N'Quinn', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-06-15', '2026-06-15', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-15'),
        (@emp, '2026-06-16', '2026-06-16', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-16'),
        (@emp, '2026-06-17', '2026-06-17', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-17'),
        (@emp, '2026-06-18', '2026-06-18', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-18'),
        (@emp, '2026-06-19', '2026-06-19', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-06-19'),
        (@emp, '2026-08-13', '2026-08-13', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-13'),
        (@emp, '2026-08-14', '2026-08-14', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-14'),
        (@emp, '2026-08-17', '2026-08-17', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-17'),
        (@emp, '2026-08-18', '2026-08-18', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-18'),
        (@emp, '2026-08-19', '2026-08-19', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-19'),
        (@emp, '2026-08-20', '2026-08-20', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-08-20');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Declan' AND LTRIM(RTRIM(Surname)) IN (N'Sherry') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Declan', N'Sherry', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-02-04', '2026-02-04', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-04'),
        (@emp, '2026-02-05', '2026-02-05', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-05'),
        (@emp, '2026-07-29', '2026-07-29', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-29'),
        (@emp, '2026-07-30', '2026-07-30', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-30'),
        (@emp, '2026-07-31', '2026-07-31', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-31'),
        (@emp, '2026-08-03', '2026-08-03', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-03'),
        (@emp, '2026-08-04', '2026-08-04', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-08-04'),
        (@emp, '2026-09-21', '2026-09-21', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-09-21'),
        (@emp, '2026-09-22', '2026-09-22', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-09-22'),
        (@emp, '2026-09-23', '2026-09-23', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-09-23'),
        (@emp, '2026-09-24', '2026-09-24', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-09-24');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Russ' AND LTRIM(RTRIM(Surname)) IN (N'Tame') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Russ', N'Tame', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'UK Sales';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-03-26', '2026-03-26', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-03-26'),
        (@emp, '2026-03-27', '2026-03-27', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-03-27'),
        (@emp, '2026-06-08', '2026-06-08', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-08'),
        (@emp, '2026-06-09', '2026-06-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-09'),
        (@emp, '2026-06-10', '2026-06-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-10'),
        (@emp, '2026-06-11', '2026-06-11', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-11'),
        (@emp, '2026-06-12', '2026-06-12', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-12'),
        (@emp, '2026-10-09', '2026-10-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-09'),
        (@emp, '2026-10-13', '2026-10-13', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-13'),
        (@emp, '2026-10-14', '2026-10-14', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-14'),
        (@emp, '2026-10-15', '2026-10-15', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-15'),
        (@emp, '2026-10-16', '2026-10-16', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-16'),
        (@emp, '2026-10-19', '2026-10-19', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-19'),
        (@emp, '2026-10-20', '2026-10-20', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-20'),
        (@emp, '2026-10-21', '2026-10-21', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-21'),
        (@emp, '2026-10-22', '2026-10-22', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-22'),
        (@emp, '2026-10-23', '2026-10-23', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-10-23');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'Jeffrey' AND LTRIM(RTRIM(Surname)) IN (N'Weir') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'Jeffrey', N'Weir', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'Factory / HGV';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-03-27', '2026-03-27', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-03-27'),
        (@emp, '2026-07-16', '2026-07-16', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-16'),
        (@emp, '2026-07-17', '2026-07-17', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-17'),
        (@emp, '2026-07-20', '2026-07-20', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-20'),
        (@emp, '2026-07-21', '2026-07-21', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-21'),
        (@emp, '2026-07-22', '2026-07-22', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-22'),
        (@emp, '2026-07-23', '2026-07-23', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-23'),
        (@emp, '2026-07-24', '2026-07-24', 1, 1, 1, N'From the 2026 spreadsheet', N'Collette', '2026-07-24'),
        (@emp, '2026-09-09', '2026-09-09', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-09-09'),
        (@emp, '2026-09-10', '2026-09-10', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-09-10'),
        (@emp, '2026-09-11', '2026-09-11', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-09-11');

    SET @user = NULL;
    SELECT TOP 1 @user = id FROM dbo.GlobeweighUser
    WHERE DateDeleted IS NULL AND LTRIM(RTRIM(FirstName)) = N'David' AND LTRIM(RTRIM(Surname)) IN (N'Woodall') ORDER BY id;
    IF @user IS NULL
    BEGIN
        INSERT INTO dbo.GlobeweighUser (UserTypeId, FirstName, Surname, RoleId, Disabled, CostingAccess, DateCreated)
        VALUES (4, N'David', N'Woodall', 0, 0, 0, GETDATE());
        SET @user = SCOPE_IDENTITY();
    END
    INSERT INTO dbo.LeaveEmployee (GlobeweighUserId, CategoryId, StartDate, Notes)
    SELECT @user, id, NULL, NULL
    FROM dbo.LeaveCategory WHERE Name = N'UK Sales';
    SET @emp = SCOPE_IDENTITY();
    INSERT INTO dbo.LeaveRequest (EmployeeId, StartDate, EndDate, DayLength, Days, Status, Note, DecidedByName, DateDecided) VALUES
        (@emp, '2026-01-20', '2026-01-20', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-20'),
        (@emp, '2026-01-21', '2026-01-21', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-21'),
        (@emp, '2026-01-22', '2026-01-22', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-22'),
        (@emp, '2026-01-23', '2026-01-23', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-23'),
        (@emp, '2026-01-26', '2026-01-26', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-26'),
        (@emp, '2026-01-27', '2026-01-27', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-27'),
        (@emp, '2026-01-28', '2026-01-28', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-28'),
        (@emp, '2026-01-29', '2026-01-29', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-29'),
        (@emp, '2026-01-30', '2026-01-30', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-01-30'),
        (@emp, '2026-02-02', '2026-02-02', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-02'),
        (@emp, '2026-02-03', '2026-02-03', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-02-03'),
        (@emp, '2026-05-26', '2026-05-26', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-05-26'),
        (@emp, '2026-05-27', '2026-05-27', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-05-27'),
        (@emp, '2026-05-28', '2026-05-28', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-05-28'),
        (@emp, '2026-05-29', '2026-05-29', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-05-29'),
        (@emp, '2026-06-29', '2026-06-29', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-29'),
        (@emp, '2026-06-30', '2026-06-30', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-06-30'),
        (@emp, '2026-07-01', '2026-07-01', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-01'),
        (@emp, '2026-07-02', '2026-07-02', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-02'),
        (@emp, '2026-07-03', '2026-07-03', 1, 1, 1, N'From the 2026 spreadsheet', N'Colin', '2026-07-03');
END
GO
