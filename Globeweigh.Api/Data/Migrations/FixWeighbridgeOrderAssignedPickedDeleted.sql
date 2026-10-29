-- vwWeighbridgeOrderAssigned counted DELETED picks in TotalQuantityPicked / TotalWeightPicked
-- — the same defect fixed in vwDispatchOrderItem (see FixDispatchOrderItemPickedDeleted.sql).
-- Unpicking a pallet soft-deletes the DispatchOrder_Picked row, but these two subqueries
-- summed every row regardless, so an unpicked pallet kept counting toward the load.
--
-- This view feeds the legacy WPF Weighbridge screens (Globeweigh.Admin WeighbridgeViewModel).
-- TotalQuantityOrdered needs no such test: DispatchOrderItem has no DateDeleted column.
-- Run once against GbEvergreen.
ALTER VIEW dbo.vwWeighbridgeOrderAssigned
AS
SELECT        dbo.WeighbridgeRecord_Order.id, dbo.WeighbridgeRecord_Order.Sequence, dbo.WeighbridgeRecord_Order.WeighbridgeRecordId, dbo.WeighbridgeRecord_Order.OrderId, dbo.DispatchOrder.DeliveryDate,
                         dbo.CustomerSuppliers.Name AS CustomerName, dbo.DispatchOrder.OrderNo, ISNULL
                             ((SELECT        SUM(Quantity) AS Expr1
                                 FROM            dbo.DispatchOrderItem
                                 WHERE        (OrderId = dbo.DispatchOrder.id)), 0) AS TotalQuantityOrdered, ISNULL
                             ((SELECT        SUM(Quantity) AS Expr1
                                 FROM            dbo.DispatchOrder_Picked
                                 WHERE        (DispatchOrderId = dbo.DispatchOrder.id) AND (DateDeleted IS NULL)), 0) AS TotalQuantityPicked, ISNULL
                             ((SELECT        SUM(Weight) AS Expr1
                                 FROM            dbo.DispatchOrder_Picked AS DispatchOrder_Picked_1
                                 WHERE        (DispatchOrderId = dbo.DispatchOrder.id) AND (DateDeleted IS NULL)), 0) AS TotalWeightPicked
FROM            dbo.WeighbridgeRecord_Order INNER JOIN
                         dbo.DispatchOrder ON dbo.WeighbridgeRecord_Order.OrderId = dbo.DispatchOrder.id INNER JOIN
                         dbo.CustomerSuppliers ON dbo.DispatchOrder.CustomerId = dbo.CustomerSuppliers.id
GO
