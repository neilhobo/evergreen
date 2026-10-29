-- Orders entered before orders had a Sales Rep (the 2026-10-13 11:29 deploy that added
-- DispatchOrder.SalesRepId, AddDispatchOrderSalesRep.sql) take their customer's CURRENT rep
-- (Customers grid, Sales Rep), so a rep sees their customers' past orders in My Orders.
-- Orders entered after that are left alone: there a blank rep may be someone's choice of
-- "— None —". Customers with no rep leave their orders blank. Bypasses the audit trail.
-- Expected on prod (preview 2026-10-16): 208 of 306 older orders filled, 88 left blank.
-- Run once against GbEvergreen, after AddDispatchOrderSalesRep.sql. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
UPDATE d SET SalesRepId = c.SalesRepId
FROM dbo.DispatchOrder d
JOIN dbo.CustomerSuppliers c ON c.id = d.CustomerId
WHERE d.SalesRepId IS NULL
  AND c.SalesRepId IS NOT NULL
  AND d.DateCreated < '2026-10-13T11:29:40';
GO
