-- Exposes DispatchOrderItem.Density on vwDispatchOrderItem, so TauriAdmin's order and load
-- panels can show the density the picker entered when marking a cubic meter line picked.
-- Otherwise the view is unchanged from AddDispatchOrderItemSoldBy.sql.
-- Needs AddDispatchOrderItemDensity.sql first. Run once against GbEvergreen. Idempotent (ALTER VIEW).
ALTER VIEW dbo.vwDispatchOrderItem
AS
SELECT        dbo.DispatchOrderItem.id, dbo.DispatchOrderItem.OrderId, dbo.DispatchOrderItem.ProductId, dbo.Product.ProductTypeId, dbo.Product.Description AS ProductDescription, dbo.Product.Size, dbo.Product.Upp,
                         dbo.DispatchOrderItem.Quantity, dbo.Product.Upp * dbo.DispatchOrderItem.Quantity AS TotalUnits, SUM(dbo.DispatchOrder_Picked.Quantity) AS PickedQuantity, ISNULL(SUM(dbo.DispatchOrder_Picked.Weight), 0) AS PickedWeight,
                         dbo.Product.Gtin, CASE WHEN dbo. Product .ProductTypeId = 1 THEN CAST(dbo. Product .Size AS nvarchar) + 'L ' + CAST(dbo. Product .Upp AS nvarchar) + ' Units ' ELSE NULL END AS UnitDisplay,
                         CAST(CASE WHEN dbo. Product .ProductTypeId = 3 THEN 1 ELSE 0 END AS bit) AS IsPosProduct, dbo.DispatchOrder.WeighbridgeRecordId, dbo.DispatchOrder.OrderNo,
                         dbo.Product.SoldBy, dbo.DispatchOrderItem.Density
FROM            dbo.DispatchOrderItem INNER JOIN
                         dbo.Product ON dbo.DispatchOrderItem.ProductId = dbo.Product.id LEFT OUTER JOIN
                         dbo.DispatchOrder_Picked ON dbo.DispatchOrderItem.OrderId = dbo.DispatchOrder_Picked.DispatchOrderId AND dbo.DispatchOrderItem.ProductId = dbo.DispatchOrder_Picked.ProductId
                             AND dbo.DispatchOrder_Picked.DateDeleted IS NULL LEFT OUTER JOIN
                         dbo.DispatchOrder ON dbo.DispatchOrderItem.OrderId = dbo.DispatchOrder.id AND dbo.DispatchOrder.DateDeleted IS NULL
GROUP BY dbo.DispatchOrderItem.id, dbo.DispatchOrderItem.OrderId, dbo.DispatchOrderItem.ProductId, dbo.Product.ProductTypeId, dbo.Product.Description, dbo.Product.Size, dbo.Product.Upp, dbo.DispatchOrderItem.Quantity,
                         dbo.Product.Gtin, dbo.DispatchOrder.WeighbridgeRecordId, dbo.DispatchOrder.OrderNo, dbo.Product.SoldBy, dbo.DispatchOrderItem.Density
GO
