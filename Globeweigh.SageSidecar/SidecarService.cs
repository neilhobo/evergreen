using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;

namespace Globeweigh.SageSidecar
{
    public class SidecarService : ServiceBase
    {
        public const string Name = "GlobeweighSageSidecar";

        private readonly SidecarConfig _cfg;
        private CancellationTokenSource _cts;
        private Thread _worker;

        public SidecarService(SidecarConfig cfg)
        {
            _cfg = cfg;
            ServiceName = Name;
            CanStop = true;
            CanPauseAndContinue = false;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _cts = new CancellationTokenSource();
            // A worker that dies takes the service down with it (ExitCode 1), so the SCM's
            // recovery actions restart it and the failure shows in Services — rather than a
            // "Running" service that does nothing. Stop() is queued off the worker thread:
            // OnStop joins that thread, and a thread cannot join itself.
            _worker = SidecarHost.Start(_cfg, _cts.Token, ex =>
            {
                try { EventLog.WriteEntry(ServiceName, "Sage sidecar stopped: " + ex, EventLogEntryType.Error); } catch { }
                ExitCode = 1;
                ThreadPool.QueueUserWorkItem(_ => Stop());
            });
        }

        protected override void OnStop()
        {
            if (_cts != null) _cts.Cancel();
            if (_worker != null && !_worker.Join(TimeSpan.FromSeconds(30)))
            {
                try { EventLog.WriteEntry(ServiceName, "Worker did not stop within 30s", EventLogEntryType.Warning); } catch { }
            }
        }
    }

    public static class SidecarHost
    {
        // The worker thread. STA, because SDO is an apartment-threaded COM server and every
        // one of its objects must be created and used on the thread that owns them.
        public static Thread Start(SidecarConfig cfg, CancellationToken ct, Action<Exception> onCrash)
        {
            var t = new Thread(() =>
            {
                try
                {
                    using (var api = new ApiClient(cfg))
                        new ExportWorker(cfg, api).Run(ct);
                }
                catch (Exception ex)
                {
                    Log.Error("Sage sidecar worker stopped with an error", ex);
                    if (onCrash != null) onCrash(ex);
                }
            });
            t.Name = "SageExport";
            t.IsBackground = false;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            return t;
        }
    }
}
