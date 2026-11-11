-- The client's "Annual Leave Entitlement & Key Information 2027" sheet: the 2027 company days
-- with which categories get each (Office 18, Factory / HGV 17 - no 3 May, UK Sales 16 - no
-- Halloween 29 Oct / 1 Nov), and the 2027 March-June peak season. Not "Copy from last year":
-- Easter and the bank-holiday Mondays move. Does nothing if 2027 already has company days.
SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM dbo.LeaveCompanyHoliday WHERE [Date] >= '2027-01-01' AND [Date] <= '2027-12-31')
BEGIN
    DECLARE @days TABLE ([Date] DATE, Name NVARCHAR(100), Office BIT, Factory BIT, Sales BIT);
    INSERT INTO @days VALUES
        ('2027-01-01', N'New Year''s Day',      1, 1, 1),
        ('2027-03-26', N'Good Friday',          1, 1, 1),
        ('2027-03-29', N'Easter Monday',        1, 1, 1),
        ('2027-05-03', N'May Bank Holiday',     1, 0, 1),
        ('2027-05-31', N'May Bank Holiday',     1, 1, 1),
        ('2027-08-30', N'August Bank Holiday',  1, 1, 1),
        ('2027-10-29', N'Halloween',            1, 1, 0),
        ('2027-11-01', N'Halloween',            1, 1, 0),
        ('2027-12-20', N'Christmas',            1, 1, 1),
        ('2027-12-21', N'Christmas',            1, 1, 1),
        ('2027-12-22', N'Christmas',            1, 1, 1),
        ('2027-12-23', N'Christmas Eve',        1, 1, 1),
        ('2027-12-24', N'Christmas Day',        1, 1, 1),
        ('2027-12-27', N'Christmas',            1, 1, 1),
        ('2027-12-28', N'Christmas',            1, 1, 1),
        ('2027-12-29', N'Christmas',            1, 1, 1),
        ('2027-12-30', N'Christmas',            1, 1, 1),
        ('2027-12-31', N'New Year''s Eve',      1, 1, 1);

    INSERT INTO dbo.LeaveCompanyHoliday ([Date], Name) SELECT [Date], Name FROM @days;

    INSERT INTO dbo.LeaveCompanyHolidayCategory (HolidayId, CategoryId)
    SELECT h.id, c.id
    FROM @days d
    JOIN dbo.LeaveCompanyHoliday h ON h.[Date] = d.[Date]
    JOIN dbo.LeaveCategory c ON (c.Name = N'Office' AND d.Office = 1)
                             OR (c.Name = N'Factory / HGV' AND d.Factory = 1)
                             OR (c.Name = N'UK Sales' AND d.Sales = 1);
END
GO
IF NOT EXISTS (SELECT 1 FROM dbo.LeaveBlockedPeriod WHERE StartDate = '2027-03-01' AND Name = N'Peak season')
    INSERT INTO dbo.LeaveBlockedPeriod (Name, StartDate, EndDate, CategoryId, MaxDays)
    VALUES (N'Peak season', '2027-03-01', '2027-06-30', NULL, 5);
GO
