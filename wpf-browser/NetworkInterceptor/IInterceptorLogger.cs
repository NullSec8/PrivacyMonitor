using System;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>Abstraction for interceptor logging. Use for diagnostics and unit tests.</summary>
    public interface IInterceptorLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message, Exception? ex = null);
    }
}
