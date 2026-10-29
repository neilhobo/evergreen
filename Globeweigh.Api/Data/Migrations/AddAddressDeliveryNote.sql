-- Delivery Note Info (TauriAdmin Customers > Edit Customer > Delivery Note Info tab):
-- one block of free text per delivery address ("MUST BOOK IN", booking-in phone numbers,
-- delivery number, delivery date...) that the office reuses on most orders to that
-- address. Shown read-only under the Addresses list on the Customers page.
-- Nullable, additive — legacy WPF unaffected. Run once against GbEvergreen (idempotent).
IF COL_LENGTH('dbo.CustomerSupplier_Address', 'DeliveryNote') IS NULL
    ALTER TABLE dbo.CustomerSupplier_Address ADD DeliveryNote nvarchar(max) NULL;
GO
