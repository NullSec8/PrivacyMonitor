using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;

namespace PrivacyMonitor
{
    public partial class MainWindow : Window
    {
        private readonly List<BrowserTab> _tabs = new();
        private string _activeTabId = "";
        private int _activeAnalysisTab;
        private bool _dirty = true;
        private bool _sidebarOpen = true;
        private bool _expertMode = false;
        private bool _antiFpEnabled = true;
        private bool _adBlockEnabled = true;
        private readonly DispatcherTimer _uiTimer;
        private readonly SessionContext _session = new();

        private Button[] _aTabButtons = Array.Empty<Button>();
        private UIElement[] _panels = Array.Empty<UIElement>();

        // Chrome palette
        private static readonly SolidColorBrush TabBarBg     = new(Color.FromRgb(222, 225, 230)); // #DEE1E6
        private static readonly SolidColorBrush TabActiveBg  = Brushes.White;
        private static readonly SolidColorBrush TabActiveFg  = new(Color.FromRgb(32, 33, 36));     // #202124
        private static readonly SolidColorBrush TabInactiveBg = new(Color.FromRgb(210, 213, 218));  // subtle
        private static readonly SolidColorBrush TabInactiveFg = new(Color.FromRgb(95, 99, 104));    // #5F6368
        private static readonly SolidColorBrush PillActive   = new(Color.FromRgb(26, 115, 232));    // #1A73E8
        private static readonly SolidColorBrush PillActiveFg = Brushes.White;
        private static readonly SolidColorBrush PillInactive = Brushes.Transparent;
        private static readonly SolidColorBrush PillInactiveFg = new(Color.FromRgb(95, 99, 104));

        private static readonly string WelcomeHtml = @"<!DOCTYPE html><html><head><style>
            *{margin:0;padding:0;box-sizing:border-box}
            body{font-family:'Segoe UI',system-ui,-apple-system,sans-serif;background:#FFF;display:flex;flex-direction:column;align-items:center;justify-content:center;height:100vh;color:#202124}
            .logo{font-size:56px;font-weight:300;margin-bottom:8px;color:#202124}
            .logo span{font-weight:600;color:#1A73E8}
            .sub{font-size:13px;color:#5F6368;margin-bottom:32px}
            .search{width:480px;max-width:90%;height:44px;border-radius:24px;border:1px solid #DFE1E5;padding:0 20px;font-size:14px;outline:none;color:#202124;transition:box-shadow .2s}
            .search:focus{box-shadow:0 1px 6px rgba(32,33,36,.28);border-color:transparent}
            .tips{margin-top:40px;display:grid;grid-template-columns:1fr 1fr;gap:12px;max-width:420px;width:100%}
            .tip{background:#F8F9FA;border-radius:12px;padding:14px 16px;font-size:12px;color:#3C4043;line-height:1.5}
            .tip b{color:#1A73E8;display:block;margin-bottom:2px;font-size:11px}
            .foot{position:fixed;bottom:16px;font-size:11px;color:#9AA0A6}
        </style></head><body>
            <div class='logo'>Privacy <span>Monitor</span></div>
            <div class='sub'>Agjencia per Informim dhe Privatesi</div>
            <input class='search' placeholder='Search or type a URL' autofocus
                   onkeydown=""if(event.key==='Enter'){let v=this.value.trim();if(v&&!v.includes('://')){v='https://'+v;}if(v)window.location.href=v;}""/>
            <div class='tips'>
                <div class='tip'><b>Ctrl+T</b>New Tab</div>
                <div class='tip'><b>Ctrl+W</b>Close Tab</div>
                <div class='tip'><b>Ctrl+L</b>Focus Address Bar</div>
                <div class='tip'><b>F5</b>Reload Page</div>
                <div class='tip'><b>Ctrl+P</b>Print Page</div>
                <div class='tip'><b>Ctrl+F</b>Find in Page</div>
                <div class='tip'><b>Ctrl+/- </b>Zoom</div>
            </div>
            <div class='foot'>Privacy Monitor v1.0 &middot; Built for Windows</div>
        </body></html>";

        public MainWindow()
        {
            InitializeComponent();
            _aTabButtons = new[] { ATab0, ATab1, ATab2, ATab3, ATab4, ATab5, ATab6 };
            _panels = new UIElement[] { Panel0, Panel1, Panel2, Panel3, Panel4, Panel5, Panel6 };
            SwitchAnalysisTab(0);

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += (_, _) => { if (_dirty) { _dirty = false; RefreshAll(); } };
            _uiTimer.Start();

            Loaded += async (_, _) => await CreateNewTab("about:welcome");
        }

        // ================================================================
        //  SIDEBAR TOGGLE
        // ================================================================
        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            _sidebarOpen = !_sidebarOpen;
            SidebarCol.Width = _sidebarOpen ? new GridLength(460) : new GridLength(0);
            SidebarPanel.Visibility = _sidebarOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ToggleExpert_Click(object sender, RoutedEventArgs e)
        {
            _expertMode = !_expertMode;
            ExpertToggleBtn.Content = _expertMode ? "Simple" : "Expert";
            ExpertToggleBtn.Background = _expertMode ? PillActive : new SolidColorBrush(Color.FromRgb(241, 243, 244));
            ExpertToggleBtn.Foreground = _expertMode ? PillActiveFg : new SolidColorBrush(Color.FromRgb(60, 64, 67));
            UpdateExpertVisibility();
            _dirty = true;
        }

        private void UpdateExpertVisibility()
        {
            var expert = _expertMode ? Visibility.Visible : Visibility.Collapsed;
            var simple = _expertMode ? Visibility.Collapsed : Visibility.Visible;

            // Fingerprint panel: simple vs expert list
            FpSimpleList.Visibility = simple;
            FingerprintList.Visibility = expert;

            // Dashboard: breakdown only in expert; mitigation tips only in simple
            BreakdownPanel.Visibility = expert;
            MitigationPanel.Visibility = simple;

            // Forensics: expert sections vs simplified
            ForensicExpertPanel.Visibility = expert;
            SimpleBehavioralCard.Visibility = simple;
        }

        // ================================================================
        //  PROTECTION CONTROLS
        // ================================================================
        private void ProtectionMode_Changed(object sender, SelectionChangedEventArgs e)
        {
            ApplyProtectionModeFromCombo();
        }

        private void ProtectionMode_DropDownClosed(object sender, EventArgs e)
        {
            // Apply again when dropdown closes so we definitely catch the user's choice
            ApplyProtectionModeFromCombo();
        }

        private void ApplyProtectionModeFromCombo()
        {
            if (ProtectionModeCombo == null) return;
            int idx = ProtectionModeCombo.SelectedIndex;
            if (idx < 0 || idx > 2) return;
            var mode = (ProtectionMode)idx;

            // Apply globally so new tabs/sites get this mode
            ProtectionEngine.GlobalDefaultMode = mode;

            var tab = ActiveTab;
            if (tab != null)
            {
                if (!string.IsNullOrEmpty(tab.CurrentHost))
                    ProtectionEngine.SetMode(tab.CurrentHost, mode);
                UpdateProtectionUI(tab);
                var profile = ProtectionEngine.GetProfile(tab.CurrentHost);
                _ = UpdatePerNavigationScriptsAsync(tab, profile);
                _ = ApplyRuntimeBlockerAsync(tab, profile);
            }
            _dirty = true;
        }

        private void ToggleAntiFp_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab != null && !string.IsNullOrEmpty(tab.CurrentHost))
            {
                var profile = ProtectionEngine.GetProfile(tab.CurrentHost);
                profile.AntiFingerprint = !profile.AntiFingerprint;
                ProtectionEngine.SetProfile(tab.CurrentHost, profile);
                _antiFpEnabled = profile.AntiFingerprint;
                UpdateAntiFpButton();
                _ = UpdatePerNavigationScriptsAsync(tab, profile);
            }
        }

        private void ToggleAdBlock_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab != null && !string.IsNullOrEmpty(tab.CurrentHost))
            {
                var profile = ProtectionEngine.GetProfile(tab.CurrentHost);
                profile.BlockAdsTrackers = !profile.BlockAdsTrackers;
                ProtectionEngine.SetProfile(tab.CurrentHost, profile);
                _adBlockEnabled = profile.BlockAdsTrackers;
                UpdateAdBlockButton();
                _ = ApplyRuntimeBlockerAsync(tab, profile);
                _ = UpdatePerNavigationScriptsAsync(tab, profile);
                _dirty = true;
            }
        }

        private void UpdateProtectionUI(BrowserTab tab)
        {
            var mode = ProtectionEngine.GetEffectiveMode(tab.CurrentHost);
            // Only update combo when it would change, to avoid overwriting user's selection
            if (ProtectionModeCombo.SelectedIndex != (int)mode)
            {
                ProtectionModeCombo.SelectionChanged -= ProtectionMode_Changed;
                ProtectionModeCombo.SelectedIndex = (int)mode;
                ProtectionModeCombo.SelectionChanged += ProtectionMode_Changed;
            }

            // Update per-site toggles
            var profile = ProtectionEngine.GetProfile(tab.CurrentHost);
            _antiFpEnabled = profile.AntiFingerprint;
            _adBlockEnabled = profile.BlockAdsTrackers;
            UpdateAntiFpButton();
            UpdateAdBlockButton();

            // Update blocked badge
            if (tab.BlockedCount > 0)
            {
                BlockedBadge.Visibility = Visibility.Visible;
                BlockedCountText.Text = tab.BlockedCount.ToString();
            }
            else
            {
                BlockedBadge.Visibility = Visibility.Collapsed;
            }

            // Status bar protection text
            string modeLabel = mode switch
            {
                ProtectionMode.Monitor => "Monitor",
                ProtectionMode.BlockKnown => "Blocking",
                ProtectionMode.Aggressive => "Aggressive",
                _ => ""
            };
            string fpLabel = _antiFpEnabled ? " | Anti-FP" : "";
            string adLabel = _adBlockEnabled ? "" : " | Ads off";
            ProtectionStatusText.Text = $"{modeLabel}{fpLabel}{adLabel}  |  {tab.BlockedCount} blocked";
            ProtectionStatusText.Foreground = mode == ProtectionMode.Monitor
                ? new SolidColorBrush(Color.FromRgb(95, 99, 104))
                : new SolidColorBrush(Color.FromRgb(24, 128, 56));
        }

        private void UpdateAntiFpButton()
        {
            AntiFpBtn.Foreground = _antiFpEnabled
                ? new SolidColorBrush(Color.FromRgb(24, 128, 56))    // green
                : new SolidColorBrush(Color.FromRgb(95, 99, 104));   // gray
            AntiFpBtn.ToolTip = _antiFpEnabled
                ? "Anti-fingerprinting: ON (click to disable)"
                : "Anti-fingerprinting: OFF (click to enable)";
        }

        private void UpdateAdBlockButton()
        {
            AdBlockBtn.Foreground = _adBlockEnabled
                ? new SolidColorBrush(Color.FromRgb(217, 48, 37))    // red
                : new SolidColorBrush(Color.FromRgb(95, 99, 104));   // gray
            AdBlockBtn.ToolTip = _adBlockEnabled
                ? "Ad/Tracker blocking: ON (click to disable)"
                : "Ad/Tracker blocking: OFF (click to enable)";
        }

        // ================================================================
        //  TAB MANAGEMENT
        // ================================================================
        private static async Task<CoreWebView2Environment?> CreateWebView2EnvironmentAsync()
        {
            string fixedRuntimePath = Path.Combine(AppContext.BaseDirectory, "Microsoft.Web.WebView2.FixedVersionRuntime.win-x64");
            if (!Directory.Exists(fixedRuntimePath))
            {
                return null;
            }

            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrivacyMonitor",
                "WebView2");

            return await CoreWebView2Environment.CreateAsync(fixedRuntimePath, userDataFolder);
        }

        private async Task CreateNewTab(string url = "about:welcome")
        {
            var tab = new BrowserTab();
            tab.WebView = new WebView2 { Visibility = Visibility.Collapsed };
            WebViewContainer.Children.Add(tab.WebView);
            tab.TabHeader = BuildTabHeader(tab);
            TabBar.Children.Add(tab.TabHeader);
            _tabs.Add(tab);

            try
            {
                var environment = await CreateWebView2EnvironmentAsync();
                await tab.WebView.EnsureCoreWebView2Async(environment);
                tab.IsReady = true;
                var cw = tab.WebView.CoreWebView2;
                cw.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                cw.WebResourceRequested += (s, e) => OnWebResourceRequested(tab, e);
                cw.WebResourceResponseReceived += (s, e) => OnWebResourceResponseReceived(tab, e);
                cw.WebMessageReceived += (s, e) => OnWebMessage(tab, e);
                cw.NavigationStarting += (s, e) => OnNavigationStarting(tab, e);
                cw.NavigationCompleted += (s, e) => OnNavigationCompleted(tab, e);
                cw.DocumentTitleChanged += (s, e) => Dispatcher.Invoke(() => UpdateTabTitle(tab));
                cw.NewWindowRequested += (s, e) => { e.Handled = true; Dispatcher.Invoke(async () => await CreateNewTab(e.Uri)); };
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(ProtectionEngine.ElementBlockerBootstrapScript);

                if (url == "about:welcome")
                    cw.NavigateToString(WelcomeHtml);
                else
                    cw.Navigate(url);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 init failed:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            SwitchToTab(tab.Id);
        }

        private void SwitchToTab(string tabId)
        {
            _activeTabId = tabId;
            foreach (var t in _tabs)
            {
                bool active = t.Id == tabId;
                t.WebView.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
                StyleTabHeader(t, active);
                if (active)
                {
                    bool isWelcome = string.IsNullOrEmpty(t.Url) || t.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
                    AddressBar.Text = isWelcome ? "" : t.Url;
                    UpdateAddressBarPlaceholder();
                    LoadingBar.Visibility = t.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                    UpdateLockIcon(t);
                    StatusText.Text = t.CurrentHost.Length > 0 ? t.CurrentHost : "Ready — enter a URL above to start";
                    UpdateProtectionUI(t);
                }
            }
            _dirty = true;
        }

        private void CloseTab(string tabId)
        {
            if (_tabs.Count <= 1) return;
            var tab = _tabs.FirstOrDefault(t => t.Id == tabId);
            if (tab == null) return;
            int idx = _tabs.IndexOf(tab);
            WebViewContainer.Children.Remove(tab.WebView);
            TabBar.Children.Remove(tab.TabHeader);
            try { tab.WebView.Dispose(); } catch { }
            _tabs.Remove(tab);
            if (tabId == _activeTabId) SwitchToTab(_tabs[Math.Min(idx, _tabs.Count - 1)].Id);
        }

        private BrowserTab? ActiveTab => _tabs.FirstOrDefault(t => t.Id == _activeTabId);

        // ================================================================
        //  CHROME-STYLE TAB HEADER
        // ================================================================
        private Border BuildTabHeader(BrowserTab tab)
        {
            // Domain initial badge
            var initial = new TextBlock
            {
                Text = "-", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            tab.InitialBlock = initial;
            var initialBorder = new Border
            {
                Width = 16, Height = 16, CornerRadius = new CornerRadius(8),
                Background = PillActive, Margin = new Thickness(0, 0, 6, 0),
                Child = initial, VerticalAlignment = VerticalAlignment.Center
            };

            // Title
            var title = new TextBlock
            {
                Text = "New Tab", FontSize = 12, FontFamily = new FontFamily("Segoe UI"),
                Foreground = TabInactiveFg, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140
            };
            tab.TitleBlock = title;

            // Close button
            var closeBtn = new Button
            {
                Content = "\u00D7", FontSize = 14, Width = 20, Height = 20,
                Background = Brushes.Transparent, Foreground = TabInactiveFg,
                BorderThickness = new Thickness(0), Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "Close  (Ctrl+W)"
            };
            var closeTpl = new ControlTemplate(typeof(Button));
            var closeBd = new FrameworkElementFactory(typeof(Border));
            closeBd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            closeBd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            closeBd.Name = "Bd";
            var closeCP = new FrameworkElementFactory(typeof(ContentPresenter));
            closeCP.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            closeCP.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            closeBd.AppendChild(closeCP);
            closeTpl.VisualTree = closeBd;
            var tr = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            tr.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)), "Bd"));
            closeTpl.Triggers.Add(tr);
            closeBtn.Template = closeTpl;
            string cid = tab.Id;
            closeBtn.Click += (_, _) => CloseTab(cid);

            // Blocked badge (per-tab)
            var blockedText = new TextBlock
            {
                Text = "0",
                FontSize = 8,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            var blockedBadge = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(217, 48, 37)),
                CornerRadius = new CornerRadius(8),
                MinWidth = 16,
                Height = 16,
                Padding = new Thickness(4, 0, 4, 0),
                Margin = new Thickness(6, 0, 0, 0),
                Visibility = Visibility.Collapsed,
                Child = blockedText,
                VerticalAlignment = VerticalAlignment.Center
            };
            tab.BlockedBadge = blockedBadge;
            tab.BlockedBadgeText = blockedText;

            // Layout
            var panel = new DockPanel();
            DockPanel.SetDock(closeBtn, Dock.Right);
            DockPanel.SetDock(blockedBadge, Dock.Right);
            panel.Children.Add(closeBtn);
            panel.Children.Add(blockedBadge);
            panel.Children.Add(initialBorder);
            panel.Children.Add(title);

            var border = new Border
            {
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Padding = new Thickness(10, 6, 8, 6),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 1, 0),
                Background = TabInactiveBg,
                Child = panel,
                MinWidth = 70, MaxWidth = 220,
                ToolTip = "New Tab"
            };
            border.MouseLeftButtonDown += (_, _) => SwitchToTab(cid);
            return border;
        }

        private void StyleTabHeader(BrowserTab tab, bool active)
        {
            tab.TabHeader.Background = active ? TabActiveBg : TabInactiveBg;
            tab.TitleBlock.Foreground = active ? TabActiveFg : TabInactiveFg;
            if (tab.TabHeader.Child is DockPanel dp && dp.Children.Count > 0 && dp.Children[0] is Button cb)
                cb.Foreground = active ? TabActiveFg : TabInactiveFg;
            // Active tab gets a subtle shadow
            tab.TabHeader.Effect = active ? new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.08, Color = Colors.Black } : null;
        }

        private void UpdateTabTitle(BrowserTab tab)
        {
            if (!tab.IsReady) return;
            tab.Title = tab.WebView.CoreWebView2.DocumentTitle ?? "New Tab";
            string d = tab.Title.Length > 22 ? tab.Title[..22] + "..." : tab.Title;
            tab.TitleBlock.Text = d;
            tab.TabHeader.ToolTip = tab.Title;
            if (tab.CurrentHost.Length > 0)
            {
                tab.InitialBlock.Text = tab.CurrentHost[0].ToString().ToUpper();
                int hash = Math.Abs(tab.CurrentHost.GetHashCode());
                byte r = (byte)(50 + hash % 150), g = (byte)(50 + (hash / 256) % 150), b = (byte)(80 + (hash / 65536) % 130);
                if (tab.InitialBlock.Parent is Border ib) ib.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }

        private void UpdateTabBlockedBadge(BrowserTab tab)
        {
            if (tab.BlockedBadge == null || tab.BlockedBadgeText == null) return;
            if (tab.BlockedCount > 0)
            {
                tab.BlockedBadge.Visibility = Visibility.Visible;
                tab.BlockedBadgeText.Text = tab.BlockedCount.ToString();
            }
            else
            {
                tab.BlockedBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateLockIcon(BrowserTab tab)
        {
            if (tab.Url.StartsWith("https://"))
            {
                LockIcon.Text = "\U0001F512"; // lock char
                LockIcon.Foreground = new SolidColorBrush(Color.FromRgb(24, 128, 56)); tab.IsSecure = true;
            }
            else if (tab.Url.StartsWith("http://"))
            {
                LockIcon.Text = "Not secure";
                LockIcon.Foreground = new SolidColorBrush(Color.FromRgb(217, 48, 37)); tab.IsSecure = false;
            }
            else { LockIcon.Text = ""; LockIcon.Foreground = new SolidColorBrush(Color.FromRgb(154, 160, 166)); }
        }

        // ================================================================
        //  WEBVIEW2 EVENTS
        // ================================================================
        private void OnNavigationStarting(BrowserTab tab, CoreWebView2NavigationStartingEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var uri = new Uri(e.Uri);
                    if (!uri.Host.Equals(tab.CurrentHost, StringComparison.OrdinalIgnoreCase))
                        tab.ResetDetection();
                    tab.CurrentHost = uri.Host; tab.Url = e.Uri; tab.IsLoading = true;
                    if (!string.IsNullOrEmpty(tab.CurrentHost))
                    {
                        var profile = ProtectionEngine.GetProfile(tab.CurrentHost);
                        profile.LastVisit = DateTime.UtcNow;
                        ProtectionEngine.SetProfile(tab.CurrentHost, profile);
                    }
                    if (tab.Id == _activeTabId)
                    {
                        bool isAbout = string.IsNullOrEmpty(e.Uri) || e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
                        AddressBar.Text = isAbout ? "" : e.Uri;
                        UpdateAddressBarPlaceholder();
                        LoadingBar.Visibility = Visibility.Visible;
                        UpdateLockIcon(tab);
                        UpdateProtectionUI(tab);
                    }
                    _dirty = true;
                }
                catch { }
            });

            try
            {
                if (tab.IsReady)
                {
                    var profile = ProtectionEngine.GetProfile(tab.CurrentHost);
                    _ = UpdatePerNavigationScriptsAsync(tab, profile);
                }
            }
            catch { }
        }

        private static async Task UpdatePerNavigationScriptsAsync(BrowserTab tab, SiteProfile profile)
        {
            if (!tab.IsReady) return;
            var cw = tab.WebView.CoreWebView2;

            try
            {
                if (!string.IsNullOrEmpty(tab.BlockerSeedScriptId))
                    cw.RemoveScriptToExecuteOnDocumentCreated(tab.BlockerSeedScriptId);
            }
            catch { }

            try
            {
                bool blockEnabled = profile.BlockAdsTrackers && profile.Mode != ProtectionMode.Monitor;
                var hosts = blockEnabled ? ProtectionEngine.GetBlockerSeedHosts() : Array.Empty<string>();
                var seed = ProtectionEngine.BuildBlockerSeedScript(hosts);
                tab.BlockerSeedScriptId = await cw.AddScriptToExecuteOnDocumentCreatedAsync(seed);
            }
            catch { tab.BlockerSeedScriptId = null; }

            try
            {
                if (!string.IsNullOrEmpty(tab.AntiFpScriptId))
                    cw.RemoveScriptToExecuteOnDocumentCreated(tab.AntiFpScriptId);
            }
            catch { }

            if (profile.AntiFingerprint)
            {
                try
                {
                    bool antiFpEnabled = profile.Mode != ProtectionMode.Monitor;
                    if (antiFpEnabled)
                    {
                        tab.AntiFpScriptId = await cw.AddScriptToExecuteOnDocumentCreatedAsync(ProtectionEngine.AntiFingerPrintScript);
                        tab.AntiFingerprintInjected = true;
                    }
                    else
                    {
                        tab.AntiFpScriptId = null;
                        tab.AntiFingerprintInjected = false;
                    }
                }
                catch { tab.AntiFpScriptId = null; }
            }
            else
            {
                tab.AntiFpScriptId = null;
                tab.AntiFingerprintInjected = false;
            }

            try
            {
                if (!string.IsNullOrEmpty(tab.FingerprintDetectScriptId))
                    cw.RemoveScriptToExecuteOnDocumentCreated(tab.FingerprintDetectScriptId);
                if (!string.IsNullOrEmpty(tab.BehavioralMonitorScriptId))
                    cw.RemoveScriptToExecuteOnDocumentCreated(tab.BehavioralMonitorScriptId);
            }
            catch { }

            bool monitoringEnabled = profile.Mode != ProtectionMode.Monitor;
            if (monitoringEnabled)
            {
                try
                {
                    tab.FingerprintDetectScriptId = await cw.AddScriptToExecuteOnDocumentCreatedAsync(PrivacyEngine.FingerprintDetectionScript);
                    tab.BehavioralMonitorScriptId = await cw.AddScriptToExecuteOnDocumentCreatedAsync(PrivacyEngine.BehavioralMonitorScript);
                }
                catch
                {
                    tab.FingerprintDetectScriptId = null;
                    tab.BehavioralMonitorScriptId = null;
                }
            }
            else
            {
                tab.FingerprintDetectScriptId = null;
                tab.BehavioralMonitorScriptId = null;
            }
        }

        private async void OnNavigationCompleted(BrowserTab tab, CoreWebView2NavigationCompletedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                tab.IsLoading = false;
                try { tab.Url = tab.WebView.CoreWebView2.Source; } catch { }
                if (tab.Id == _activeTabId)
                {
                    bool isWelcome = string.IsNullOrEmpty(tab.Url) || tab.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
                    AddressBar.Text = isWelcome ? "" : tab.Url;
                    UpdateAddressBarPlaceholder();
                    LoadingBar.Visibility = Visibility.Collapsed;
                    UpdateLockIcon(tab);
                    StatusText.Text = string.IsNullOrEmpty(tab.CurrentHost) ? "Ready — enter a URL above to start" : tab.CurrentHost;
                    UpdateProtectionUI(tab);
                }
            });
            if (!tab.IsReady) return;

            try
            {
                var profile = ProtectionEngine.GetProfile(tab.CurrentHost);
                await ApplyRuntimeBlockerAsync(tab, profile);
            }
            catch { }

            try { await tab.WebView.CoreWebView2.ExecuteScriptAsync(PrivacyEngine.StorageEnumerationScript); } catch { }
            try { await tab.WebView.CoreWebView2.ExecuteScriptAsync(PrivacyEngine.WebRtcLeakScript); } catch { }
            _dirty = true;
        }

        private static async Task ApplyRuntimeBlockerAsync(BrowserTab tab, SiteProfile profile)
        {
            if (!tab.IsReady) return;
            try
            {
                bool blockEnabled = profile.BlockAdsTrackers && profile.Mode != ProtectionMode.Monitor;
                var hosts = blockEnabled ? ProtectionEngine.GetBlockerSeedHosts() : Array.Empty<string>();
                var seed = ProtectionEngine.BuildBlockerSeedScript(hosts);
                await tab.WebView.CoreWebView2.ExecuteScriptAsync(seed);
            }
            catch { }
        }

        private void OnWebResourceRequested(BrowserTab tab, CoreWebView2WebResourceRequestedEventArgs e)
        {
            try
            {
                var request = e.Request; var uri = new Uri(request.Uri);
                bool isThirdParty = !string.IsNullOrEmpty(tab.CurrentHost) &&
                    !uri.Host.Equals(tab.CurrentHost, StringComparison.OrdinalIgnoreCase) &&
                    !uri.Host.EndsWith("." + tab.CurrentHost, StringComparison.OrdinalIgnoreCase);
                var headers = new Dictionary<string, string>();
                var iter = request.Headers.GetEnumerator();
                while (iter.MoveNext()) headers[iter.Current.Key] = iter.Current.Value;
                var entry = new RequestEntry
                {
                    Id = System.Threading.Interlocked.Increment(ref tab.NextRequestId),
                    Time = DateTime.Now, Method = request.Method, Host = uri.Host,
                    Path = uri.PathAndQuery, FullUrl = uri.AbsoluteUri, IsThirdParty = isThirdParty,
                    HasBody = request.Content != null, RequestHeaders = headers,
                    ResourceContext = e.ResourceContext.ToString()
                };
                bool isMedia = e.ResourceContext == CoreWebView2WebResourceContext.Media;
                // Signal-based analysis (thread-safe, no UI access)
                PrivacyEngine.AnalyzeRequest(entry, tab.CurrentHost);

                // JS injection heuristic (third-party scripts with tracking signals)
                if (entry.IsThirdParty && e.ResourceContext == CoreWebView2WebResourceContext.Script &&
                    (!string.IsNullOrEmpty(entry.TrackerLabel) || entry.TrackingParams.Count > 0))
                {
                    entry.Signals.Add(new DetectionSignal
                    {
                        SignalType = "js_injection",
                        Source = entry.Host,
                        Detail = "Third-party script injection with tracking signals",
                        Confidence = 0.75,
                        Risk = RiskType.Tracking,
                        Severity = 4,
                        Evidence = $"Script from {entry.Host}",
                        GdprArticle = "Art. 5(1)(a)"
                    });
                }
                entry.ThreatConfidence = entry.Signals.Count > 0
                    ? Math.Min(1.0, entry.Signals.Max(s => s.Confidence) + entry.Signals.Count * 0.02)
                    : 0;

                // ── ACTIVE PROTECTION: Evaluate blocking decision ──
                var profile = ProtectionEngine.GetProfile(tab.CurrentHost);
                var decision = ProtectionEngine.ShouldBlock(entry, tab.CurrentHost, profile);

                // In Monitor mode only the browser cancels requests (e.g. navigation); we never block.
                bool shouldCancelInBrowser = decision.Blocked && profile.Mode != ProtectionMode.Monitor;

                if (shouldCancelInBrowser)
                {
                    entry.IsBlocked = true;
                    entry.BlockReason = decision.Reason;
                    entry.BlockCategory = string.IsNullOrEmpty(decision.Category) ? "Tracking" : decision.Category;
                    entry.BlockConfidence = decision.Confidence > 0 ? decision.Confidence : entry.ThreatConfidence;
                    // Block the request by returning an empty 403 response
                    try
                    {
                        e.Response = tab.WebView.CoreWebView2.Environment.CreateWebResourceResponse(
                            null, 403, "Blocked by PrivacyMonitor", "");
                    }
                    catch { }

                    // Record blocked request for forensic trail
                    tab.BlockedCount++;
                    tab.BlockedRequests.Add(new BlockedRequestInfo
                    {
                        Time = DateTime.Now, Host = uri.Host, Url = uri.AbsoluteUri,
                        Reason = decision.Reason, Category = entry.BlockCategory,
                        Confidence = decision.Confidence, TrackerLabel = decision.TrackerLabel,
                        ResourceType = entry.ResourceContext, Method = entry.Method
                    });
                    if (tab.BlockedRequests.Count > BrowserTab.MaxBlockedRequests)
                        tab.BlockedRequests.RemoveAt(0);

                    // Add host to in-page element blocker
                    if (tab.IsReady)
                    {
                        string script = ProtectionEngine.BuildBlockerAddHostScript(uri.Host);
                        if (!string.IsNullOrEmpty(script))
                            Dispatcher.BeginInvoke(() => tab.WebView.CoreWebView2.ExecuteScriptAsync(script));
                    }
                }

                // Adaptive learning: record tracker signals (exclude generic cross-site)
                var trackerSignals = entry.Signals.Where(s =>
                    s.SignalType == "known_tracker" ||
                    s.SignalType == "heuristic_tracker" ||
                    s.SignalType == "tracking_param" ||
                    s.SignalType == "high_entropy_param" ||
                    s.SignalType == "pixel_tracking" ||
                    s.SignalType == "cookie_sync" ||
                    s.SignalType == "data_exfil").ToList();

                foreach (var sig in trackerSignals)
                    if (!isMedia)
                        ProtectionEngine.ObserveTrackerSignal(uri.Host, sig.SignalType, sig.Confidence);

                // Cross-site learning: track third-party domain appearances
                if (isThirdParty && trackerSignals.Count > 0 && !isMedia)
                    ProtectionEngine.ObserveCrossSiteAppearance(uri.Host, tab.CurrentHost);

                // Enqueue for batched drain (avoids per-request Dispatcher.Invoke)
                tab.PendingRequests.Enqueue(entry);
                _dirty = true;
            }
            catch { }
        }

        private void OnWebResourceResponseReceived(BrowserTab tab, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            try
            {
                var uri = e.Request.Uri; int statusCode = e.Response.StatusCode;
                var respHeaders = new Dictionary<string, string>();
                var iter = e.Response.Headers.GetEnumerator();
                while (iter.MoveNext()) respHeaders[iter.Current.Key] = iter.Current.Value;
                Dispatcher.Invoke(() =>
                {
                    // Drain pending so the request is in Requests when we match (avoids race with 500ms RefreshAll)
                    tab.DrainPending();
                    for (int i = tab.Requests.Count - 1; i >= 0; i--)
                    {
                        if (tab.Requests[i].FullUrl == uri && tab.Requests[i].StatusCode == 0)
                        {
                            tab.Requests[i].StatusCode = statusCode; tab.Requests[i].ResponseHeaders = respHeaders;
                            // Capture content-type and response size for evidence
                            if (respHeaders.TryGetValue("content-type", out var ct))
                                tab.Requests[i].ContentType = ct;
                            if (respHeaders.TryGetValue("content-length", out var cl) && long.TryParse(cl, out var clVal))
                                tab.Requests[i].ResponseSize = clVal;
                            if (tab.Requests[i].Host == tab.CurrentHost && tab.SecurityHeaders.Count == 0 && statusCode >= 200 && statusCode < 400)
                                tab.SecurityHeaders = PrivacyEngine.AnalyzeSecurityHeaders(respHeaders);
                            break;
                        }
                    }
                    _dirty = true;
                });
            }
            catch { }
        }

        private void OnWebMessage(BrowserTab tab, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                var root = doc.RootElement;
                string cat = root.GetProperty("cat").GetString() ?? "";
                if (cat == "fp")
                {
                    string type = root.GetProperty("type").GetString() ?? "";
                    string detail = root.GetProperty("detail").GetString() ?? "";
                    string source = root.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "";
                    long ts = root.TryGetProperty("ts", out var tsv) ? tsv.GetInt64() : 0;
                    if (!tab.Fingerprints.Any(f => f.Type == type))
                    {
                        string severity = type.Contains("Canvas") || type.Contains("Audio") || type.Contains("Surveillance") ? "Critical" :
                            type.Contains("Behavioral") || type.Contains("Session Replay") || type.Contains("Obfuscation") ? "High" : "Medium";
                        tab.Fingerprints.Add(new FingerprintFinding {
                            Time = DateTime.Now, Type = type, Detail = detail,
                            Severity = severity, GdprArticle = "Art. 5(1)(c)",
                            ScriptSource = source, Timestamp = ts });
                        _dirty = true;
                    }
                }
                else if (cat == "storage")
                {
                    tab.Cookies.Clear(); tab.Storage.Clear();
                    ParseStorage(tab, root);
                    tab.ConsentDetected = tab.Cookies.Any(c => c.Classification == "Consent") || tab.Storage.Any(s => s.Classification == "Consent");
                    _dirty = true;
                }
                else if (cat == "webrtc")
                {
                    string ip = root.GetProperty("ip").GetString() ?? "";
                    if (!tab.WebRtcLeaks.Any(l => l.IpAddress == ip))
                    {
                        string tp = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                        tab.WebRtcLeaks.Add(new WebRtcLeak { Time = DateTime.Now, IpAddress = ip, Type = tp });
                        _dirty = true;
                    }
                }
            }
            catch { }
        }

        private static void ParseStorage(BrowserTab tab, JsonElement root)
        {
            if (root.TryGetProperty("cookies", out var ca))
                foreach (var c in ca.EnumerateArray()) { string n = c.GetProperty("name").GetString() ?? ""; string v = c.TryGetProperty("value", out var vv) ? vv.GetString() ?? "" : "";
                    tab.Cookies.Add(new CookieItem { Name = n, Value = v, Domain = tab.CurrentHost, Classification = PrivacyEngine.ClassifyCookie(n) }); }
            if (root.TryGetProperty("localStorage", out var ls))
                foreach (var item in ls.EnumerateArray()) { string k = item.GetProperty("key").GetString() ?? ""; int sz = item.TryGetProperty("size", out var s) ? s.GetInt32() : 0;
                    tab.Storage.Add(new StorageItem { Store = "localStorage", Key = k, Size = sz, Classification = PrivacyEngine.ClassifyStorageKey(k) }); }
            if (root.TryGetProperty("sessionStorage", out var ss))
                foreach (var item in ss.EnumerateArray()) { string k = item.GetProperty("key").GetString() ?? ""; int sz = item.TryGetProperty("size", out var s) ? s.GetInt32() : 0;
                    tab.Storage.Add(new StorageItem { Store = "sessionStorage", Key = k, Size = sz, Classification = PrivacyEngine.ClassifyStorageKey(k) }); }
            if (root.TryGetProperty("indexedDB", out var idb))
                foreach (var item in idb.EnumerateArray()) { string n = item.GetProperty("name").GetString() ?? "";
                    tab.Storage.Add(new StorageItem { Store = "IndexedDB", Key = n, Classification = PrivacyEngine.ClassifyStorageKey(n) }); }
        }

        // ================================================================
        //  UI REFRESH
        // ================================================================
        private void RefreshAll()
        {
            var tab = ActiveTab; if (tab == null) return;

            // Drain pending requests from all tabs (batched, reduces UI thread contention)
            foreach (var t in _tabs) t.DrainPending();

            // Update per-tab blocked badges even if sidebar is hidden
            foreach (var t in _tabs) UpdateTabBlockedBadge(t);

            if (!_sidebarOpen) return; // skip heavy panel work if hidden

            var scan = BuildScanResult(tab);
            // Collect all signals across requests for aggregate analysis
            scan.AllSignals = tab.Requests.SelectMany(r => r.Signals).ToList();
            var score = PrivacyEngine.CalculateScore(scan);
            scan.Score = score; scan.GdprFindings = PrivacyEngine.MapToGdpr(scan);

            // Threat Tier banner
            UpdateTierBanner(score);

            // Shield badge
            ShieldGrade.Text = score.Grade;
            ShieldBadge.Background = ScoreBadgeBg(score.NumericScore);

            // Score banner
            GradeText.Text = score.Grade; GradeText.Foreground = new SolidColorBrush(Color.FromRgb(32, 33, 36));
            ScoreNum.Text = $"{score.NumericScore} / 100";
            ScoreSummary.Text = score.Summary;
            ScoreChip.Text = $"Score {score.NumericScore}";
            TierChip.Text = score.TierLabel;
            UpdateScoreRing(score.NumericScore);
            try { var parent = ScoreBarFill.Parent as Grid; if (parent != null && parent.ActualWidth > 0) ScoreBarFill.Width = parent.ActualWidth * score.NumericScore / 100.0; else ScoreBarFill.Width = 0; } catch { }
            ScoreBarFill.Background = ScoreBadgeBg(score.NumericScore);

            // Category score bars
            UpdateCategoryBars(score.CategoryScores);

            // Stats
            int allTrackingCookies = PrivacyEngine.CountAllTrackingCookies(scan);
            StatTotal.Text = tab.Requests.Count.ToString();
            StatBlocked.Text = tab.BlockedCount.ToString();
            StatThirdParty.Text = tab.Requests.Count(r => r.IsThirdParty).ToString();
            StatTrackers.Text = tab.Requests.Count(r => !string.IsNullOrEmpty(r.TrackerLabel) && !r.IsBlocked).ToString();
            StatFingerprints.Text = tab.Fingerprints.Count.ToString();
            StatCookies.Text = allTrackingCookies.ToString();

            // Update protection UI (badge, status bar)
            UpdateProtectionUI(tab);

            // Status bar
            int totalAll = _tabs.Sum(t => t.Requests.Count);
            RequestCountText.Text = $"{tab.Requests.Count} requests  |  {_tabs.Count} tab(s)" +
                (tab.ConsentDetected ? "  |  Consent detected" : "");

            // Breakdown
            var bk = new StringBuilder();
            foreach (var kv in score.Breakdown.Where(b => b.Value != 0)) bk.Append($"{kv.Key}: {kv.Value}   ");
            BreakdownText.Text = bk.ToString();

            // GDPR
            GdprList.ItemsSource = scan.GdprFindings.Select(g => new GdprListItem { Article = g.Article, Title = g.Title,
                Description = g.Description, Severity = g.Severity, SeverityColor = SeverityBrush(g.Severity), Count = $"({g.Count})" }).ToList();

            // Top trackers
            var topT = tab.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerLabel)).GroupBy(r => r.TrackerLabel)
                .OrderByDescending(g => g.Count()).Take(8).Select(g => {
                    double avgConf = g.Average(r => r.ThreatConfidence);
                    var (cLabel, cColor) = ConfidenceToLabel(avgConf);
                    return new TrackerSummaryItem { Name = g.Key, Count = $"{g.Count()} req", SampleHost = g.First().Host,
                        ConfidenceLabel = cLabel, ConfidenceColor = cColor };
                }).ToList();
            TopTrackersList.ItemsSource = topT;
            NoTrackersText.Visibility = topT.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Live feed
            string liveSearch = LiveFeedSearch?.Text?.Trim() ?? "";
            bool blockedOnly = LiveFeedBlockedOnly?.IsChecked == true;
            var liveItems = tab.Requests.AsEnumerable();
            if (blockedOnly) liveItems = liveItems.Where(r => r.IsBlocked);
            if (!string.IsNullOrEmpty(liveSearch)) liveItems = liveItems.Where(r => r.Host.Contains(liveSearch, StringComparison.OrdinalIgnoreCase));

            LiveFeed.ItemsSource = liveItems.TakeLast(18).Reverse().Select(r => {
                string label; SolidColorBrush color;
                if (r.IsBlocked)
                {
                    string shortLabel = ConfidenceShortLabel(r.BlockConfidence > 0 ? r.BlockConfidence : r.ThreatConfidence);
                    string baseLabel = r.BlockCategory == "Ad" ? "BLOCKED AD" : r.BlockCategory == "Behavioral" ? "BLOCKED BEHAVIOR" : "BLOCKED TRACKER";
                    label = shortLabel.Length > 0 ? $"{baseLabel} ({shortLabel})" : baseLabel;
                    color = new SolidColorBrush(Color.FromRgb(217, 48, 37));
                }
                else if (!string.IsNullOrEmpty(r.TrackerLabel))
                {
                    if (_expertMode)
                        label = "TRACKER";
                    else
                    {
                        var (cLabel, _) = ConfidenceToLabel(r.ThreatConfidence);
                        label = cLabel.Length > 0 ? cLabel.Split(' ')[0] : "TRACKER"; // "Confirmed" / "Likely" / "Possibly"
                    }
                    color = new SolidColorBrush(Color.FromRgb(217, 48, 37));
                }
                else if (r.IsThirdParty) { label = "3RD"; color = new SolidColorBrush(Color.FromRgb(227, 116, 0)); }
                else { label = "1ST"; color = new SolidColorBrush(Color.FromRgb(24, 128, 56)); }
                return new LiveFeedItem { Time = r.Time.ToString("HH:mm:ss"), Host = r.Host, Label = label, LabelColor = color };
            }).ToList();

            // Recommendations (confidence-weighted)
            RecommendationsList.ItemsSource = GenerateRecommendations(scan);

            // Mitigation tips (simple-mode, confidence-weighted)
            var tips = GenerateMitigationTips(scan, score);
            MitigationTipsList.ItemsSource = tips;
            NoMitigationText.Visibility = tips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Dynamic category tooltips with mitigation guidance
            UpdateCategoryTooltips(scan, score.CategoryScores);

            RefreshRequestList(tab);
            RefreshStorageList(tab);
            RefreshFingerprintList(tab);
            RefreshSecurityList(tab);
            RefreshForensicsPanel(tab, scan, score);
        }

        private void RefreshRequestList(BrowserTab tab)
        {
            bool filterOn = FilterCheck?.IsChecked == true;
            string search = SearchBox?.Text?.Trim().ToLowerInvariant() ?? "";
            var visible = tab.Requests.AsEnumerable();
            if (filterOn) visible = visible.Where(r => r.IsThirdParty || !string.IsNullOrEmpty(r.TrackerLabel) || r.TrackingParams.Count > 0);
            if (search.Length > 0) visible = visible.Where(r => r.Host.Contains(search, StringComparison.OrdinalIgnoreCase) || r.Path.Contains(search, StringComparison.OrdinalIgnoreCase) || (r.TrackerLabel?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
            RequestList.ItemsSource = visible.Reverse().Take(300).Select(r => new RequestListItem {
                Host = r.Host, Path = r.Path.Length > 50 ? r.Path[..50] + "..." : r.Path, Method = r.Method,
                Status = r.IsBlocked ? "BLK" : r.StatusCode > 0 ? r.StatusCode.ToString() : "...",
                TypeLabel = r.IsBlocked ? (r.BlockCategory == "Ad" ? "BLOCKED AD" : r.BlockCategory == "Behavioral" ? "BLOCKED BEHAVIOR" : "BLOCKED TRACKER") :
                    !string.IsNullOrEmpty(r.TrackerLabel) ? "TRACKER" : r.IsThirdParty ? "THIRD-PARTY" : "FIRST-PARTY",
                TypeColor = r.IsBlocked ? new SolidColorBrush(Color.FromRgb(217, 48, 37)) :
                    !string.IsNullOrEmpty(r.TrackerLabel) ? new SolidColorBrush(Color.FromRgb(217, 48, 37)) :
                    r.IsThirdParty ? new SolidColorBrush(Color.FromRgb(227, 116, 0)) : new SolidColorBrush(Color.FromRgb(24, 128, 56)),
                ConfidenceLabel = r.IsBlocked ? ConfidenceShortLabel(r.BlockConfidence > 0 ? r.BlockConfidence : r.ThreatConfidence) : r.ThreatConfidence > 0 ? $"{r.ThreatConfidence:P0}" : "",
                ToolTip = BuildBlockedTooltip(r),
                Entry = r }).ToList();
        }

        private void RefreshStorageList(BrowserTab tab)
        {
            var items = new List<StorageListItem>();
            var setCookieNames = tab.Requests.SelectMany(r => r.ResponseHeaders.Where(h => h.Key.Equals("set-cookie", StringComparison.OrdinalIgnoreCase)))
                .Select(h => h.Value.Split(';')[0].Split('=')[0].Trim()).Distinct().ToList();
            foreach (var c in tab.Cookies)
                items.Add(new StorageListItem { Label = "CK", Name = c.Name, Store = "Cookie (JS)", Classification = c.Classification, ClassColor = ClassBrush(c.Classification), Size = c.Value.Length > 0 ? $"{c.Value.Length}ch" : "" });
            foreach (var n in setCookieNames.Where(n => !tab.Cookies.Any(c => c.Name == n)))
            { var cls = PrivacyEngine.ClassifyCookie(n); items.Add(new StorageListItem { Label = "HC", Name = n, Store = "Cookie (HttpOnly)", Classification = cls, ClassColor = ClassBrush(cls) }); }
            foreach (var s in tab.Storage)
                items.Add(new StorageListItem { Label = s.Store switch { "localStorage" => "LS", "sessionStorage" => "SS", "IndexedDB" => "DB", _ => "--" },
                    Name = s.Key, Store = s.Store, Classification = s.Classification, ClassColor = ClassBrush(s.Classification), Size = s.Size > 0 ? $"{s.Size:N0}ch" : "" });
            int tracking = items.Count(i => i.Classification.Contains("Tracking"));
            StorageSummaryText.Text = $"{tab.Cookies.Count} cookies   {setCookieNames.Count} set-cookie   {tab.Storage.Count} storage   {tracking} tracking";
            StorageList.ItemsSource = items;
        }

        private void RefreshFingerprintList(BrowserTab tab)
        {
            // Expert mode: full technical detail
            FingerprintList.ItemsSource = tab.Fingerprints.Select(f => new FingerprintListItem {
                Type = f.Type,
                Detail = f.Detail + (string.IsNullOrEmpty(f.ScriptSource) ? "" : $"\nSource: {f.ScriptSource}"),
                Severity = f.Severity,
                SeverityColor = SeverityBrush(f.Severity), Time = f.Time.ToString("HH:mm:ss") }).ToList();

            // Simple mode: human-readable cards
            FpSimpleList.ItemsSource = tab.Fingerprints.Select(f => {
                double conf = f.Severity == "Critical" ? 0.9 : f.Severity == "High" ? 0.7 : 0.5;
                var (cLabel, cColor) = ConfidenceToLabel(conf);
                return new FpSimpleItem {
                    Description = HumanizeFingerprint(f.Type),
                    ConfidenceLabel = cLabel,
                    SeverityColor = SeverityBrush(f.Severity),
                    ConfidenceColor = cColor
                };
            }).ToList();

            int behavioral = tab.Fingerprints.Count(f => f.Type.StartsWith("Behavioral:") || f.Type.Contains("Session Replay") || f.Type.Contains("Dynamic") || f.Type.Contains("Obfuscation") || f.Type.Contains("Beacon") || f.Type.Contains("Cross-Frame"));
            int apiLevel = tab.Fingerprints.Count - behavioral;
            FpSummaryText.Text = tab.Fingerprints.Count == 0 ? "No fingerprinting detected." :
                _expertMode ? $"{apiLevel} API probe(s), {behavioral} behavioral technique(s) detected."
                : $"{tab.Fingerprints.Count} privacy technique(s) detected on this page.";
            if (tab.WebRtcLeaks.Count > 0)
            {
                if (_expertMode)
                { var sb = new StringBuilder(); sb.AppendLine($"{tab.WebRtcLeaks.Count} IP address(es) leaked via WebRTC:");
                    foreach (var l in tab.WebRtcLeaks) sb.AppendLine($"   {l.IpAddress}  ({l.Type})");
                    sb.AppendLine("\nWebsites can discover your real IP even through a VPN.");
                    WebRtcText.Text = sb.ToString(); }
                else
                    WebRtcText.Text = $"This site can see your real IP address ({tab.WebRtcLeaks.Count} leak detected). Websites can discover your IP even through a VPN.";
            }
            else WebRtcText.Text = "No WebRTC IP leaks detected.";
        }

        private void RefreshSecurityList(BrowserTab tab)
        {
            if (tab.SecurityHeaders.Count == 0) { SecSummaryText.Text = "Load a page to see security headers."; SecurityList.ItemsSource = null; return; }
            int p = tab.SecurityHeaders.Count(h => h.Status == "Present"), m = tab.SecurityHeaders.Count(h => h.Status == "Missing"), w = tab.SecurityHeaders.Count(h => h.Status == "Weak");
            SecSummaryText.Text = $"{p} present   {w} weak   {m} missing";
            SecurityList.ItemsSource = tab.SecurityHeaders.Select(h => new SecurityListItem { Header = h.Header,
                StatusLabel = h.Status switch { "Present" => "PASS", "Weak" => "WARN", _ => "FAIL" },
                StatusColor = h.Status switch { "Present" => new SolidColorBrush(Color.FromRgb(24, 128, 56)), "Weak" => new SolidColorBrush(Color.FromRgb(234, 134, 0)), _ => new SolidColorBrush(Color.FromRgb(217, 48, 37)) },
                Value = h.Value, Explanation = h.Explanation }).ToList();
        }

        // ================================================================
        //  THREAT TIER BANNER
        // ================================================================
        private void UpdateTierBanner(PrivacyScore score)
        {
            TierLabel.Text = score.TierLabel;
            TierDetail.Text = score.Tier switch
            {
                ThreatTier.SurveillanceGrade => "Extensive tracking infrastructure with identity stitching, session replay, or data broker activity.",
                ThreatTier.AggressiveTracking => "Multiple advertising trackers, fingerprinting, or session replay detected.",
                ThreatTier.TypicalWebTracking => "Standard analytics and third-party tracking present.",
                _ => "Minimal or no tracking detected. Basic web infrastructure only."
            };
            TierIcon.Text = score.Tier switch
            {
                ThreatTier.SurveillanceGrade => "\u26A0",  // warning
                ThreatTier.AggressiveTracking => "\u2622",  // radioactive
                ThreatTier.TypicalWebTracking => "\u25CF",  // filled circle
                _ => "\u2713"  // checkmark
            };
            TierBanner.Background = score.Tier switch
            {
                ThreatTier.SurveillanceGrade => new SolidColorBrush(Color.FromRgb(165, 14, 14)),
                ThreatTier.AggressiveTracking => new SolidColorBrush(Color.FromRgb(217, 48, 37)),
                ThreatTier.TypicalWebTracking => new SolidColorBrush(Color.FromRgb(227, 116, 0)),
                _ => new SolidColorBrush(Color.FromRgb(24, 128, 56))
            };
        }

        // ================================================================
        //  CATEGORY SCORE BARS
        // ================================================================
        private void UpdateCategoryBars(Dictionary<string, int> cats)
        {
            void Set(TextBlock val, Border bar, string key)
            {
                int s = cats.TryGetValue(key, out var v) ? Math.Clamp(v, 0, 100) : 100;
                val.Text = s.ToString();
                val.Foreground = ScoreBadgeBg(s);
                bar.Background = ScoreBadgeBg(s);
                try { var parent = bar.Parent as Grid; if (parent != null && parent.ActualWidth > 0) bar.Width = parent.ActualWidth * s / 100.0; else bar.Width = 0; } catch { }
            }
            Set(CatTrackingVal, CatTrackingBar, "Tracking");
            Set(CatFingerprintVal, CatFingerprintBar, "Fingerprinting");
            Set(CatLeakageVal, CatLeakageBar, "DataLeakage");
            Set(CatSecurityVal, CatSecurityBar, "Security");
            Set(CatBehavioralVal, CatBehavioralBar, "Behavioral");
        }

        private void UpdateScoreRing(int score)
        {
            if (ScoreRing == null || ScoreRingBase == null) return;
            double radius = Math.Max(0, (ScoreRingBase.Width - ScoreRingBase.StrokeThickness) / 2.0);
            if (radius <= 0) radius = 45;
            double circumference = 2 * Math.PI * radius;
            double progress = Math.Clamp(score, 0, 100) / 100.0 * circumference;
            ScoreRing.Stroke = ScoreBadgeBg(score);
            ScoreRing.StrokeDashArray = new DoubleCollection { progress, Math.Max(0, circumference - progress) };
            ScoreRing.StrokeDashOffset = circumference * 0.25;
        }

        // ================================================================
        //  FORENSICS PANEL
        // ================================================================
        private void RefreshForensicsPanel(BrowserTab tab, ScanResult scan, PrivacyScore score)
        {
            // Only compute forensics if the panel is visible
            if (_activeAnalysisTab != 6) return;

            // Cross-tab session context update
            int crossTabLinks = ForensicEngine.UpdateSessionContext(
                _session, tab.IdentifierToDomains,
                tab.SeenTrackerDomains, tab.SeenTrackerCompanies, tab.DomainRequestCounts);
            int crossTabIds = ForensicEngine.CountCrossTabIdentifiers(_session);

            // Identity links
            var identityLinks = ForensicEngine.BuildIdentityLinks(tab.IdentifierToDomains);
            IdentityLinksList.ItemsSource = identityLinks.Select(l => new IdentityLinkItem
            {
                ParameterName = l.ParameterName,
                RiskLevel = l.RiskLevel,
                DomainsText = $"{l.DomainCount} domains: {string.Join(", ", l.Domains.Take(5))}"
            }).ToList();

            // Company clusters
            var clusters = ForensicEngine.ClusterByCompany(tab.Requests);
            CompanyClustersList.ItemsSource = clusters.Take(10).Select(c => new CompanyClusterItem
            {
                Company = c.Company,
                RequestsLabel = $"{c.TotalRequests} req",
                ServicesText = $"Services: {string.Join(", ", c.Services.Take(4))}",
                DataTypesText = c.DataTypes.Count > 0 ? $"Data: {string.Join(", ", c.DataTypes.Take(5))}" : ""
            }).ToList();

            // Behavioral patterns
            var patterns = ForensicEngine.DetectBehavioralPatterns(tab.Fingerprints);
            if (patterns.Count > 0)
            {
                var bSb = new StringBuilder();
                foreach (var p in patterns)
                    bSb.AppendLine($"[{p.Name}] {p.Detail} (confidence: {p.Confidence:P0})");
                BehavioralText.Text = bSb.ToString();
            }
            else
                BehavioralText.Text = "No behavioral fingerprinting patterns detected yet.";

            // Request bursts
            var bursts = ForensicEngine.DetectRequestBursts(tab.Requests);
            if (bursts.Count > 0)
            {
                var burstSb = new StringBuilder();
                foreach (var b in bursts.Take(5))
                    burstSb.AppendLine($"{b.Domain}: {b.Count} requests in {b.WindowMs}ms ({b.RequestsPerSecond} req/s)");
                BurstText.Text = burstSb.ToString();
            }
            else
                BurstText.Text = "No request burst patterns detected.";

            // Cookie sync chains
            int cookieSyncChains = ForensicEngine.DetectCookieSyncChains(tab.DataFlowEdges);

            // Cross-tab correlation display
            var ctSb = new StringBuilder();
            ctSb.AppendLine($"Session-wide tracker domains: {_session.AllSeenTrackerDomains.Count}");
            ctSb.AppendLine($"Session-wide companies: {_session.AllSeenCompanies.Count}");
            ctSb.AppendLine($"Cross-tab identifiers (3+ domains): {crossTabIds}");
            if (crossTabLinks > 0) ctSb.AppendLine($"New cross-tab links detected: {crossTabLinks}");
            CrossTabText.Text = ctSb.ToString();

            // Session risk (enhanced)
            var sessionRisk = ForensicEngine.AssessSessionRisk(scan, identityLinks, patterns, tab.DataFlowEdges, clusters, bursts, cookieSyncChains, crossTabLinks);

            if (_expertMode)
            {
                var riskSb = new StringBuilder();
                riskSb.AppendLine($"Overall: {sessionRisk.OverallRisk}   |   Tier: {sessionRisk.TierLabel}");
                riskSb.AppendLine($"Identity Stitching: {sessionRisk.IdentityStitchingRisk}   |   Data Propagation: {sessionRisk.DataPropagationRisk}");
                riskSb.AppendLine($"Fingerprinting: {sessionRisk.FingerprintingRisk}   |   Behavioral: {sessionRisk.BehavioralTrackingRisk}");
                riskSb.AppendLine($"Concentration: {sessionRisk.ConcentrationRisk}");
                if (sessionRisk.RequestBursts > 0) riskSb.AppendLine($"Request bursts: {sessionRisk.RequestBursts}   Cookie syncs: {sessionRisk.CookieSyncChains}   Cross-tab: {sessionRisk.CrossTabLinks}");
                riskSb.AppendLine();
                riskSb.AppendLine(sessionRisk.Summary);
                SessionRiskText.Text = riskSb.ToString();
            }
            else
            {
                // Simple mode: plain English session risk
                SessionRiskText.Text = sessionRisk.Summary;
            }

            // Simple behavioral summary for normal users
            SimpleBehavioralText.Text = BuildSimpleBehavioralSummary(tab, clusters, patterns, identityLinks, bursts);

            // Forensic timeline
            ForensicTimelineList.ItemsSource = tab.ForensicTimeline.TakeLast(25).Reverse().Select(fe => new ForensicTimelineItem
            {
                TimeText = fe.Time.ToString("HH:mm:ss"),
                Summary = fe.Summary
            }).ToList();

            // Score explanations
            var explanations = ForensicEngine.ExplainScore(score, scan, clusters);
            ExplanationsList.ItemsSource = explanations.Select(ex => new ScoreExplanationItem
            {
                Category = ex.Category,
                PenaltyText = ex.Penalty.ToString(),
                Justification = ex.Justification,
                GdprRelevance = $"GDPR: {ex.GdprRelevance}"
            }).ToList();
        }

        // ================================================================
        //  RECOMMENDATIONS
        // ================================================================
        private static List<RecommendationItem> GenerateRecommendations(ScanResult scan)
        {
            var recs = new List<RecommendationItem>();

            // Only report confirmed/likely trackers (confidence >= 0.5)
            var confirmedTrackers = scan.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerLabel) && r.ThreatConfidence >= 0.5).ToList();
            int trackers = confirmedTrackers.Select(r => r.TrackerLabel).Distinct().Count();
            int fps = scan.Fingerprints.Count;
            int highFps = scan.Fingerprints.Count(f => f.Severity == "Critical" || f.Severity == "High");
            int trackingCookies = PrivacyEngine.CountAllTrackingCookies(scan);
            int missingHeaders = scan.SecurityHeaders.Count(h => h.Status == "Missing");
            int leaks = scan.WebRtcLeaks.Count;
            int postTp = scan.Requests.Count(r => r.IsThirdParty && r.HasBody);
            int behavioral = scan.Fingerprints.Count(f => f.Type.StartsWith("Behavioral:") || f.Type.Contains("Session Replay") || f.Type.Contains("Obfuscation"));

            if (trackers > 0) recs.Add(new RecommendationItem { Title = $"{trackers} confirmed tracking service(s)",
                Description = "Block these trackers with uBlock Origin, Privacy Badger, or enable your browser's built-in tracking protection.",
                Severity = "High", SeverityColor = new SolidColorBrush(Color.FromRgb(217, 48, 37)) });
            if (highFps > 0) recs.Add(new RecommendationItem { Title = $"{highFps} high-risk fingerprinting technique(s)",
                Description = "Use Firefox with resistFingerprinting enabled, or install a canvas/WebGL blocker to prevent device profiling.",
                Severity = "Critical", SeverityColor = new SolidColorBrush(Color.FromRgb(165, 14, 14)) });
            if (behavioral > 0) recs.Add(new RecommendationItem { Title = $"{behavioral} behavioral monitoring technique(s)",
                Description = "This site monitors your mouse, scrolling, or keystrokes. Consider disabling JavaScript or using a script blocker like NoScript.",
                Severity = "High", SeverityColor = new SolidColorBrush(Color.FromRgb(217, 48, 37)) });
            if (trackingCookies > 3) recs.Add(new RecommendationItem { Title = $"{trackingCookies} tracking cookie(s)",
                Description = "Clear cookies after each session, use container tabs, or enable automatic cookie cleanup in your browser.",
                Severity = "High", SeverityColor = new SolidColorBrush(Color.FromRgb(227, 116, 0)) });
            else if (trackingCookies > 0) recs.Add(new RecommendationItem { Title = $"{trackingCookies} tracking cookie(s)",
                Description = "Use private browsing mode or clear cookies periodically to limit persistent tracking.",
                Severity = "Medium", SeverityColor = new SolidColorBrush(Color.FromRgb(227, 116, 0)) });
            if (missingHeaders > 3) recs.Add(new RecommendationItem { Title = $"{missingHeaders} security headers missing",
                Description = "This site lacks important protections. Avoid entering passwords or personal data here.",
                Severity = "Medium", SeverityColor = new SolidColorBrush(Color.FromRgb(227, 116, 0)) });
            if (leaks > 0) recs.Add(new RecommendationItem { Title = "WebRTC leaking your real IP",
                Description = "Your IP is exposed even through VPN. Disable WebRTC in browser settings or install WebRTC Leak Shield.",
                Severity = "Critical", SeverityColor = new SolidColorBrush(Color.FromRgb(217, 48, 37)) });
            if (postTp > 0) recs.Add(new RecommendationItem { Title = $"{postTp} form submission(s) to external servers",
                Description = "Data you enter may be sent to third parties. Only submit sensitive info on sites you fully trust.",
                Severity = "High", SeverityColor = new SolidColorBrush(Color.FromRgb(227, 116, 0)) });
            if (recs.Count == 0) recs.Add(new RecommendationItem { Title = "No major issues found",
                Description = "This site appears to respect user privacy. Standard web infrastructure only.",
                Severity = "Good", SeverityColor = new SolidColorBrush(Color.FromRgb(24, 128, 56)) });
            return recs;
        }

        // ================================================================
        //  REQUEST DETAIL
        // ================================================================
        private void RequestList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (RequestList.SelectedItem is not RequestListItem item) return;
            var r = item.Entry; var sb = new StringBuilder();

            if (_expertMode)
            {
                // EXPERT: Full technical detail
                if (r.IsBlocked)
                {
                    sb.AppendLine($"[BLOCKED] {r.BlockReason}");
                    sb.AppendLine($"Request was blocked by ProtectionEngine.");
                    if (!string.IsNullOrEmpty(r.BlockCategory)) sb.AppendLine($"Category: {r.BlockCategory}");
                    if (r.BlockConfidence > 0) sb.AppendLine($"Confidence: {r.BlockConfidence:P0}");
                    if (!string.IsNullOrEmpty(r.ResourceContext)) sb.AppendLine($"Type: {r.ResourceContext}");
                    sb.AppendLine();
                }
                if (!string.IsNullOrEmpty(r.TrackerLabel))
                {
                    sb.AppendLine($"[TRACKER] {r.TrackerLabel}");
                    if (!string.IsNullOrEmpty(r.TrackerCompany)) sb.AppendLine($"Company: {r.TrackerCompany}");
                    if (!string.IsNullOrEmpty(r.TrackerCategoryName)) sb.AppendLine($"Category: {r.TrackerCategoryName}");
                    sb.AppendLine($"Confidence: {r.ThreatConfidence:P0}");
                    sb.AppendLine();
                }
                else if (r.IsThirdParty) sb.AppendLine("[THIRD-PARTY]\n");
                else sb.AppendLine("[FIRST-PARTY]\n");

                if (r.Signals.Count > 0)
                {
                    sb.AppendLine("DETECTION SIGNALS:");
                    foreach (var sig in r.Signals)
                        sb.AppendLine($"  [{sig.Confidence:F2}] {sig.SignalType}: {sig.Detail}");
                    sb.AppendLine();
                }
                if (r.DataClassifications.Count > 0) { sb.AppendLine("DATA TYPES:"); foreach (var d in r.DataClassifications) sb.AppendLine($"  {d}"); sb.AppendLine(); }
                if (r.TrackingParams.Count > 0) { sb.AppendLine("TRACKING PARAMS:"); foreach (var p in r.TrackingParams) sb.AppendLine($"  {p}"); sb.AppendLine(); }
                sb.AppendLine($"{r.Method} {r.Path}\nHost: {r.Host}\nStatus: {(r.StatusCode > 0 ? r.StatusCode.ToString() : "pending")}\nType: {r.ResourceContext}\nTime: {r.Time:HH:mm:ss.fff}");
                if (!string.IsNullOrEmpty(r.ContentType)) sb.AppendLine($"Content-Type: {r.ContentType}");
                if (r.ResponseSize > 0) sb.AppendLine($"Response Size: {FormatBytes(r.ResponseSize)}");
                if (r.RequestHeaders.Count > 0) { sb.AppendLine("\nREQUEST HEADERS:"); foreach (var kv in r.RequestHeaders.Take(15)) sb.AppendLine($"  {kv.Key}: {(kv.Value.Length > 120 ? kv.Value[..120] + "..." : kv.Value)}"); }
                if (r.ResponseHeaders.Count > 0) { sb.AppendLine("\nRESPONSE HEADERS:"); foreach (var kv in r.ResponseHeaders.Take(15)) sb.AppendLine($"  {kv.Key}: {(kv.Value.Length > 120 ? kv.Value[..120] + "..." : kv.Value)}"); }
            }
            else
            {
                // SIMPLE: Human-readable summary
                if (r.IsBlocked)
                {
                    sb.AppendLine("This request was BLOCKED by PrivacyMonitor.");
                    sb.AppendLine($"Reason: {r.BlockReason}");
                    if (!string.IsNullOrEmpty(r.BlockCategory)) sb.AppendLine($"Category: {r.BlockCategory}");
                    if (r.BlockConfidence > 0) sb.AppendLine($"Confidence: {r.BlockConfidence:P0}");
                    var suggestion = MitigationSuggestion(r.BlockCategory);
                    if (!string.IsNullOrEmpty(suggestion)) sb.AppendLine($"Suggestion: {suggestion}");
                    sb.AppendLine();
                }
                if (!string.IsNullOrEmpty(r.TrackerLabel))
                {
                    var (cLabel, _) = ConfidenceToLabel(r.ThreatConfidence);
                    sb.AppendLine($"{r.TrackerLabel}  ({cLabel})");
                    if (!string.IsNullOrEmpty(r.TrackerCompany)) sb.AppendLine($"Owned by: {r.TrackerCompany}");
                    sb.AppendLine();
                    if (r.DataClassifications.Count > 0)
                        sb.AppendLine($"Collects: {string.Join(", ", r.DataClassifications)}");
                    if (r.TrackingParams.Count > 0)
                        sb.AppendLine($"Tracking parameters found in URL ({r.TrackingParams.Count})");
                    sb.AppendLine($"\nSent to: {r.Host}");
                    if (r.HasBody) sb.AppendLine("Form data was sent to this server.");
                }
                else if (r.IsThirdParty)
                {
                    sb.AppendLine($"Third-party request to {r.Host}");
                    if (r.DataClassifications.Count > 0) sb.AppendLine($"Data types: {string.Join(", ", r.DataClassifications)}");
                }
                else
                    sb.AppendLine($"First-party request to {r.Host}");
            }
            DetailText.Text = sb.ToString();
        }

        // ================================================================
        //  REPORT / SCREENSHOT / EXPORT
        // ================================================================
        private void SaveReport_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab; if (tab == null) return;
            try
            {
                var scan = BuildScanResult(tab); scan.ScanEnd = DateTime.Now;
                scan.AllSignals = tab.Requests.SelectMany(r => r.Signals).ToList();
                scan.Score = PrivacyEngine.CalculateScore(scan); scan.GdprFindings = PrivacyEngine.MapToGdpr(scan);
                var dlg = new SaveFileDialog { Filter = "HTML|*.html", FileName = $"privacy-audit-{tab.CurrentHost}-{DateTime.Now:yyyyMMdd-HHmmss}.html" };
                if (dlg.ShowDialog() == true)
                {
                    File.WriteAllText(dlg.FileName, ReportGenerator.GenerateHtml(scan, tab.DataFlowEdges, tab.IdentifierToDomains), Encoding.UTF8);
                    ReportStatusText.Text = $"Saved: {dlg.FileName}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex) { ReportStatusText.Text = $"Error: {ex.Message}"; }
        }

        private async void Screenshot_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab; if (tab == null || !tab.IsReady) return;
            try
            {
                var dlg = new SaveFileDialog { Filter = "PNG|*.png", FileName = $"screenshot-{tab.CurrentHost}-{DateTime.Now:yyyyMMdd-HHmmss}.png" };
                if (dlg.ShowDialog() == true)
                {
                    using var ms = new MemoryStream();
                    await tab.WebView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                    ms.Seek(0, SeekOrigin.Begin); await using var fs = File.Create(dlg.FileName); await ms.CopyToAsync(fs);
                    ReportStatusText.Text = $"Saved: {dlg.FileName}";
                }
            }
            catch (Exception ex) { ReportStatusText.Text = $"Error: {ex.Message}"; }
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab; if (tab == null) return;
            try
            {
                var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"requests-{tab.CurrentHost}-{DateTime.Now:yyyyMMdd-HHmmss}.csv" };
                if (dlg.ShowDialog() == true)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Time,Method,Host,Path,Status,ThirdParty,Tracker,TrackingParams,HasBody,DataTypes");
                    foreach (var r in tab.Requests)
                        sb.AppendLine($"{r.Time:HH:mm:ss.fff},{r.Method},\"{r.Host}\",\"{r.Path.Replace("\"", "\"\"")}\",{r.StatusCode},{r.IsThirdParty},\"{r.TrackerLabel}\",\"{string.Join(";", r.TrackingParams)}\",{r.HasBody},\"{string.Join(";", r.DataClassifications)}\"");
                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    ReportStatusText.Text = $"Exported: {dlg.FileName}";
                }
            }
            catch (Exception ex) { ReportStatusText.Text = $"Error: {ex.Message}"; }
        }

        // ================================================================
        //  NAVIGATION
        // ================================================================
        private void Back_Click(object sender, RoutedEventArgs e) => ActiveTab?.WebView.CoreWebView2?.GoBack();
        private void Forward_Click(object sender, RoutedEventArgs e) => ActiveTab?.WebView.CoreWebView2?.GoForward();
        private void Reload_Click(object sender, RoutedEventArgs e) => ActiveTab?.WebView.CoreWebView2?.Reload();
        private void Go_Click(object sender, RoutedEventArgs e) => Navigate();
        private void AddressBar_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Navigate(); }

        private void AddressBar_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => UpdateAddressBarPlaceholder();

        private void AddressBar_GotFocus(object sender, RoutedEventArgs e)
            => UpdateAddressBarPlaceholder();

        private void AddressBar_LostFocus(object sender, RoutedEventArgs e)
            => UpdateAddressBarPlaceholder();

        private void UpdateAddressBarPlaceholder()
        {
            if (AddressBarPlaceholder == null || AddressBar == null) return;
            bool isEmpty = string.IsNullOrWhiteSpace(AddressBar.Text);
            AddressBarPlaceholder.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }
        private async void NewTab_Click(object sender, RoutedEventArgs e) => await CreateNewTab();

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab?.IsReady == true) tab.WebView.CoreWebView2.NavigateToString(WelcomeHtml);
        }

        private const string SearchEngineUrl = "https://duckduckgo.com/?q=";

        private static bool LooksLikeUrl(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return false;
            var t = input.Trim();
            if (t.Contains(' ')) return false;
            if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                t.StartsWith("file:", StringComparison.OrdinalIgnoreCase)) return true;
            if (t.Contains("://")) return true;
            if (t.Contains('.')) return true; // e.g. google.com, sub.domain.co.uk
            return false;
        }

        private void Navigate()
        {
            var tab = ActiveTab; if (tab == null || !tab.IsReady) return;
            var input = AddressBar.Text.Trim(); if (string.IsNullOrEmpty(input)) return;

            string url;
            if (LooksLikeUrl(input))
            {
                url = input.Contains("://") ? input : "https://" + input;
            }
            else
            {
                url = SearchEngineUrl + Uri.EscapeDataString(input);
            }
            try { tab.WebView.CoreWebView2.Navigate(url); } catch { }
        }

        /// <summary>Show Find in Page bar (Ctrl+F). Uses WebView2 Find API.</summary>
        private async void ShowFindInPage()
        {
            var tab = ActiveTab;
            if (tab?.IsReady != true) return;
            try
            {
                var cw = tab.WebView.CoreWebView2;
                var options = cw.Environment.CreateFindOptions();
                options.FindTerm = "";
                options.ShouldHighlightAllMatches = true;
                await cw.Find.StartAsync(options);
            }
            catch { }
        }

        // ================================================================
        //  KEYBOARD SHORTCUTS
        // ================================================================
        private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var tab = ActiveTab;
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                switch (e.Key)
                {
                    case Key.T: await CreateNewTab(); e.Handled = true; break;
                    case Key.W: CloseTab(_activeTabId); e.Handled = true; break;
                    case Key.L: AddressBar.Focus(); AddressBar.SelectAll(); e.Handled = true; break;
                    case Key.F: ShowFindInPage(); e.Handled = true; break;
                    case Key.P: if (tab?.IsReady == true) { try { await tab.WebView.CoreWebView2.ExecuteScriptAsync("window.print()"); } catch { } } e.Handled = true; break;
                    case Key.OemPlus: case Key.Add: if (tab?.IsReady == true) tab.WebView.ZoomFactor = Math.Min(3.0, tab.WebView.ZoomFactor + 0.1); e.Handled = true; break;
                    case Key.OemMinus: case Key.Subtract: if (tab?.IsReady == true) tab.WebView.ZoomFactor = Math.Max(0.25, tab.WebView.ZoomFactor - 0.1); e.Handled = true; break;
                    case Key.D0: case Key.NumPad0: if (tab?.IsReady == true) tab.WebView.ZoomFactor = 1.0; e.Handled = true; break;
                    case Key.Tab:
                        if (_tabs.Count > 1) { int idx = _tabs.FindIndex(t => t.Id == _activeTabId);
                            int next = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? (idx - 1 + _tabs.Count) % _tabs.Count : (idx + 1) % _tabs.Count;
                            SwitchToTab(_tabs[next].Id); }
                        e.Handled = true; break;
                }
            }
            if (e.Key == Key.F5) { Reload_Click(this, new RoutedEventArgs()); e.Handled = true; }
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                if (e.Key == Key.Left) { Back_Click(this, new RoutedEventArgs()); e.Handled = true; }
                if (e.Key == Key.Right) { Forward_Click(this, new RoutedEventArgs()); e.Handled = true; }
            }
        }

        // ================================================================
        //  ANALYSIS TAB SWITCHING
        // ================================================================
        private void ATab_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && int.TryParse(btn.Tag?.ToString(), out int idx)) SwitchAnalysisTab(idx); }
        private void SwitchAnalysisTab(int idx)
        {
            _activeAnalysisTab = idx;
            for (int i = 0; i < _panels.Length; i++)
            {
                _panels[i].Visibility = i == idx ? Visibility.Visible : Visibility.Collapsed;
                _aTabButtons[i].Background = i == idx ? PillActive : PillInactive;
                _aTabButtons[i].Foreground = i == idx ? PillActiveFg : PillInactiveFg;
            }
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) { var tab = ActiveTab; if (tab != null && RequestList != null) RefreshRequestList(tab); }
        private void Search_Changed(object sender, TextChangedEventArgs e) { var tab = ActiveTab; if (tab != null && RequestList != null) RefreshRequestList(tab); }
        private void LiveFeedFilter_Changed(object sender, RoutedEventArgs e) { _dirty = true; }
        private void LiveFeedSearch_Changed(object sender, TextChangedEventArgs e) { _dirty = true; }

        // ================================================================
        //  HUMAN-READABLE TRANSLATIONS
        // ================================================================
        private static string HumanizeFingerprint(string type)
        {
            if (type.Contains("Canvas")) return "This site creates a hidden image to uniquely identify your device.";
            if (type.Contains("Audio")) return "This site uses audio processing to create a unique device signature.";
            if (type.Contains("WebGL")) return "This site probes your graphics hardware for device identification.";
            if (type.Contains("Navigator")) return "This site collects detailed browser and device information.";
            if (type.Contains("Screen")) return "This site reads your display configuration to identify you.";
            if (type.Contains("Timezone")) return "This site uses your timezone setting for tracking.";
            if (type.Contains("Plugin")) return "This site scans installed browser plugins for identification.";
            if (type.Contains("Network Fingerprinting")) return "This site detects your network connection type.";
            if (type.Contains("Font")) return "This site scans your installed fonts for identification.";
            if (type.Contains("Mouse")) return "This site is monitoring your mouse movements.";
            if (type.Contains("Scroll")) return "This site is tracking your scrolling behavior.";
            if (type.Contains("Keystroke") || type.Contains("Key Tracking")) return "This site is monitoring your typing patterns.";
            if (type.Contains("Touch")) return "This site is monitoring touch screen interactions.";
            if (type.Contains("Session Replay") || type.Contains("Surveillance")) return "This site records your entire browsing session for replay.";
            if (type.Contains("Dynamic Script")) return "This site loads hidden tracking scripts after page load.";
            if (type.Contains("Obfuscation")) return "This site uses code obfuscation to hide tracking behavior.";
            if (type.Contains("Beacon")) return "This site silently sends data to trackers in the background.";
            if (type.Contains("Cross-Frame")) return "This site shares tracking data between embedded frames.";
            if (type.Contains("MutationObserver")) return "This site monitors page changes to detect your behavior.";
            return $"Privacy technique detected: {type}";
        }

        private static (string Label, SolidColorBrush Color) ConfidenceToLabel(double conf)
        {
            if (conf >= 0.8) return ("Confirmed tracking", new SolidColorBrush(Color.FromRgb(217, 48, 37)));
            if (conf >= 0.5) return ("Likely tracking", new SolidColorBrush(Color.FromRgb(227, 116, 0)));
            if (conf > 0) return ("Possibly tracking", new SolidColorBrush(Color.FromRgb(95, 99, 104)));
            return ("", Brushes.Transparent);
        }

        private static string ConfidenceShortLabel(double conf)
        {
            if (conf >= 0.8) return "Confirmed";
            if (conf >= 0.5) return "Likely";
            if (conf > 0) return "Possible";
            return "";
        }

        private static string MitigationSuggestion(string category)
        {
            return category switch
            {
                "Ad" => "Block this domain or use container tabs/private mode.",
                "Behavioral" => "Disable JavaScript or use a script blocker like NoScript.",
                "Tracking" => "Block this domain or isolate the site in a privacy profile.",
                _ => ""
            };
        }

        private static string BuildBlockedTooltip(RequestEntry r)
        {
            if (!r.IsBlocked) return "";
            var lines = new List<string> { $"Blocked: {r.BlockReason}" };
            if (!string.IsNullOrEmpty(r.BlockCategory)) lines.Add($"Category: {r.BlockCategory}");
            double conf = r.BlockConfidence > 0 ? r.BlockConfidence : r.ThreatConfidence;
            if (conf > 0) lines.Add($"Confidence: {conf:P0}");
            var suggestion = MitigationSuggestion(r.BlockCategory);
            if (!string.IsNullOrEmpty(suggestion)) lines.Add($"Suggestion: {suggestion}");
            return string.Join("\n", lines);
        }

        private static string BuildSimpleBehavioralSummary(BrowserTab tab, List<CompanyCluster> clusters, List<BehavioralPattern> patterns, List<IdentityLink> links, List<RequestBurst> bursts)
        {
            var lines = new List<string>();

            // Trackers
            int trackerCount = tab.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerLabel)).Select(r => r.TrackerLabel).Distinct().Count();
            if (trackerCount > 0)
            {
                var companies = clusters.Select(c => c.Company).Distinct().Take(3);
                lines.Add($"This site uses {trackerCount} tracking service(s)" +
                    (clusters.Count > 0 ? $" from {string.Join(", ", companies)}" : "") + ".");
            }

            // Fingerprinting
            int fpCount = tab.Fingerprints.Count;
            if (fpCount > 0)
            {
                bool hasCanvas = tab.Fingerprints.Any(f => f.Type.Contains("Canvas") || f.Type.Contains("WebGL") || f.Type.Contains("Audio"));
                bool hasBehavioral = tab.Fingerprints.Any(f => f.Type.StartsWith("Behavioral:") || f.Type.Contains("Session Replay"));
                if (hasCanvas && hasBehavioral)
                    lines.Add("It creates a unique fingerprint of your device AND monitors your mouse, scrolling, or typing.");
                else if (hasCanvas)
                    lines.Add("It creates a unique fingerprint of your device using hidden rendering.");
                else if (hasBehavioral)
                    lines.Add("It monitors your mouse, scrolling, or typing behavior.");
                else
                    lines.Add($"It uses {fpCount} technique(s) to identify your browser.");
            }

            // Identity stitching
            if (links.Count > 0)
                lines.Add($"Tracking IDs are shared across {links.Sum(l => l.DomainCount)} domains - your activity is linked across sites.");

            // Bursts
            if (bursts.Count > 0)
                lines.Add($"Rapid-fire data bursts detected ({bursts.Count} burst pattern(s)).");

            // Cookies
            int trackingCookies = tab.Cookies.Count(c => c.Classification.Contains("Tracking"));
            if (trackingCookies > 0)
                lines.Add($"{trackingCookies} tracking cookie(s) are stored on your device.");

            if (lines.Count == 0)
                return "No significant privacy concerns detected on this page.";

            // Append actionable guidance
            var actions = new List<string>();
            if (trackerCount > 0) actions.Add("install a tracker blocker (e.g. uBlock Origin)");
            if (fpCount > 0) actions.Add("enable anti-fingerprinting in your browser");
            bool hasBeh = tab.Fingerprints.Any(f => f.Type.StartsWith("Behavioral:") || f.Type.Contains("Session Replay"));
            if (hasBeh) actions.Add("consider disabling JavaScript on this site");
            if (trackingCookies > 0) actions.Add("clear cookies after this session");
            if (actions.Count > 0)
                lines.Add("What you can do: " + string.Join(", ", actions) + ".");

            return string.Join("\n\n", lines);
        }

        // ================================================================
        //  MITIGATION TIPS (Simple Mode)
        // ================================================================
        private static List<MitigationTip> GenerateMitigationTips(ScanResult scan, PrivacyScore score)
        {
            var tips = new List<MitigationTip>();
            var cats = score.CategoryScores;

            // Tracking – only for confirmed/likely trackers
            var confirmedTrackers = scan.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerLabel) && r.ThreatConfidence >= 0.5).ToList();
            int trackerCount = confirmedTrackers.Select(r => r.TrackerLabel).Distinct().Count();
            if (trackerCount > 0)
            {
                double maxConf = confirmedTrackers.Max(r => r.ThreatConfidence);
                var (cLabel, cColor) = ConfidenceToLabel(maxConf);
                tips.Add(new MitigationTip
                {
                    Icon = "\uD83D\uDEE1", // shield
                    Title = $"{trackerCount} tracker(s) sending your data",
                    Action = "Use container tabs or private mode, and block this domain or enable a tracker blocker like uBlock Origin.",
                    ConfidenceLabel = cLabel,
                    CategoryColor = ScoreBadgeBg(cats.GetValueOrDefault("Tracking", 100)),
                    ConfidenceColor = cColor
                });
            }

            // Fingerprinting – only for high-severity
            int fpHigh = scan.Fingerprints.Count(f => f.Severity == "Critical" || f.Severity == "High");
            if (fpHigh > 0)
            {
                tips.Add(new MitigationTip
                {
                    Icon = "\uD83D\uDD0D", // magnifier
                    Title = $"{fpHigh} fingerprinting technique(s) profiling your device",
                    Action = "Use Firefox with privacy.resistFingerprinting enabled, or install CanvasBlocker to randomize device signatures.",
                    ConfidenceLabel = "Confirmed tracking",
                    CategoryColor = ScoreBadgeBg(cats.GetValueOrDefault("Fingerprinting", 100)),
                    ConfidenceColor = new SolidColorBrush(Color.FromRgb(217, 48, 37))
                });
            }

            // Behavioral tracking
            int behavioral = scan.Fingerprints.Count(f => f.Type.StartsWith("Behavioral:") || f.Type.Contains("Session Replay") ||
                f.Type.Contains("Obfuscation") || f.Type.Contains("Dynamic Script"));
            if (behavioral > 0)
            {
                tips.Add(new MitigationTip
                {
                    Icon = "\uD83D\uDC41", // eye
                    Title = "Behavioral monitoring active",
                    Action = "This site watches your mouse, scrolling, or typing. Disable JavaScript with NoScript, or block these scripts on this site.",
                    ConfidenceLabel = behavioral >= 2 ? "Confirmed tracking" : "Likely tracking",
                    CategoryColor = ScoreBadgeBg(cats.GetValueOrDefault("Behavioral", 100)),
                    ConfidenceColor = behavioral >= 2 ? new SolidColorBrush(Color.FromRgb(217, 48, 37)) : new SolidColorBrush(Color.FromRgb(227, 116, 0))
                });
            }

            // Data leakage / cookies
            int trackingCookies = PrivacyEngine.CountAllTrackingCookies(scan);
            int crossTabIds = scan.Requests.SelectMany(r => r.TrackingParams).Distinct().Count();
            if (trackingCookies > 2 || crossTabIds > 3)
            {
                tips.Add(new MitigationTip
                {
                    Icon = "\uD83C\uDF6A", // cookie
                    Title = $"{trackingCookies} tracking cookie(s) and {crossTabIds} tracking parameter(s)",
                    Action = "Clear cookies after this session. Use container tabs or enable automatic cookie cleanup. Consider isolating sensitive sites in separate browser profiles.",
                    ConfidenceLabel = trackingCookies > 5 ? "Confirmed tracking" : "Likely tracking",
                    CategoryColor = ScoreBadgeBg(cats.GetValueOrDefault("DataLeakage", 100)),
                    ConfidenceColor = trackingCookies > 5 ? new SolidColorBrush(Color.FromRgb(217, 48, 37)) : new SolidColorBrush(Color.FromRgb(227, 116, 0))
                });
            }

            // WebRTC IP leak
            if (scan.WebRtcLeaks.Count > 0)
            {
                tips.Add(new MitigationTip
                {
                    Icon = "\uD83C\uDF10", // globe
                    Title = "Your real IP address is exposed",
                    Action = "This site can see your IP even through a VPN. Disable WebRTC in browser settings or install WebRTC Leak Shield.",
                    ConfidenceLabel = "Confirmed",
                    CategoryColor = new SolidColorBrush(Color.FromRgb(217, 48, 37)),
                    ConfidenceColor = new SolidColorBrush(Color.FromRgb(217, 48, 37))
                });
            }

            // Security headers
            int missingHeaders = scan.SecurityHeaders.Count(h => h.Status == "Missing");
            if (missingHeaders > 4)
            {
                tips.Add(new MitigationTip
                {
                    Icon = "\u26A0", // warning
                    Title = $"{missingHeaders} security protections missing",
                    Action = "This site lacks important security headers. Avoid entering passwords or personal data. Use HTTPS-only mode in your browser.",
                    ConfidenceLabel = "",
                    CategoryColor = ScoreBadgeBg(cats.GetValueOrDefault("Security", 100)),
                    ConfidenceColor = Brushes.Transparent
                });
            }

            return tips;
        }

        // ================================================================
        //  DYNAMIC CATEGORY TOOLTIPS
        // ================================================================
        private void UpdateCategoryTooltips(ScanResult scan, Dictionary<string, int> cats)
        {
            int trackingScore = cats.GetValueOrDefault("Tracking", 100);
            int fpScore = cats.GetValueOrDefault("Fingerprinting", 100);
            int leakScore = cats.GetValueOrDefault("DataLeakage", 100);
            int secScore = cats.GetValueOrDefault("Security", 100);
            int behScore = cats.GetValueOrDefault("Behavioral", 100);

            CatTrackingRow.ToolTip = trackingScore < 85
                ? $"Tracking: {trackingScore}/100 - Active trackers detected.\nTip: Use a tracker blocker like uBlock Origin."
                : $"Tracking: {trackingScore}/100 - No significant tracker activity.";

            CatFingerprintRow.ToolTip = fpScore < 85
                ? $"Fingerprinting: {fpScore}/100 - Device profiling detected.\nTip: Enable anti-fingerprinting protection in your browser."
                : $"Fingerprinting: {fpScore}/100 - No fingerprinting detected.";

            CatLeakageRow.ToolTip = leakScore < 85
                ? $"Data Leakage: {leakScore}/100 - Tracking cookies or data exfiltration.\nTip: Clear cookies regularly and use private browsing."
                : $"Data Leakage: {leakScore}/100 - Minimal data exposure.";

            CatSecurityRow.ToolTip = secScore < 85
                ? $"Security: {secScore}/100 - Missing HTTP security headers.\nTip: Be cautious entering sensitive data on this site."
                : $"Security: {secScore}/100 - Good security header coverage.";

            CatBehavioralRow.ToolTip = behScore < 85
                ? $"Behavioral: {behScore}/100 - Mouse/scroll/keystroke monitoring.\nTip: Disable JavaScript or use a script blocker."
                : $"Behavioral: {behScore}/100 - No behavioral tracking detected.";
        }

        // ================================================================
        //  HELPERS
        // ================================================================
        private static ScanResult BuildScanResult(BrowserTab tab) => new()
        {
            TargetUrl = tab.CurrentHost, ScanStart = tab.ScanStart, ScanEnd = DateTime.Now,
            Requests = tab.Requests.ToList(), Fingerprints = tab.Fingerprints.ToList(), Cookies = tab.Cookies.ToList(),
            Storage = tab.Storage.ToList(), SecurityHeaders = tab.SecurityHeaders.ToList(), WebRtcLeaks = tab.WebRtcLeaks.ToList()
        };

        private static SolidColorBrush ScoreBadgeBg(int s) =>
            s >= 90 ? new(Color.FromRgb(24, 128, 56)) :   // green
            s >= 75 ? new(Color.FromRgb(26, 115, 232)) :   // blue
            s >= 60 ? new(Color.FromRgb(227, 116, 0)) :    // orange
            s >= 40 ? new(Color.FromRgb(217, 48, 37)) :    // red
            new(Color.FromRgb(165, 14, 14));                // dark red

        private static SolidColorBrush SeverityBrush(string sev) => sev switch
        {
            "Critical" => new(Color.FromRgb(217, 48, 37)),
            "High" => new(Color.FromRgb(227, 116, 0)),
            "Medium" => new(Color.FromRgb(180, 130, 30)),
            _ => new(Color.FromRgb(95, 99, 104))
        };

        private static SolidColorBrush ClassBrush(string cls) => cls switch
        {
            "Tracking / Analytics" => new(Color.FromRgb(217, 48, 37)),
            "Session / Security" or "Authentication" => new(Color.FromRgb(26, 115, 232)),
            "Consent" => new(Color.FromRgb(24, 128, 56)),
            "Preference" => new(Color.FromRgb(95, 99, 104)),
            "Cache / Service Worker" => new(Color.FromRgb(95, 99, 104)),
            _ => new(Color.FromRgb(140, 140, 140))
        };

        private static string FormatBytes(long bytes) =>
            bytes >= 1_048_576 ? $"{bytes / 1_048_576.0:F1} MB" :
            bytes >= 1_024 ? $"{bytes / 1_024.0:F1} KB" :
            $"{bytes} B";
    }
}
