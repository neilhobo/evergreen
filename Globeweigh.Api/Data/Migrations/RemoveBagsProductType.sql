-- Removes the "Bags" product type (id 5) added by the since-deleted AddBagsProductType.sql.
-- Only deletes it while no product uses it. Idempotent; run once against GbEvergreen.
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF EXISTS (SELECT 1 FROM dbo.Product WHERE ProductTypeId = 5)
    RAISERROR('Products still use ProductType 5 (Bags) - move or delete them first.', 16, 1);
ELSE
    DELETE FROM dbo.ProductType WHERE id = 5 AND TypeDescription = 'Bags';
GO
