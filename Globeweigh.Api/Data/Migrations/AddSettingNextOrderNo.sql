-- Order numbers continue the legacy system's series at cut-over.
--
-- Evergreen runs the old system alongside this one, and its order numbers are a
-- single incremental series they want to keep going. Two columns, both edited from
-- Admin > Settings > Order Numbering:
--
--   NextOrderNo         the next number to issue
--   NextOrderNoEnabled  whether to issue it - 0 until they say go live
--
-- While disabled, order creation issues MAX(OrderNo)+1 exactly as it always has, so
-- the number can be entered and staged well ahead of the hand-over and switched on
-- (or back off) on the day without a redeploy.
--
-- See DispatchController.AllocateOrderNo and SettingsController.
IF COL_LENGTH('dbo.Setting', 'NextOrderNo') IS NULL
    ALTER TABLE dbo.Setting ADD NextOrderNo int NULL;
GO

IF COL_LENGTH('dbo.Setting', 'NextOrderNoEnabled') IS NULL
    ALTER TABLE dbo.Setting ADD NextOrderNoEnabled bit NOT NULL
        CONSTRAINT DF_Setting_NextOrderNoEnabled DEFAULT(0);
GO
