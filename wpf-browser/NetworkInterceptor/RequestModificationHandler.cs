using System.Collections.Generic;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Modifies request/response headers for replay or inspection. Use with ReplayOptions
    /// or inject into RequestReplayHandler for header overrides before replay.
    /// </summary>
    public sealed class RequestModificationHandler : IRequestModificationHandler
    {
        private readonly IReadOnlyDictionary<string, string>? _requestOverrides;
        private readonly IReadOnlyDictionary<string, string>? _responseOverrides;

        public RequestModificationHandler(
            IReadOnlyDictionary<string, string>? requestOverrides = null,
            IReadOnlyDictionary<string, string>? responseOverrides = null)
        {
            _requestOverrides = requestOverrides;
            _responseOverrides = responseOverrides;
        }

        public bool TryModifyRequest(InterceptedRequestItem request, out IReadOnlyDictionary<string, string>? newHeaders)
        {
            if (_requestOverrides == null || _requestOverrides.Count == 0)
            {
                newHeaders = null;
                return false;
            }
            var merged = new Dictionary<string, string>(request.RequestHeaders ?? new Dictionary<string, string>(), System.StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _requestOverrides)
                merged[kv.Key] = kv.Value;
            newHeaders = merged;
            return true;
        }

        public bool TryModifyResponse(InterceptedRequestItem request, out IReadOnlyDictionary<string, string>? newHeaders)
        {
            if (_responseOverrides == null || _responseOverrides.Count == 0)
            {
                newHeaders = null;
                return false;
            }
            var merged = new Dictionary<string, string>(request.ResponseHeaders ?? new Dictionary<string, string>(), System.StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _responseOverrides)
                merged[kv.Key] = kv.Value;
            newHeaders = merged;
            return true;
        }
    }
}
