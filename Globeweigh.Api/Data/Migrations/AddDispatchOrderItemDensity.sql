-- Adds dbo.DispatchOrderItem.Density (kg/m³, 2 decimals) — entered by the picker in the
-- Picking app when marking a cubic meter line (raw peat) as picked. Kept for later use
-- (volume <-> weight; see Setting.BulkDensity for the site-wide figure). NULL on every
-- other line, and on a peat line until it is picked.
-- Run once against GbEvergreen. Idempotent. Additive only.
IF COL_LENGTH('dbo.DispatchOrderItem', 'Density') IS NULL
    ALTER TABLE dbo.DispatchOrderItem ADD Density decimal(9,2) NULL;
GO
