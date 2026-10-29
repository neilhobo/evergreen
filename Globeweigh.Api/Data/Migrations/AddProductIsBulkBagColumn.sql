-- Adds the "Bulk Bag" flag to dbo.Product (Finished Products add/edit checkbox in
-- TauriAdmin) and exposes it through vwProduct for the admin grid read-model.
-- Run once against GbEvergreen. The SET options are required (sqlcmd defaults
-- QUOTED_IDENTIFIER off, which the table's computed Gtin13 column rejects).
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.Product', 'IsBulkBag') IS NULL
    ALTER TABLE dbo.Product ADD IsBulkBag bit NOT NULL CONSTRAINT DF_Product_IsBulkBag DEFAULT(0);
GO

ALTER VIEW dbo.vwProduct
AS
SELECT dbo.Product.id, dbo.Product.Description, dbo.ProductType.TypeDescription AS ProductType, dbo.Product.SkuCode, dbo.Product.Size, dbo.Product.Upp, dbo.Product.ProductTypeId, dbo.ReferenceData.DisplayName AS CategoryName,
                  dbo.Product.DateDeleted, dbo.Product.CategoryId, dbo.Product.PosId, ProductPos.Description AS PosDescription, CAST(CASE WHEN dbo.Product.PosId IS NULL OR
                  dbo.Product.PosId = 0 THEN 0 ELSE 1 END AS bit) AS HasPos, dbo.Product.Gtin, dbo.Product.CommodityCode, dbo.Product.IsBulkBag
FROM     dbo.Product LEFT OUTER JOIN
                  dbo.ProductType ON dbo.Product.ProductTypeId = dbo.ProductType.id LEFT OUTER JOIN
                  dbo.ReferenceData ON dbo.Product.CategoryId = dbo.ReferenceData.id LEFT OUTER JOIN
                  dbo.Product AS ProductPos ON dbo.Product.PosId = ProductPos.id
GROUP BY dbo.Product.id, dbo.Product.Description, dbo.ProductType.TypeDescription, dbo.Product.ProductTypeId, dbo.Product.DateDeleted, dbo.Product.SkuCode, dbo.Product.Size, dbo.Product.Upp, dbo.ReferenceData.DisplayName,
                  dbo.Product.PosId, dbo.Product.CategoryId, ProductPos.Description, dbo.Product.Gtin, dbo.Product.CommodityCode, dbo.Product.IsBulkBag
GO
