-- Standard bulk density (kg/m³) for converting a manual pallet's weight to volume:
-- Volume (m³) = Weight (kg) / BulkDensity. Peat is sold by volume, not weight.
-- Run once against GbEvergreen; the default populates the existing settings row with 320.
IF COL_LENGTH('dbo.Setting', 'BulkDensity') IS NULL
    ALTER TABLE dbo.Setting ADD BulkDensity decimal(9,2) NOT NULL CONSTRAINT DF_Setting_BulkDensity DEFAULT(320);
GO
