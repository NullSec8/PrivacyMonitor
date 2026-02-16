using System;
using System.Collections.Generic;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Tab-scoped runtime fingerprint signal provider. MainWindow feeds signals from WebView2
    /// postMessage (cat:'fp'); this provider returns signals for the attached tab only.
    /// Sandboxed JS (PrivacyEngine.FingerprintDetectionScript) runs in document context;
    /// signals are mapped to short names for RiskScoring (canvas, webgl, audioctx, etc.).
    /// </summary>
    public sealed class TabScopedRuntimeFingerprintProvider : IRuntimeFingerprintSignalProvider
    {
        private readonly string _tabId;
        private readonly IDictionary<string, List<string>> _signalsByTab;

        public TabScopedRuntimeFingerprintProvider(string tabId, IDictionary<string, List<string>> signalsByTab)
        {
            _tabId = tabId ?? "";
            _signalsByTab = signalsByTab ?? throw new ArgumentNullException(nameof(signalsByTab));
        }

        public IReadOnlyList<string> GetActiveSignals()
        {
            lock (_signalsByTab)
            {
                if (_signalsByTab.TryGetValue(_tabId, out var list) && list.Count > 0)
                    return list.ToArray();
            }
            return Array.Empty<string>();
        }

        public void ClearSignals()
        {
            lock (_signalsByTab)
            {
                if (_signalsByTab.TryGetValue(_tabId, out var list))
                    list.Clear();
            }
        }

        /// <summary>Map script-reported type (e.g. "Canvas Fingerprinting") to RiskScoring signal name.</summary>
        public static string MapFingerprintTypeToSignal(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return "";
            var t = type.Trim();
            if (t.Contains("Canvas", StringComparison.OrdinalIgnoreCase)) return "canvas";
            if (t.Contains("WebGL", StringComparison.OrdinalIgnoreCase)) return "webgl";
            if (t.Contains("Audio", StringComparison.OrdinalIgnoreCase)) return "audioctx";
            if (t.Contains("Navigator", StringComparison.OrdinalIgnoreCase)) return "client-hints";
            if (t.Contains("Screen", StringComparison.OrdinalIgnoreCase)) return "client-hints";
            if (t.Contains("Font", StringComparison.OrdinalIgnoreCase)) return "canvas";
            if (t.Contains("Battery", StringComparison.OrdinalIgnoreCase)) return "client-hints";
            if (t.Contains("MediaDevice", StringComparison.OrdinalIgnoreCase)) return "client-hints";
            if (t.Contains("Timezone", StringComparison.OrdinalIgnoreCase)) return "client-hints";
            if (t.Contains("Performance", StringComparison.OrdinalIgnoreCase)) return "client-hints";
            if (t.Contains("Plugin", StringComparison.OrdinalIgnoreCase)) return "client-hints";
            if (t.Contains("Network", StringComparison.OrdinalIgnoreCase)) return "client-hints";
            if (t.Contains("Behavioral", StringComparison.OrdinalIgnoreCase)) return "behavioral";
            if (t.Contains("Evercookie", StringComparison.OrdinalIgnoreCase)) return "evercookie";
            return "client-hints";
        }
    }
}
