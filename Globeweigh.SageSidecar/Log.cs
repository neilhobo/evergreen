using System;
using System.IO;

namespace Globeweigh.SageSidecar
{
    // Console AND Logs\SageSidecar-yyyyMMdd.log. The file half is what matters: as a Windows
    // service there is no console, and a worker that logged only there would be invisible in
    // production. Job outcomes are also on the queue row itself (LastError, SageOrderNo); the
    // log is for the story between the rows.
    public static class Log
    {
        private static readonly object Gate = new object();
        private static string _dir;

        public static void Init(string dir)
        {
            _dir = dir;
            try { Directory.CreateDirectory(dir); } catch { /* console only, then */ }
        }

        public static void Info(string msg) { Write("INFO ", msg); }
        public static void Warn(string msg) { Write("WARN ", msg); }
        public static void Error(string msg, Exception ex = null)
        {
            Write("ERROR", ex == null ? msg : msg + Environment.NewLine + ex);
        }

        private static void Write(string level, string msg)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + level + " " + msg;
            lock (Gate)
            {
                Console.WriteLine(line);
                if (_dir == null) return;
                try
                {
                    var file = Path.Combine(_dir, "SageSidecar-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                    File.AppendAllText(file, line + Environment.NewLine);
                }
                catch
                {
                    // Logging must never take the worker down.
                }
            }
        }
    }
}
