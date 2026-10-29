-- When the "specified order number" series was switched on (SettingsController stamps it
-- on the off->on transition, and clears it when switched back off). Order-number editing
-- in the dispatch Edit Order popup keys off this: an OPEN order created BEFORE this instant
-- carries an ad-hoc number keyed during the side-by-side period and may be corrected; one
-- created after was issued from the continued series and is fixed. NULL = the switch has
-- never been on, so every open order still counts as pre-hand-over (editable).
-- See Setting.NextOrderNoEnabledDate, DispatchController.IsOrderNoEditable, and
-- Migrations/AddSettingNextOrderNo.sql.
IF COL_LENGTH('dbo.Setting', 'NextOrderNoEnabledDate') IS NULL
    ALTER TABLE dbo.Setting ADD NextOrderNoEnabledDate datetime NULL;
GO
