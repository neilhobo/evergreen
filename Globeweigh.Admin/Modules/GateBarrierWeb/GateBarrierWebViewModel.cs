using System;
using System.Configuration;
using System.Windows;
using Globeweigh.Model;
using Globeweigh.Shared;

namespace Globeweigh.Admin
{
    /// <summary>
    /// Experimental web-tech version of the Gate/Barrier view. Hosts the React UI
    /// (Globeweigh.WebAdmin) in a WebView2 control; all data/relay calls go through
    /// Globeweigh.Api. No VLC, no relay TCP, no repositories here — see the original
    /// <see cref="GateBarrierViewModel"/> for the native version that runs alongside this.
    /// </summary>
    public class GateBarrierWebViewModel : AdminViewModelBase, IViewModel
    {
        private Uri _sourceUri;
        public Uri SourceUri
        {
            get { return _sourceUri; }
            set { Set(ref _sourceUri, value); }
        }

        public GateBarrierWebViewModel()
        {
            SourceUri = new Uri(ResolveWebUrl());
        }

        /// <summary>
        /// URL the WebView2 loads. By default the built UI served by Globeweigh.Api at
        /// <c>&lt;ApiBaseUrl&gt;/gate-web/</c> — so only the API needs to be running, and it
        /// follows local/prod automatically (see <see cref="Database.SetDatabaseConnectionString"/>).
        /// Override via app.config <appSettings> key "GateBarrierWebUrl" — e.g. set it to
        /// http://localhost:1420 to use the Vite dev server (npm run dev) for hot reload.
        /// </summary>
        private static string ResolveWebUrl()
        {
            var overrideUrl = ConfigurationManager.AppSettings["GateBarrierWebUrl"];
            if (!string.IsNullOrWhiteSpace(overrideUrl)) return overrideUrl;

            var apiBase = GlobalVariables.ApiBaseUrl;
            if (string.IsNullOrWhiteSpace(apiBase)) apiBase = "http://localhost:5099";
            return apiBase.TrimEnd('/') + "/gate-web/";
        }

        public void Load(FrameworkElement element)
        {
            // WebView2 initialises itself and navigates when Source is bound; nothing to do.
        }

        public void Unload(FrameworkElement element)
        {
        }
    }
}
