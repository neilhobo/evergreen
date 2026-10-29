using System.Text.RegularExpressions;
using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Services;

// The automatic emails and their wording — what Admin > Automatic Emails edits.
//
// Each email is a fixed layout (LoadEmailComposer) with a handful of PIECES of prose in it: the
// subject, the greeting, the intro, the footer, and so on. This is the list of those pieces, each
// with its default wording, and the placeholders the wording may carry ({Ref}, {Haulier}, ...).
// The office changes the words; the layout, the reference panel and the deliveries table stay
// as they are, so an edit cannot break the email.
//
// What has been changed is held in dbo.AutomaticEmailText, one row per piece; a piece with no
// row reads as its default here, so a new piece needs no data change and the defaults live in
// one place.
public static class AutomaticEmails
{
    // The emails, by the same names the admin UI's Send Email dialog uses (LoadEmailKind).
    public const string Ready = "ready";
    public const string Dispatched = "dispatched";
    // The dispatch confirmation again, to each customer on the load: their own orders only,
    // with only their delivery notes. Sent from the same dialog as the haulier's.
    public const string DispatchedCustomer = "dispatched-customer";
    // The pre-commercial invoices, to the haulier from Load detail while the load is still on the
    // Loads grid: each order's estimated invoice (ordered pallets at average weights) attached.
    public const string PreCommercial = "pre-commercial";
    // The order confirmation, to the customer from Open Orders before the order is picked: the
    // order as taken, with the order confirmation PDF (the delivery note, lines as ordered).
    public const string OrderConfirmation = "order-confirmation";

    // The pieces. Not every email has every one.
    public const string Subject = "subject";
    public const string Greeting = "greeting";
    public const string Intro = "intro";
    public const string RefLabel = "refLabel";     // ready: the small label over the big 3-digit ref
    public const string RefNote = "refNote";       // ready: what to do with the ref
    public const string Headline = "headline";     // dispatched: the big word in the panel
    public const string WeighedOut = "weighedOut"; // dispatched: the line under it, when there is a time
    public const string Delivery = "delivery";     // order confirmation: the line under the headline
    public const string Footer = "footer";

    // The longest a piece may be. Prose, not a document.
    public const int MaxLength = 2000;

    public sealed record Placeholder(string Name, string Meaning);

    public sealed record Field(string Key, string Label, string Hint, string Default, bool Multiline);

    public sealed record Definition(
        string Kind, string Title, string Description,
        IReadOnlyList<Placeholder> Placeholders, IReadOnlyList<Field> Fields)
    {
        public Field? FieldFor(string key) =>
            Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly Placeholder[] Shared =
    {
        new("Haulier", "The haulier's name"),
        new("Ref", "The load's 3-digit reference"),
        new("Company", LoadEmailComposer.Company),
        new("Orders", "How many orders are on the load, e.g. \"3 orders\""),
        new("Trailer", "The trailer number, if one is recorded"),
    };

    public static readonly IReadOnlyList<Definition> All = new[]
    {
        new Definition(
            Ready,
            "Load ready for collection",
            "Sent to the haulier from the Loads grid once the load is ready. Its key line is the "
            + "3-digit reference, which the driver keys into the weighbridge on arrival to book in.",
            Shared.Concat(new[]
            {
                new Placeholder("Pallets", "How many pallets are ordered, e.g. \"26 pallets\""),
                new Placeholder("WeekCommencing", "The load's collection date, if one is set"),
            }).ToArray(),
            new[]
            {
                new Field(Subject, "Subject", "One line.",
                    "Load {Ref} ready for collection – {Orders}, {Pallets}", false),
                new Field(Greeting, "Greeting", "The first line of the email.",
                    "Hello {Haulier},", false),
                new Field(Intro, "Introduction", "The paragraph before the load reference.",
                    "Your load is ready for collection from {Company}.", true),
                new Field(RefLabel, "Reference label", "The small heading over the big 3-digit reference.",
                    "Your load reference", false),
                new Field(RefNote, "Reference note", "Under the reference: what the driver does with it.",
                    "Enter this number at the weighbridge when you arrive. It books you in: you will be weighed and sent round to the yard to be loaded.", true),
                new Field(Footer, "Footer", "The small print at the bottom.",
                    "Sent by {Company} dispatch. Please do not reply to this email.", true),
            }),
        new Definition(
            Dispatched,
            "Load dispatched (haulier)",
            "The dispatch confirmation, sent to the haulier from the Completed Loads grid once the "
            + "load has weighed out and left. Each order's delivery note goes with it as a PDF.",
            Shared.Concat(new[]
            {
                new Placeholder("Pallets", "How many pallets were loaded, e.g. \"25 pallets\""),
                new Placeholder("LoadNo", "The full load number, e.g. \"#1234\""),
                new Placeholder("WeighedOut", "When the load weighed out, e.g. \"22 Sep 2026 14:05\""),
                new Placeholder("Telephone", "The haulier's telephone number, if one is recorded"),
            }).ToArray(),
            new[]
            {
                new Field(Subject, "Subject", "One line.",
                    "Load {LoadNo} dispatched – {Orders}, {Pallets}", false),
                new Field(Greeting, "Greeting", "The first line of the email.",
                    "Hello {Haulier},", false),
                new Field(Intro, "Introduction", "The paragraph before the Dispatched panel.",
                    "Load {LoadNo} has been loaded and has left {Company}. This is your dispatch confirmation, with the delivery note for each order attached.", true),
                new Field(Headline, "Headline", "The big word in the green panel.",
                    "Dispatched", false),
                new Field(WeighedOut, "Weighed-out line", "Under the headline. Only shown when the load has a weigh-out time.",
                    "Weighed out {WeighedOut}", false),
                new Field(Footer, "Footer", "The small print at the bottom.",
                    "Sent by {Company} dispatch. Please do not reply to this email.", true),
            }),
        new Definition(
            DispatchedCustomer,
            "Load dispatched (customer)",
            "The dispatch confirmation, sent to each customer on the load from the same Completed "
            + "Loads dialog as the haulier's. It lists their own orders only, and only their delivery "
            + "notes go with it.",
            new[]
            {
                new Placeholder("Customer", "The customer's name"),
                new Placeholder("OrderNumbers", "Their order number(s) on the load, e.g. \"48120\" or \"48120, 48121\""),
                new Placeholder("Orders", "How many of their orders are on the load, e.g. \"1 order\""),
                new Placeholder("Pallets", "How many of their pallets were loaded, e.g. \"12 pallets\""),
                new Placeholder("Haulier", "The haulier's name"),
                new Placeholder("LoadNo", "The full load number, e.g. \"#1234\""),
                new Placeholder("Trailer", "The trailer number, if one is recorded"),
                new Placeholder("WeighedOut", "When the load weighed out, e.g. \"22 Sep 2026 14:05\""),
                new Placeholder("Company", LoadEmailComposer.Company),
            },
            new[]
            {
                new Field(Subject, "Subject", "One line.",
                    "Order {OrderNumbers} dispatched – {Pallets}", false),
                new Field(Greeting, "Greeting", "The first line of the email.",
                    "Hello {Customer},", false),
                new Field(Intro, "Introduction", "The paragraph before the Dispatched panel.",
                    "Your order {OrderNumbers} has been loaded and has left {Company} with {Haulier}. This is your dispatch confirmation, with the delivery note attached.", true),
                new Field(Headline, "Headline", "The big word in the green panel.",
                    "Dispatched", false),
                new Field(WeighedOut, "Dispatched line", "Under the headline. Only shown when the load has a weigh-out time.",
                    "Left {Company} {WeighedOut}", false),
                new Field(Footer, "Footer", "The small print at the bottom.",
                    "Sent by {Company} dispatch. Please do not reply to this email.", true),
            }),
        new Definition(
            PreCommercial,
            "Pre-commercial invoice (haulier)",
            "Sent to the haulier from Load detail while the load is on the Loads grid, before it is "
            + "picked. Each order's pre-commercial invoice goes with it as a PDF: the ordered pallets "
            + "at the products' average weights, so the weights are estimates.",
            Shared.Concat(new[]
            {
                new Placeholder("Pallets", "How many pallets are ordered, e.g. \"26 pallets\""),
                new Placeholder("LoadNo", "The full load number, e.g. \"#1234\""),
                new Placeholder("WeekCommencing", "The load's collection date, if one is set"),
            }).ToArray(),
            new[]
            {
                new Field(Subject, "Subject", "One line.",
                    "Load {Ref} pre-commercial invoices – {Orders}, {Pallets}", false),
                new Field(Greeting, "Greeting", "The first line of the email.",
                    "Hello {Haulier},", false),
                new Field(Intro, "Introduction", "The paragraph before the panel.",
                    "Please find attached the pre-commercial invoice for each order on load {Ref} from {Company}. The weights are estimates based on average pallet weights; the commercial invoices will follow once the load has been picked and weighed.", true),
                new Field(Headline, "Headline", "The big line in the green panel.",
                    "Pre-Commercial Invoice", false),
                new Field(Footer, "Footer", "The small print at the bottom.",
                    "Sent by {Company} dispatch. Please do not reply to this email.", true),
            }),
        new Definition(
            OrderConfirmation,
            "Order confirmation (customer)",
            "Sent to the customer from Open Orders (the envelope over the grid) so they can check the "
            + "order before it is picked. The order confirmation goes with it as a PDF: the delivery "
            + "note, headed Order Confirmation, with the quantities as ordered.",
            new[]
            {
                new Placeholder("Customer", "The customer's name"),
                new Placeholder("OrderNumbers", "The order number, e.g. \"48120\""),
                new Placeholder("PoNumber", "The customer's order (PO) number, if one was given"),
                new Placeholder("Pallets", "How many pallets are ordered, e.g. \"12 pallets\""),
                new Placeholder("DeliveryDate", "The delivery date, e.g. \"22 Sep 2026\", or \"ASAP\""),
                new Placeholder("Haulier", "The haulier's name, if one is set on the order"),
                new Placeholder("Company", LoadEmailComposer.Company),
            },
            new[]
            {
                new Field(Subject, "Subject", "One line.",
                    "Order {OrderNumbers} confirmation – {Pallets}", false),
                new Field(Greeting, "Greeting", "The first line of the email.",
                    "Hello {Customer},", false),
                new Field(Intro, "Introduction", "The paragraph before the panel.",
                    "Thank you for your order. Please find attached the order confirmation for order {OrderNumbers}. Please check it, and let us know as soon as possible if anything is not right.", true),
                new Field(Headline, "Headline", "The big line in the green panel.",
                    "Order Confirmation", false),
                new Field(Delivery, "Delivery line", "Under the headline.",
                    "Delivery: {DeliveryDate}", false),
                new Field(Footer, "Footer", "The small print at the bottom.",
                    "Sent by {Company}.", true),
            }),
    };

    public static Definition? Find(string? kind) =>
        All.FirstOrDefault(d => string.Equals(d.Kind, kind, StringComparison.OrdinalIgnoreCase));

    // The wording of one email: every piece, the saved text where there is one and the default
    // otherwise. Keyed by field, as the composer reads it.
    public static async Task<Dictionary<string, string>> WordingAsync(
        GbEvergreenContext db, string kind, CancellationToken ct)
    {
        var def = Find(kind) ?? throw new ArgumentException($"No automatic email '{kind}'.", nameof(kind));
        var wording = def.Fields.ToDictionary(f => f.Key, f => f.Default, StringComparer.OrdinalIgnoreCase);
        var saved = await db.Set<AutomaticEmailTextEntity>().AsNoTracking()
            .Where(t => t.Email == def.Kind)
            .Select(t => new { t.Field, t.Text })
            .ToListAsync(ct);
        foreach (var row in saved)
            if (def.FieldFor(row.Field) is { } f) wording[f.Key] = row.Text;
        return wording;
    }

    // Expand {Placeholders} in a piece of wording. Case does not matter; one the email does not
    // know is left as typed, so a typo shows up in the preview rather than vanishing.
    private static readonly Regex PlaceholderPattern = new(@"\{([A-Za-z]+)\}", RegexOptions.Compiled);

    public static string Fill(string? template, IReadOnlyDictionary<string, string> values)
    {
        if (string.IsNullOrEmpty(template)) return "";
        return PlaceholderPattern.Replace(template, m =>
            values.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);
    }

    // What is wrong with a piece as typed, or null. The subject is the one piece that must be
    // there and must be one line; anything else may be blank, which leaves it out of the email.
    public static string? Validate(Field field, string text)
    {
        if (text.Length > MaxLength)
            return $"{field.Label} is too long — {MaxLength:N0} characters at most.";
        if (field.Key == Subject)
        {
            if (string.IsNullOrWhiteSpace(text)) return "The subject cannot be blank.";
            if (text.IndexOfAny(new[] { '\r', '\n' }) >= 0) return "The subject must be one line.";
        }
        return null;
    }
}
