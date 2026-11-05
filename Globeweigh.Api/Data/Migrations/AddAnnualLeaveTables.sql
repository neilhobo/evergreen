-- Annual Leave (TauriAdmin > Annual Leave): the client's staff holiday entitlement, replacing
-- their "Annual Leave Request Log" spreadsheet. Staged (FeatureFlag AnnualLeave) - the
-- super-admin sees it before anyone else.
--
-- The rules (from their 2026_Annual_Leave_Entitlement.pdf):
--   * every employee has 28 days a leave year (1 Jan - 31 Dec), no carry-over;
--   * part of that is SET company days (bank holidays, the Christmas shutdown...) which differ
--     by staff category, the rest are FLOATING days the employee books;
--   * a starter's 28 is pro-rated from their start date, and only the set days on/after it count.
-- Services/LeaveCalculator.cs does the sums; nothing here is derived.
--
-- Run once against GbEvergreen. Idempotent. Seeds the three categories and the 2026 company
-- days exactly as the PDF ticks them.
SET QUOTED_IDENTIFIER ON;
GO
-- Office / Factory & HGV / UK Sales. AnnualDays is the full-year entitlement (28 for all three).
IF OBJECT_ID('dbo.LeaveCategory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveCategory (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LeaveCategory PRIMARY KEY,
        Name        NVARCHAR(100) NOT NULL,
        AnnualDays  DECIMAL(6,2)  NOT NULL CONSTRAINT DF_LeaveCategory_AnnualDays DEFAULT (28),
        SortOrder   INT           NOT NULL CONSTRAINT DF_LeaveCategory_SortOrder DEFAULT (0),
        DateCreated DATETIME      NOT NULL CONSTRAINT DF_LeaveCategory_DateCreated DEFAULT (GETDATE()),
        DateDeleted DATETIME      NULL
    );
END
GO
-- A company day off (bank holiday, shutdown day). Which categories get it is the junction below.
IF OBJECT_ID('dbo.LeaveCompanyHoliday', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveCompanyHoliday (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LeaveCompanyHoliday PRIMARY KEY,
        [Date]      DATE          NOT NULL,
        Name        NVARCHAR(100) NOT NULL,
        DateCreated DATETIME      NOT NULL CONSTRAINT DF_LeaveCompanyHoliday_DateCreated DEFAULT (GETDATE())
    );
    CREATE UNIQUE INDEX UX_LeaveCompanyHoliday_Date ON dbo.LeaveCompanyHoliday ([Date]);
END
GO
IF OBJECT_ID('dbo.LeaveCompanyHolidayCategory', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveCompanyHolidayCategory (
        HolidayId  INT NOT NULL CONSTRAINT FK_LeaveCompanyHolidayCategory_Holiday
                       REFERENCES dbo.LeaveCompanyHoliday (id) ON DELETE CASCADE,
        CategoryId INT NOT NULL CONSTRAINT FK_LeaveCompanyHolidayCategory_Category
                       REFERENCES dbo.LeaveCategory (id),
        CONSTRAINT PK_LeaveCompanyHolidayCategory PRIMARY KEY (HolidayId, CategoryId)
    );
END
GO
-- A stretch where leave is discouraged: peak season (March-June, "no extended leave"), the
-- trade shows for Sales. CategoryId NULL = everyone. MaxDays: a request of MORE than this many
-- days inside the period is flagged; NULL = any request inside it is flagged. Warnings only -
-- the approver decides.
IF OBJECT_ID('dbo.LeaveBlockedPeriod', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveBlockedPeriod (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LeaveBlockedPeriod PRIMARY KEY,
        Name        NVARCHAR(100) NOT NULL,
        StartDate   DATE          NOT NULL,
        EndDate     DATE          NOT NULL,
        CategoryId  INT           NULL,
        MaxDays     DECIMAL(6,2)  NULL,
        DateCreated DATETIME      NOT NULL CONSTRAINT DF_LeaveBlockedPeriod_DateCreated DEFAULT (GETDATE())
    );
END
GO
-- The people leave is kept for. Separate from GlobeweighUser: most factory staff never sign in.
-- GlobeweighUserId links one to a login for the self-service side (still to come);
-- ApproverUserId is the office user who signs their requests off.
IF OBJECT_ID('dbo.LeaveEmployee', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveEmployee (
        id               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LeaveEmployee PRIMARY KEY,
        Forename         NVARCHAR(100) NOT NULL,
        Surname          NVARCHAR(100) NOT NULL,
        CategoryId       INT           NOT NULL CONSTRAINT FK_LeaveEmployee_Category REFERENCES dbo.LeaveCategory (id),
        StartDate        DATE          NULL,
        EndDate          DATE          NULL,
        Email            NVARCHAR(200) NULL,
        Mobile           NVARCHAR(50)  NULL,
        ApproverUserId   INT           NULL,
        GlobeweighUserId INT           NULL,
        Notes            NVARCHAR(1000) NULL,
        DateCreated      DATETIME      NOT NULL CONSTRAINT DF_LeaveEmployee_DateCreated DEFAULT (GETDATE()),
        DateDeleted      DATETIME      NULL
    );
END
GO
-- Manual corrections to one employee's floating days for one year (+ or -), with the reason.
IF OBJECT_ID('dbo.LeaveAdjustment', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveAdjustment (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LeaveAdjustment PRIMARY KEY,
        EmployeeId  INT           NOT NULL CONSTRAINT FK_LeaveAdjustment_Employee REFERENCES dbo.LeaveEmployee (id),
        [Year]      INT           NOT NULL,
        Days        DECIMAL(6,2)  NOT NULL,
        Reason      NVARCHAR(300) NOT NULL,
        CreatedById INT           NULL,
        DateCreated DATETIME      NOT NULL CONSTRAINT DF_LeaveAdjustment_DateCreated DEFAULT (GETDATE()),
        DateDeleted DATETIME      NULL
    );
    CREATE INDEX IX_LeaveAdjustment_Employee ON dbo.LeaveAdjustment (EmployeeId, [Year]);
END
GO
-- A request for floating leave. Days is worked out when the request is made (working days in
-- the range, less the employee's company days, times DayLength for a single day) and kept, so a
-- later change to the company days never rewrites leave already taken.
-- DayLength: 1 full, 0.5 half, 0.25 quarter - single-day requests only.
-- Status: 0 Waiting, 1 Approved, 2 Declined, 3 Cancelled.
-- DecidedByName is kept as written: the spreadsheet's approvers (Colin, Collette) need not be logins.
IF OBJECT_ID('dbo.LeaveRequest', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveRequest (
        id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LeaveRequest PRIMARY KEY,
        EmployeeId    INT            NOT NULL CONSTRAINT FK_LeaveRequest_Employee REFERENCES dbo.LeaveEmployee (id),
        StartDate     DATE           NOT NULL,
        EndDate       DATE           NOT NULL,
        DayLength     DECIMAL(4,2)   NOT NULL CONSTRAINT DF_LeaveRequest_DayLength DEFAULT (1),
        Days          DECIMAL(6,2)   NOT NULL,
        Status        INT            NOT NULL CONSTRAINT DF_LeaveRequest_Status DEFAULT (0),
        Note          NVARCHAR(1000) NULL,
        DecisionNote  NVARCHAR(1000) NULL,
        RequestedById INT            NULL,
        DecidedById   INT            NULL,
        DecidedByName NVARCHAR(100)  NULL,
        DateDecided   DATETIME       NULL,
        DateCreated   DATETIME       NOT NULL CONSTRAINT DF_LeaveRequest_DateCreated DEFAULT (GETDATE()),
        DateDeleted   DATETIME       NULL
    );
    CREATE INDEX IX_LeaveRequest_Employee ON dbo.LeaveRequest (EmployeeId, StartDate);
    CREATE INDEX IX_LeaveRequest_Dates ON dbo.LeaveRequest (StartDate, EndDate);
END
GO
-- ---- seed: the three categories ------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.LeaveCategory)
    INSERT INTO dbo.LeaveCategory (Name, AnnualDays, SortOrder) VALUES
        (N'Office', 28, 1), (N'Factory / HGV', 28, 2), (N'UK Sales', 28, 3);
GO
-- ---- seed: the 2026 company days, ticked per category as in the PDF ---------------------------
-- Office gets all 18; Factory / HGV not Mon 4 May; UK Sales not the two Halloween days.
IF NOT EXISTS (SELECT 1 FROM dbo.LeaveCompanyHoliday WHERE [Date] BETWEEN '2026-01-01' AND '2026-12-31')
BEGIN
    INSERT INTO dbo.LeaveCompanyHoliday ([Date], Name) VALUES
        ('2026-01-01', N'New Year''s Day'), ('2026-01-02', N'New Year''s Holiday'),
        ('2026-04-03', N'Good Friday'), ('2026-04-06', N'Easter Monday'),
        ('2026-05-04', N'May Bank Holiday'), ('2026-05-25', N'May Bank Holiday'),
        ('2026-08-31', N'August Bank Holiday'),
        ('2026-10-29', N'Halloween'), ('2026-10-30', N'Halloween'),
        ('2026-12-21', N'Christmas'), ('2026-12-22', N'Christmas'), ('2026-12-23', N'Christmas'),
        ('2026-12-24', N'Christmas Eve'), ('2026-12-25', N'Christmas Day'),
        ('2026-12-28', N'Christmas'), ('2026-12-29', N'Christmas'), ('2026-12-30', N'Christmas'),
        ('2026-12-31', N'Christmas');

    INSERT INTO dbo.LeaveCompanyHolidayCategory (HolidayId, CategoryId)
    SELECT h.id, c.id
    FROM dbo.LeaveCompanyHoliday h
    CROSS JOIN dbo.LeaveCategory c
    WHERE h.[Date] BETWEEN '2026-01-01' AND '2026-12-31'
      AND NOT (c.Name = N'Factory / HGV' AND h.[Date] = '2026-05-04')
      AND NOT (c.Name = N'UK Sales' AND h.[Date] IN ('2026-10-29', '2026-10-30'));
END
GO
-- ---- seed: peak season ----------------------------------------------------------------------
-- "Extended periods of leave must not be taken during peak seasonal periods, defined as March
-- through to June." Extended = more than 5 days until the client says otherwise.
IF NOT EXISTS (SELECT 1 FROM dbo.LeaveBlockedPeriod)
    INSERT INTO dbo.LeaveBlockedPeriod (Name, StartDate, EndDate, CategoryId, MaxDays) VALUES
        (N'Peak season', '2026-03-01', '2026-06-30', NULL, 5);
GO
