using System.Collections.Generic;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Optional provider for runtime fingerprint signals (e.g. from injected JS hooks
    /// detecting canvas, WebGL, AudioContext usage). When the host injects scripts and
    /// detects API usage, it can push signals here; RiskScoring can weight them.
    /// </summary>
    public interface IRuntimeFingerprintSignalProvider
    {
        /// <summary>Get current runtime signals (e.g. "canvas", "webgl", "audioctx").</summary>
        IReadOnlyList<string> GetActiveSignals();

        /// <summary>Optional: clear signals after consumption (e.g. per-request).</summary>
        void ClearSignals();
    }
}
