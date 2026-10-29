using Globeweigh.Api.Data;
using Globeweigh.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Globeweigh.Api.Services;

// Replaces the Sage customer mirror (SageCustomer / SageCustomerAddress) with the snapshot
// the sidecar read from Sage. See Data/Entities/SageCustomer.cs for what the mirror is for.
public static class SageCustomerMirror
{
    // ---- The contract with the sidecar ------------------------------------------------
    // PUT /api/sage/sidecar/customers. Field names match the sidecar's CustomerRow /
    // AddressRow (SageSopWriter.cs) — change both together.

    public class Snapshot
    {
        public string? Machine { get; set; }
        public string? Company { get; set; }
        public DateTime? ReadAt { get; set; }
        public int ReadMs { get; set; }
        public List<CustomerIn> Customers { get; set; } = [];
        public List<AddressIn> Addresses { get; set; } = [];
    }

    public class CustomerIn
    {
        public string? AccountRef { get; set; }
        public string? Name { get; set; }
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public string? Address3 { get; set; }
        public string? Address4 { get; set; }
        public string? Address5 { get; set; }
        public string? Contact { get; set; }
        public string? Telephone { get; set; }
        public string? Telephone2 { get; set; }
        public string? Email { get; set; }
        public string? VatNumber { get; set; }
        public string? DelName { get; set; }
        public short DefTaxCode { get; set; }
        public double Balance { get; set; }
        public bool OnHold { get; set; }
        public bool Inactive { get; set; }
    }

    public class AddressIn
    {
        public string? AccountRef { get; set; }
        public int AddressNumber { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public string? Type { get; set; }                  // sdoDeliveryAddress etc.
        public string? Address1 { get; set; }
        public string? Address2 { get; set; }
        public string? Address3 { get; set; }
        public string? Address4 { get; set; }
        public string? Address5 { get; set; }
        public string? Telephone { get; set; }
        public string? Contact { get; set; }
        public string? Email { get; set; }
    }

    // One transaction, so a reader never sees half a sync. Customers are upserted by
    // account ref (FirstSeenUtc survives) and accounts no longer in Sage are deleted;
    // addresses are replaced wholesale — Sage renumbers them, there is no identity to keep.
    // Returns what the mirror now holds.
    public static async Task<(int Customers, int Addresses)> ReplaceAsync(
        GbEvergreenContext db, Snapshot snap, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var existing = await db.Set<SageCustomer>()
            .ToDictionaryAsync(c => c.AccountRef, StringComparer.OrdinalIgnoreCase, ct);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in snap.Customers)
        {
            var key = Key(c.AccountRef);
            // A blank ref is not an account; a repeated one (Sage does hold the odd ghost)
            // keeps its first occurrence, as the unique index insists.
            if (key.Length == 0 || !seen.Add(key)) continue;
            if (!existing.TryGetValue(key, out var row))
            {
                row = new SageCustomer { AccountRef = key, FirstSeenUtc = now };
                db.Add(row);
            }
            row.Name = T(c.Name, 60);
            row.Address1 = T(c.Address1, 60);
            row.Address2 = T(c.Address2, 60);
            row.Address3 = T(c.Address3, 60);
            row.Address4 = T(c.Address4, 60);
            row.Address5 = T(c.Address5, 60);
            row.Contact = T(c.Contact, 30);
            row.Telephone = T(c.Telephone, 30);
            row.Telephone2 = T(c.Telephone2, 30);
            row.Email = T(c.Email, 255);
            row.VatNumber = T(c.VatNumber, 30);
            row.DelName = T(c.DelName, 60);
            row.DefTaxCode = c.DefTaxCode;
            row.Balance = (decimal)Math.Round(c.Balance, 2);
            row.OnHold = c.OnHold;
            row.Inactive = c.Inactive;
            row.LastSeenUtc = now;
        }
        foreach (var gone in existing.Where(kv => !seen.Contains(kv.Key)))
            db.Remove(gone.Value);

        await db.Set<SageCustomerAddress>().ExecuteDeleteAsync(ct);
        var addresses = 0;
        foreach (var a in snap.Addresses)
        {
            var key = Key(a.AccountRef);
            if (key.Length == 0) continue;                 // Sage's unowned first entry
            db.Add(new SageCustomerAddress
            {
                AccountRef = key,
                AddressNumber = a.AddressNumber,
                Name = T(a.Name, 60),
                Description = T(a.Description, 60),
                AddressType = TypeName(a.Type),
                Address1 = T(a.Address1, 60),
                Address2 = T(a.Address2, 60),
                Address3 = T(a.Address3, 60),
                Address4 = T(a.Address4, 60),
                Address5 = T(a.Address5, 60),
                Telephone = T(a.Telephone, 30),
                Contact = T(a.Contact, 30),
                Email = T(a.Email, 255),
                LastSeenUtc = now,
            });
            addresses++;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (seen.Count, addresses);
    }

    private static string Key(string? accountRef) => (accountRef ?? "").Trim().ToUpperInvariant();

    // "sdoDeliveryAddress" -> "Delivery"; anything else is kept as sent, minus the prefix.
    private static string? TypeName(string? sdoType)
    {
        if (string.IsNullOrWhiteSpace(sdoType)) return null;
        var t = sdoType.Trim();
        if (t.StartsWith("sdo", StringComparison.OrdinalIgnoreCase)) t = t[3..];
        if (t.EndsWith("Address", StringComparison.OrdinalIgnoreCase)) t = t[..^7];
        return T(t, 30);
    }

    // Trimmed and cut to the column — Sage's own limits are at or under these, but a
    // snapshot must never fail the whole sync on one odd value.
    private static string? T(string? s, int max)
    {
        if (s == null) return null;
        var v = s.Trim();
        if (v.Length == 0) return null;
        return v.Length <= max ? v : v[..max];
    }
}
