using System.Net;
using System.Text;
using Globeweigh.Api.Data.Entities;
using Globeweigh.Api.Data.ReadModels;

namespace Globeweigh.Api.Services;

// A file that goes with the message, rendered to PDF when it is sent: an order's delivery note
// (Kind "note"), or on the commercial copy of the dispatch confirmation, its commercial
// invoice (Kind "invoice").
public record LoadEmailAttachment(int OrderId, int OrderNo, string FileName, string Kind = LoadEmailAttachment.Note)
{
    public const string Note = "note";
    public const string Invoice = "invoice";
    // The pre-commercial invoice: ordered pallets at average weights (before picking).
    public const string PreInvoice = "preinvoice";
    // The order confirmation: the delivery note titled so, its lines as ordered.
    public const string Confirmation = "confirmation";
}

// A composed message, as the Send Email dialog previews it and as it goes out.
public sealed record LoadEmail(
    string Ref, string HaulierName, List<string> Recipients, string Subject, string Html, string Text,
    List<LoadEmailAttachment> Attachments);

// What both messages are built from: the load, its 3-digit ref, its haulier's name,
// telephone and addresses, its orders in drop order, the orders' delivery-note comments, and the files that
// go with it (the dispatch confirmation carries each order's delivery note; the other, none).
// CustomerName is set only on a customer's copy (LoadEmailComposer.ForCustomer), whose orders,
// attachments and recipients are that customer's alone.
public sealed record LoadMailData(
    WeighbridgeRecord Rec, string Reference, string HaulierName, List<string> Recipients,
    List<DispatchOrderRow> Orders, Dictionary<int, string?> NoteById, List<LoadEmailAttachment> Attachments,
    string? CustomerName = null, string HaulierTelephone = "");

// The two emails to the haulier, each composed in one place so the Send Email dialog's preview
// is exactly what goes out:
//  - "ready for collection", from the Loads grid. The 3-digit ref is the key line: it is what
//    the driver keys into the weighbridge app on arrival to book in, be weighed and be sent
//    round to be loaded. The orders and their delivery addresses follow, in drop order.
//  - the dispatch confirmation, from the Completed Loads grid once the load has weighed out: it
//    has been loaded and has left, with what is on board.
// The prose in each — subject, greeting, intro, footer, and so on — is the WORDING passed in,
// keyed by AutomaticEmails field (the defaults, or what Admin > Automatic Emails has changed
// them to), with its {Placeholders} filled from the load. The layout, the panel and the
// deliveries table are fixed here.
public static class LoadEmailComposer
{
    public const string Company = "Evergreen Horticulture";

    public static LoadEmail Compose(string kind, LoadMailData d, IReadOnlyDictionary<string, string> wording) =>
        string.Equals(kind, AutomaticEmails.DispatchedCustomer, StringComparison.OrdinalIgnoreCase)
            ? ComposeDispatched(d, wording, customer: true)
            : string.Equals(kind, AutomaticEmails.Dispatched, StringComparison.OrdinalIgnoreCase)
                ? ComposeDispatched(d, wording, customer: false)
                : string.Equals(kind, AutomaticEmails.PreCommercial, StringComparison.OrdinalIgnoreCase)
                    ? ComposePreCommercial(d, wording)
                    : string.Equals(kind, AutomaticEmails.OrderConfirmation, StringComparison.OrdinalIgnoreCase)
                        ? ComposeConfirmation(d, wording)
                        : ComposeReady(d, wording);

    // One customer's copy of the load: only their orders, only those orders' delivery notes,
    // and their addresses as the recipients.
    public static LoadMailData ForCustomer(LoadMailData d, int customerId, string customerName, List<string> recipients)
    {
        var orders = d.Orders.Where(o => o.CustomerId == customerId).ToList();
        var ids = orders.Select(o => o.Id).ToHashSet();
        return d with
        {
            Orders = orders,
            Attachments = d.Attachments.Where(a => ids.Contains(a.OrderId)).ToList(),
            Recipients = recipients,
            CustomerName = customerName,
        };
    }

    // A made-up load for Admin > Automatic Emails to preview the wording on: two drops, one with
    // a delivery note, one short a pallet, so every part of the layout is in the picture.
    // The customer's copy is the second drop's customer: the one with the note and the short pallet.
    // The order confirmation is that customer's order alone, not yet on a load.
    public static LoadMailData Sample(string kind)
    {
        if (string.Equals(kind, AutomaticEmails.OrderConfirmation, StringComparison.OrdinalIgnoreCase))
        {
            var ready = Sample(AutomaticEmails.Ready);
            var order = ready.Orders.Single(o => o.Id == 2);
            order.DeliveryDate = ready.Rec.CollectionDate;
            order.PoNumber = "PO-7731";
            return ForCustomer(ready, 2, "Hillside Nurseries", new List<string>()) with
            {
                Rec = new WeighbridgeRecord(),
                Reference = order.OrderNo.ToString(),
                Attachments = new List<LoadEmailAttachment>
                {
                    new(order.Id, order.OrderNo, $"Order Confirmation {order.OrderNo}.pdf", LoadEmailAttachment.Confirmation),
                },
            };
        }
        var customer = string.Equals(kind, AutomaticEmails.DispatchedCustomer, StringComparison.OrdinalIgnoreCase);
        var dispatched = customer || string.Equals(kind, AutomaticEmails.Dispatched, StringComparison.OrdinalIgnoreCase);
        var now = DateTime.Now;
        // The coming Monday, as a collection date would be.
        var monday = now.Date.AddDays(((int)DayOfWeek.Monday - (int)now.DayOfWeek + 7) % 7);
        if (monday == now.Date) monday = monday.AddDays(7);
        var rec = new WeighbridgeRecord
        {
            Id = 123,
            IsOutgoing = true,
            HaulierName = "Sample Haulage",
            TrailerNo = "TR 42",
            CollectionDate = monday,
            DateWeightIn = dispatched ? now.AddMinutes(-95) : null,
            DateWeightOut = dispatched ? now : null,
            DateCreated = now.AddDays(-1),
        };
        var orders = new List<DispatchOrderRow>
        {
            new()
            {
                Id = 1, OrderNo = 48120, CustomerId = 1, CustomerName = "Greenfields Garden Centre",
                AddressFull = "Unit 4, Mill Lane\r\nBallymena\r\nBT42 1AB",
                TotalQuantityOrdered = 12, PickedQuantity = 12, SortOrder = 1, WeighbridgeRecordId = 123,
            },
            new()
            {
                Id = 2, OrderNo = 48121, CustomerId = 2, CustomerName = "Hillside Nurseries",
                AddressFull = "The Old Farm\r\nMoira Road\r\nLisburn\r\nBT28 2XY",
                TotalQuantityOrdered = 14, PickedQuantity = 13, SortOrder = 2, WeighbridgeRecordId = 123,
            },
        };
        var notes = new Dictionary<int, string?> { [2] = "Ring on arrival - forklift needed" };
        var attachments = dispatched
            ? orders.Select(o => new LoadEmailAttachment(o.Id, o.OrderNo, $"Delivery Note {o.OrderNo}.pdf")).ToList()
            : string.Equals(kind, AutomaticEmails.PreCommercial, StringComparison.OrdinalIgnoreCase)
                ? orders.Select(o => new LoadEmailAttachment(o.Id, o.OrderNo,
                    $"Pre-Commercial Invoice {rec.Id}-{o.OrderNo}.pdf", LoadEmailAttachment.PreInvoice)).ToList()
                : new List<LoadEmailAttachment>();
        var load = new LoadMailData(rec, "123", rec.HaulierName!, new List<string>(), orders, notes, attachments,
            HaulierTelephone: "028 9012 3456");
        return customer ? ForCustomer(load, 2, "Hillside Nurseries", new List<string>()) : load;
    }

    // ── The pieces the two messages share ────────────────────────────────────────────────
    private const string MailCell = "padding:6px 8px;border-bottom:1px solid #eef2f7;vertical-align:top;";
    private const string MailCellOpen = "padding:6px 8px;vertical-align:top;";
    private const string MailLabel = "color:#64748b;padding:0 18px 0 0;";
    private const string MailMuted = "color:#64748b;";
    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
    // A typed piece of wording, for HTML: encoded, its line breaks kept.
    private static string HLines(string s) =>
        string.Join("<br>", s.Split('\n').Select(l => H(l.TrimEnd('\r'))));
    private static string[] Lines(string? s) => (s ?? "")
        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
        .Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
    private static string Plural(int n, string word) => n == 1 ? word : word + "s";
    private static string Count(int n, string word) => $"{n} {Plural(n, word)}";

    // The placeholders' values for this load. The pallets figure is the one the two messages
    // differ on — ordered before collection, loaded after — so each passes its own.
    private static Dictionary<string, string> Values(LoadMailData d, string pallets, string? weekCommencing, string? weighedOut) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Haulier"] = d.HaulierName,
            ["Telephone"] = d.HaulierTelephone,
            ["Ref"] = d.Reference,
            ["LoadNo"] = $"#{d.Rec.Id}",
            ["Company"] = Company,
            ["Orders"] = Count(d.Orders.Count, "order"),
            ["Pallets"] = pallets,
            ["Trailer"] = d.Rec.TrailerNo?.Trim() ?? "",
            ["WeekCommencing"] = weekCommencing ?? "",
            ["WeighedOut"] = weighedOut ?? "",
            ["Customer"] = d.CustomerName ?? "",
            ["OrderNumbers"] = string.Join(", ", d.Orders.Select(o => o.OrderNo)),
        };

    // Each piece of wording with its placeholders filled, trimmed; a piece that is blank (or
    // missing from the wording) comes back empty and is left out of the message.
    private sealed class Pieces
    {
        private readonly IReadOnlyDictionary<string, string> _wording;
        private readonly IReadOnlyDictionary<string, string> _values;
        public Pieces(IReadOnlyDictionary<string, string> wording, IReadOnlyDictionary<string, string> values)
        {
            _wording = wording;
            _values = values;
        }
        public string this[string field] =>
            AutomaticEmails.Fill(_wording.TryGetValue(field, out var t) ? t : null, _values).Trim();
        // The subject, one line whatever was typed, and never blank: with nothing usable, the
        // default wording stands in.
        public string Subject(string kind)
        {
            var s = this[AutomaticEmails.Subject];
            if (string.IsNullOrWhiteSpace(s))
                s = AutomaticEmails.Fill(AutomaticEmails.Find(kind)?.FieldFor(AutomaticEmails.Subject)?.Default, _values);
            return string.Join(" ", Lines(s)).Trim();
        }
    }

    private static void MailOpen(StringBuilder html, string greeting)
    {
        html.Append("<!doctype html><html><body style=\"margin:0;padding:0;background:#f3f5f8;font-family:'Segoe UI',Arial,sans-serif;color:#0f172a;\">");
        html.Append("<div style=\"max-width:660px;margin:0 auto;padding:24px 16px;\">");
        html.Append("<div style=\"background:#ffffff;border:1px solid #e5e7eb;border-radius:6px;padding:22px 24px;\">");
        if (greeting.Length > 0)
            html.Append($"<p style=\"margin:0 0 12px;font-size:14px;\">{HLines(greeting)}</p>");
    }

    private static void MailIntro(StringBuilder html, string intro)
    {
        if (intro.Length > 0)
            html.Append($"<p style=\"margin:0 0 18px;font-size:14px;line-height:1.5;\">{HLines(intro)}</p>");
    }

    private static void MailClose(StringBuilder html, string footer)
    {
        if (footer.Length > 0)
            html.Append($"<p style=\"margin:20px 0 0;font-size:12px;line-height:1.5;color:#64748b;\">{HLines(footer)}</p>");
        html.Append("</div></div></body></html>");
    }

    // The green panel both messages headline with: a small label, a big line, a note under it.
    private static void MailPanel(StringBuilder html, string label, string big, string bigStyle, string note)
    {
        html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;border-collapse:separate;margin:0 0 20px;\"><tr>");
        html.Append("<td style=\"background:#e4f4e9;border:1px solid #bfe3cb;border-radius:6px;padding:14px 18px;\">");
        if (label.Length > 0)
            html.Append($"<div style=\"font-size:11px;font-weight:600;letter-spacing:.5px;text-transform:uppercase;color:#1f7a3d;\">{H(label)}</div>");
        if (big.Length > 0)
            html.Append($"<div style=\"{bigStyle}color:#0f172a;\">{H(big)}</div>");
        if (note.Length > 0)
            html.Append($"<div style=\"font-size:12.5px;line-height:1.5;color:#1f7a3d;\">{HLines(note)}</div>");
        html.Append("</td></tr></table>");
    }

    // Where the load is going, in drop order: one row per order with its delivery address and
    // note. The pallets column is the one figure the two messages differ on, so each passes
    // its own. A customer's copy has only their orders, so it leaves out the drop (the haulier's
    // business) and the customer (always them).
    private static void MailDeliveries(
        StringBuilder html, LoadMailData d, Func<DispatchOrderRow, string> pallets, string emptyText,
        string? title = null)
    {
        var customer = d.CustomerName != null;
        var heads = customer
            ? new[] { ("Order", "left"), ("Delivery address", "left"), ("Pallets", "right") }
            : new[] { ("Drop", "left"), ("Order", "left"), ("Customer", "left"), ("Delivery address", "left"), ("Pallets", "right") };
        title ??= customer ? "Your deliveries" : "Deliveries, in drop order";
        html.Append($"<div style=\"font-size:11px;font-weight:600;letter-spacing:.5px;text-transform:uppercase;color:#64748b;border-bottom:1px solid #eef2f7;padding-bottom:4px;margin:0 0 8px;\">{title}</div>");
        html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;border-collapse:collapse;font-size:12.5px;\"><tr>");
        foreach (var (head, align) in heads)
            html.Append($"<th align=\"{align}\" style=\"padding:6px 8px;background:#f8fafc;border-bottom:1px solid #e5e7eb;font-weight:600;white-space:nowrap;\">{head}</th>");
        html.Append("</tr>");
        if (d.Orders.Count == 0)
            html.Append($"<tr><td colspan=\"{heads.Length}\" style=\"padding:10px 8px;color:#94a3b8;\">{emptyText}</td></tr>");
        var drop = 0;
        foreach (var o in d.Orders)
        {
            drop++;
            var address = string.Join("<br>", Lines(o.AddressFull).Select(H));
            var note = d.NoteById.TryGetValue(o.Id, out var comments) ? Lines(comments) : Array.Empty<string>();
            // With a note to follow, the rule under the order moves down to under the note.
            var cell = note.Length > 0 ? MailCellOpen : MailCell;
            html.Append("<tr>");
            if (!customer) html.Append($"<td style=\"{cell}\">{drop}</td>");
            html.Append($"<td style=\"{cell}white-space:nowrap;\">{o.OrderNo}</td>");
            if (!customer) html.Append($"<td style=\"{cell}\">{H(o.CustomerName)}</td>");
            html.Append($"<td style=\"{cell}line-height:1.4;\">{address}</td>");
            html.Append($"<td align=\"right\" style=\"{cell}white-space:nowrap;\">{pallets(o)}</td>");
            html.Append("</tr>");
            // The note runs the full width of the table on one line, its typed line breaks
            // folded into separators; a long one wraps.
            if (note.Length > 0)
            {
                html.Append($"<tr><td colspan=\"{heads.Length}\" style=\"padding:0 8px 8px;border-bottom:1px solid #eef2f7;font-size:12.5px;line-height:1.5;color:#334155;\">");
                html.Append("<span style=\"font-size:10.5px;font-weight:600;letter-spacing:.4px;text-transform:uppercase;color:#64748b;margin-right:8px;\">Delivery note</span>");
                html.Append(string.Join(" &middot; ", note.Select(H)));
                html.Append("</td></tr>");
            }
        }
        html.Append("</table>");
    }

    // The same drops, for the plain-text alternative.
    private static void MailDeliveriesText(
        StringBuilder text, LoadMailData d, Func<DispatchOrderRow, string> pallets, string emptyText,
        string? title = null)
    {
        var customer = d.CustomerName != null;
        text.AppendLine((title ?? (customer ? "Your deliveries" : "Deliveries, in drop order")).ToUpperInvariant());
        var drop = 0;
        foreach (var o in d.Orders)
        {
            drop++;
            text.AppendLine(customer
                ? $"Order {o.OrderNo} - {pallets(o)}"
                : $"{drop}. Order {o.OrderNo} - {o.CustomerName} - {pallets(o)}");
            foreach (var line in Lines(o.AddressFull)) text.AppendLine($"   {line}");
            var noteLines = d.NoteById.TryGetValue(o.Id, out var c) ? Lines(c) : Array.Empty<string>();
            if (noteLines.Length > 0) text.AppendLine($"   Delivery note: {string.Join(" · ", noteLines)}");
        }
        if (d.Orders.Count == 0) text.AppendLine(emptyText);
    }

    private static void TextPara(StringBuilder text, string piece)
    {
        if (piece.Length == 0) return;
        text.AppendLine(piece);
        text.AppendLine();
    }

    // ── "Ready for collection" ───────────────────────────────────────────────────────────
    // The 3-digit ref, large, and what to do with it; the load's lines; then the drops with
    // the pallets ordered.
    private static LoadEmail ComposeReady(LoadMailData d, IReadOnlyDictionary<string, string> wording)
    {
        var rec = d.Rec;
        var pallets = d.Orders.Sum(o => o.TotalQuantityOrdered);
        var weekCommencing = rec.CollectionDate?.ToString("d MMM yyyy");
        var p = new Pieces(wording, Values(d, Count(pallets, "pallet"), weekCommencing, null));
        var subject = p.Subject(AutomaticEmails.Ready);
        var greeting = p[AutomaticEmails.Greeting];
        var intro = p[AutomaticEmails.Intro];
        var refLabel = p[AutomaticEmails.RefLabel];
        var refNote = p[AutomaticEmails.RefNote];
        var footer = p[AutomaticEmails.Footer];
        const string Empty = "No orders on this load yet.";

        var html = new StringBuilder();
        MailOpen(html, greeting);
        MailIntro(html, intro);
        // The reference, large, and what to do with it.
        MailPanel(html, refLabel, d.Reference, "font-size:36px;font-weight:700;letter-spacing:6px;line-height:1.2;", refNote);
        // The load's own lines.
        html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"border-collapse:collapse;font-size:13px;line-height:1.7;margin:0 0 20px;\">");
        if (weekCommencing != null)
            html.Append($"<tr><td style=\"{MailLabel}\">Week commencing</td><td>{H(weekCommencing)}</td></tr>");
        if (!string.IsNullOrWhiteSpace(rec.TrailerNo))
            html.Append($"<tr><td style=\"{MailLabel}\">Trailer</td><td>{H(rec.TrailerNo)}</td></tr>");
        html.Append($"<tr><td style=\"{MailLabel}\">Load</td><td>{Count(d.Orders.Count, "order")}, {Count(pallets, "pallet")}</td></tr>");
        html.Append("</table>");
        MailDeliveries(html, d, o => o.TotalQuantityOrdered.ToString(), Empty);
        MailClose(html, footer);

        var text = new StringBuilder();
        TextPara(text, greeting);
        TextPara(text, intro);
        text.AppendLine(refLabel.Length > 0 ? $"{refLabel.ToUpperInvariant()}: {d.Reference}" : d.Reference);
        if (refNote.Length > 0) text.AppendLine(refNote);
        text.AppendLine();
        if (weekCommencing != null) text.AppendLine($"Week commencing: {weekCommencing}");
        if (!string.IsNullOrWhiteSpace(rec.TrailerNo)) text.AppendLine($"Trailer: {rec.TrailerNo}");
        text.AppendLine($"Load: {Count(d.Orders.Count, "order")}, {Count(pallets, "pallet")}");
        text.AppendLine();
        MailDeliveriesText(text, d, o => Count(o.TotalQuantityOrdered, "pallet"), Empty);
        if (footer.Length > 0)
        {
            text.AppendLine();
            text.AppendLine(footer);
        }

        return new LoadEmail(d.Reference, d.HaulierName, d.Recipients, subject, html.ToString(), text.ToString(),
            d.Attachments);
    }

    // ── The pre-commercial invoices ──────────────────────────────────────────────────────
    // Before the load is picked: the headline says what is attached, then the drops with the
    // pallets ordered (what the estimates are built on).
    private static LoadEmail ComposePreCommercial(LoadMailData d, IReadOnlyDictionary<string, string> wording)
    {
        var rec = d.Rec;
        var pallets = d.Orders.Sum(o => o.TotalQuantityOrdered);
        var weekCommencing = rec.CollectionDate?.ToString("d MMM yyyy");
        var p = new Pieces(wording, Values(d, Count(pallets, "pallet"), weekCommencing, null));
        var subject = p.Subject(AutomaticEmails.PreCommercial);
        var greeting = p[AutomaticEmails.Greeting];
        var intro = p[AutomaticEmails.Intro];
        var headline = p[AutomaticEmails.Headline];
        var footer = p[AutomaticEmails.Footer];
        var panelLabel = $"Load {d.Reference}";
        var panelNote = $"{Count(d.Orders.Count, "order")}, {Count(pallets, "pallet")} ordered. Weights are estimates.";
        const string Empty = "No orders on this load yet.";

        var html = new StringBuilder();
        MailOpen(html, greeting);
        MailIntro(html, intro);
        MailPanel(html, panelLabel, headline, "font-size:26px;font-weight:700;line-height:1.3;", panelNote);
        MailDeliveries(html, d, o => o.TotalQuantityOrdered.ToString(), Empty);
        MailClose(html, footer);

        var text = new StringBuilder();
        TextPara(text, greeting);
        TextPara(text, intro);
        text.AppendLine(panelLabel.ToUpperInvariant() + (headline.Length > 0 ? $": {headline.ToUpperInvariant()}" : ""));
        text.AppendLine(panelNote);
        text.AppendLine();
        MailDeliveriesText(text, d, o => Count(o.TotalQuantityOrdered, "pallet"), Empty);
        if (footer.Length > 0)
        {
            text.AppendLine();
            text.AppendLine(footer);
        }

        return new LoadEmail(d.Reference, d.HaulierName, d.Recipients, subject, html.ToString(), text.ToString(),
            d.Attachments);
    }

    // ── The dispatch confirmation ────────────────────────────────────────────────────────
    // The load has weighed out and left. The headline is that and when; then the drops with
    // the pallets actually loaded, so the haulier has the record of what is on board and where
    // it goes. The weighbridge figures are deliberately not in it. The customer's copy is the
    // same layout on their orders only, headed by their order number(s) rather than the load.
    private static LoadEmail ComposeDispatched(LoadMailData d, IReadOnlyDictionary<string, string> wording, bool customer)
    {
        var rec = d.Rec;
        var loaded = d.Orders.Sum(o => o.PickedQuantity);
        var weighedOut = rec.DateWeightOut?.ToString("d MMM yyyy HH:mm");
        var p = new Pieces(wording, Values(d, Count(loaded, "pallet"), null, weighedOut));
        var subject = p.Subject(customer ? AutomaticEmails.DispatchedCustomer : AutomaticEmails.Dispatched);
        var panelLabel = customer
            ? $"{Plural(d.Orders.Count, "Order")} {string.Join(", ", d.Orders.Select(o => o.OrderNo))}"
            : $"Load #{rec.Id}";
        var greeting = p[AutomaticEmails.Greeting];
        var intro = p[AutomaticEmails.Intro];
        var headline = p[AutomaticEmails.Headline];
        // The line under the headline only means something with a time in it.
        var weighedOutLine = weighedOut != null ? p[AutomaticEmails.WeighedOut] : "";
        var footer = p[AutomaticEmails.Footer];
        const string Empty = "No orders on this load.";
        // The pallets loaded; where that is not what was ordered (short, or one over), the
        // order's figure follows, so 3 is not passed off as the whole of 4.
        static string PalletsCell(DispatchOrderRow o) =>
            o.PickedQuantity == o.TotalQuantityOrdered
                ? o.PickedQuantity.ToString()
                : $"{o.PickedQuantity} <span style=\"{MailMuted}\">({o.TotalQuantityOrdered} ordered)</span>";
        static string PalletsText(DispatchOrderRow o) =>
            $"{Count(o.PickedQuantity, "pallet")} loaded"
            + (o.PickedQuantity == o.TotalQuantityOrdered ? "" : $" ({o.TotalQuantityOrdered} ordered)");

        var html = new StringBuilder();
        MailOpen(html, greeting);
        MailIntro(html, intro);
        // The headline: dispatched, and when.
        MailPanel(html, panelLabel, headline, "font-size:28px;font-weight:700;line-height:1.3;", weighedOutLine);
        MailDeliveries(html, d, PalletsCell, Empty);
        MailClose(html, footer);

        var text = new StringBuilder();
        TextPara(text, greeting);
        TextPara(text, intro);
        text.AppendLine(panelLabel.ToUpperInvariant()
            + (headline.Length > 0 ? $": {headline.ToUpperInvariant()}" : "")
            + (weighedOutLine.Length > 0 ? $" - {weighedOutLine}" : ""));
        text.AppendLine();
        MailDeliveriesText(text, d, PalletsText, Empty);
        if (footer.Length > 0)
        {
            text.AppendLine();
            text.AppendLine(footer);
        }

        return new LoadEmail(d.Reference, d.HaulierName, d.Recipients, subject, html.ToString(), text.ToString(),
            d.Attachments);
    }

    // ── The order confirmation ───────────────────────────────────────────────────────────
    // To the customer from Open Orders, before the order is picked: their order as taken, headed
    // by its number, with the pallets ordered and when it is to be delivered. The order
    // confirmation PDF goes with it. One order, so d.Orders has exactly one.
    private static LoadEmail ComposeConfirmation(LoadMailData d, IReadOnlyDictionary<string, string> wording)
    {
        var order = d.Orders.FirstOrDefault();
        var pallets = d.Orders.Sum(o => o.TotalQuantityOrdered);
        var values = Values(d, Count(pallets, "pallet"), null, null);
        values["DeliveryDate"] = order?.DeliveryDate?.ToString("d MMM yyyy") ?? "ASAP";
        values["PoNumber"] = order?.PoNumber?.Trim() ?? "";
        var p = new Pieces(wording, values);
        var subject = p.Subject(AutomaticEmails.OrderConfirmation);
        var panelLabel = $"Order {d.Reference}";
        var greeting = p[AutomaticEmails.Greeting];
        var intro = p[AutomaticEmails.Intro];
        var headline = p[AutomaticEmails.Headline];
        var delivery = p[AutomaticEmails.Delivery];
        var footer = p[AutomaticEmails.Footer];
        const string Empty = "No order.";
        const string Title = "Your order";

        var html = new StringBuilder();
        MailOpen(html, greeting);
        MailIntro(html, intro);
        MailPanel(html, panelLabel, headline, "font-size:28px;font-weight:700;line-height:1.3;", delivery);
        MailDeliveries(html, d, o => o.TotalQuantityOrdered.ToString(), Empty, Title);
        MailClose(html, footer);

        var text = new StringBuilder();
        TextPara(text, greeting);
        TextPara(text, intro);
        text.AppendLine(panelLabel.ToUpperInvariant()
            + (headline.Length > 0 ? $": {headline.ToUpperInvariant()}" : "")
            + (delivery.Length > 0 ? $" - {delivery}" : ""));
        text.AppendLine();
        MailDeliveriesText(text, d, o => Count(o.TotalQuantityOrdered, "pallet"), Empty, Title);
        if (footer.Length > 0)
        {
            text.AppendLine();
            text.AppendLine(footer);
        }

        return new LoadEmail(d.Reference, d.HaulierName, d.Recipients, subject, html.ToString(), text.ToString(),
            d.Attachments);
    }
}
