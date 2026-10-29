-- @manual - one-off data backfill, not part of the schema line a deploy applies.
-- Backfills DispatchOrder.AddressFull (CRLF-joined address lines) for orders created
-- by TauriAdmin before the API started denormalising the chosen address onto the
-- order (legacy WPF always wrote it; vwDispatchOrder and the grids read it).
-- Run once against GbEvergreen. Idempotent (only touches NULL AddressFull rows).
UPDATE o
SET AddressFull = STUFF(
      ISNULL(NCHAR(13) + NCHAR(10) + NULLIF(LTRIM(RTRIM(a.Address1)), ''), '')
    + ISNULL(NCHAR(13) + NCHAR(10) + NULLIF(LTRIM(RTRIM(a.Address2)), ''), '')
    + ISNULL(NCHAR(13) + NCHAR(10) + NULLIF(LTRIM(RTRIM(a.Address3)), ''), '')
    + ISNULL(NCHAR(13) + NCHAR(10) + NULLIF(LTRIM(RTRIM(a.Address4)), ''), '')
    + ISNULL(NCHAR(13) + NCHAR(10) + NULLIF(LTRIM(RTRIM(a.Address5)), ''), '')
    , 1, 2, '')
FROM dbo.DispatchOrder o
INNER JOIN dbo.CustomerSupplier_Address a ON a.id = o.AddressId
WHERE o.AddressFull IS NULL AND o.AddressId > 0;
GO
