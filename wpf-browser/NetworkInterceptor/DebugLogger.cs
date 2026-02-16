using System;
using System.Diagnostics;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>Logger that writes to System.Diagnostics.Debug. Use for production diagnostics.</summary>
    public sealed class DebugLogger : IInterceptorLogger
    {
        private readonly string _prefix;

        public DebugLogger(string prefix = "Interceptor")
        {
            _prefix = prefix ?? "Interceptor";
        }

        public void Debug(string message) => Write("DBG", message, null);
        public void Info(string message) => Write("INF", message, null);
        public void Warn(string message) => Write("WRN", message, null);
        public void Error(string message, Exception? ex = null) => Write("ERR", message, ex);

        private void Write(string level, string message, Exception? ex)
        {
            var line = $"[{_prefix}] [{level}] {message}";
            if (ex != null)
                line += " | " + ex.Message;
            System.Diagnostics.Debug.WriteLine(line);
            if (ex != null)
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
        }
    }
}
