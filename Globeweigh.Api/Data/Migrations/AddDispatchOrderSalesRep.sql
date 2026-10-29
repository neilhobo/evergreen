-- The sales rep an order is credited to: a GlobeweighUser with the Sales role (eUserRole 6).
-- An order approved from My Orders takes the rep who sent it, fixed; any other order may be
-- given one from the Sales Rep dropdown in the Add/Edit Order dialog. NULL = no rep.
-- Read and written by DispatchController (InsertOrder / UpdateOrder / edit-items) and
-- SalesOrdersController.Approve; vwDispatchOrder is left as it is.
-- Run once against GbEvergreen, after AddSalesOrderTables.sql. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.DispatchOrder', 'SalesRepId') IS NULL
    ALTER TABLE dbo.DispatchOrder ADD SalesRepId INT NULL;
GO
-- Orders already approved from sales take their rep. Guarded: the runner may reach this
-- script before AddSalesOrderTables.sql (alphabetical), when there is nothing to backfill.
IF OBJECT_ID('dbo.SalesOrder', 'U') IS NOT NULL
UPDATE d SET SalesRepId = s.SalesUserId
FROM dbo.DispatchOrder d
JOIN dbo.SalesOrder s ON s.DispatchOrderId = d.id
WHERE d.SalesRepId IS NULL;
GO
