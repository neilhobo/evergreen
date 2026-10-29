-- Adds dbo.Product.AverageWeight (kg per pallet) — the orange "Average Weight" column on the
-- Finished Products grid, editable in the product dialog.
--
-- It is the Picking API's last resort for a pallet with no measured weight (a no-scan label,
-- or a code CodicoPallet doesn't know): PickingController.EstimatePalletWeight still prefers
-- the median of the product's own pallets, and only when there are none — the bark products,
-- which had never had a weighed pallet and so were picked at 0 kg — does it use this figure.
--
-- Seeded once from the average of every weighed pallet (CodicoPallet.Weight > 0) of the
-- product, matched on the 13-digit GTIN as the estimate matches it. Products with no weighed
-- pallet are left NULL for someone to type in. Only NULLs are seeded, so a rerun never
-- overwrites a figure typed in since.
--
-- Run once against GbEvergreen. Idempotent. Additive only. The SET options are required
-- (sqlcmd defaults QUOTED_IDENTIFIER off, which the table's computed Gtin13 column rejects).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.Product', 'AverageWeight') IS NULL
    ALTER TABLE dbo.Product ADD AverageWeight decimal(10,2) NULL;
GO

UPDATE p
SET p.AverageWeight = w.AvgWeight
FROM dbo.Product p
CROSS APPLY (
    SELECT CAST(AVG(c.Weight) AS decimal(10,2)) AS AvgWeight
    FROM dbo.CodicoPallet c
    WHERE c.Weight > 0 AND (c.Code13 = p.Gtin13 OR c.Code = p.Gtin13)
) w
WHERE p.AverageWeight IS NULL AND w.AvgWeight IS NOT NULL;
GO

ALTER VIEW dbo.vwProduct
AS
SELECT dbo.Product.id, dbo.Product.Description, dbo.ProductType.TypeDescription AS ProductType, dbo.Product.SkuCode, dbo.Product.Size, dbo.Product.Upp, dbo.Product.ProductTypeId, dbo.ReferenceData.DisplayName AS CategoryName,
                  dbo.Product.DateDeleted, dbo.Product.CategoryId, dbo.Product.PosId, ProductPos.Description AS PosDescription, CAST(CASE WHEN dbo.Product.PosId IS NULL OR
                  dbo.Product.PosId = 0 THEN 0 ELSE 1 END AS bit) AS HasPos, dbo.Product.Gtin, dbo.Product.CommodityCode, dbo.Product.IsBulkBag, dbo.Product.AverageWeight
FROM     dbo.Product LEFT OUTER JOIN
                  dbo.ProductType ON dbo.Product.ProductTypeId = dbo.ProductType.id LEFT OUTER JOIN
                  dbo.ReferenceData ON dbo.Product.CategoryId = dbo.ReferenceData.id LEFT OUTER JOIN
                  dbo.Product AS ProductPos ON dbo.Product.PosId = ProductPos.id
GROUP BY dbo.Product.id, dbo.Product.Description, dbo.ProductType.TypeDescription, dbo.Product.ProductTypeId, dbo.Product.DateDeleted, dbo.Product.SkuCode, dbo.Product.Size, dbo.Product.Upp, dbo.ReferenceData.DisplayName,
                  dbo.Product.PosId, dbo.Product.CategoryId, ProductPos.Description, dbo.Product.Gtin, dbo.Product.CommodityCode, dbo.Product.IsBulkBag, dbo.Product.AverageWeight
GO
