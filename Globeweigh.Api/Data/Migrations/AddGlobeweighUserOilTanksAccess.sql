-- Who can see the Oil Tanks page (TauriAdmin > Oil Tanks).
--
-- A per-user switch, like CostingAccess beside it, ticked user by user in
-- Super Admin > Oil Tanks. 0 for everyone to start with: the page shows only for the
-- users switched on there (and always for the super-admin, who sets it). The API checks
-- the same column, so the page's data is not reachable by a user it is hidden from.
--
-- See OilTanksController (CanViewAsync, the access endpoints).
IF COL_LENGTH('dbo.GlobeweighUser', 'OilTanksAccess') IS NULL
    ALTER TABLE dbo.GlobeweighUser ADD OilTanksAccess bit NOT NULL
        CONSTRAINT DF_GlobeweighUser_OilTanksAccess DEFAULT(0);
GO
