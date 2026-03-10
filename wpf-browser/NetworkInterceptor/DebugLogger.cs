using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Logger that writes diagnostic messages to System.Diagnostics.Debug,
    /// enriches logs with timestamp, thread, and supports event notification.
    /// Useful for production diagnostics and automated test listeners.
    /// </summary>
    public sealed class DebugLogger : IInterceptorLogger, IDisposable
    {
        private readonly string _prefix;
        private readonly object _lock = new();
        private readonly StringBuilder _recentBuffer = new();
        private readonly int _bufferLimit = 20 * 1024; // 20 KB buffer for recent logs

        /// <summary>
        /// Raised when a new log message is written.
        /// </summary>
        public event Action<string>? LogWritten;

        /// <summary>
        /// If set, additionally write log lines to this TextWriter (e.g. file, UI).
        /// </summary>
        public TextWriter? Output { get; set; }

        public DebugLogger(string prefix = "Interceptor")
        {
            _prefix = string.IsNullOrWhiteSpace(prefix) ? "Interceptor" : prefix;
        }

        public void Debug(string message) => Write("DBG", message);
        public void Info(string message) => Write("INF", message);
        public void Warn(string message) => Write("WRN", message);
        public void Error(string message, Exception? ex = null) => Write("ERR", message, ex);

        private void Write(string level, string message, Exception? ex = null)
        {
            var time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var tid = Environment.CurrentManagedThreadId;
            var fullLine = $"[{time}] [{_prefix}] [{level}] (T{tid}) {message}";
            if (ex != null)
                fullLine += $" | {ex.GetType().Name}: {ex.Message}";

            lock (_lock)
            {
                // Add to rolling buffer for post-mortem debug
                _recentBuffer.AppendLine(fullLine);
                if (ex != null)
                    _recentBuffer.AppendLine(ex.StackTrace ?? "");
                // Trim buffer if needed
                if (_recentBuffer.Length > _bufferLimit)
                {
                    var extra = _recentBuffer.Length - _bufferLimit;
                    _recentBuffer.Remove(0, extra / 2); // Consume half overflow
                }
            }

            System.Diagnostics.Debug.WriteLine(fullLine);
            if (ex != null)
                System.Diagnostics.Debug.WriteLine(ex.ToString());

            Output?.WriteLine(fullLine);
            if (ex != null)
                Output?.WriteLine(ex);

            LogWritten?.Invoke(fullLine + (ex != null ? Environment.NewLine + ex : ""));
        }

        /// <summary>
        /// Returns recent log messages (approx. last 20kb).
        /// </summary>
        public string GetRecentLogs()
        {
            lock (_lock)
                return _recentBuffer.ToString();
        }

        public void Dispose()
        {
            Output?.Dispose();
        }
    }
}
