-- Order line quantities can be fractional, for products sold by the cubic meter (order 26700:
-- two lines of 67.5 m³). DispatchOrderItem.Quantity and SalesOrderItem.Quantity were INT, so
-- saving 67.5 failed JSON binding in the API with a bare 400. Pallet products stay whole —
-- the API rejects a fraction on any line that is not sold by the cubic meter.
--
-- Only the LINE quantity becomes decimal. The order/load totals built from it
-- (TotalQuantityOrdered in vwDispatchOrder, vwOpenDispatchOrderUnnasigned,
-- vwWeighbridgeOrderAssigned) and vwDispatchOrderItem.TotalUnits stay INT, rounded up, so
-- their readers (load emails, the picking app's pending loads, the legacy Model) are untouched.
-- The views are otherwise unchanged from their current definitions.
-- Run once against GbEvergreen. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.DispatchOrderItem')
           AND name = 'Quantity' AND system_type_id = TYPE_ID('int'))
    ALTER TABLE dbo.DispatchOrderItem ALTER COLUMN Quantity DECIMAL(10,2) NULL;
GO
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.SalesOrderItem')
           AND name = 'Quantity' AND system_type_id = TYPE_ID('int'))
    ALTER TABLE dbo.SalesOrderItem ALTER COLUMN Quantity DECIMAL(10,2) NULL;
GO
ALTER VIEW dbo.vwDispatchOrderItem
AS
SELECT        dbo.DispatchOrderItem.id, dbo.DispatchOrderItem.OrderId, dbo.DispatchOrderItem.ProductId, dbo.Product.ProductTypeId, dbo.Product.Description AS ProductDescription, dbo.Product.Size, dbo.Product.Upp,
                         dbo.DispatchOrderItem.Quantity, CAST(CEILING(dbo.Product.Upp * dbo.DispatchOrderItem.Quantity) AS int) AS TotalUnits, SUM(dbo.DispatchOrder_Picked.Quantity) AS PickedQuantity, ISNULL(SUM(dbo.DispatchOrder_Picked.Weight), 0) AS PickedWeight,
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
ALTER VIEW dbo.vwDispatchOrder
AS
SELECT dbo.DispatchOrder.id, dbo.DispatchOrder.OrderNo, dbo.DispatchOrder.CustomerId, dbo.DispatchOrder.DeliveryDate, dbo.DispatchOrder.Comments, dbo.DispatchOrder.DateCreated, dbo.DispatchOrder.DateDispatched,
                  dbo.DispatchOrder.DateDeleted, CASE WHEN dbo.DispatchOrder.OrderTypeId = 1 THEN 'EVR' ELSE dbo.CustomerSuppliers.Code + RIGHT('000' + CAST(dbo.DispatchOrder.OrderNo AS VARCHAR(4)), 4) END AS CustomerRef,
                  CASE WHEN dbo.DispatchOrder.OrderTypeId = 1 THEN 'EVERGREEN' ELSE dbo.CustomerSuppliers.Name END AS CustomerName,
                  CASE WHEN dbo.DispatchOrder.OrderTypeId = 1 THEN dbo.StockLocation.LocationName ELSE dbo.DispatchOrder.AddressFull END AS AddressFull, CASE WHEN dbo.DispatchOrder.DateDispatched IS NULL
                  THEN 'Open' ELSE 'Dispatched' END AS OrderStatus, dbo.DispatchOrder.VehicleReg, dbo.CustomerSuppliers.Code, dbo.GlobeweighUser.FirstName + ' ' + dbo.GlobeweighUser.Surname AS CreatedBy,
                  dbo.DispatchOrder.DatePickingComplete, dbo.Haulier.Name AS HaulierName, dbo.DispatchOrder.PoNumber, dbo.DispatchOrder.TrailerNo, ISNULL(SUM(dbo.DispatchOrder_Picked.Quantity), 0) AS PickedQuantity,
                  ISNULL(SUM(dbo.DispatchOrder_Picked.Weight), 0) AS PickedWeight, dbo.DispatchOrder.OrderTypeId, dbo.DispatchOrder.TransferOrderNo, dbo.DispatchOrder.HaulierId, CAST(ISNULL
                      ((SELECT CEILING(SUM(Quantity)) AS Expr1
                        FROM      dbo.DispatchOrderItem
                        WHERE   (OrderId = dbo.DispatchOrder.id)), 0) AS int) AS TotalQuantityOrdered, dbo.DispatchOrder.WeighbridgeRecordId, CAST(ISNULL(CASE WHEN dbo.DispatchOrder.WeighbridgeRecordId > 0 THEN 1 ELSE 0 END, 0) AS bit) AS Assigned,
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
ALTER VIEW dbo.vwOpenDispatchOrderUnnasigned
AS
SELECT dbo.DispatchOrder.id, CASE WHEN dbo.DispatchOrder.OrderTypeId = 0 THEN CAST(dbo.DispatchOrder.OrderNo AS nvarchar) ELSE 'T-' + CAST(dbo.DispatchOrder.TransferOrderNo AS nvarchar) END AS OrderNoDisplay,
                  dbo.DispatchOrder.CustomerId, dbo.DispatchOrder.DeliveryDate, CASE WHEN dbo.DispatchOrder.OrderTypeId = 1 THEN 'EVR' ELSE dbo.CustomerSuppliers.Code + RIGHT('000' + CAST(dbo.DispatchOrder.OrderNo AS VARCHAR(4)), 4)
                  END AS CustomerRef, CASE WHEN dbo.DispatchOrder.OrderTypeId = 1 THEN 'EVERGREEN' ELSE dbo.CustomerSuppliers.Name END AS CustomerName, dbo.Haulier.Name AS HaulierName, COUNT(dbo.WeighbridgeRecord_Order.id)
                  AS AssignedOrderCount, dbo.DispatchOrder.OrderTypeId, dbo.DispatchOrder.HaulierId, CAST(ISNULL
                      ((SELECT CEILING(SUM(Quantity)) AS Expr1
                        FROM      dbo.DispatchOrderItem
                        WHERE   (OrderId = dbo.DispatchOrder.id)), 0) AS int) AS TotalQuantityOrdered
FROM     dbo.DispatchOrder LEFT OUTER JOIN
                  dbo.CustomerSuppliers ON dbo.DispatchOrder.CustomerId = dbo.CustomerSuppliers.id LEFT OUTER JOIN
                  dbo.Haulier ON dbo.DispatchOrder.HaulierId = dbo.Haulier.id LEFT OUTER JOIN
                  dbo.WeighbridgeRecord_Order ON dbo.DispatchOrder.id = dbo.WeighbridgeRecord_Order.OrderId
WHERE  (dbo.DispatchOrder.DateDeleted IS NULL) AND (dbo.DispatchOrder.DateDispatched IS NULL)
GROUP BY dbo.DispatchOrder.id, dbo.DispatchOrder.OrderNo, dbo.DispatchOrder.CustomerId, dbo.DispatchOrder.DeliveryDate, dbo.DispatchOrder.OrderTypeId, dbo.CustomerSuppliers.Code, dbo.CustomerSuppliers.Name, dbo.Haulier.Name,
                  dbo.DispatchOrder.TransferOrderNo, dbo.DispatchOrder.HaulierId
GO
ALTER VIEW dbo.vwWeighbridgeOrderAssigned
AS
SELECT        dbo.WeighbridgeRecord_Order.id, dbo.WeighbridgeRecord_Order.Sequence, dbo.WeighbridgeRecord_Order.WeighbridgeRecordId, dbo.WeighbridgeRecord_Order.OrderId, dbo.DispatchOrder.DeliveryDate,
                         dbo.CustomerSuppliers.Name AS CustomerName, dbo.DispatchOrder.OrderNo, CAST(ISNULL
                             ((SELECT        CEILING(SUM(Quantity)) AS Expr1
                                 FROM            dbo.DispatchOrderItem
                                 WHERE        (OrderId = dbo.DispatchOrder.id)), 0) AS int) AS TotalQuantityOrdered, ISNULL
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
