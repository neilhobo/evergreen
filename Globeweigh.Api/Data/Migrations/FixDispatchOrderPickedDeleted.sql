-- vwDispatchOrder counted DELETED picks in PickedQuantity / PickedWeight.
--
-- Unpicking a pallet soft-deletes the DispatchOrder_Picked row (DateDeleted), but this
-- view's LEFT JOIN on DispatchOrder_Picked had no DateDeleted test, so an unpicked pallet
-- kept counting in the order header: order 163 (2026-09-14) showed Picked 14 against 12
-- ordered — 14 scans, 2 of them unpicked, 12 live. The per-item view (fixed on 03 Sep by
-- FixDispatchOrderItemPickedDeleted.sql, whose note wrongly assumed this view already
-- filtered picks — it filters deleted ORDERS, not deleted picks) added up to 12.
-- Affects the Completed Orders "Picked" column, the order's picked weight, and the
-- Pending load's picking-progress bar, which all read this view. 12 dispatched orders on
-- 2026-09-14 had an unpick; none open.
--
-- The test belongs in the ON clause, not a WHERE — a WHERE would turn the LEFT JOIN into
-- an INNER one and drop every order that has no picks at all.
-- No data correction needed: the stored picks are right. Run once against GbEvergreen.
ALTER VIEW dbo.vwDispatchOrder
AS
SELECT dbo.DispatchOrder.id, dbo.DispatchOrder.OrderNo, dbo.DispatchOrder.CustomerId, dbo.DispatchOrder.DeliveryDate, dbo.DispatchOrder.Comments, dbo.DispatchOrder.DateCreated, dbo.DispatchOrder.DateDispatched,
                  dbo.DispatchOrder.DateDeleted, CASE WHEN dbo.DispatchOrder.OrderTypeId = 1 THEN 'EVR' ELSE dbo.CustomerSuppliers.Code + RIGHT('000' + CAST(dbo.DispatchOrder.OrderNo AS VARCHAR(4)), 4) END AS CustomerRef,
                  CASE WHEN dbo.DispatchOrder.OrderTypeId = 1 THEN 'EVERGREEN' ELSE dbo.CustomerSuppliers.Name END AS CustomerName,
                  CASE WHEN dbo.DispatchOrder.OrderTypeId = 1 THEN dbo.StockLocation.LocationName ELSE dbo.DispatchOrder.AddressFull END AS AddressFull, CASE WHEN dbo.DispatchOrder.DateDispatched IS NULL
                  THEN 'Open' ELSE 'Dispatched' END AS OrderStatus, dbo.DispatchOrder.VehicleReg, dbo.CustomerSuppliers.Code, dbo.GlobeweighUser.FirstName + ' ' + dbo.GlobeweighUser.Surname AS CreatedBy,
                  dbo.DispatchOrder.DatePickingComplete, dbo.Haulier.Name AS HaulierName, dbo.DispatchOrder.PoNumber, dbo.DispatchOrder.TrailerNo, ISNULL(SUM(dbo.DispatchOrder_Picked.Quantity), 0) AS PickedQuantity,
                  ISNULL(SUM(dbo.DispatchOrder_Picked.Weight), 0) AS PickedWeight, dbo.DispatchOrder.OrderTypeId, dbo.DispatchOrder.TransferOrderNo, dbo.DispatchOrder.HaulierId, ISNULL
                      ((SELECT SUM(Quantity) AS Expr1
                        FROM      dbo.DispatchOrderItem
                        WHERE   (OrderId = dbo.DispatchOrder.id)), 0) AS TotalQuantityOrdered, dbo.DispatchOrder.WeighbridgeRecordId, CAST(ISNULL(CASE WHEN dbo.DispatchOrder.WeighbridgeRecordId > 0 THEN 1 ELSE 0 END, 0) AS bit) AS Assigned,
                  dbo.DispatchOrder.SortOrder, CHAR(64 + dbo.DispatchOrder.SortOrder) AS SortOrderLetter
FROM     dbo.DispatchOrder LEFT OUTER JOIN
                  dbo.CustomerSuppliers ON dbo.DispatchOrder.CustomerId = dbo.CustomerSuppliers.id LEFT OUTER JOIN
                  dbo.Haulier ON dbo.DispatchOrder.HaulierId = dbo.Haulier.id LEFT OUTER JOIN
                  dbo.GlobeweighUser ON dbo.DispatchOrder.CreatedById = dbo.GlobeweighUser.id LEFT OUTER JOIN
                  dbo.DispatchOrder_Picked ON dbo.DispatchOrder.id = dbo.DispatchOrder_Picked.DispatchOrderId
                      AND dbo.DispatchOrder_Picked.DateDeleted IS NULL LEFT OUTER JOIN
                  dbo.StockLocation ON dbo.DispatchOrder.TransferLocationId = dbo.StockLocation.id
WHERE  (dbo.DispatchOrder.DateDeleted IS NULL)
GROUP BY dbo.DispatchOrder.id, dbo.DispatchOrder.OrderNo, dbo.DispatchOrder.CustomerId, dbo.DispatchOrder.DeliveryDate, dbo.DispatchOrder.Comments, dbo.DispatchOrder.DateCreated, dbo.DispatchOrder.DateDispatched,
                  dbo.DispatchOrder.DateDeleted, dbo.CustomerSuppliers.Name, dbo.DispatchOrder.AddressFull, dbo.DispatchOrder.VehicleReg, dbo.CustomerSuppliers.Code, dbo.GlobeweighUser.FirstName, dbo.GlobeweighUser.Surname,
                  dbo.DispatchOrder.DatePickingComplete, dbo.Haulier.Name, dbo.DispatchOrder.PoNumber, dbo.DispatchOrder.TrailerNo, dbo.DispatchOrder.OrderTypeId, dbo.DispatchOrder.TransferOrderNo, dbo.StockLocation.LocationName,
                  dbo.DispatchOrder.HaulierId, dbo.DispatchOrder.WeighbridgeRecordId, dbo.DispatchOrder.SortOrder
GO
