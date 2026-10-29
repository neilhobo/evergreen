-- Adds the "Bespoke" product type at the fixed id 6 (BESPOKE_PRODUCT_TYPE_ID in
-- TauriAdmin src/api/products.ts, eProductType.Bespoke in Globeweigh.Model). 6, not 5,
-- so it can never collide with the old Bags row whichever order the scripts run in.
-- Idempotent; run once against GbEvergreen.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF NOT EXISTS (SELECT 1 FROM dbo.ProductType WHERE id = 6)
BEGIN
    SET IDENTITY_INSERT dbo.ProductType ON;
    INSERT INTO dbo.ProductType (id, TypeDescription, CreatedBy, QuarantineDefault, IsKilos, DateCreated)
    VALUES (6, 'Bespoke', 0, 0, 0, GETDATE());
    SET IDENTITY_INSERT dbo.ProductType OFF;
END
ELSE IF NOT EXISTS (SELECT 1 FROM dbo.ProductType WHERE id = 6 AND TypeDescription = 'Bespoke')
    RAISERROR('ProductType 6 already exists and is not Bespoke.', 16, 1);
GO
