using System.Collections.Concurrent;
using System.Diagnostics;

namespace Globeweigh.Api.Services;

// Renders an order's delivery note to an A4 PDF, for attaching to email (the dispatch
// confirmation, and the Send Email dialog's view of it). The note is HTML+CSS whose page breaks
// are measured from the rendered DOM, so only a browser can lay it out: headless Chrome loads
// the same loopback-only print page the GlobeweighService PrintWorker prints from
// (/api/dispatch/orders/{id}/delivery-note/print) and prints it to a file, with the flags the
// worker has proven on the server's Chrome 109. Renders are cached in memory under a key the
// caller supplies (a hash of the note's data), so the dialog's preview and its per-address
// sends render each note once, and an order edited in between gets a fresh one. The commercial
// invoice (the dispatch confirmation's commercial copy) renders the same way from its own print
// page (/api/dispatch/orders/{id}/commercial-invoice/print): pass document CommercialInvoice.
//
// Config (appsettings, both optional):
//   Print:ChromePath   — default C:\Program Files\Google\Chrome\Application\chrome.exe
//   Print:SelfBaseUrl  — where THIS API answers on the box, default http://localhost:5099
//                        (Kestrel in dev and the IIS binding on evergreen; PrintWorker's default too)
public class DeliveryNotePdfRenderer
{
    // A blank render is ~900 bytes; a real note ~115 kB. Under this, the page did not render.
    private const long MinPlausiblePdfBytes = 4096;
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(90);

    private readonly string _chrome;
    private readonly string _selfBase;
    private readonly string _workDir;
    private readonly ILogger<DeliveryNotePdfRenderer> _log;
    private readonly ConcurrentDictionary<string, (DateTime At, byte[] Pdf)> _cache = new();
    // Two Chromes at a time is plenty; a dozen would only fight each other for the CPU.
    private readonly SemaphoreSlim _gate = new(2, 2);

    public DeliveryNotePdfRenderer(IConfiguration config, ILogger<DeliveryNotePdfRenderer> log)
    {
        _log = log;
        _chrome = FirstNonBlank(config["Print:ChromePath"], @"C:\Program Files\Google\Chrome\Application\chrome.exe");
        _selfBase = FirstNonBlank(config["Print:SelfBaseUrl"], "http://localhost:5099").TrimEnd('/');
        _workDir = Path.Combine(Path.GetTempPath(), "Globeweigh.Api", "delivery-note-pdf");
    }

    private static string FirstNonBlank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    public const string DeliveryNote = "delivery-note";
    public const string CommercialInvoice = "commercial-invoice";
    // The estimate (ordered pallets at average weights), from its own print page.
    public const string PreCommercialInvoice = "pre-commercial-invoice";
    // The order confirmation (the note titled so, lines as ordered), from its own print page.
    public const string OrderConfirmation = "order-confirmation";

    // The PDF of one order's note (or, with document, its commercial invoice): the cached render
    // when the key has been rendered recently, else rendered now and kept. The key must tell the
    // two documents apart.
    public async Task<byte[]> RenderAsync(int orderId, string cacheKey, CancellationToken ct,
        string document = DeliveryNote)
    {
        if (TryCached(cacheKey, out var pdf)) return pdf;
        await _gate.WaitAsync(ct);
        try
        {
            // Another request may have rendered it while this one waited its turn.
            if (TryCached(cacheKey, out pdf)) return pdf;
            pdf = await RenderOnceAsync(orderId, document, ct);
            Prune();
            _cache[cacheKey] = (DateTime.UtcNow, pdf);
            return pdf;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryCached(string key, out byte[] pdf)
    {
        if (_cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < CacheFor)
        {
            pdf = hit.Pdf;
            return true;
        }
        pdf = Array.Empty<byte>();
        return false;
    }

    private void Prune()
    {
        foreach (var (key, entry) in _cache)
            if (DateTime.UtcNow - entry.At >= CacheFor) _cache.TryRemove(key, out _);
    }

    private async Task<byte[]> RenderOnceAsync(int orderId, string document, CancellationToken ct)
    {
        if (!File.Exists(_chrome))
            throw new InvalidOperationException($"Chrome not found at '{_chrome}' (set Print:ChromePath).");

        // A folder of its own per render, profile included: Chrome will not share a profile
        // between two running instances, and a fresh profile is the cold-cache case the print
        // page's font wait handles best. Gone again when the render is done.
        var run = Path.Combine(_workDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var pdf = Path.Combine(run, "note.pdf");
        var url = $"{_selfBase}/api/dispatch/orders/{orderId}/{document}/print";

        // The PrintWorker's flags (GlobeweighService/PrintWorker.cs says why each one is there).
        var args = string.Join(" ",
            "--headless",
            "--disable-gpu",
            "--no-sandbox",
            "--no-pdf-header-footer",
            "--force-device-scale-factor=1",
            Quote($"--user-data-dir={Path.Combine(run, "profile")}"),
            "--virtual-time-budget=20000",
            Quote($"--print-to-pdf={pdf}"),
            Quote(url));
        try
        {
            var (exit, output) = await RunAsync(_chrome, args, ct);
            if (exit != 0)
                throw new InvalidOperationException($"Chrome exited {exit}. {Trim(output)}");
            if (!File.Exists(pdf))
                throw new InvalidOperationException($"Chrome produced no PDF. {Trim(output)}");
            var bytes = await File.ReadAllBytesAsync(pdf, ct);
            if (bytes.Length < MinPlausiblePdfBytes)
                throw new InvalidOperationException(
                    $"The rendered PDF is only {bytes.Length} bytes: the page almost certainly came out blank. Check {url} renders.");
            _log.LogInformation("Rendered {Document} for order {OrderId}: {Bytes} bytes", document, orderId, bytes.Length);
            return bytes;
        }
        finally
        {
            try { Directory.Delete(run, recursive: true); }
            catch (Exception e) { _log.LogDebug(e, "Could not remove render folder {Dir}", run); }
        }
    }

    private static async Task<(int Exit, string Output)> RunAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}.");
        // Both pipes are drained while waiting: a child that fills one would otherwise hang.
        var err = p.StandardError.ReadToEndAsync(ct);
        var outp = p.StandardOutput.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RenderTimeout);
        try
        {
            await p.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Chrome is several processes; the tree goes, not just the launcher.
            try { p.Kill(entireProcessTree: true); } catch { /* already gone */ }
            if (ct.IsCancellationRequested) throw;
            return (-1, $"timed out after {RenderTimeout.TotalSeconds:N0}s");
        }
        return (p.ExitCode, await err + await outp);
    }

    private static string Quote(string s) => $"\"{s}\"";
    private static string Trim(string s) => s.Length <= 400 ? s.Trim() : s[..400].Trim() + "…";
}
