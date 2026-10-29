-- Adds "Sold By" to dbo.Product (Add/Edit Product dialog, Finished Products) and exposes it
-- through vwProduct:
--   SoldBy  'Pallet' or 'CubicMeter'. The DEFAULT ... WITH VALUES sets every existing product
--           to Pallet, and a new product is Pallet unless the dialog says otherwise.
-- Run once against GbEvergreen. Idempotent. Additive only. The SET options are required
-- (sqlcmd defaults QUOTED_IDENTIFIER off, which the table's computed Gtin13 column rejects).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.Product', 'SoldBy') IS NULL
    ALTER TABLE dbo.Product ADD SoldBy varchar(20) NOT NULL
        CONSTRAINT DF_Product_SoldBy DEFAULT('Pallet') WITH VALUES
        CONSTRAINT CK_Product_SoldBy CHECK (SoldBy IN ('Pallet', 'CubicMeter'));
GO

ALTER VIEW dbo.vwProduct
AS
SELECT dbo.Product.id, dbo.Product.Description, dbo.ProductType.TypeDescription AS ProductType, dbo.Product.SkuCode, dbo.Product.Size, dbo.Product.Upp, dbo.Product.ProductTypeId, dbo.ReferenceData.DisplayName AS CategoryName,
                  dbo.Product.DateDeleted, dbo.Product.CategoryId, dbo.Product.PosId, ProductPos.Description AS PosDescription, CAST(CASE WHEN dbo.Product.PosId IS NULL OR
                  dbo.Product.PosId = 0 THEN 0 ELSE 1 END AS bit) AS HasPos, dbo.Product.Gtin, dbo.Product.CommodityCode, dbo.Product.IsBulkBag, dbo.Product.AverageWeight,
                  dbo.Product.PalletWeight, dbo.Product.BagWeightGrams, dbo.Product.SoldBy
FROM     dbo.Product LEFT OUTER JOIN
                  dbo.ProductType ON dbo.Product.ProductTypeId = dbo.ProductType.id LEFT OUTER JOIN
                  dbo.ReferenceData ON dbo.Product.CategoryId = dbo.ReferenceData.id LEFT OUTER JOIN
                  dbo.Product AS ProductPos ON dbo.Product.PosId = ProductPos.id
GROUP BY dbo.Product.id, dbo.Product.Description, dbo.ProductType.TypeDescription, dbo.Product.ProductTypeId, dbo.Product.DateDeleted, dbo.Product.SkuCode, dbo.Product.Size, dbo.Product.Upp, dbo.ReferenceData.DisplayName,
                  dbo.Product.PosId, dbo.Product.CategoryId, ProductPos.Description, dbo.Product.Gtin, dbo.Product.CommodityCode, dbo.Product.IsBulkBag, dbo.Product.AverageWeight,
                  dbo.Product.PalletWeight, dbo.Product.BagWeightGrams, dbo.Product.SoldBy
GO
