-- Cancel for "Refresh from Sage": Admin's Cancel stamps CancelRequestedUtc on the entity's sync
-- row, and the sidecar, which reads it every few seconds during a pull, stops when it sees the
-- value change from the one it started with. See Data/Entities/SageSyncState.cs.
-- Run once against GbEvergreen. Idempotent.

IF COL_LENGTH('dbo.SageSyncState', 'CancelRequestedUtc') IS NULL
    ALTER TABLE dbo.SageSyncState ADD CancelRequestedUtc DATETIME NULL;
GO
