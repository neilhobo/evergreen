-- Adds picking comments to dbo.DispatchOrder — instructions for the pickers, kept
-- separate from Comments (which prints on the delivery note).
-- Run once against GbEvergreen. Idempotent.
IF COL_LENGTH('dbo.DispatchOrder', 'PickingComments') IS NULL
    ALTER TABLE dbo.DispatchOrder ADD PickingComments nvarchar(MAX) NULL;
GO
