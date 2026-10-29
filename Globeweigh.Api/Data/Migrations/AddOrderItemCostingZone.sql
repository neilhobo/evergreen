-- The costing zone an order line was priced from, when it is NOT the customer's own zone: a
-- sales rep building an order in My Orders can press Change beside Costing Zone in Add Order
-- Item and price that one line from another zone. Only that line — the customer's zone is
-- left alone. NULL = priced from the customer's zone, as every line was before.
-- Carried from SalesOrderItem to DispatchOrderItem when the office approves the order
-- (SalesOrdersController.Approve, which also writes the rep's audit row for it).
-- Run once against GbEvergreen, after AddSalesOrderTables.sql. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.DispatchOrderItem', 'CostingZoneId') IS NULL
    ALTER TABLE dbo.DispatchOrderItem ADD CostingZoneId INT NULL;
GO
-- Guarded: the runner may reach this script before AddSalesOrderTables.sql (alphabetical).
IF OBJECT_ID('dbo.SalesOrderItem', 'U') IS NOT NULL AND COL_LENGTH('dbo.SalesOrderItem', 'CostingZoneId') IS NULL
    ALTER TABLE dbo.SalesOrderItem ADD CostingZoneId INT NULL;
GO
