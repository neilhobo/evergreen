-- vwDispatchOrderItem counted DELETED picks in PickedQuantity / PickedWeight.
--
-- Unpicking a pallet soft-deletes the DispatchOrder_Picked row (DateDeleted), but this
-- view's LEFT JOIN had no DateDeleted test — alone among the DispatchOrder_Picked views
-- (vwDispatchOrderPicked, vwDispatchOrderItemPicked, vwDispatchOrder, vwWeighbridgePending
-- all filter it). So an unpicked pallet kept counting: load 4766 showed 23 pallets on the
-- Picked Pallets tab (which does filter) against a picked total of 24 on the order items.
--
-- The test belongs in the ON clause, not a WHERE — a WHERE would turn the LEFT JOIN into
-- an INNER one and drop every order line that has no picks at all.
--
-- Same fix on the DispatchOrder join, for the same reason: 22 soft-deleted orders are
-- still linked to a load, so their 91 item rows were listed under it — orders the Orders
-- grid above doesn't show, because vwDispatchOrder DOES filter DateDeleted. Gating the
-- join (not the rows) leaves the items visible to the WPF screens, which fetch by
-- OrderId; it only stops them claiming a WeighbridgeRecordId / OrderNo they no longer have.
--
-- Read by the API (Dispatch grids, View Load) AND the legacy WPF apps (Admin
-- FullDispatchOrder, Touch DispatchOrderDetail / ScanDispatchPicking), so this corrects
-- the picked totals everywhere at once. Run once against GbEvergreen.
ALTER VIEW dbo.vwDispatchOrderItem
AS
SELECT        dbo.DispatchOrderItem.id, dbo.DispatchOrderItem.OrderId, dbo.DispatchOrderItem.ProductId, dbo.Product.ProductTypeId, dbo.Product.Description AS ProductDescription, dbo.Product.Size, dbo.Product.Upp,
                         dbo.DispatchOrderItem.Quantity, dbo.Product.Upp * dbo.DispatchOrderItem.Quantity AS TotalUnits, SUM(dbo.DispatchOrder_Picked.Quantity) AS PickedQuantity, ISNULL(SUM(dbo.DispatchOrder_Picked.Weight), 0) AS PickedWeight,
                         dbo.Product.Gtin, CASE WHEN dbo. Product .ProductTypeId = 1 THEN CAST(dbo. Product .Size AS nvarchar) + 'L ' + CAST(dbo. Product .Upp AS nvarchar) + ' Units ' ELSE NULL END AS UnitDisplay,
                         CAST(CASE WHEN dbo. Product .ProductTypeId = 3 THEN 1 ELSE 0 END AS bit) AS IsPosProduct, dbo.DispatchOrder.WeighbridgeRecordId, dbo.DispatchOrder.OrderNo
FROM            dbo.DispatchOrderItem INNER JOIN
                         dbo.Product ON dbo.DispatchOrderItem.ProductId = dbo.Product.id LEFT OUTER JOIN
                         dbo.DispatchOrder_Picked ON dbo.DispatchOrderItem.OrderId = dbo.DispatchOrder_Picked.DispatchOrderId AND dbo.DispatchOrderItem.ProductId = dbo.DispatchOrder_Picked.ProductId
                             AND dbo.DispatchOrder_Picked.DateDeleted IS NULL LEFT OUTER JOIN
                         dbo.DispatchOrder ON dbo.DispatchOrderItem.OrderId = dbo.DispatchOrder.id AND dbo.DispatchOrder.DateDeleted IS NULL
GROUP BY dbo.DispatchOrderItem.id, dbo.DispatchOrderItem.OrderId, dbo.DispatchOrderItem.ProductId, dbo.Product.ProductTypeId, dbo.Product.Description, dbo.Product.Size, dbo.Product.Upp, dbo.DispatchOrderItem.Quantity,
                         dbo.Product.Gtin, dbo.DispatchOrder.WeighbridgeRecordId, dbo.DispatchOrder.OrderNo
GO
