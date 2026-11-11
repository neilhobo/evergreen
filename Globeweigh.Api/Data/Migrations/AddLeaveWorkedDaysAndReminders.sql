-- Annual Leave, from the client's 2027 policy sheet:
--
-- LeaveHolidayWorked - "An administrator may approve an exception for an employee to work on a
-- fixed holiday and substitute a floating day, with the adjustment recorded so their total paid
-- entitlement is not increased." The company day stops being one of THEIR company days: their set
-- days drop by one and their floating days rise by one (total unchanged), and the calendar shows
-- them at work that day. Removing the row undoes it.
--
-- LeaveReminder - "Where leave remains unallocated on 31 August, employees must confirm by 15
-- September when they intend to take it." Each reminder email sent from Annual Leave >
-- Outstanding, with the unbooked days at the time, so the page shows who has been asked.
SET QUOTED_IDENTIFIER ON;
GO
IF OBJECT_ID('dbo.LeaveHolidayWorked', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveHolidayWorked (
        id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LeaveHolidayWorked PRIMARY KEY,
        EmployeeId  INT           NOT NULL CONSTRAINT FK_LeaveHolidayWorked_Employee REFERENCES dbo.LeaveEmployee (id),
        HolidayId   INT           NOT NULL CONSTRAINT FK_LeaveHolidayWorked_Holiday REFERENCES dbo.LeaveCompanyHoliday (id) ON DELETE CASCADE,
        Reason      NVARCHAR(300) NOT NULL,
        CreatedById INT           NULL,
        DateCreated DATETIME      NOT NULL CONSTRAINT DF_LeaveHolidayWorked_DateCreated DEFAULT (GETDATE())
    );
    CREATE UNIQUE INDEX UX_LeaveHolidayWorked ON dbo.LeaveHolidayWorked (EmployeeId, HolidayId);
END
GO
IF OBJECT_ID('dbo.LeaveReminder', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveReminder (
        id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LeaveReminder PRIMARY KEY,
        EmployeeId INT           NOT NULL CONSTRAINT FK_LeaveReminder_Employee REFERENCES dbo.LeaveEmployee (id),
        [Year]     INT           NOT NULL,
        Days       DECIMAL(6,2)  NOT NULL,
        SentTo     NVARCHAR(200) NOT NULL,
        SentById   INT           NULL,
        DateSent   DATETIME      NOT NULL CONSTRAINT DF_LeaveReminder_DateSent DEFAULT (GETDATE())
    );
    CREATE INDEX IX_LeaveReminder_Employee ON dbo.LeaveReminder (EmployeeId, [Year]);
END
GO
