using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace Globeweigh.SageSidecar
{
    // The sidecar's view of the API: SageController's /api/sage/sidecar/* endpoints (the
    // export queue) and SageCustomersController's (the customer mirror), authenticated with
    // the shared X-Sidecar-Key. Deliberately synchronous — the caller is the STA thread that
    // owns the SDO objects, and an await would resume on the thread pool.
    public sealed class ApiClient : IDisposable
    {
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

        private readonly HttpClient _http;

        public ApiClient(SidecarConfig cfg)
        {
            _http = new HttpClient
            {
                BaseAddress = new Uri(cfg.ApiBaseUrl.TrimEnd('/') + "/"),
                // Per request instead (Send): the customer snapshot needs longer than a claim.
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _http.DefaultRequestHeaders.Add("X-Sidecar-Key", cfg.SidecarKey ?? "");
            _http.DefaultRequestHeaders.Add("User-Agent", "Globeweigh.SageSidecar (" + Environment.MachineName + ")");
        }

        // ---- export queue ----

        public PingResult Ping()
        {
            using (var resp = Send(HttpMethod.Get, "api/sage/sidecar/ping", null))
                return Read<PingResult>(resp);
        }

        // The next job, or null when the queue is empty (204).
        public ClaimedJob Claim(string machine)
        {
            using (var resp = Send(HttpMethod.Post, "api/sage/sidecar/claim", new { machine }))
            {
                if (resp.StatusCode == HttpStatusCode.NoContent) return null;
                return Read<ClaimedJob>(resp);
            }
        }

        public void Complete(int jobId, int sageOrderNo)
        {
            using (Send(HttpMethod.Post, "api/sage/sidecar/jobs/" + jobId + "/complete", new { sageOrderNo })) { }
        }

        public FailResult Fail(int jobId, string error, bool final)
        {
            using (var resp = Send(HttpMethod.Post, "api/sage/sidecar/jobs/" + jobId + "/fail", new { error, final }))
                return Read<FailResult>(resp);
        }

        public void Release(int jobId, string reason)
        {
            using (Send(HttpMethod.Post, "api/sage/sidecar/jobs/" + jobId + "/release", new { reason })) { }
        }

        // ---- customer mirror ----

        public List<SyncState> GetSyncStates()
        {
            using (var resp = Send(HttpMethod.Get, "api/sage/sidecar/sync", null))
                return Read<List<SyncState>>(resp);
        }

        // A few MB and a few seconds of server work: its own, longer timeout.
        public SnapshotResult PutCustomers(CustomerSnapshot snapshot)
        {
            using (var resp = Send(HttpMethod.Put, "api/sage/sidecar/customers", snapshot, TimeSpan.FromMinutes(3)))
                return Read<SnapshotResult>(resp);
        }

        // The sales mirror (audit trail + invoice lines for the window): bigger than the
        // customers and more server work, so a longer timeout again.
        public SalesSnapshotResult PutSales(SageSopWriter.SalesSnapshot snapshot)
        {
            using (var resp = Send(HttpMethod.Put, "api/sage/sidecar/sales", snapshot, TimeSpan.FromMinutes(10)))
                return Read<SalesSnapshotResult>(resp);
        }

        public void SyncFailed(string entity, string error)
        {
            using (Send(HttpMethod.Post, "api/sage/sidecar/sync/" + entity + "/failed", new { error, machine = Environment.MachineName })) { }
        }

        // ---- plumbing ----

        private HttpResponseMessage Send(HttpMethod method, string path, object body, TimeSpan? timeout = null)
        {
            var req = new HttpRequestMessage(method, path);
            if (body != null)
                req.Content = new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json");

            HttpResponseMessage resp;
            try
            {
                using (var cts = new CancellationTokenSource(timeout ?? DefaultTimeout))
                    resp = _http.SendAsync(req, cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                throw new ApiException(0, "Could not reach the API at " + _http.BaseAddress + path + ": " + Root(ex).Message, ex);
            }

            if (!resp.IsSuccessStatusCode)
            {
                var status = (int)resp.StatusCode;
                var detail = SafeBody(resp);
                resp.Dispose();
                throw new ApiException(status, method + " " + path + " -> " + status + " " + resp.ReasonPhrase
                    + (detail.Length > 0 ? ": " + detail : ""));
            }
            return resp;
        }

        private static T Read<T>(HttpResponseMessage resp)
        {
            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return JsonConvert.DeserializeObject<T>(json);
        }

        private static string SafeBody(HttpResponseMessage resp)
        {
            try
            {
                var s = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
                s = s.Replace("\r", " ").Replace("\n", " ").Trim();
                return s.Length <= 400 ? s : s.Substring(0, 400);
            }
            catch { return ""; }
        }

        private static Exception Root(Exception ex)
        {
            // A timeout surfaces as a cancellation with no inner exception; say what it was.
            if (ex is OperationCanceledException) return new TimeoutException("timed out");
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }

        public void Dispose() { _http.Dispose(); }
    }

    // 0 = the API could not be reached at all; otherwise the HTTP status it answered with.
    public class ApiException : Exception
    {
        public int StatusCode { get; private set; }
        public ApiException(int statusCode, string message, Exception inner = null) : base(message, inner)
        {
            StatusCode = statusCode;
        }
    }

    public class PingResult
    {
        public bool Ok { get; set; }
        public DateTime Utc { get; set; }
        public bool AutoExportEnabled { get; set; }
    }

    public class ClaimedJob
    {
        public int Id { get; set; }
        public int DispatchOrderId { get; set; }
        public int OrderNo { get; set; }
        public int Attempts { get; set; }
        public string Reason { get; set; }
        public SalesOrderPayload Payload { get; set; }
    }

    public class FailResult
    {
        public int Id { get; set; }
        public string Status { get; set; }
        public int Attempts { get; set; }
        public bool Final { get; set; }
    }

    public class SyncState
    {
        public string Entity { get; set; }
        public DateTime? RequestedUtc { get; set; }
        public DateTime? LastSuccessUtc { get; set; }
        // Admin's Cancel; a pull stops when this differs from the value it started with.
        public DateTime? CancelRequestedUtc { get; set; }
    }

    // What PUT /api/sage/sidecar/customers takes: the ledger and address book as read.
    // Field names match the API's SageCustomerMirror.Snapshot.
    public class CustomerSnapshot
    {
        public string Machine { get; set; }
        public string Company { get; set; }
        public DateTime ReadAt { get; set; }
        public int ReadMs { get; set; }
        public List<SageSopWriter.CustomerRow> Customers { get; set; }
        public List<SageSopWriter.AddressRow> Addresses { get; set; }
    }

    public class SnapshotResult
    {
        public int Customers { get; set; }
        public int Addresses { get; set; }
    }

    public class SalesSnapshotResult
    {
        public int Transactions { get; set; }
        public int Invoices { get; set; }
        public int Lines { get; set; }
    }
}
