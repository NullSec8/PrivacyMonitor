using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PrivacyMonitor.NetworkInterceptor
{
    public partial class ReplayModifyWindow : Window
    {
        public ReplayModifyWindow(InterceptedRequestItem request)
        {
            InitializeComponent();
            Request = request ?? throw new ArgumentNullException(nameof(request));
            var headers = request.RequestHeaders ?? new Dictionary<string, string>();
            OriginalHeadersText.Text = headers.Count == 0
                ? "(no headers)"
                : string.Join(Environment.NewLine, headers.Select(kv => $"{kv.Key}: {kv.Value}"));
            var items = new System.Collections.ObjectModel.ObservableCollection<KeyValueItem>();
            foreach (var kv in headers)
                items.Add(new KeyValueItem { Key = kv.Key, Value = kv.Value, OriginalValue = kv.Value });
            HeadersGrid.ItemsSource = items;
            UpdateRiskPreview();
        }

        private void UpdateRiskPreview()
        {
            if (Request == null) return;
            RiskPreviewText.Text = "Current risk: " + Request.RiskLevel + " (" + Request.RiskScore + "). Not recalculated until you click Replay.";
        }

        public InterceptedRequestItem Request { get; }
        public IReadOnlyDictionary<string, string>? OverrideHeaders { get; private set; }
        public string? OverrideBody { get; private set; }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Replay_Click(object sender, RoutedEventArgs e)
        {
            if (HeadersGrid.ItemsSource is System.Collections.ObjectModel.ObservableCollection<KeyValueItem> items)
            {
                var dict = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    if (string.IsNullOrWhiteSpace(item.Key)) continue;
                    dict[item.Key.Trim()] = item.Value ?? "";
                }
                OverrideHeaders = dict;
            }
            OverrideBody = string.IsNullOrWhiteSpace(BodyBox.Text) ? null : BodyBox.Text;
            DialogResult = true;
            Close();
        }
    }
}
