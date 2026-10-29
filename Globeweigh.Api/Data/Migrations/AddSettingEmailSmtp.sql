-- Outbound email account (Super Admin > Email Setup): the SMTP relay the API sends
-- automatic emails through. SmtpSecurity is one of None / StartTls / Ssl. SmtpPassword
-- is DPAPI-protected (machine scope) by the API before it is stored - see
-- Auth/DpapiSecret.cs - so it only decrypts on the box that saved it: a copy of this
-- database restored on another machine shows the password as not set and it is entered
-- again there. See Setting.cs and Services/EmailSender.cs.
IF COL_LENGTH('dbo.Setting', 'SmtpHost') IS NULL
    ALTER TABLE dbo.Setting ADD SmtpHost nvarchar(200) NULL;
IF COL_LENGTH('dbo.Setting', 'SmtpPort') IS NULL
    ALTER TABLE dbo.Setting ADD SmtpPort int NULL;
IF COL_LENGTH('dbo.Setting', 'SmtpSecurity') IS NULL
    ALTER TABLE dbo.Setting ADD SmtpSecurity nvarchar(20) NULL;
IF COL_LENGTH('dbo.Setting', 'SmtpUsername') IS NULL
    ALTER TABLE dbo.Setting ADD SmtpUsername nvarchar(200) NULL;
IF COL_LENGTH('dbo.Setting', 'SmtpPassword') IS NULL
    ALTER TABLE dbo.Setting ADD SmtpPassword nvarchar(1000) NULL;
IF COL_LENGTH('dbo.Setting', 'EmailFromAddress') IS NULL
    ALTER TABLE dbo.Setting ADD EmailFromAddress nvarchar(200) NULL;
IF COL_LENGTH('dbo.Setting', 'EmailFromName') IS NULL
    ALTER TABLE dbo.Setting ADD EmailFromName nvarchar(100) NULL;
GO
