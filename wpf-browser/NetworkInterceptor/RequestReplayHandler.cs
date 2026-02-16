using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using PrivacyMonitor;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>
    /// Replays captured requests via HttpClient. Records replayed request/response through
    /// the same sink so they appear in the grid and go through RiskScoring + logging.
    /// </summary>
    public sealed class RequestReplayHandler : IRequestReplayHandler
    {
        private readonly IReplayCapableSink _sink;
        private readonly string _tabId;
        private readonly IRequestModificationHandler? _modifier;
        private readonly IInterceptorLogger? _logger;
        private static readonly HttpClient SharedClient = new HttpClient(new HttpClientHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };

        public RequestReplayHandler(IReplayCapableSink sink, string tabId,
            IRequestModificationHandler? modifier = null,
            IInterceptorLogger? logger = null)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _tabId = tabId ?? throw new ArgumentNullException(nameof(tabId));
            _modifier = modifier;
            _logger = logger;
        }

        public async Task<bool> ReplayAsync(InterceptedRequestItem request, ReplayOptions? options = null)
        {
            if (request == null || string.IsNullOrEmpty(request.FullUrl))
                return false;

            options ??= new ReplayOptions();
            var correlationId = Guid.NewGuid();

            try
            {
                if (!TryValidateUri(request.FullUrl, out var failureMessage))
                {
                    _sink.RecordReplayRequest(_tabId, BuildSyntheticEntry(request, options), correlationId, false);
                    _sink.SetReplayFailed(correlationId, failureMessage ?? "Invalid URL");
                    return false;
                }

                var entry = BuildSyntheticEntry(request, options);
                IReadOnlyDictionary<string, string>? newHeaders = null;
                var usedModifier = _modifier != null && _modifier.TryModifyRequest(request, out newHeaders) && newHeaders != null;
                if (usedModifier && newHeaders != null)
                    entry.RequestHeaders = new Dictionary<string, string>(newHeaders);
                else if (options.OverrideHeaders != null && options.OverrideHeaders.Count > 0)
                {
                    var merged = new Dictionary<string, string>(entry.RequestHeaders, StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in options.OverrideHeaders)
                        merged[kv.Key] = kv.Value;
                    entry.RequestHeaders = merged;
                }
                var isModifiedReplay = usedModifier || (options.OverrideHeaders != null && options.OverrideHeaders.Count > 0);
                _sink.RecordReplayRequest(_tabId, entry, correlationId, isModifiedReplay);

                using var req = new HttpRequestMessage(ToHttpMethod(request.Method), request.FullUrl);
                string? contentTypeHeader = null;
                var hasBody = !string.IsNullOrEmpty(options.OverrideBody) && IsBodyAllowed(request.Method);
                foreach (var h in entry.RequestHeaders)
                {
                    var key = h.Key;
                    if (string.Equals(key, "content-type", StringComparison.OrdinalIgnoreCase))
                    {
                        contentTypeHeader = h.Value;
                        if (!hasBody)
                            TryAddHeader(req, key, h.Value);
                    }
                    else if (string.Equals(key, "content-length", StringComparison.OrdinalIgnoreCase))
                        continue;
                    else
                        TryAddHeader(req, key, h.Value);
                }
                if (hasBody)
                {
                    var mediaType = contentTypeHeader ?? "application/octet-stream";
                    req.Content = new StringContent(options.OverrideBody!, Encoding.UTF8, mediaType);
                }

                using var response = await SharedClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                var responseHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var header in response.Headers)
                    responseHeaders[header.Key] = string.Join(" ", header.Value);
                if (response.Content?.Headers != null)
                {
                    foreach (var header in response.Content.Headers)
                        responseHeaders[header.Key] = string.Join(" ", header.Value);
                }
                var contentType = response.Content?.Headers?.ContentType?.ToString() ?? "";
                var size = response.Content?.Headers?.ContentLength ?? 0;

                _sink.RecordResponseByCorrelation(_tabId, correlationId, request.FullUrl,
                    (int)response.StatusCode, responseHeaders, contentType, size);

                _logger?.Info($"Replay completed: {request.Method} {request.FullUrl} -> {(int)response.StatusCode}");
                return true;
            }
            catch (Exception ex)
            {
                _sink.SetReplayFailed(correlationId, ex.Message);
                _logger?.Error($"Replay failed: {request.FullUrl}", ex);
                return false;
            }
        }

        private static void TryAddHeader(HttpRequestMessage req, string name, string value)
        {
            try
            {
                req.Headers.TryAddWithoutValidation(name, value);
            }
            catch (InvalidOperationException)
            {
                // Reserved or invalid header (e.g. Host); skip so replay still succeeds
            }
        }

        private static bool TryValidateUri(string fullUrl, out string? failureMessage)
        {
            failureMessage = null;
            if (string.IsNullOrWhiteSpace(fullUrl))
            {
                failureMessage = "URL is empty.";
                return false;
            }
            if (!Uri.TryCreate(fullUrl, UriKind.Absolute, out var uri) || !uri.IsAbsoluteUri)
            {
                failureMessage = "Invalid or relative URL.";
                return false;
            }
            var scheme = uri.Scheme.ToUpperInvariant();
            if (scheme != "HTTP" && scheme != "HTTPS")
            {
                failureMessage = "Only HTTP/HTTPS URLs can be replayed.";
                return false;
            }
            return true;
        }

        private static bool IsBodyAllowed(string method)
        {
            var m = method?.ToUpperInvariant();
            return m == "POST" || m == "PUT" || m == "PATCH";
        }

        private static RequestEntry BuildSyntheticEntry(InterceptedRequestItem request, ReplayOptions options)
        {
            var reqHeaders = request.RequestHeaders ?? new Dictionary<string, string>();
            var resHeaders = request.ResponseHeaders ?? new Dictionary<string, string>();
            return new RequestEntry
            {
                Time = DateTime.Now,
                Method = request.Method ?? "",
                Host = request.Domain ?? "",
                Path = request.Path ?? "",
                FullUrl = request.FullUrl ?? "",
                IsThirdParty = request.IsThirdParty,
                RequestHeaders = new Dictionary<string, string>(reqHeaders),
                ResponseHeaders = new Dictionary<string, string>(resHeaders),
                ResourceContext = request.ResourceType ?? "",
                TrackerLabel = request.IsTracker ? "tracker" : "",
                ThreatConfidence = request.IsTracker ? 0.5 : 0
            };
        }

        private static HttpMethod ToHttpMethod(string method)
        {
            return method?.ToUpperInvariant() switch
            {
                "GET" => HttpMethod.Get,
                "POST" => HttpMethod.Post,
                "PUT" => HttpMethod.Put,
                "DELETE" => HttpMethod.Delete,
                "PATCH" => new HttpMethod("PATCH"),
                "HEAD" => HttpMethod.Head,
                "OPTIONS" => HttpMethod.Options,
                _ => HttpMethod.Get
            };
        }
    }
}
