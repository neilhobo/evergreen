using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Globeweigh.SageSidecar
{
    // The loop: claim a job from the API, post it to Sage, report back — and, on a timer or
    // when someone in Admin asks, read the customer ledger out of Sage into the API's mirror
    // (Admin > Sage Link > Customers). Runs on ONE STA thread (SidecarHost) because SDO is
    // apartment-threaded, so everything here is synchronous on purpose.
    //
    // The one thing this must never do is post the same order twice. The API's claim is
    // atomic and a claimed job goes stale after 15 minutes; the gap that leaves is "posted
    // to Sage, then died before telling the API". Two guards close it:
    //   1. a done-marker (state\job-N.done, holding the Sage order number) written AFTER
    //      the post and BEFORE the acknowledgement — a re-handed job with a marker is
    //      acknowledged, never re-posted;
    //   2. before posting, the newest Sage orders are scanned for one already carrying the
    //      Evergreen reference (SageSopWriter.FindExistingOrder), for when the marker is
    //      gone (box rebuilt).
    public sealed class ExportWorker
    {
        private const string CustomersEntity = "Customers";
        private const string SalesEntity = "Sales";
        // How often the API is asked whether a customer pull is wanted. Cheap, but there is
        // no point asking on every 10 s poll.
        private static readonly TimeSpan SyncCheckEvery = TimeSpan.FromSeconds(30);
        // After a failed pull, leave Sage alone for this long before trying again on the
        // timer — without it a dead Sage would be re-dialled every check.
        private static readonly TimeSpan SyncFailureBackoff = TimeSpan.FromMinutes(10);

        private readonly SidecarConfig _cfg;
        private readonly ApiClient _api;
        private readonly string _machine = Environment.MachineName;
        private DateTime _nextSyncCheck = DateTime.MinValue;
        private DateTime _nextSalesCheck = DateTime.MinValue.AddSeconds(15);
        // After a pull cancelled from Admin, the timer leaves that entity alone for one interval —
        // otherwise a cancelled pull that was also due would start straight over again. A new
        // Refresh from Admin still goes ahead.
        private DateTime _customersNotDueBefore = DateTime.MinValue;
        private DateTime _salesNotDueBefore = DateTime.MinValue;

        public ExportWorker(SidecarConfig cfg, ApiClient api)
        {
            _cfg = cfg;
            _api = api;
        }

        public void Run(CancellationToken ct)
        {
            Log.Info("Sage sidecar starting on " + _machine + " ("
                + (Environment.Is64BitProcess ? "64-bit - WRONG, SDO needs the 32-bit build" : "32-bit") + ")");
            Log.Info(Environment.NewLine + _cfg.Summary());

            var problems = new List<string>(_cfg.Problems());
            if (problems.Count > 0)
                throw new InvalidOperationException("Configuration problems:" + Environment.NewLine
                    + "  - " + string.Join(Environment.NewLine + "  - ", problems));

            if (!_cfg.Enabled)
            {
                Log.Info("Enabled=false - worker idle");
                ct.WaitHandle.WaitOne();
                return;
            }

            Directory.CreateDirectory(_cfg.StateDir);
            PingApi();

            SageSopWriter sage = null;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    MaybeSyncCustomers(ref sage);   // handles its own failures
                    MaybeSyncSales(ref sage);       // likewise

                    ClaimedJob job = null;
                    try
                    {
                        job = _api.Claim(_machine);
                        if (job == null)
                        {
                            // Queue empty: give the Sage seat back while we wait.
                            DisposeSage(ref sage);
                            if (Sleep(_cfg.Poll, ct)) break;
                            continue;
                        }

                        var lineCount = job.Payload == null || job.Payload.Lines == null ? 0 : job.Payload.Lines.Count;
                        Log.Info("Job " + job.Id + ": order " + job.OrderNo + ", " + lineCount + " line(s), attempt " + job.Attempts);
                        var sageNo = Process(job, ref sage);
                        Log.Info("Job " + job.Id + ": order " + job.OrderNo + " -> Sage order " + sageNo);
                    }
                    catch (SageValidationException ex)
                    {
                        Log.Warn("Job " + JobId(job) + ": " + ex.Message);
                        TryFail(job, ex.Message, true);
                    }
                    catch (SageConnectException ex)
                    {
                        Log.Error("Job " + JobId(job) + ": cannot get into Sage - " + ex.Message);
                        DisposeSage(ref sage);
                        TryRelease(job, ex.Message);
                        if (Sleep(TimeSpan.FromTicks(_cfg.ErrorBackoff.Ticks * 2), ct)) break;
                    }
                    catch (ApiException ex)
                    {
                        // The API is the thing we can't reach, so there is nobody to tell. A
                        // claimed job stays Exporting and is re-handed after the stale window;
                        // a done-marker, if we got that far, makes that harmless.
                        Log.Error("API: " + ex.Message);
                        if (Sleep(_cfg.ErrorBackoff, ct)) break;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Job " + JobId(job) + ": " + ex.Message, ex);
                        DisposeSage(ref sage);   // a wounded SDO session is not worth keeping
                        TryFail(job, ex.Message, false);
                        if (Sleep(_cfg.ErrorBackoff, ct)) break;
                    }
                }
            }
            finally
            {
                DisposeSage(ref sage);
            }
            Log.Info("Sage sidecar stopped");
        }

        // ---- exports -----------------------------------------------------------------------

        private int Process(ClaimedJob job, ref SageSopWriter sage)
        {
            // 1. Posted before, never acknowledged: acknowledge now and do NOT post again.
            var marker = MarkerPath(job.Id);
            if (File.Exists(marker))
            {
                var no = int.Parse(File.ReadAllText(marker).Trim(), CultureInfo.InvariantCulture);
                Log.Warn("Job " + job.Id + ": already posted as Sage order " + no + " (done-marker found) - acknowledging, not re-posting");
                Acknowledge(job.Id, no);
                File.Delete(marker);
                return no;
            }

            // 2. Connect (kept across consecutive jobs; dropped when the queue empties).
            EnsureSage(ref sage);

            // 3. Already in Sage from an attempt whose marker is gone?
            var existing = sage.FindExistingOrder(job.Payload.Reference);
            if (existing.HasValue)
            {
                Log.Warn("Job " + job.Id + ": " + job.Payload.Reference + " is already in Sage as order " + existing.Value + " - acknowledging, not re-posting");
                Acknowledge(job.Id, existing.Value);
                return existing.Value;
            }

            // 4. Post, remember, tell.
            var sageNo = sage.PostSalesOrder(job.Payload);
            File.WriteAllText(marker, sageNo.ToString(CultureInfo.InvariantCulture));
            Acknowledge(job.Id, sageNo);
            File.Delete(marker);
            return sageNo;
        }

        // The post has happened; failing to say so must never turn into a second post, so
        // this tries hard, and when it gives up the marker stays for the next hand-out.
        private void Acknowledge(int jobId, int sageNo)
        {
            ApiException last = null;
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    _api.Complete(jobId, sageNo);
                    return;
                }
                catch (ApiException ex)
                {
                    last = ex;
                    // 4xx: the API understood and said no (job gone, or already Done under
                    // another number). Repeating the question won't change the answer.
                    if (ex.StatusCode >= 400 && ex.StatusCode < 500) break;
                    Log.Warn("Job " + jobId + ": could not report completion (attempt " + attempt + "): " + ex.Message);
                    Thread.Sleep(TimeSpan.FromSeconds(5 * attempt));
                }
            }
            throw new ApiException(last == null ? 0 : last.StatusCode,
                "Job " + jobId + " WAS posted as Sage order " + sageNo + " but the API could not be told: "
                + (last == null ? "" : last.Message) + ". The done-marker is kept and will be acknowledged when the job is handed out again.");
        }

        private void TryFail(ClaimedJob job, string error, bool final)
        {
            if (job == null) return;
            try
            {
                var r = _api.Fail(job.Id, error, final);
                Log.Info("Job " + job.Id + ": " + (r.Final ? "FAILED after " + r.Attempts + " attempt(s)" : "will retry (" + r.Attempts + "/3)"));
            }
            catch (Exception ex)
            {
                Log.Error("Job " + job.Id + ": could not record the failure: " + ex.Message);
            }
        }

        private void TryRelease(ClaimedJob job, string reason)
        {
            if (job == null) return;
            try
            {
                _api.Release(job.Id, reason);
                Log.Info("Job " + job.Id + ": released back to the queue (no attempt spent)");
            }
            catch (Exception ex)
            {
                Log.Error("Job " + job.Id + ": could not release: " + ex.Message);
            }
        }

        // ---- customer mirror -------------------------------------------------------------

        // Every SyncCheckEvery: is a pull wanted (Admin pressed Refresh, or the interval has
        // passed)? If so read the whole ledger and address book and hand them to the API.
        // Shares the SDO connection with the exports — same thread, same Sage seat.
        private void MaybeSyncCustomers(ref SageSopWriter sage)
        {
            if (DateTime.UtcNow < _nextSyncCheck) return;
            _nextSyncCheck = DateTime.UtcNow + SyncCheckEvery;

            SyncState state;
            try
            {
                state = _api.GetSyncStates()
                    .Find(s => string.Equals(s.Entity, CustomersEntity, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Warn("Customer sync state: " + ex.Message);
                return;
            }
            if (state == null) return;

            var requested = state.RequestedUtc.HasValue;
            var due = _cfg.CustomerSyncMinutes > 0
                && DateTime.UtcNow >= _customersNotDueBefore
                && (state.LastSuccessUtc == null
                    || Utc(state.LastSuccessUtc.Value).AddMinutes(_cfg.CustomerSyncMinutes) <= DateTime.UtcNow);
            if (!requested && !due) return;

            Log.Info("Customer sync " + (requested ? "requested from Admin" : "due") + " - reading Sage...");
            var sw = Stopwatch.StartNew();
            try
            {
                EnsureSage(ref sage);
                sage.CancelCheck = CancelWatch(CustomersEntity, state.CancelRequestedUtc);
                var customers = sage.ListCustomers(0);
                var addresses = sage.ListAddresses(0);
                var readMs = (int)sw.ElapsedMilliseconds;
                var result = _api.PutCustomers(new CustomerSnapshot
                {
                    Machine = _machine,
                    Company = sage.Describe(),
                    ReadAt = DateTime.Now,
                    ReadMs = readMs,
                    Customers = customers,
                    Addresses = addresses,
                });
                Log.Info("Customer sync done: " + result.Customers + " customers, " + result.Addresses + " addresses (Sage read "
                    + (readMs / 1000.0).ToString("N0", CultureInfo.InvariantCulture) + "s, total "
                    + sw.Elapsed.TotalSeconds.ToString("N0", CultureInfo.InvariantCulture) + "s)");
            }
            catch (OperationCanceledException)
            {
                // Not a failure: the mirror keeps what it had and nothing is recorded against it.
                Log.Info("Customer sync cancelled from Admin after "
                    + sw.Elapsed.TotalSeconds.ToString("N0", CultureInfo.InvariantCulture) + "s");
                _customersNotDueBefore = DateTime.UtcNow.AddMinutes(Math.Max(1, _cfg.CustomerSyncMinutes));
            }
            catch (SageConnectException ex)
            {
                Log.Error("Customer sync: cannot get into Sage - " + ex.Message);
                DisposeSage(ref sage);
                TryReportSyncFailure(ex.Message);
                _nextSyncCheck = DateTime.UtcNow + SyncFailureBackoff;
            }
            catch (Exception ex)
            {
                Log.Error("Customer sync failed: " + ex.Message, ex);
                DisposeSage(ref sage);
                TryReportSyncFailure(ex.Message);
                _nextSyncCheck = DateTime.UtcNow + SyncFailureBackoff;
            }
            finally
            {
                if (sage != null) sage.CancelCheck = null;
            }
        }

        // The stop test handed to SageSopWriter for one pull: has Admin pressed Cancel since the
        // pull started? Compared with the CancelRequestedUtc seen at the start, never with the
        // clock, so the API's and this machine's clocks need not agree and an old Cancel cannot
        // stop a new pull. An API that cannot be reached is not a reason to stop.
        private Func<bool> CancelWatch(string entity, DateTime? atStart)
        {
            return () =>
            {
                try
                {
                    var s = _api.GetSyncStates().Find(x => string.Equals(x.Entity, entity, StringComparison.OrdinalIgnoreCase));
                    return s != null && s.CancelRequestedUtc.HasValue && s.CancelRequestedUtc != atStart;
                }
                catch (Exception ex)
                {
                    Log.Warn(entity + " sync: cancel check failed - " + ex.Message);
                    return false;
                }
            };
        }

        private void TryReportSyncFailure(string error)
        {
            try { _api.SyncFailed(CustomersEntity, error); }
            catch (Exception ex) { Log.Error("Customer sync: could not record the failure: " + ex.Message); }
        }

        // ---- sales mirror ----------------------------------------------------------------

        // Same shape as the customer sync, for the audit trail + invoice lines behind the
        // Reports pages. Checked on its own timer (offset so the two checks do not coincide)
        // and, being the longer read (a few minutes), with the same failure backoff.
        private void MaybeSyncSales(ref SageSopWriter sage)
        {
            if (DateTime.UtcNow < _nextSalesCheck) return;
            _nextSalesCheck = DateTime.UtcNow + SyncCheckEvery;

            SyncState state;
            try
            {
                state = _api.GetSyncStates()
                    .Find(s => string.Equals(s.Entity, SalesEntity, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Log.Warn("Sales sync state: " + ex.Message);
                return;
            }
            if (state == null) return;   // the API has not had the sales migration yet

            var requested = state.RequestedUtc.HasValue;
            var due = _cfg.SalesSyncMinutes > 0
                && DateTime.UtcNow >= _salesNotDueBefore
                && (state.LastSuccessUtc == null
                    || Utc(state.LastSuccessUtc.Value).AddMinutes(_cfg.SalesSyncMinutes) <= DateTime.UtcNow);
            if (!requested && !due) return;

            Log.Info("Sales sync " + (requested ? "requested from Admin" : "due") + " - reading Sage from "
                + _cfg.SalesHistoryFrom.ToString("yyyy-MM-dd") + "...");
            var sw = Stopwatch.StartNew();
            try
            {
                EnsureSage(ref sage);
                sage.CancelCheck = CancelWatch(SalesEntity, state.CancelRequestedUtc);
                var snap = sage.ReadSalesMirror(_cfg.SalesHistoryFrom, _cfg.SalesScanStopRun);
                snap.Machine = _machine;
                var result = _api.PutSales(snap);
                Log.Info("Sales sync done: " + result.Transactions + " transactions, " + result.Invoices + " invoices, " + result.Lines
                    + " lines (scanned " + snap.HeadersScanned + " audit entries + " + snap.InvoicesScanned + " invoices; Sage read "
                    + (snap.ReadMs / 1000.0).ToString("N0", CultureInfo.InvariantCulture) + "s, total "
                    + sw.Elapsed.TotalSeconds.ToString("N0", CultureInfo.InvariantCulture) + "s)");
                foreach (var n in snap.Notes) Log.Warn("Sales sync: " + n);
            }
            catch (OperationCanceledException)
            {
                Log.Info("Sales sync cancelled from Admin after "
                    + sw.Elapsed.TotalSeconds.ToString("N0", CultureInfo.InvariantCulture) + "s");
                _salesNotDueBefore = DateTime.UtcNow.AddMinutes(Math.Max(1, _cfg.SalesSyncMinutes));
            }
            catch (SageConnectException ex)
            {
                Log.Error("Sales sync: cannot get into Sage - " + ex.Message);
                DisposeSage(ref sage);
                TryReportSalesSyncFailure(ex.Message);
                _nextSalesCheck = DateTime.UtcNow + SyncFailureBackoff;
            }
            catch (Exception ex)
            {
                Log.Error("Sales sync failed: " + ex.Message, ex);
                DisposeSage(ref sage);
                TryReportSalesSyncFailure(ex.Message);
                _nextSalesCheck = DateTime.UtcNow + SyncFailureBackoff;
            }
            finally
            {
                if (sage != null) sage.CancelCheck = null;
            }
        }

        private void TryReportSalesSyncFailure(string error)
        {
            try { _api.SyncFailed(SalesEntity, error); }
            catch (Exception ex) { Log.Error("Sales sync: could not record the failure: " + ex.Message); }
        }

        // ---- shared ----------------------------------------------------------------------

        private void EnsureSage(ref SageSopWriter sage)
        {
            if (sage != null) return;
            var w = new SageSopWriter(_cfg);
            try { w.Connect(); }
            catch { w.Dispose(); throw; }
            sage = w;
            Log.Info("Connected: " + sage.Describe());
        }

        private void PingApi()
        {
            try
            {
                var r = _api.Ping();
                Log.Info("API reachable" + (r.AutoExportEnabled
                    ? ""
                    : " - automatic export is OFF on the server (Sage:ExportEnabled), only manual exports will arrive"));
            }
            catch (Exception ex)
            {
                Log.Warn("API not reachable yet: " + ex.Message);
            }
        }

        private string MarkerPath(int jobId)
        {
            return Path.Combine(_cfg.StateDir, "job-" + jobId + ".done");
        }

        private static string JobId(ClaimedJob job)
        {
            return job == null ? "?" : job.Id.ToString(CultureInfo.InvariantCulture);
        }

        // The API's times are UTC; JSON without a "Z" comes back Unspecified and must not be
        // read as local.
        private static DateTime Utc(DateTime d)
        {
            return d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : d.ToUniversalTime();
        }

        // True when cancelled during the wait.
        private static bool Sleep(TimeSpan duration, CancellationToken ct)
        {
            return ct.WaitHandle.WaitOne(duration);
        }

        private static void DisposeSage(ref SageSopWriter sage)
        {
            if (sage == null) return;
            try { sage.Dispose(); } catch { /* going away anyway */ }
            sage = null;
        }
    }
}
