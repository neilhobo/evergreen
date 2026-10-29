using System.Security.Cryptography;
using System.Text;

namespace Globeweigh.Api.Auth;

// Secrets kept in the database (the SMTP password) go through Windows DPAPI, machine
// scope: readable by any process on this box, opaque anywhere else. Unprotect answers null
// rather than throwing for a value protected on another machine — which is what a prod
// database restored onto the dev PC looks like — so the caller treats it as "not set" and
// the password is simply entered again.
public static class DpapiSecret
{
    // Ties the blob to this use, so a value lifted from elsewhere on the box won't decrypt.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Globeweigh.Api.Setting");

    // The API only ever runs on Windows (the server and the dev PCs); DPAPI is Windows-only.
#pragma warning disable CA1416
    public static string Protect(string plain) =>
        Convert.ToBase64String(ProtectedData.Protect(
            Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.LocalMachine));

    public static string? Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        try
        {
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(
                Convert.FromBase64String(stored), Entropy, DataProtectionScope.LocalMachine));
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }
#pragma warning restore CA1416
}
