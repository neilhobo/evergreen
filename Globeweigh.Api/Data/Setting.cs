using System;
using System.Collections.Generic;

namespace Globeweigh.Api.Data;

public partial class Setting
{
    public int Id { get; set; }

    public string? UpdateUrl { get; set; }

    public int PalletLabelPrintCount { get; set; }

    public string? InvoiceHeaderText { get; set; }

    public string? GateRelayIpAddress { get; set; }

    public string? WeighbridgeMoxaIpAddress { get; set; }

    public int GateRelayPortNo { get; set; }

    public int WeighbridgeMoxaPortNo { get; set; }

    public bool GateEnabled { get; set; }

    public int MaxLorryLoad { get; set; }

    public int DefaultPalletWeight { get; set; }

    // Standard bulk density (kg/m³) — converts a manual pallet's weight to sold volume:
    // Volume (m³) = Weight (kg) / BulkDensity. See Migrations/AddSettingBulkDensity.sql.
    public decimal BulkDensity { get; set; }

    // Auto-printed delivery notes (see Entities/PrintJob.cs). Kept in Setting so the copy
    // count and the target queue can change without a rebuild or a redeploy.
    // See Migrations/AddPrintJobTable.sql.
    public int DeliveryNoteCopies { get; set; }
    public string? DeliveryNotePrinter { get; set; }

    // The next order number to issue, continuing the series from the legacy system they
    // run alongside this one, and whether to issue it at all. Both are set in Admin >
    // Settings; until Enabled is ticked, order creation stays on MAX(OrderNo)+1, so the
    // number can be staged ahead of the hand-over and switched on on the day.
    // See Migrations/AddSettingNextOrderNo.sql and DispatchController.AllocateOrderNo.
    public int? NextOrderNo { get; set; }
    public bool NextOrderNoEnabled { get; set; }

    // When NextOrderNoEnabled was last switched on (null while off / never on). Order-number
    // editing keys off it - an open order created before this instant is a pre-hand-over
    // ad-hoc number that may be corrected. See DispatchController.IsOrderNoEditable and
    // Migrations/AddSettingNextOrderNoEnabledDate.sql.
    public DateTime? NextOrderNoEnabledDate { get; set; }

    // Outbound email - the SMTP account automatic emails go out through, set up in
    // Super Admin > Email Setup. SmtpSecurity is an EmailSender.SmtpSecurity name.
    // SmtpPassword is stored DPAPI-protected (Auth/DpapiSecret.cs), never in the clear.
    // See Migrations/AddSettingEmailSmtp.sql and Services/EmailSender.cs.
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpSecurity { get; set; }
    public string? SmtpUsername { get; set; }
    public string? SmtpPassword { get; set; }
    public string? EmailFromAddress { get; set; }
    public string? EmailFromName { get; set; }
}
