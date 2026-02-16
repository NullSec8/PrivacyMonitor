using System;
using System.Collections.Generic;
using PrivacyMonitor;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Sink for live network request/response events. Implemented by the interceptor service;
    /// MainWindow pushes to it when a tab is being monitored.
    /// </summary>
    public interface INetworkInterceptorSink
    {
        void RecordRequest(string tabId, RequestEntry entry);
        void RecordResponse(string tabId, string requestUri, int statusCode,
            IReadOnlyDictionary<string, string> responseHeaders,
            string contentType, long responseSize);
    }

    /// <summary>
    /// Extended sink for replay: record request/response by correlation ID so replayed
    /// requests are matched correctly without URL collision.
    /// </summary>
    public interface IReplayCapableSink : INetworkInterceptorSink
    {
        void RecordReplayRequest(string tabId, RequestEntry entry, Guid correlationId, bool isModifiedReplay = false);
        void RecordResponseByCorrelation(string tabId, Guid correlationId, string requestUri, int statusCode,
            IReadOnlyDictionary<string, string> responseHeaders,
            string contentType, long responseSize);
        /// <summary>Mark a replayed request (already in grid) as failed; updates row and batch UI.</summary>
        void SetReplayFailed(Guid correlationId, string errorMessage);
    }
}
