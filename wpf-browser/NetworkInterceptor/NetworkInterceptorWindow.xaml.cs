using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PrivacyMonitor.NetworkInterceptor
{
    public partial class NetworkInterceptorWindow : Window
    {
        private readonly NetworkInterceptorViewModel _viewModel;
        private readonly string _siteHost;
        private string _searchFilter = "";
        private readonly Action<InterceptorQuickRuleRequest>? _quickRuleCallback;

        public NetworkInterceptorWindow(NetworkInterceptorService service, string tabId, string? siteHost = null, Action<InterceptorQuickRuleRequest>? quickRuleCallback = null)
        {
            InitializeComponent();
            _viewModel = new NetworkInterceptorViewModel(service, Dispatcher, tabId ?? "", this);
            DataContext = _viewModel;
            _siteHost = siteHost ?? "";
            _quickRuleCallback = quickRuleCallback;
            Closed += (_, _) => _viewModel.Dispose();

            var view = CollectionViewSource.GetDefaultView(_viewModel.Requests);
            view.Filter = FilterRequest;
            _viewModel.QuickRuleRequested += OnQuickRuleRequested;
            _viewModel.Requests.CollectionChanged += OnRequestsChanged;
            RenderTimeline();
        }

        private bool FilterRequest(object obj)
        {
            if (string.IsNullOrWhiteSpace(_searchFilter)) return true;
            if (obj is not InterceptedRequestItem r) return true;
            var q = _searchFilter;
            return (r.FullUrl?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (r.Domain?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (r.Method?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (r.ResourceType?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (r.RiskLevel?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (r.Category?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private void InterceptorSearch_Changed(object sender, TextChangedEventArgs e)
        {
            _searchFilter = (sender as TextBox)?.Text?.Trim() ?? "";
            CollectionViewSource.GetDefaultView(_viewModel.Requests)?.Refresh();
        }

        private void RequestGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (RequestGrid.SelectedItems.Count > 0)
            {
                var list = new List<InterceptedRequestItem>();
                foreach (var sel in RequestGrid.SelectedItems)
                    if (sel is InterceptedRequestItem ri)
                        list.Add(ri);
                _viewModel.SetSelectedRequests(list);
            }
            else
                _viewModel.SetSelectedRequests(Array.Empty<InterceptedRequestItem>());

            var item = _viewModel.SelectedRequest;
            if (item == null)
            {
                RequestHeadersText.Text = "";
                ResponseHeadersText.Text = "";
                ResponsePreviewText.Text = "";
                CookiesText.Text = "";
                TimingText.Text = "";
                PrivacyAnalysisText.Text = "";
                RawHttpText.Text = "";
                HexViewText.Text = "";
                return;
            }
            var reqH = item.RequestHeaders;
            var resH = item.ResponseHeaders;
            RequestHeadersText.Text = (reqH == null || reqH.Count == 0)
                ? "(no request headers captured)"
                : string.Join("\r\n", reqH.Select(kv => $"{kv.Key}: {kv.Value}"));
            ResponseHeadersText.Text = (resH == null || resH.Count == 0)
                ? "(no response headers yet)"
                : string.Join("\r\n", resH.Select(kv => $"{kv.Key}: {kv.Value}"));
            ResponsePreviewText.Text = string.IsNullOrEmpty(item.ResponsePreview) ? "(no preview)" : item.ResponsePreview;
            CookiesText.Text = string.IsNullOrEmpty(item.CookiesRaw) ? "(no cookies)" : item.CookiesRaw;
            TimingText.Text = BuildTimingView(item);
            PrivacyAnalysisText.Text = string.IsNullOrEmpty(item.PrivacyAnalysis) ? "(no analysis)" : item.PrivacyAnalysis;
            RawHttpText.Text = BuildRawHttp(item);
            HexViewText.Text = BuildHexView(item.ResponsePreview);
            RenderTimeline();
        }

        private void OnQuickRuleRequested(InterceptorQuickRuleRequest request)
        {
            if (_quickRuleCallback != null)
            {
                _quickRuleCallback(request);
                _viewModel.SetStatus("Rule added from interceptor context menu.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_siteHost))
            {
                _viewModel.SetStatus("No site host available for rule scope.");
                return;
            }

            var rule = new SiteRule { ScopeHost = _siteHost };
            switch (request.Action)
            {
                case InterceptorQuickRuleAction.BlockPathContains:
                    rule.Action = SiteRuleAction.BlockPathContains;
                    rule.MatchValue = request.Value ?? "";
                    break;
                case InterceptorQuickRuleAction.ForceHttps:
                    rule.Action = SiteRuleAction.ForceHttps;
                    rule.MatchValue = string.IsNullOrWhiteSpace(request.Value) ? _siteHost : request.Value;
                    break;
                case InterceptorQuickRuleAction.RewriteHeader:
                    rule.Action = SiteRuleAction.RewriteHeader;
                    rule.HeaderName = request.HeaderName ?? "";
                    rule.HeaderValue = request.HeaderValue ?? "";
                    break;
            }
            SiteRulesEngine.AddRule(rule);
            _viewModel.SetStatus("Rule added: " + rule.Action);
        }

        private void OnRequestsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RenderTimeline();
        }

        private void TimelineFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RenderTimeline();
        }

        private void RenderTimeline()
        {
            if (TimelineText == null) return;
            IEnumerable<InterceptedRequestItem> items = _viewModel.Requests;
            var filter = TimelineFilterCombo?.SelectedIndex ?? 0;
            if (filter == 1)
                items = items.Where(r => r.IsThirdParty && string.Equals(r.Method, "POST", StringComparison.OrdinalIgnoreCase));
            else if (filter == 2)
                items = items.Where(r => string.Equals(r.RiskLevel, "High", StringComparison.OrdinalIgnoreCase) || string.Equals(r.RiskLevel, "Critical", StringComparison.OrdinalIgnoreCase));
            else if (filter == 3)
                items = items.Where(r => r.IsTracker);

            var grouped = items
                .OrderBy(r => r.Timestamp)
                .GroupBy(r => new { r.Domain, Bucket = new DateTime(r.Timestamp.Year, r.Timestamp.Month, r.Timestamp.Day, r.Timestamp.Hour, r.Timestamp.Minute, r.Timestamp.Second / 10 * 10) })
                .OrderBy(g => g.Key.Bucket)
                .TakeLast(120);

            var sb = new System.Text.StringBuilder();
            foreach (var g in grouped)
            {
                int count = g.Count();
                var high = g.Count(x => x.RiskLevel == "High" || x.RiskLevel == "Critical");
                var post = g.Count(x => string.Equals(x.Method, "POST", StringComparison.OrdinalIgnoreCase));
                var mark = high > 0 ? "[!]" : post > 0 ? "[P]" : "[ ]";
                sb.AppendLine($"{mark} {g.Key.Bucket:HH:mm:ss}  {g.Key.Domain,-30}  burst={count,3}  high={high,2}  post={post,2}");
                foreach (var r in g.OrderBy(x => x.Timestamp).Take(4))
                {
                    var risk = string.IsNullOrWhiteSpace(r.RiskLevel) ? "-" : r.RiskLevel;
                    var tp = r.IsThirdParty ? "3P" : "1P";
                    sb.AppendLine($"      {r.Timestamp:HH:mm:ss.fff} {r.Method,-6} {tp} {risk,-8} {r.Path}");
                }
            }
            TimelineText.Text = sb.Length == 0 ? "(no timeline data yet)" : sb.ToString();
        }

        private void ExportChartButton_Click(object sender, RoutedEventArgs e)
        {
            if (ChartPanelBorder == null) return;
            var exp = FindParent<Expander>(ChartPanelBorder);
            bool wasExpanded = exp?.IsExpanded ?? true;
            if (exp != null && !exp.IsExpanded)
            {
                exp.IsExpanded = true;
                ChartPanelBorder.UpdateLayout();
            }
            try
            {
                double width = ChartPanelBorder.ActualWidth;
                double height = ChartPanelBorder.ActualHeight;
                if (width < 10 || height < 10) { width = 800; height = 400; }
                var rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(ChartPanelBorder);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rtb));
                var dlg = new SaveFileDialog
                {
                    Filter = "PNG image|*.png",
                    DefaultExt = ".png",
                    FileName = $"interceptor_chart_{DateTime.Now:yyyyMMdd_HHmmss}.png"
                };
                if (dlg.ShowDialog() == true)
                {
                    using (var fs = File.Create(dlg.FileName))
                        encoder.Save(fs);
                    _viewModel.SetStatus($"Chart exported to {dlg.FileName}");
                }
            }
            finally
            {
                if (exp != null && !wasExpanded)
                    exp.IsExpanded = false;
            }
        }

        private static string BuildTimingView(InterceptedRequestItem item)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Timestamp:   {item.Timestamp:yyyy-MM-dd HH:mm:ss.fff} UTC");
            sb.AppendLine($"Duration:    {item.DurationMs:F0} ms");
            sb.AppendLine($"Method:      {item.Method}");
            sb.AppendLine($"Status:      {item.StatusCode}");
            sb.AppendLine($"Size:        {item.ResponseSize:N0} bytes");
            sb.AppendLine($"Content:     {item.ContentType ?? "unknown"}");
            sb.AppendLine($"Third Party: {(item.IsThirdParty ? "Yes" : "No")}");
            sb.AppendLine($"Tracker:     {(item.IsTracker ? "Yes" : "No")}");
            sb.AppendLine($"Risk:        {item.RiskLevel} ({item.RiskScore:F1})");
            if (!string.IsNullOrEmpty(item.Category))
                sb.AppendLine($"Category:    {item.Category}");

            double ms = item.DurationMs;
            sb.AppendLine();
            sb.AppendLine("── Waterfall ──────────────────────────────");
            int barWidth = 40;
            int filled = ms > 0 ? Math.Clamp((int)(ms / 50.0), 1, barWidth) : 0;
            sb.Append("  [");
            sb.Append(new string('█', filled));
            sb.Append(new string('░', Math.Max(0, barWidth - filled)));
            sb.Append($"] {ms:F0} ms");
            return sb.ToString();
        }

        private static string BuildRawHttp(InterceptedRequestItem item)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"{item.Method ?? "GET"} {item.Path ?? "/"} HTTP/1.1");
            sb.AppendLine($"Host: {item.Domain ?? "unknown"}");
            if (item.RequestHeaders != null)
                foreach (var kv in item.RequestHeaders)
                    sb.AppendLine($"{kv.Key}: {kv.Value}");
            sb.AppendLine();
            sb.AppendLine("──── Response ────");
            sb.AppendLine($"HTTP/1.1 {item.StatusCode} {(item.StatusCode >= 200 && item.StatusCode < 300 ? "OK" : item.StatusCode >= 300 && item.StatusCode < 400 ? "Redirect" : item.StatusCode >= 400 ? "Error" : "")}");
            if (item.ResponseHeaders != null)
                foreach (var kv in item.ResponseHeaders)
                    sb.AppendLine($"{kv.Key}: {kv.Value}");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(item.ResponsePreview))
                sb.Append(item.ResponsePreview.Length > 4096 ? item.ResponsePreview[..4096] + "\n…(truncated)" : item.ResponsePreview);
            return sb.ToString();
        }

        private static string BuildHexView(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "(no data)";
            var bytes = System.Text.Encoding.UTF8.GetBytes(text);
            var maxBytes = Math.Min(bytes.Length, 8192);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Offset    00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F   ASCII");
            sb.AppendLine("────────  ───────────────────────  ───────────────────────   ────────────────");
            for (int i = 0; i < maxBytes; i += 16)
            {
                sb.Append($"{i:X8}  ");
                var ascii = new char[16];
                for (int j = 0; j < 16; j++)
                {
                    if (i + j < maxBytes)
                    {
                        sb.Append($"{bytes[i + j]:X2} ");
                        ascii[j] = bytes[i + j] >= 32 && bytes[i + j] < 127 ? (char)bytes[i + j] : '.';
                    }
                    else
                    {
                        sb.Append("   ");
                        ascii[j] = ' ';
                    }
                    if (j == 7) sb.Append(' ');
                }
                sb.Append("  ");
                sb.AppendLine(new string(ascii));
            }
            if (bytes.Length > maxBytes)
                sb.AppendLine($"\n…({bytes.Length - maxBytes} more bytes not shown)");
            return sb.ToString();
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is T t) return t;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }
    }
}
