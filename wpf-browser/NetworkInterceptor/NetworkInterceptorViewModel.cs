using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PrivacyMonitor.NetworkInterceptor
{
    public sealed class NetworkInterceptorViewModel : INotifyPropertyChanged, IDisposable
    {
        private readonly NetworkInterceptorService _service;
        private readonly Dispatcher _dispatcher;
        private readonly string _tabId;
        private readonly Queue<double> _rpsRing = new Queue<double>(8);
        private const int RpsMovingAverageWindow = 5;
        private const int ChartSamplesMax = 60;
        private InterceptedRequestItem? _selectedRequest;
        private readonly List<InterceptedRequestItem> _selectedRequests = new List<InterceptedRequestItem>();
        private string _statusText = "Capturing";
        private TrafficMetrics _metrics = new TrafficMetrics();
        private bool _disposed;
        private bool _isReplaying;
        private bool _exportInProgress;
        private int _exportProgress; // 0–100
        private bool _exportWithGzip;
        private bool _chartPanelExpanded;
        private int _replayBatchCurrent;
        private int _replayBatchTotal;
        private CancellationTokenSource? _exportCts;
        private readonly AlertManager _alertManager = new AlertManager();

        private readonly Window? _ownerWindow;

        public NetworkInterceptorViewModel(NetworkInterceptorService service, Dispatcher dispatcher, string tabId, Window? ownerWindow = null)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
            _tabId = tabId ?? "";
            _ownerWindow = ownerWindow;
            Requests = new ObservableCollection<InterceptedRequestItem>();

            PauseCommand = new RelayCommand(Pause, () => !IsPaused);
            ResumeCommand = new RelayCommand(Resume, () => IsPaused);
            ClearCommand = new RelayCommand(Clear);
            CopyUrlCommand = new RelayCommand(CopyUrl, () => SelectedRequest != null);
            BlockDomainCommand = new RelayCommand(BlockDomain, () => SelectedRequest != null);
            ExportRequestCommand = new RelayCommand(ExportSelectedRequest, () => SelectedRequest != null);
            ExportSessionCommand = new RelayCommand(ExportFullSession);
            ReplayRequestCommand = new RelayCommand(ReplayRequestAsync, () => SelectedRequest != null && !_isReplaying);
            ReplayWithModifyCommand = new RelayCommand<object?>(ReplayWithModify, _ => SelectedRequest != null && !_isReplaying);
            ReplaySelectedCommand = new RelayCommand(ReplaySelectedAsync, () => _selectedRequests.Count > 0 && !_isReplaying);
            ReplaySelectedWithModifyCommand = new RelayCommand<object?>(ReplaySelectedWithModify, _ => _selectedRequests.Count > 0 && !_isReplaying);
            ExportSessionChunkedCommand = new RelayCommand(ExportFullSessionChunked, () => !_exportInProgress);
            ExportSessionStreamingCommand = new RelayCommand(ExportSessionStreamingAsync, () => !_exportInProgress);
            CancelExportCommand = new RelayCommand(CancelExport, () => _exportInProgress);
            ChartSamples = new ObservableCollection<MetricSample>();
            TopDomainsByHighRisk = new ObservableCollection<DomainRiskItem>();

            _service.BatchRequestAdded += OnBatchRequestAdded;
            _service.BatchRequestUpdated += OnBatchRequestUpdated;
            _service.MetricsUpdated += OnMetricsUpdated;
            _service.Cleared += OnCleared;
            _service.PausedPendingCountChanged += OnPausedPendingCountChanged;

            _alertManager.HighRiskDetected += OnHighRiskAlert;
            _alertManager.CriticalRiskDetected += OnCriticalRiskAlert;
        }

        /// <summary>Alert thresholds and notifications (High/Critical). Optional: bind UI for threshold settings.</summary>
        public AlertManager AlertManager => _alertManager;

        public ObservableCollection<InterceptedRequestItem> Requests { get; }
        public InterceptedRequestItem? SelectedRequest
        {
            get => _selectedRequest;
            set { _selectedRequest = value; OnPropertyChanged(nameof(SelectedRequest)); }
        }

        public TrafficMetrics Metrics => _metrics;

        public bool IsPaused => _service.IsPaused;
        public int PausedPendingCount => _service.PausedPendingCount;
        /// <summary>e.g. "Paused — 3 requests pending" or "Capturing".</summary>
        public string PausedStatusText => IsPaused ? $"Paused — {PausedPendingCount} requests pending" : "Capturing";
        public string StatusText { get => _statusText; private set { _statusText = value; OnPropertyChanged(nameof(StatusText)); } }
        public int MaxHistory { get => _service.MaxHistory; set { _service.MaxHistory = value; OnPropertyChanged(nameof(MaxHistory)); } }
        public int BatchIntervalMs { get => _service.BatchIntervalMs; set { _service.BatchIntervalMs = value; OnPropertyChanged(nameof(BatchIntervalMs)); } }

        public RelayCommand PauseCommand { get; }
        public RelayCommand ResumeCommand { get; }
        public RelayCommand ClearCommand { get; }
        public RelayCommand CopyUrlCommand { get; }
        public RelayCommand BlockDomainCommand { get; }
        public RelayCommand ExportRequestCommand { get; }
        public RelayCommand ExportSessionCommand { get; }
        public RelayCommand ReplayRequestCommand { get; }
        public RelayCommand<object?> ReplayWithModifyCommand { get; }
        public RelayCommand ReplaySelectedCommand { get; }
        public RelayCommand<object?> ReplaySelectedWithModifyCommand { get; }
        public RelayCommand ExportSessionChunkedCommand { get; }
        public RelayCommand ExportSessionStreamingCommand { get; }
        public RelayCommand CancelExportCommand { get; }

        public bool ExportInProgress { get => _exportInProgress; private set { _exportInProgress = value; OnPropertyChanged(nameof(ExportInProgress)); System.Windows.Input.CommandManager.InvalidateRequerySuggested(); } }
        public int ExportProgress { get => _exportProgress; private set { _exportProgress = value; OnPropertyChanged(nameof(ExportProgress)); } }
        public bool ExportWithGzip { get => _exportWithGzip; set { _exportWithGzip = value; OnPropertyChanged(nameof(ExportWithGzip)); } }
        /// <summary>Chart expander state (remembered for session).</summary>
        public bool ChartPanelExpanded { get => _chartPanelExpanded; set { _chartPanelExpanded = value; OnPropertyChanged(nameof(ChartPanelExpanded)); } }
        /// <summary>Batch replay progress: current index (1-based). 0 when idle.</summary>
        public int ReplayBatchCurrent { get => _replayBatchCurrent; private set { _replayBatchCurrent = value; OnPropertyChanged(nameof(ReplayBatchCurrent)); OnPropertyChanged(nameof(ReplayBatchProgressText)); } }
        /// <summary>Batch replay progress: total count. 0 when idle.</summary>
        public int ReplayBatchTotal { get => _replayBatchTotal; private set { _replayBatchTotal = value; OnPropertyChanged(nameof(ReplayBatchTotal)); OnPropertyChanged(nameof(ReplayBatchProgressText)); } }
        /// <summary>e.g. "Replaying 3/7…" or empty when not replaying.</summary>
        public string ReplayBatchProgressText => _replayBatchTotal > 0 ? $"Replaying {_replayBatchCurrent}/{_replayBatchTotal}…" : "";
        /// <summary>Tooltip for batch replay: shows progress when running, else instruction.</summary>
        public string ReplayBatchTooltip => _replayBatchTotal > 0 ? ReplayBatchProgressText : "Replay all selected requests in order; status shows progress.";

        public ObservableCollection<MetricSample> ChartSamples { get; }
        public ObservableCollection<DomainRiskItem> TopDomainsByHighRisk { get; }

        /// <summary>Set status text from view (e.g. after chart export).</summary>
        public void SetStatus(string message)
        {
            if (_disposed) return;
            StatusText = message ?? "";
        }

        /// <summary>Called from window when DataGrid selection changes (multi-select).</summary>
        public void SetSelectedRequests(IEnumerable<InterceptedRequestItem> items)
        {
            _selectedRequests.Clear();
            if (items != null)
                _selectedRequests.AddRange(items);
        }

        /// <summary>Invoked when Replay with modification is chosen; parameter can be the owner Window (or null to use constructor owner).</summary>
        public async void ReplayWithModify(object? ownerWindow)
        {
            if (SelectedRequest == null || _isReplaying) return;
            var owner = ownerWindow as Window ?? _ownerWindow;
            if (owner == null) return;
            var dlg = new ReplayModifyWindow(SelectedRequest) { Owner = owner };
            if (dlg.ShowDialog() != true) return;
            var options = new ReplayOptions
            {
                OverrideHeaders = dlg.OverrideHeaders,
                OverrideBody = dlg.OverrideBody,
                ModifyHeaders = dlg.OverrideHeaders != null && dlg.OverrideHeaders.Count > 0
            };
            await ReplayOneAsync(SelectedRequest, options).ConfigureAwait(false);
        }

        public event Action<string>? BlockDomainRequested;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void OnBatchRequestAdded(IReadOnlyList<InterceptedRequestItem>? items)
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed || items == null || items.Count == 0) return;
                foreach (var item in items)
                    Requests.Add(item);
                StatusText = $"Capturing · {Requests.Count} requests";
            });
        }

        private void OnHighRiskAlert(object? sender, RiskAlertEventArgs e)
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed || e == null) return;
                StatusText = "High risk: " + (e.Item.Domain ?? "") + " - " + (e.Item.RiskExplanation ?? "");
            });
        }

        private void OnCriticalRiskAlert(object? sender, RiskAlertEventArgs e)
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed || e == null) return;
                StatusText = "Critical risk: " + (e.Item.Domain ?? "") + " - " + (e.Item.RiskExplanation ?? "");
            });
        }

        private void OnBatchRequestUpdated(IReadOnlyList<InterceptedRequestItem> items)
        {
            foreach (var item in items)
                _alertManager.Evaluate(item, isReplay: false);
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                StatusText = $"Capturing · {Requests.Count} requests";
            });
        }

        private void OnMetricsUpdated(TrafficMetrics metrics)
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                _metrics.TotalRequests = metrics.TotalRequests;
                _metrics.ThirdPartyCount = metrics.ThirdPartyCount;
                _metrics.TrackerCount = metrics.TrackerCount;
                _metrics.HighRiskCount = metrics.HighRiskCount;
                _metrics.AverageRiskScore = Math.Round(metrics.AverageRiskScore, 1);
                _metrics.TotalTransferredBytes = metrics.TotalTransferredBytes;
                _metrics.RequestsPerSecond = Math.Round(metrics.RequestsPerSecond, 1);
                _rpsRing.Enqueue(metrics.RequestsPerSecond);
                while (_rpsRing.Count > RpsMovingAverageWindow) _rpsRing.Dequeue();
                _metrics.RpsMovingAverage = _rpsRing.Count > 0 ? Math.Round(_rpsRing.Average(), 1) : 0;
                var sample = new MetricSample
                {
                    Time = DateTime.Now,
                    Rps = metrics.RequestsPerSecond,
                    RpsMovingAverage = _metrics.RpsMovingAverage,
                    HighRisk = metrics.HighRiskCount,
                    ThirdParty = metrics.ThirdPartyCount,
                    Tracker = metrics.TrackerCount,
                    Total = metrics.TotalRequests
                };
                ChartSamples.Add(sample);
                RefreshTopDomainsByHighRisk();
                while (ChartSamples.Count > ChartSamplesMax)
                    ChartSamples.RemoveAt(0);
                OnPropertyChanged(nameof(Metrics));
            });
        }

        private void RefreshTopDomainsByHighRisk()
        {
            const int topN = 10;
            var groups = Requests
                .Where(r => !string.IsNullOrEmpty(r.Domain))
                .GroupBy(r => r.Domain!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new { Domain = g.Key, Total = g.Count(), HighRisk = g.Count(r => r.RiskLevel == "High" || r.RiskLevel == "Critical") })
                .OrderByDescending(x => x.HighRisk)
                .ThenByDescending(x => x.Total)
                .Take(topN)
                .ToList();
            TopDomainsByHighRisk.Clear();
            foreach (var x in groups)
            {
                TopDomainsByHighRisk.Add(new DomainRiskItem { Domain = x.Domain, TotalCount = x.Total, HighRiskCount = x.HighRisk });
            }
        }

        private void OnCleared()
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                Requests.Clear();
                SelectedRequest = null;
                _metrics.TotalRequests = 0;
                _metrics.ThirdPartyCount = 0;
                _metrics.TrackerCount = 0;
                _metrics.HighRiskCount = 0;
                _metrics.AverageRiskScore = 0;
                _metrics.TotalTransferredBytes = 0;
                _metrics.RequestsPerSecond = 0;
                _metrics.RpsMovingAverage = 0;
                _rpsRing.Clear();
                ChartSamples.Clear();
                TopDomainsByHighRisk.Clear();
                OnPropertyChanged(nameof(Metrics));
                StatusText = "Log cleared";
            });
        }

        private async void ReplayRequestAsync()
        {
            if (SelectedRequest == null || _isReplaying) return;
            await ReplayOneAsync(SelectedRequest, null);
        }

        /// <param name="clearReplayingFlag">If false, caller (e.g. batch replay) will clear _isReplaying itself.</param>
        private async Task ReplayOneAsync(InterceptedRequestItem item, ReplayOptions? options, bool clearReplayingFlag = true)
        {
            _isReplaying = true;
            if (!_disposed)
                _dispatcher.Invoke(() => { StatusText = "Replaying…"; System.Windows.Input.CommandManager.InvalidateRequerySuggested(); });
            try
            {
                var handler = new RequestReplayHandler((IReplayCapableSink)_service, _tabId, null, null);
                var ok = await handler.ReplayAsync(item, options).ConfigureAwait(false);
                if (!_disposed)
                    _dispatcher.Invoke(() => StatusText = ok ? "Replay sent; check grid for response." : "Replay failed.");
            }
            catch (Exception)
            {
                if (!_disposed)
                    _dispatcher.Invoke(() => StatusText = "Replay failed.");
            }
            finally
            {
                if (clearReplayingFlag)
                    _isReplaying = false;
                if (!_disposed)
                    _dispatcher.Invoke(() => System.Windows.Input.CommandManager.InvalidateRequerySuggested());
            }
        }

        private async void ReplaySelectedAsync()
        {
            if (_selectedRequests.Count == 0 || _isReplaying) return;
            _isReplaying = true;
            var list = _selectedRequests.ToList();
            var total = list.Count;
            ReplayBatchTotal = total;
            ReplayBatchCurrent = 0;
            var handler = new RequestReplayHandler((IReplayCapableSink)_service, _tabId, null, null);
            var done = 0;
            foreach (var item in list)
            {
                if (!_disposed)
                    _dispatcher.Invoke(() => { ReplayBatchCurrent = done + 1; StatusText = ReplayBatchProgressText; });
                try
                {
                    await handler.ReplayAsync(item, null).ConfigureAwait(false);
                    done++;
                }
                catch { }
            }
            _isReplaying = false;
            if (!_disposed)
                _dispatcher.Invoke(() =>
                {
                    ReplayBatchCurrent = 0;
                    ReplayBatchTotal = 0;
                    StatusText = $"Replay done: {done}/{total} requests sent.";
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                });
        }

        /// <summary>Batch replay with modification: open ReplayModify for each selected; send only when user clicks Replay.</summary>
        private async void ReplaySelectedWithModify(object? ownerWindow)
        {
            if (_selectedRequests.Count == 0 || _isReplaying) return;
            var owner = ownerWindow as System.Windows.Window ?? _ownerWindow;
            var list = _selectedRequests.ToList();
            _isReplaying = true;
            ReplayBatchTotal = list.Count;
            ReplayBatchCurrent = 0;
            var done = 0;
            foreach (var item in list)
            {
                _dispatcher.Invoke(() => { ReplayBatchCurrent = done + 1; StatusText = ReplayBatchProgressText; });
                var dlg = new ReplayModifyWindow(item) { Owner = owner };
                if (dlg.ShowDialog() == true)
                {
                    var options = new ReplayOptions
                    {
                        OverrideHeaders = dlg.OverrideHeaders,
                        OverrideBody = dlg.OverrideBody,
                        ModifyHeaders = dlg.OverrideHeaders != null && dlg.OverrideHeaders.Count > 0
                    };
                    await ReplayOneAsync(item, options, clearReplayingFlag: false).ConfigureAwait(false);
                    done++;
                }
            }
            _isReplaying = false;
            if (!_disposed)
                _dispatcher.Invoke(() =>
                {
                    ReplayBatchCurrent = 0;
                    ReplayBatchTotal = 0;
                    StatusText = $"Replay with modification: {done}/{list.Count} sent.";
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                });
        }

        private void Pause()
        {
            _service.Pause();
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(PausedStatusText));
            StatusText = PausedStatusText;
        }

        private void Resume()
        {
            _service.Resume();
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(PausedStatusText));
            StatusText = "Resuming…";
            _dispatcher.BeginInvoke(() => { StatusText = "Capturing"; });
        }

        private void OnPausedPendingCountChanged()
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                OnPropertyChanged(nameof(PausedPendingCount));
                OnPropertyChanged(nameof(PausedStatusText));
                if (IsPaused)
                    StatusText = PausedStatusText;
            });
        }

        private void Clear() => _service.Clear();

        private void CopyUrl()
        {
            if (SelectedRequest?.FullUrl == null) return;
            try { Clipboard.SetText(SelectedRequest.FullUrl); } catch { }
        }

        private void BlockDomain()
        {
            if (SelectedRequest?.Domain == null) return;
            BlockDomainRequested?.Invoke(SelectedRequest.Domain);
        }

        private void ExportSelectedRequest()
        {
            if (SelectedRequest == null) return;
            try
            {
                var root = BuildExportRoot(new[] { SelectedRequest }, fullSession: false);
                var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
                var path = Path.Combine(Path.GetTempPath(), $"request_{SelectedRequest.Id}.json");
                File.WriteAllText(path, json);
                StatusText = $"Exported to {path}";
            }
            catch (Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }

        private void ExportFullSession()
        {
            try
            {
                var list = Requests.ToList();
                if (list.Count == 0)
                {
                    StatusText = "No requests to export.";
                    return;
                }
                var root = BuildExportRoot(list, fullSession: true);
                root.SessionMetrics = new InterceptorExportMetrics
                {
                    TotalRequests = _metrics.TotalRequests,
                    ThirdPartyCount = _metrics.ThirdPartyCount,
                    TrackerCount = _metrics.TrackerCount,
                    HighRiskCount = _metrics.HighRiskCount,
                    AverageRiskScore = _metrics.AverageRiskScore,
                    TotalTransferredBytes = _metrics.TotalTransferredBytes,
                    RequestsPerSecond = _metrics.RequestsPerSecond
                };
                var json = JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true });
                var path = Path.Combine(Path.GetTempPath(), $"interceptor_session_{DateTime.UtcNow:yyyyMMddHHmmss}.json");
                File.WriteAllText(path, json);
                StatusText = $"Session exported ({list.Count} requests) to {path}";
            }
            catch (Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }

        private void ExportFullSessionChunked()
        {
            var list = Requests.ToList();
            if (list.Count == 0) { StatusText = "No requests to export."; return; }
            const int chunkThreshold = 10000;
            if (list.Count < chunkThreshold) { ExportFullSession(); return; }
#pragma warning disable CS4014
            _ = ExportSessionStreamingAsync(list, useGzip: false, showProgress: false);
#pragma warning restore CS4014
        }

        private async void ExportSessionStreamingAsync()
        {
            var list = Requests.ToList();
            if (list.Count == 0) { StatusText = "No requests to export."; return; }
            await ExportSessionStreamingAsync(list, useGzip: ExportWithGzip, showProgress: true).ConfigureAwait(false);
        }

        private void CancelExport()
        {
            _exportCts?.Cancel();
        }

        private async Task ExportSessionStreamingAsync(List<InterceptedRequestItem> list, bool useGzip, bool showProgress)
        {
            if (list.Count == 0) return;
            _exportCts?.Dispose();
            _exportCts = new CancellationTokenSource();
            var token = _exportCts.Token;
            ExportInProgress = true;
            ExportProgress = 0;
            var progress = new Progress<int>(p =>
            {
                _dispatcher.BeginInvoke(() => { ExportProgress = p; });
            });
            var ext = useGzip ? "ndjson.gz" : "ndjson";
            var path = Path.Combine(Path.GetTempPath(), $"interceptor_session_{DateTime.UtcNow:yyyyMMddHHmmss}.{ext}");
            var statusMessage = "";
            try
            {
                await Task.Run(() =>
                {
                    var total = list.Count;
                    var header = new
                    {
                        Version = InterceptorExportRoot.SchemaVersion,
                        ExportId = Guid.NewGuid().ToString("N"),
                        ExportedAtUtc = DateTime.UtcNow,
                        FullSession = true,
                        RequestCount = total,
                        SessionMetrics = new InterceptorExportMetrics
                        {
                            TotalRequests = _metrics.TotalRequests,
                            ThirdPartyCount = _metrics.ThirdPartyCount,
                            TrackerCount = _metrics.TrackerCount,
                            HighRiskCount = _metrics.HighRiskCount,
                            AverageRiskScore = _metrics.AverageRiskScore,
                            TotalTransferredBytes = _metrics.TotalTransferredBytes,
                            RequestsPerSecond = _metrics.RequestsPerSecond
                        }
                    };
                    Stream stream = File.OpenWrite(path);
                    if (useGzip)
                        stream = new GZipStream(stream, CompressionLevel.Fastest);
                    using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: false))
                    {
                        writer.WriteLine(JsonSerializer.Serialize(header));
                        for (var i = 0; i < list.Count; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            var r = list[i];
                            var req = new InterceptorExportRequest
                            {
                                Id = r.Id, CorrelationId = r.CorrelationId, Timestamp = r.Timestamp,
                                Method = r.Method ?? "", FullUrl = r.FullUrl ?? "", Domain = r.Domain ?? "", Path = r.Path ?? "",
                                ResourceType = r.ResourceType, StatusCode = r.StatusCode, ContentType = r.ContentType,
                                ResponseSize = r.ResponseSize, DurationMs = r.DurationMs, IsThirdParty = r.IsThirdParty,
                                IsTracker = r.IsTracker, RiskScore = r.RiskScore, RiskLevel = r.RiskLevel,
                                Category = r.Category, RiskExplanation = r.RiskExplanation, PrivacyAnalysis = r.PrivacyAnalysis,
                                RequestHeaders = new Dictionary<string, string>(r.RequestHeaders ?? new Dictionary<string, string>()),
                                ResponseHeaders = new Dictionary<string, string>(r.ResponseHeaders ?? new Dictionary<string, string>()),
                                Replay = new ReplayMetadata { CorrelationId = r.CorrelationId, CanReplay = CanReplayMethod(r.Method ?? ""), Method = r.Method ?? "", FullUrl = r.FullUrl ?? "", ModifiedReplay = r.IsReplayedWithModification }
                            };
                            writer.WriteLine(JsonSerializer.Serialize(req));
                            if (showProgress && total > 0 && (i % 500 == 0 || i == total - 1))
                                ((IProgress<int>)progress).Report((int)((i + 1) * 100.0 / total));
                        }
                    }
                }, token).ConfigureAwait(false);

                statusMessage = $"Session exported (streaming, {list.Count} requests) to {path}";
            }
            catch (OperationCanceledException)
            {
                statusMessage = "Export cancelled.";
            }
            catch (Exception ex)
            {
                statusMessage = $"Export failed: {ex.Message}";
            }
            finally
            {
                _exportCts?.Dispose();
                _exportCts = null;
#pragma warning disable CS4014
                _ = _dispatcher.BeginInvoke(() =>
                {
                    if (_disposed) return;
                    ExportProgress = 100;
                    ExportInProgress = false;
                    StatusText = statusMessage;
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                });
#pragma warning restore CS4014
            }
        }

        private static InterceptorExportRoot BuildExportRoot(IReadOnlyList<InterceptedRequestItem> items, bool fullSession)
        {
            var requests = items.Select(r => new InterceptorExportRequest
            {
                Id = r.Id,
                CorrelationId = r.CorrelationId,
                Timestamp = r.Timestamp,
                Method = r.Method,
                FullUrl = r.FullUrl,
                Domain = r.Domain,
                Path = r.Path,
                ResourceType = r.ResourceType,
                StatusCode = r.StatusCode,
                ContentType = r.ContentType,
                ResponseSize = r.ResponseSize,
                DurationMs = r.DurationMs,
                IsThirdParty = r.IsThirdParty,
                IsTracker = r.IsTracker,
                RiskScore = r.RiskScore,
                RiskLevel = r.RiskLevel,
                Category = r.Category,
                RiskExplanation = r.RiskExplanation,
                PrivacyAnalysis = r.PrivacyAnalysis,
                RequestHeaders = new Dictionary<string, string>(r.RequestHeaders ?? new Dictionary<string, string>()),
                ResponseHeaders = new Dictionary<string, string>(r.ResponseHeaders ?? new Dictionary<string, string>()),
                Replay = new ReplayMetadata
                {
                    CorrelationId = r.CorrelationId,
                    CanReplay = CanReplayMethod(r.Method ?? ""),
                    Method = r.Method ?? "",
                    FullUrl = r.FullUrl ?? "",
                    ModifiedReplay = r.IsReplayedWithModification
                }
            }).ToList();

            return new InterceptorExportRoot
            {
                Version = InterceptorExportRoot.SchemaVersion,
                ExportId = Guid.NewGuid().ToString("N"),
                ExportedAtUtc = DateTime.UtcNow,
                FullSession = fullSession,
                Requests = requests
            };
        }

        private static bool CanReplayMethod(string method)
        {
            var m = (method ?? "").ToUpperInvariant();
            return m == "GET" || m == "HEAD" || m == "OPTIONS" || m == "POST" || m == "PUT" || m == "PATCH" || m == "DELETE";
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _service.BatchRequestAdded -= OnBatchRequestAdded;
            _service.BatchRequestUpdated -= OnBatchRequestUpdated;
            _service.MetricsUpdated -= OnMetricsUpdated;
            _service.Cleared -= OnCleared;
            _service.PausedPendingCountChanged -= OnPausedPendingCountChanged;
            _alertManager.HighRiskDetected -= OnHighRiskAlert;
            _alertManager.CriticalRiskDetected -= OnCriticalRiskAlert;
            if (_service is IDisposable d)
                d.Dispose();
        }
    }
}
