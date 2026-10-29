-- When a Picking device last changed app version or Shorebird patch (UTC), stamped by the
-- register check-in. Device Manager hides a finished push status/detail older than this, so
-- the grid only shows pushes the device hasn't moved on from (Pending always shows).
-- The backfill stamps every existing device now, clearing the old statuses already there.
-- Run once against GbEvergreen. Idempotent. Additive only.
IF COL_LENGTH('dbo.Device', 'DateOfLastUpdateUtc') IS NULL
    ALTER TABLE dbo.Device ADD DateOfLastUpdateUtc datetime NULL;
GO

UPDATE dbo.Device SET DateOfLastUpdateUtc = GETUTCDATE() WHERE DateOfLastUpdateUtc IS NULL;
GO
