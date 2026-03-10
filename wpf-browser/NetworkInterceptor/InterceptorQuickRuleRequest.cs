namespace PrivacyMonitor.NetworkInterceptor
{
    public enum InterceptorQuickRuleAction
    {
        BlockPathContains,
        ForceHttps,
        RewriteHeader
    }

    public sealed class InterceptorQuickRuleRequest
    {
        public InterceptorQuickRuleAction Action { get; set; }
        public string Value { get; set; } = "";
        public string HeaderName { get; set; } = "";
        public string HeaderValue { get; set; } = "";
    }
}
