using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PrivacyMonitor.NetworkInterceptor
{
    public partial class NetworkInterceptorWindow : Window
    {
        private readonly NetworkInterceptorViewModel _viewModel;

        public NetworkInterceptorWindow(NetworkInterceptorService service, string tabId)
        {
            InitializeComponent();
            _viewModel = new NetworkInterceptorViewModel(service, Dispatcher, tabId ?? "", this);
            DataContext = _viewModel;
            Closed += (_, _) => _viewModel.Dispose();
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
            TimingText.Text = $"Duration: {item.DurationMs:F0} ms\nTimestamp: {item.Timestamp:yyyy-MM-dd HH:mm:ss.fff}";
            PrivacyAnalysisText.Text = string.IsNullOrEmpty(item.PrivacyAnalysis) ? "(no analysis)" : item.PrivacyAnalysis;
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
