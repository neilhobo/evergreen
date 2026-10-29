using System;
using System.Configuration;
using System.Data;
using System.Data.Entity.Core.EntityClient;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GlobeweighService
{
    // Prints the dockets the API queues in dbo.PrintJob — today the delivery notes that go
    // out automatically when a picker completes a load.
    //
    // Why here and not in the API: the API runs under IIS as an app-pool identity, a poor
    // place to drive a print driver, and printing must never be able to fail the request
    // that dispatches orders and writes stock history. This service already runs on the
    // same box as SYSTEM, which CAN see the Weighbridge Printer (a machine-level TCP/IP
    // queue on 192.168.250.72, not a per-user connection).
    //
    // Pipeline per job:
    //   1. claim the row       (single atomic UPDATE, so a restart can't double-print)
    //   2. headless Chrome     -> A4 PDF of /api/dispatch/orders/{id}/delivery-note/print
    //   3. SumatraPDF          -> N copies to the printer, simplex, unscaled
    //
    // Chrome rather than a report engine because the note is HTML+CSS whose page breaks are
    // measured from real rendered geometry — only a browser lays it out correctly, and the
    // same component renders the on-screen preview in TauriAdmin. Chrome 109 is the newest
    // Chromium the server's Windows Server 2012 R2 can run; there is no Edge on that box.
    public class PrintWorker
    {
        private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(30);

        // A job is retried this many times before it is parked as Failed. Attempts is
        // incremented at claim time, so a crash mid-print still counts.
        private const int MaxAttempts = 3;

        // A rendered note is ~100 kB (two logos, a subset font). A blank A4 is under 1 kB —
        // which is exactly what a CSS regression produced during development, and a blank
        // docket handed to a driver is worse than a failed job. Treat suspiciously small
        // output as a render failure so it retries and then shows up as Failed.
        private const long MinPlausiblePdfBytes = 4096;

        private readonly bool _enabled;
        private readonly string _chrome;
        private readonly string _sumatra;
        private readonly string _workDir;
        private readonly string _apiBase;
        private readonly string _sql;

        public PrintWorker()
        {
            _enabled  = ReadBool("PrintEnabled", true);
            _chrome   = Read("ChromePath",  @"C:\Program Files\Google\Chrome\Application\chrome.exe");
            _sumatra  = Read("SumatraPath", @"C:\Globeweigh\PrintTools\SumatraPDF.exe");
            _workDir  = Read("PrintWorkDir", @"C:\Globeweigh\PrintTmp");
            _apiBase  = Read("PrintApiBaseUrl", "http://localhost:5099").TrimEnd('/');
            // AppConfig holds the EF metadata connection string; the queue is plain ADO.NET
            // (PrintJob is not in the EDMX and does not need to be).
            _sql = new EntityConnectionStringBuilder(AppConfig.ConnectionString).ProviderConnectionString;
        }

        public async Task RunAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                Log("PrintEnabled=false - worker idle");
                return;
            }

            Log("Print worker started");
            Log($"  chrome  : {_chrome}   {(File.Exists(_chrome) ? "OK" : "*** MISSING ***")}");
            Log($"  sumatra : {_sumatra}   {(File.Exists(_sumatra) ? "OK" : "*** MISSING ***")}");
            Log($"  api     : {_apiBase}");

            try { Directory.CreateDirectory(_workDir); }
            catch (Exception ex) { Log($"Cannot create work dir '{_workDir}': {ex.Message}"); }

            while (!stoppingToken.IsCancellationRequested)
            {
                PrintJobRow job = null;
                try
                {
                    job = ClaimNextJob();
                    if (job == null)
                    {
                        await Task.Delay(IdleDelay, stoppingToken).ConfigureAwait(false);
                        continue;
                    }

                    Log($"Job {job.Id}: order {job.DispatchOrderId}, {job.Copies} copies -> '{job.PrinterName}' (attempt {job.Attempts})");
                    ProcessJob(job);
                    Complete(job.Id);
                    Log($"Job {job.Id}: printed");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Job {job?.Id.ToString() ?? "?"}: {ex.Message}");
                    if (job != null)
                    {
                        try { Fail(job, ex.Message); }
                        catch (Exception ex2) { Log($"Could not record failure: {ex2.Message}"); }
                    }

                    // Back off either way. A retried job goes straight back to Queued, so
                    // without this the loop would re-claim it immediately and spend all
                    // MaxAttempts inside a second — turning a printer that is briefly
                    // offline (or a paper jam someone is walking over to clear) into a
                    // permanently Failed docket.
                    try { await Task.Delay(ErrorDelay, stoppingToken).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }

            Log("Print worker stopped");
        }

        private void ProcessJob(PrintJobRow job)
        {
            var pdf = Path.Combine(_workDir, $"printjob-{job.Id}.pdf");
            try { if (File.Exists(pdf)) File.Delete(pdf); } catch { /* overwritten below anyway */ }

            RenderPdf(job, pdf);
            SendToPrinter(job, pdf);

            try { File.Delete(pdf); } catch { /* a stale temp file is not worth failing over */ }
        }

        private void RenderPdf(PrintJobRow job, string pdf)
        {
            if (!File.Exists(_chrome))
                throw new InvalidOperationException($"Chrome not found at '{_chrome}' (set ChromePath).");

            var url = $"{_apiBase}/api/dispatch/orders/{job.DispatchOrderId}/delivery-note/print";

            // --headless (not =new): Chrome 109 predates the new headless mode.
            // --no-pdf-header-footer: without it Chromium reserves margin for its own
            //   header/footer, ignores the note's @page{margin:0}, and scales the 210mm
            //   sheet down to fit — the fat-margin output the client complained about.
            // --virtual-time-budget: the page's data is inlined, so one layout pass is all
            //   it needs; this is the capture trigger.
            // --user-data-dir: SYSTEM has no usable default profile in session 0.
            var args = string.Join(" ",
                "--headless",
                "--disable-gpu",
                "--no-sandbox",
                "--no-pdf-header-footer",
                "--force-device-scale-factor=1",
                Quote($"--user-data-dir={Path.Combine(_workDir, "profile")}"),
                "--virtual-time-budget=20000",
                Quote($"--print-to-pdf={pdf}"),
                Quote(url));

            var exit = RunProcess(_chrome, args, TimeSpan.FromSeconds(90), out var stderr);
            if (exit != 0)
                throw new InvalidOperationException($"Chrome exited {exit}. {Trim(stderr)}");
            if (!File.Exists(pdf))
                throw new InvalidOperationException($"Chrome produced no PDF. {Trim(stderr)}");

            var size = new FileInfo(pdf).Length;
            if (size < MinPlausiblePdfBytes)
                throw new InvalidOperationException(
                    $"Rendered PDF is only {size} bytes - the note almost certainly came out blank. " +
                    $"Check {url} loads and renders.");
        }

        private void SendToPrinter(PrintJobRow job, string pdf)
        {
            if (!File.Exists(_sumatra))
                throw new InvalidOperationException($"SumatraPDF not found at '{_sumatra}' (set SumatraPath).");

            var printer = string.IsNullOrWhiteSpace(job.PrinterName) ? "Weighbridge Printer" : job.PrinterName;

            // simplex is deliberate: the queue's driver reports CanDuplex, and two copies of
            // a one-page docket duplexed would come out as one sheet printed both sides -
            // useless when the driver signs one copy and the customer keeps the other.
            // noscale keeps the sheet at true size, matching the on-screen preview.
            var settings = $"{job.Copies}x,simplex,paper=A4,noscale";

            var args = string.Join(" ",
                "-print-to", Quote(printer),
                "-print-settings", Quote(settings),
                "-silent",
                "-exit-when-done",
                Quote(pdf));

            var exit = RunProcess(_sumatra, args, TimeSpan.FromSeconds(120), out var stderr);
            if (exit != 0)
                throw new InvalidOperationException($"SumatraPDF exited {exit} printing to '{printer}'. {Trim(stderr)}");
        }

        private static int RunProcess(string exe, string args, TimeSpan timeout, out string stderr)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };

            using (var p = Process.Start(psi))
            {
                // Read stderr on a background task: a child that fills the pipe while we
                // block on WaitForExit would deadlock.
                var errTask = p.StandardError.ReadToEndAsync();
                var outTask = p.StandardOutput.ReadToEndAsync();

                if (!p.WaitForExit((int)timeout.TotalMilliseconds))
                {
                    try { p.Kill(); } catch { /* already gone */ }
                    stderr = $"timed out after {timeout.TotalSeconds:N0}s";
                    return -1;
                }

                // The timed overload can return before the redirected streams are drained
                // and the exit code is settled; the parameterless one waits for both. This
                // matters here because a failed print is reported ONLY by a non-zero exit
                // code (SumatraPDF returns 1 and still says "finished ok" on stdout), so a
                // missed code would silently mark a job Done that never reached paper.
                p.WaitForExit();

                stderr = (errTask.Result ?? "") + (outTask.Result ?? "");
                return p.ExitCode;
            }
        }

        // ---- queue access (plain ADO.NET; PrintJob is not in the EDMX) ----

        // Claim in ONE statement so a second worker - or this one after a restart - cannot
        // pick up a job that is already being printed.
        private PrintJobRow ClaimNextJob()
        {
            const string sql = @"
UPDATE p
   SET Status     = 'Printing',
       StartedUtc = GETUTCDATE(),
       Attempts   = p.Attempts + 1
OUTPUT inserted.id, inserted.DispatchOrderId, inserted.Copies, inserted.PrinterName, inserted.Attempts
  FROM dbo.PrintJob p
 WHERE p.id = (SELECT TOP 1 id FROM dbo.PrintJob WHERE Status = 'Queued' ORDER BY id);";

            using (var cn = new SqlConnection(_sql))
            using (var cmd = new SqlCommand(sql, cn))
            {
                cn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    if (!r.Read()) return null;
                    return new PrintJobRow
                    {
                        Id = r.GetInt32(0),
                        DispatchOrderId = r.GetInt32(1),
                        Copies = r.GetInt32(2),
                        PrinterName = r.IsDBNull(3) ? null : r.GetString(3),
                        Attempts = r.GetInt32(4),
                    };
                }
            }
        }

        private void Complete(int id)
        {
            Exec("UPDATE dbo.PrintJob SET Status='Done', CompletedUtc=GETUTCDATE(), LastError=NULL WHERE id=@id",
                cmd => cmd.Parameters.AddWithValue("@id", id));
        }

        // Back to Queued for another go, or parked as Failed once the attempts are spent.
        private void Fail(PrintJobRow job, string error)
        {
            var final = job.Attempts >= MaxAttempts;
            Exec(final
                    ? "UPDATE dbo.PrintJob SET Status='Failed', CompletedUtc=GETUTCDATE(), LastError=@e WHERE id=@id"
                    : "UPDATE dbo.PrintJob SET Status='Queued', LastError=@e WHERE id=@id",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@id", job.Id);
                    cmd.Parameters.AddWithValue("@e", Truncate(error, 1000));
                });

            Log(final
                ? $"Job {job.Id}: FAILED after {job.Attempts} attempts - {error}"
                : $"Job {job.Id}: will retry ({job.Attempts}/{MaxAttempts})");
        }

        private void Exec(string sql, Action<SqlCommand> bind)
        {
            using (var cn = new SqlConnection(_sql))
            using (var cmd = new SqlCommand(sql, cn))
            {
                bind(cmd);
                cn.Open();
                cmd.ExecuteNonQuery();
            }
        }

        private class PrintJobRow
        {
            public int Id;
            public int DispatchOrderId;
            public int Copies;
            public string PrinterName;
            public int Attempts;
        }

        // ---- helpers ----

        private static string Read(string key, string fallback)
        {
            var v = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(v) ? fallback : v.Trim();
        }

        private static bool ReadBool(string key, bool fallback)
        {
            bool b;
            var v = ConfigurationManager.AppSettings[key];
            return bool.TryParse(v, out b) ? b : fallback;
        }

        private static string Quote(string s) => "\"" + s + "\"";

        private static string Trim(string s) =>
            string.IsNullOrWhiteSpace(s) ? "" : Truncate(s.Replace("\r", " ").Replace("\n", " ").Trim(), 400);

        private static string Truncate(string s, int max) =>
            s == null ? null : (s.Length <= max ? s : s.Substring(0, max));

        // Console AND Logs\Weighbridge.log, the same as the other workers. The file half
        // is not optional: ConsoleHelper.EnsureConsole() is DEBUG-only, so when this runs
        // as a Windows service there is no console attached and console-only output goes
        // nowhere — the worker would be invisible in production, with no way to confirm it
        // even started. (Job failures are still recorded in PrintJob.LastError regardless.)
        private static void Log(string msg)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [PRINT] {msg}";
            Console.WriteLine(line);
            try
            {
                var logPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Logs", "Weighbridge.log");
                var dir = Path.GetDirectoryName(logPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(logPath, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the worker down.
            }
        }
    }
}
