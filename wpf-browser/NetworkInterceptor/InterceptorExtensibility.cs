using System.Collections.Generic;
using System.Threading.Tasks;

namespace PrivacyMonitor.NetworkInterceptor
{
    /// <summary>Future: replay a captured request (e.g. resend with same headers/body).</summary>
    public interface IRequestReplayHandler
    {
        Task<bool> ReplayAsync(InterceptedRequestItem request, ReplayOptions? options = null);
    }

    public class ReplayOptions
    {
        public bool ModifyHeaders { get; set; }
        public IReadOnlyDictionary<string, string>? OverrideHeaders { get; set; }
        /// <summary>Optional body for POST/PUT; sent only when Replay is clicked.</summary>
        public string? OverrideBody { get; set; }
    }

    /// <summary>Future: modify request/response before or after it is sent.</summary>
    public interface IRequestModificationHandler
    {
        bool TryModifyRequest(InterceptedRequestItem request, out IReadOnlyDictionary<string, string>? newHeaders);
        bool TryModifyResponse(InterceptedRequestItem request, out IReadOnlyDictionary<string, string>? newHeaders);
    }

    /// <summary>Future: domain blocking engine integration (block list, allow list).</summary>
    public interface IBlockedDomainProvider
    {
        bool IsBlocked(string domain);
        void BlockDomain(string domain);
        void AllowDomain(string domain);
    }

    /// <summary>Future: send captured traffic or risk events to a remote endpoint.</summary>
    public interface IRemoteLoggingSink
    {
        Task FlushAsync(IReadOnlyList<InterceptedRequestItem> batch);
    }

    /// <summary>Future: plugin or external tracker list (e.g. EasyPrivacy, Disconnect).</summary>
    public interface ITrackerListProvider
    {
        string Name { get; }
        bool IsTrackerDomain(string domain);
        string? GetTrackerLabel(string domain);
    }
}
