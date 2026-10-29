-- The sales rep a customer belongs to: a GlobeweighUser with the Sales role (eUserRole 6),
-- picked in the Sales Rep column of the Customers grid. NULL = no rep. Read and written by
-- CustomersSuppliersController (List / PUT {id}/sales-rep); the vwCustomerSupplier view is
-- left as it is. Run once against GbEvergreen. Idempotent.
SET QUOTED_IDENTIFIER ON;
GO
IF COL_LENGTH('dbo.CustomerSuppliers', 'SalesRepId') IS NULL
    ALTER TABLE dbo.CustomerSuppliers ADD SalesRepId INT NULL;
GO
