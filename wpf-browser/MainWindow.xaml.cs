using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
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
    /// <summary>Exposed to WebView2 script so Settings page can save without relying on postMessage (which may be blocked or unavailable for NavigateToString content).</summary>
    public class SettingsSaveBridge
    {
        private readonly MainWindow _window;
        private readonly BrowserTab _tab;

        public SettingsSaveBridge(MainWindow window, BrowserTab tab) { _window = window; _tab = tab; }

        public void Save(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            _window?.Dispatcher.Invoke(() =>
            {
                try
                {
                    _window.ApplySettingsFromJson(json);
                    _window.SaveSettings();
                    try { _tab?.WebView?.CoreWebView2?.NavigateToString(_window.GetSettingsHtml(showSavedMessage: true)); } catch { }
                }
                catch { }
            });
        }
    }

    public partial class MainWindow : Window
    {
        private sealed class TaskManagerRow
        {
            public string Id { get; init; } = "";
            public string Title { get; init; } = "";
            public string Url { get; init; } = "";
            public string Memory { get; init; } = "";
            public string RequestsLabel { get; init; } = "";
            public string BlockedLabel { get; init; } = "";
            public string SleepLabel { get; init; } = "";
        }
        private readonly List<BrowserTab> _tabs = new();
        private string _activeTabId = "";
        private int _activeAnalysisTab;
        private bool _dirty = true;
        private long _lastDirtySetTicks; // throttle _dirty from request handler
        private bool _sidebarOpen = true;
        private bool _expertMode = false;
        private bool _antiFpEnabled = true;
        private bool _adBlockEnabled = true;
        private readonly DispatcherTimer _uiTimer;
        // Idle tab sleep (performance & resource control)
        private readonly TimeSpan _idleBeforeSleep = TimeSpan.FromMinutes(15);
        private DateTime _lastSleepCheckUtc = DateTime.UtcNow;
        private readonly SessionContext _session = new();
        private List<BookmarkEntry> _bookmarks = new();
        private AppSettings _settings = new();
        private readonly bool _isPrivate;
        /// <summary>In-memory site profiles for private window; null for normal window.</summary>
        private readonly ConcurrentDictionary<string, SiteProfile>? _privateProfiles = null;
        /// <summary>Temp WebView2 user data folder for private window (cookies/storage not persisted).</summary>
        private string? _privateUserDataFolder;
        private List<(string Title, string Url, DateTime Visited)> _recentUrls = new();
        private const int MaxRecent = 50;

        /// <summary>When set, live request/response data is pushed to the network interceptor window.</summary>
        private NetworkInterceptor.INetworkInterceptorSink? _interceptorSink;
        private NetworkInterceptor.NetworkInterceptorService? _interceptorService;
        private string? _interceptorTabId;

        /// <summary>Requests held when interceptor is paused (true Burp-style: not sent to network until Resume).</summary>
        private readonly List<(CoreWebView2Deferral Deferral, RequestEntry Entry, BrowserTab Tab)> _pausedInterceptorDeferred = new();
        private readonly object _pausedInterceptorLock = new();

        /// <summary>Runtime fingerprint signals per tab (from sandboxed JS postMessage cat:'fp'). Fed to interceptor RiskScoring.</summary>
        private readonly Dictionary<string, List<string>> _fingerprintSignalsByTab = new();

        private Button[] _aTabButtons = Array.Empty<Button>();
        private UIElement[] _panels = Array.Empty<UIElement>();

        // Chrome palette (theme-aware; set in ApplyTheme)
        private SolidColorBrush TabBarBg = new(Color.FromRgb(222, 225, 230));
        private SolidColorBrush TabActiveBg = Brushes.White;
        private SolidColorBrush TabActiveFg = new(Color.FromRgb(15, 23, 42));
        private SolidColorBrush TabInactiveBg = new(Color.FromRgb(226, 232, 240));
        private SolidColorBrush TabInactiveFg = new(Color.FromRgb(71, 85, 105));
        private SolidColorBrush PillActive = new(Color.FromRgb(8, 145, 178));
        private SolidColorBrush PillActiveFg = Brushes.White;
        private SolidColorBrush PillInactive = Brushes.Transparent;
        private SolidColorBrush PillInactiveFg = new(Color.FromRgb(95, 99, 104));
        private bool _isDarkTheme;

        private static readonly string WelcomeHtml = @"<!DOCTYPE html><html><head><meta name='color-scheme' content='light dark'/><meta charset='utf-8'/><style>
            *{margin:0;padding:0;box-sizing:border-box}
            :root{--bg:#fff;--text:#202124;--text-muted:#5F6368;--accent:#0891B2;--tip-bg:#F8F9FA;--search-border:#DFE1E5;--border:#E2E8F0;--foot:#9AA0A6}
            @media(prefers-color-scheme:dark){:root{--bg:#202124;--text:#E8EAED;--text-muted:#9AA0A6;--accent:#5EB8D9;--tip-bg:#35363A;--search-border:#5F6368;--border:#3C4043;--foot:#80868B}}
            body{font-family:'Segoe UI',system-ui,-apple-system,sans-serif;background:var(--bg);color:var(--text);display:flex;flex-direction:column;align-items:center;justify-content:center;min-height:100vh;padding:24px;transition:background .2s,color .2s}
            .hero{margin-bottom:40px;text-align:center}
            .logo{font-size:48px;font-weight:200;letter-spacing:-1px;color:var(--text);margin-bottom:6px}
            .logo span{font-weight:600;color:var(--accent)}
            .sub{font-size:13px;color:var(--text-muted);letter-spacing:.3px}
            .search-wrap{margin:32px 0 48px 0}
            .search{width:520px;max-width:95%;height:48px;border-radius:24px;border:1px solid var(--search-border);background:var(--tip-bg);padding:0 24px;font-size:15px;outline:none;color:var(--text);transition:box-shadow .2s,border-color .2s;display:block}
            .search::placeholder{color:var(--text-muted)}
            .search:focus{box-shadow:0 2px 12px rgba(0,0,0,.12);border-color:var(--accent)}
            .tips{margin:0 auto;display:grid;grid-template-columns:repeat(2,1fr);gap:14px;max-width:460px}
            .tip{background:var(--tip-bg);border-radius:14px;padding:16px 18px;font-size:12px;color:var(--text);line-height:1.5;border:1px solid var(--border);transition:border-color .15s}
            .tip:hover{border-color:var(--accent)}
            .tip b{color:var(--accent);display:block;margin-bottom:4px;font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.5px}
            .foot{position:fixed;bottom:20px;font-size:11px;color:var(--foot)}
        </style></head><body>
            <div class='hero'>
                <div class='logo'>Privacy <span>Monitor</span></div>
                <div class='sub'>Agjencia per Informim dhe Privatesi</div>
            </div>
            <div class='search-wrap'>
                <form action='https://duckduckgo.com/' method='get' target='_top' id='sf'>
                <input class='search' name='q' placeholder='Search with DuckDuckGo or type a URL' autofocus
                       onkeydown=""if(event.key==='Enter'){var v=this.value.trim();if(!v)return;if(v.indexOf(' ')>=0)return;if(v.indexOf('://')>=0||v.indexOf('.')>=0){event.preventDefault();if(v.indexOf('://')>=0||/^https?:\\/\\//i.test(v)||/^file:/i.test(v)){window.location.href=v}else{window.location.href='https://'+v}}}""/>
                </form>
            </div>
            <div class='tips'>
                <div class='tip'><b>Search</b> Type a phrase and press Enter → opens DuckDuckGo</div>
                <div class='tip'><b>URL</b> Type a site (e.g. google.com) and Enter → opens it</div>
                <div class='tip'><b>Ctrl+T</b> New tab</div>
                <div class='tip'><b>Ctrl+W</b> Close tab</div>
                <div class='tip'><b>Ctrl+L</b> Focus address bar</div>
                <div class='tip'><b>F5</b> Reload page</div>
                <div class='tip'><b>Escape</b> Stop loading</div>
                <div class='tip'><b>Ctrl+P</b> Print</div>
                <div class='tip'><b>Ctrl+F</b> Find in page</div>
                <div class='tip'><b>Ctrl+H</b> History</div>
            </div>
            <div class='foot'>Privacy Monitor v1.0 · Built for Windows</div>
        </body></html>";

        private static string EscapeHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private string GetWelcomeHtml() => WelcomeHtml;

        private static readonly string HistoryPageCss = @"*{margin:0;padding:0;box-sizing:border-box}
            :root{--bg:#fff;--text:#202124;--text-muted:#5F6368;--accent:#0891B2;--tip-bg:#F8F9FA;--search-border:#DFE1E5;--border:#E2E8F0;--card-bg:#FAFAFA}
            @media(prefers-color-scheme:dark){:root{--bg:#202124;--text:#E8EAED;--text-muted:#9AA0A6;--accent:#5EB8D9;--tip-bg:#35363A;--search-border:#5F6368;--border:#3C4043;--card-bg:#2D2D2D}}
            body{font-family:'Segoe UI',system-ui,sans-serif;background:var(--bg);color:var(--text);min-height:100vh;padding:28px 24px}
            .header{display:flex;align-items:center;justify-content:space-between;gap:20px;margin-bottom:24px;flex-wrap:wrap}
            .header h1{font-size:28px;font-weight:600;color:var(--text);letter-spacing:-0.5px}
            .header-links{display:flex;gap:16px;align-items:center}
            .header a{color:var(--accent);text-decoration:none;font-size:14px;font-weight:500}
            .header a:hover{text-decoration:underline}
            .header a.danger{color:#D93025}
            @media(prefers-color-scheme:dark){.header a.danger{color:#EA4335}}
            .btn-danger{display:inline-block;background:#D93025;color:#fff;border:none;padding:8px 14px;border-radius:8px;cursor:pointer;font-size:13px;font-family:inherit;text-decoration:none}
            .btn-danger:hover{background:#B71C1C;color:#fff;text-decoration:none}
            @media(prefers-color-scheme:dark){.btn-danger{background:#EA4335}.btn-danger:hover{background:#D93025}}
            .search-wrap{margin-bottom:20px}
            .search{width:100%;max-width:420px;height:44px;border-radius:22px;border:1px solid var(--search-border);background:var(--tip-bg);padding:0 20px;font-size:14px;color:var(--text);outline:none;transition:border-color .2s}
            .search:focus{border-color:var(--accent)}
            .search::placeholder{color:var(--text-muted)}
            .section{margin-top:28px}
            .section-title{font-size:11px;font-weight:600;text-transform:uppercase;letter-spacing:.5px;color:var(--text-muted);margin-bottom:10px}
            .list{max-width:680px}
            .list ul{list-style:none}
            .list li{margin:0 0 8px 0;padding:14px 16px;border-radius:12px;background:var(--card-bg);border:1px solid var(--border);display:flex;flex-direction:column;gap:4px;transition:background .15s}
            .list li:hover{background:var(--tip-bg)}
            .list li.hide{display:none}
            .list .row{display:flex;justify-content:space-between;align-items:flex-start;gap:12px}
            .list a{color:var(--accent);text-decoration:none;font-size:15px;font-weight:500;flex:1;min-width:0}
            .list a:hover{text-decoration:underline}
            .list .url{font-size:12px;color:var(--text-muted);word-break:break-all}
            .list .time{font-size:11px;color:var(--text-muted);white-space:nowrap;flex-shrink:0}
            .empty{color:var(--text-muted);margin-top:24px;font-size:15px;line-height:1.5}";

        private static string FormatVisitedTime(DateTime utc)
        {
            var local = utc.ToLocalTime();
            var now = DateTime.Now;
            var d = local.Date;
            var today = now.Date;
            if (d == today) return "Today";
            if (d == today.AddDays(-1)) return "Yesterday";
            if (now - local < TimeSpan.FromDays(7)) return local.ToString("dddd");
            return local.ToString("MMM d", System.Globalization.CultureInfo.CurrentUICulture);
        }

        private string GetHistoryHtml()
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta name='color-scheme' content='light dark'/><meta charset='utf-8'/><title>History</title><style>");
            sb.Append(HistoryPageCss);
            sb.Append("</style></head><body><div class='header'><h1>History</h1><div class='header-links'><a href='about:welcome'>New tab</a> <span style='color:var(--text-muted)'>|</span> <a href='about:clearhistory' class='btn-danger'>Clear history</a></div></div>");
            sb.Append("<div class='search-wrap'><input type='text' class='search' id='q' placeholder='Search history' autofocus/></div>");
            sb.Append("<div class='section'><div class='section-title'>Browsing history</div><div class='list'><ul id='list'>");
            foreach (var (title, url, visited) in _recentUrls)
            {
                var t = EscapeHtml(title.Length > 0 ? title : url).Replace("'", "&#39;");
                var u = EscapeHtml(url).Replace("'", "&#39;");
                var timeStr = EscapeHtml(FormatVisitedTime(visited)).Replace("'", "&#39;");
                sb.Append("<li data-title='").Append(t).Append("' data-url='").Append(u).Append("'>");
                sb.Append("<div class='row'><a href='").Append(u).Append("'>").Append(t).Append("</a><span class='time'>").Append(timeStr).Append("</span></div>");
                sb.Append("<span class='url'>").Append(u).Append("</span></li>");
            }
            sb.Append("</ul></div></div>");
            if (_recentUrls.Count == 0)
                sb.Append("<p class='empty'>No history yet. Browse the web to see visited pages here.</p>");
            sb.Append(@"<script>
                var q=document.getElementById('q'),list=document.getElementById('list');
                if(list){
                    var items=list.getElementsByTagName('li');
                    q.oninput=function(){
                        var v=this.value.toLowerCase();
                        for(var i=0;i<items.length;i++){
                            var li=items[i];
                            li.classList.toggle('hide',v&&(li.getAttribute('data-title').toLowerCase().indexOf(v)<0&&li.getAttribute('data-url').toLowerCase().indexOf(v)<0));
                        }
                    };
                }
            </script></body></html>");
            return sb.ToString();
        }

        internal string GetSettingsHtml(bool showSavedMessage = false)
        {
            string home = EscapeHtml(_settings.HomePage ?? "about:welcome");
            string startup = EscapeHtml(_settings.Startup ?? "restore");
            string search = EscapeHtml(_settings.SearchEngineUrl ?? "https://duckduckgo.com/?q=");
            string bp = _settings.BlockPopups ? " checked" : "";
            string hi = _settings.HideInPageAds ? " checked" : "";
            string usage = _settings.AllowUsageData ? " checked" : "";
            string proxy = EscapeHtml(_settings.ProxyUrl ?? "");
            string msgContent = showSavedMessage ? "Saved. Restart or open a new tab to apply." : "";
            return $@"<!DOCTYPE html><html><head><meta name='color-scheme' content='light dark'/><meta charset='utf-8'/><title>Settings</title><style>
            *{{margin:0;padding:0;box-sizing:border-box}}
            :root{{--bg:#fff;--text:#202124;--muted:#5F6368;--accent:#0891B2;--border:#E2E8F0}}
            @media(prefers-color-scheme:dark){{:root{{--bg:#202124;--text:#E8EAED;--muted:#9AA0A6;--accent:#5EB8D9;--border:#3C4043}}}}
            body{{font-family:'Segoe UI',system-ui,sans-serif;background:var(--bg);color:var(--text);padding:24px;line-height:1.5}}
            .header{{margin-bottom:20px}}
            .header h1{{font-size:20px;font-weight:600}}
            .section{{margin:16px 0}}
            .section label{{display:block;font-size:12px;color:var(--muted);margin-bottom:4px}}
            .section input[type=text],.section select{{width:100%;max-width:400px;padding:8px 12px;border:1px solid var(--border);border-radius:8px;background:var(--bg);color:var(--text);font-size:14px}}
            .section input[type=checkbox]{{margin-right:8px;vertical-align:middle}}
            .btn{{margin-top:16px;padding:10px 20px;background:var(--accent);color:#fff;border:none;border-radius:8px;cursor:pointer;font-size:13px}}
            .btn:hover{{opacity:0.9}}
            #msg{{margin-top:12px;font-size:12px;color:var(--muted)}}
            </style></head><body>
            <div class='header'><h1>Settings</h1><p style='font-size:12px;color:var(--muted);margin-top:4px'>Privacy Monitor preferences</p></div>
            <div class='section'><label>Home page (URL or about:welcome)</label><input type='text' id='homePage' value='{home}'/></div>
            <div class='section'><label>On startup</label><select id='startup'><option value='restore'{ (startup == "restore" ? " selected" : "") }>Restore previous session</option><option value='welcome'{ (startup == "welcome" ? " selected" : "") }>Open welcome page</option></select></div>
            <div class='section'><label>Search engine URL (use %s for query, or e.g. https://duckduckgo.com/?q=)</label><input type='text' id='searchEngineUrl' value='{search}'/></div>
            <div class='section'><label><input type='checkbox' id='blockPopups'{bp}/> Block pop-ups</label></div>
            <div class='section'><label><input type='checkbox' id='hideInPageAds'{hi}/> Hide in-page ads (cosmetic filter)</label></div>
            <div class='section'><label><input type='checkbox' id='allowUsageData'{usage}/> Help improve Privacy Monitor by sending anonymous usage data (version, OS, protection level). No URLs or personal data.</label></div>
            <div class='section'><label>Proxy (optional — hides your IP from sites)</label><input type='text' id='proxyUrl' value='{proxy}' placeholder='e.g. http://127.0.0.1:8080 or socks5://127.0.0.1:1080'/></div>
            <button type='button' class='btn' id='save'>Save</button>
            <div id='msg'>{msgContent}</div>
            <script>
            (function(){{
                var home=document.getElementById('homePage'), start=document.getElementById('startup'), search=document.getElementById('searchEngineUrl'), blockPopups=document.getElementById('blockPopups'), hideInPageAds=document.getElementById('hideInPageAds'), allowUsageData=document.getElementById('allowUsageData'), proxyUrl=document.getElementById('proxyUrl'), save=document.getElementById('save'), msgEl=document.getElementById('msg');
                save.onclick=function(ev){{
                    if(ev){{ ev.preventDefault(); ev.stopPropagation(); }}
                    var hp=(home.value||'').trim()||'about:welcome', st=start.value||'restore', se=(search.value||'').trim()||'https://duckduckgo.com/?q=', px=(proxyUrl&&proxyUrl.value)?proxyUrl.value.trim():'';
                    var msg={{cat:'settings',homePage:hp,startup:st,searchEngineUrl:se,blockPopups:blockPopups.checked,hideInPageAds:hideInPageAds.checked,allowUsageData:!!(allowUsageData&&allowUsageData.checked),proxyUrl:px}}, j=JSON.stringify(msg);
                    try{{
                        if(window.chrome&&window.chrome.webview&&window.chrome.webview.hostObjects&&window.chrome.webview.hostObjects.sync&&window.chrome.webview.hostObjects.sync.settingsBridge){{ window.chrome.webview.hostObjects.sync.settingsBridge.Save(j); return false; }}
                        if(window.chrome&&window.chrome.webview&&window.chrome.webview.postMessage){{ window.chrome.webview.postMessage(j); return false; }}
                    }}catch(e){{}}
                    msgEl.textContent='Save failed. Use Settings from the app menu.';
                    return false;
                }};
            }})();
            </script></body></html>";
        }

        private static string GetShortcutsHtml()
        {
            return @"<!DOCTYPE html><html><head><meta name='color-scheme' content='light dark'/><meta charset='utf-8'/><title>Keyboard shortcuts</title><style>
            *{margin:0;padding:0;box-sizing:border-box}
            :root{--bg:#fff;--text:#202124;--muted:#5F6368;--accent:#0891B2;--border:#E2E8F0}
            @media(prefers-color-scheme:dark){:root{--bg:#202124;--text:#E8EAED;--muted:#9AA0A6;--accent:#5EB8D9;--border:#3C4043}}
            body{font-family:'Segoe UI',system-ui,sans-serif;background:var(--bg);color:var(--text);padding:24px}
            h1{font-size:20px;margin-bottom:16px}
            table{width:100%;max-width:500px;border-collapse:collapse}
            th,td{padding:8px 12px;text-align:left;border-bottom:1px solid var(--border)}
            th{font-size:11px;color:var(--muted);text-transform:uppercase}
            kbd{background:var(--border);padding:2px 6px;border-radius:4px;font-size:12px}
            </style></head><body>
            <h1>Keyboard shortcuts</h1>
            <table><tr><th>Shortcut</th><th>Action</th></tr>
            <tr><td><kbd>Ctrl+T</kbd></td><td>New tab</td></tr>
            <tr><td><kbd>Ctrl+W</kbd></td><td>Close tab</td></tr>
            <tr><td><kbd>Ctrl+H</kbd></td><td>History</td></tr>
            <tr><td><kbd>Ctrl+L</kbd></td><td>Focus address bar</td></tr>
            <tr><td><kbd>Ctrl+F</kbd></td><td>Find in page</td></tr>
            <tr><td><kbd>F5</kbd></td><td>Reload</td></tr>
            <tr><td><kbd>Ctrl++</kbd> / <kbd>Ctrl+-</kbd></td><td>Zoom in / out</td></tr>
            <tr><td><kbd>Ctrl+0</kbd></td><td>Reset zoom</td></tr>
            <tr><td><kbd>Ctrl+P</kbd></td><td>Print</td></tr>
            <tr><td><kbd>Ctrl+Tab</kbd></td><td>Next tab</td></tr>
            <tr><td><kbd>Ctrl+Shift+Tab</kbd></td><td>Previous tab</td></tr>
            </table></body></html>";
        }

        public MainWindow()
        {
            InitializeComponent();
            _aTabButtons = new[] { ATab0, ATab1, ATab2, ATab3, ATab4, ATab5, ATab6 };
            _panels = new UIElement[] { Panel0, Panel1, Panel2, Panel3, Panel4, Panel5, Panel6 };
            SwitchAnalysisTab(0);
            Loaded += (_, _) =>
            {
                UpdateExpertVisibility(); // apply simple view by default
                ApplyTheme(SystemThemeDetector.IsDarkMode);
                SetProtectionMenuChecked(ProtectionEngine.GlobalDefaultMode); // sync pill + menu with default
                SystemThemeDetector.ThemeChanged += OnSystemThemeChanged;
                if (App.UpdateAvailableVersion != null && MenuCheckUpdatesItem != null)
                    MenuCheckUpdatesItem.Header = $"Update available (v{App.UpdateAvailableVersion})";
            };
            App.OnUpdateAvailable += (_, newVer) =>
            {
                if (MenuCheckUpdatesItem != null)
                    MenuCheckUpdatesItem.Header = $"Update available (v{newVer})";
            };

            Loaded += (_, _) =>
            {
                var delay = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                delay.Tick += (_, _) =>
                {
                    delay.Stop();
                    _ = UpdateService.SendUsageAsync(_settings.AllowUsageData, ProtectionEngine.GlobalDefaultMode.ToString());
                };
                delay.Start();
            };

            _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _uiTimer.Tick += (_, _) =>
            {
                if (_dirty)
                {
                    _dirty = false;
                    RefreshAll();
                }
                CheckIdleTabsForSleep();
            };
            _uiTimer.Start();

            Closing += MainWindow_Closing;
            if (!_isPrivate) { LoadBookmarks(); LoadRecent(); }
            LoadSettings();
            Loaded += async (_, _) => await RestoreOrWelcomeAsync();
        }

        public MainWindow(bool isPrivate) : this()
        {
            _isPrivate = isPrivate;
            _privateProfiles = isPrivate ? new ConcurrentDictionary<string, SiteProfile>(StringComparer.OrdinalIgnoreCase) : null;
            if (isPrivate && PrivateModeIndicator != null)
            {
                PrivateModeIndicator.Visibility = Visibility.Visible;
                if (TabBarBorder != null) TabBarBorder.Background = new SolidColorBrush(Color.FromRgb(0x5C, 0x4D, 0x7A)); // purple tint for private
            }
        }

        /// <summary>Ephemeral profile store for private window; null for normal window (use global persisted profiles).</summary>
        private ConcurrentDictionary<string, SiteProfile>? GetProfileStore() => _privateProfiles;

        private static string AppDataDir()
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PrivacyMonitor");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return dir;
        }
        private static string SessionPath() => Path.Combine(AppDataDir(), "session.json");
        private static string BookmarksPath() => Path.Combine(AppDataDir(), "bookmarks.json");
        private static string RecentPath() => Path.Combine(AppDataDir(), "recent.json");
        private static string SettingsPath() => Path.Combine(AppDataDir(), "settings.json");
        private static string ProxyPath() => Path.Combine(AppDataDir(), "proxy.txt");

        private void LoadSettings()
        {
            try
            {
                string path = SettingsPath();
                if (File.Exists(path))
                {
                    string raw = File.ReadAllText(path);
                    var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    _settings = JsonSerializer.Deserialize<AppSettings>(raw, opts) ?? new AppSettings();
                    if (raw != null && !raw.Contains("BlockPopups", StringComparison.OrdinalIgnoreCase)) _settings.BlockPopups = true;
                    if (raw != null && !raw.Contains("HideInPageAds", StringComparison.OrdinalIgnoreCase)) _settings.HideInPageAds = true;
                    if (_settings.ProxyUrl == null) _settings.ProxyUrl = "";
                }
                else
                    _settings = new AppSettings();

                // Proxy: always load from dedicated file (source of truth) so it never gets lost
                string proxyFile = ProxyPath();
                if (File.Exists(proxyFile))
                {
                    try
                    {
                        string proxy = File.ReadAllText(proxyFile).Trim();
                        if (!string.IsNullOrEmpty(proxy))
                            _settings.ProxyUrl = proxy;
                    }
                    catch { }
                }
            }
            catch { _settings = new AppSettings(); }
        }

        /// <summary>Apply form data from settings page (JSON). Used by both postMessage and host-object Save.</summary>
        internal void ApplySettingsFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                _settings.HomePage = root.TryGetProperty("homePage", out var hp) ? hp.GetString() ?? "about:welcome" : "about:welcome";
                _settings.Startup = root.TryGetProperty("startup", out var st) ? st.GetString() ?? "restore" : "restore";
                _settings.SearchEngineUrl = root.TryGetProperty("searchEngineUrl", out var se) ? se.GetString() ?? "https://duckduckgo.com/?q=" : "https://duckduckgo.com/?q=";
                if (root.TryGetProperty("blockPopups", out var bp)) _settings.BlockPopups = bp.ValueKind == JsonValueKind.True;
                if (root.TryGetProperty("hideInPageAds", out var hi)) _settings.HideInPageAds = hi.ValueKind == JsonValueKind.True;
                if (root.TryGetProperty("allowUsageData", out var au)) _settings.AllowUsageData = au.ValueKind == JsonValueKind.True;
                if (root.TryGetProperty("proxyUrl", out var px)) _settings.ProxyUrl = px.GetString() ?? "";
            }
            catch { }
        }

        internal void SaveSettings()
        {
            if (_isPrivate) return;
            try
            {
                string dir = AppDataDir();
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string path = SettingsPath();
                var opts = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(path, JsonSerializer.Serialize(_settings, opts));

                // Proxy: always write to dedicated file so it persists across restarts
                string proxyFile = ProxyPath();
                string proxyValue = _settings.ProxyUrl?.Trim() ?? "";
                File.WriteAllText(proxyFile, proxyValue);
            }
            catch { }
        }

        private void LoadBookmarks()
        {
            if (_isPrivate) return;
            try
            {
                if (File.Exists(BookmarksPath()))
                    _bookmarks = JsonSerializer.Deserialize<List<BookmarkEntry>>(File.ReadAllText(BookmarksPath())) ?? new List<BookmarkEntry>();
            }
            catch { _bookmarks = new List<BookmarkEntry>(); }
        }
        private void SaveBookmarks()
        {
            if (_isPrivate) return;
            try { File.WriteAllText(BookmarksPath(), JsonSerializer.Serialize(_bookmarks, new JsonSerializerOptions { WriteIndented = true })); } catch { }
        }
        private void LoadRecent()
        {
            if (_isPrivate) return;
            try
            {
                if (File.Exists(RecentPath()))
                {
                    var raw = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(RecentPath()));
                    _recentUrls = (raw ?? new List<JsonElement>()).Select(e =>
                {
                    var title = e.GetProperty("Title").GetString() ?? "";
                    var url = e.GetProperty("Url").GetString() ?? "";
                    var visited = DateTime.UtcNow;
                    try { if (e.TryGetProperty("Visited", out var v)) visited = DateTime.Parse(v.GetString() ?? "", null, System.Globalization.DateTimeStyles.RoundtripKind); } catch { }
                    return (title, url, visited);
                }).Where(t => t.Item2.Length > 0).Take(MaxRecent).ToList();
                }
            }
            catch { _recentUrls = new List<(string, string, DateTime)>(); }
        }
        private void AddRecent(string title, string url)
        {
            if (_isPrivate) return;
            if (string.IsNullOrWhiteSpace(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
            _recentUrls.RemoveAll(t => string.Equals(t.Url, url, StringComparison.OrdinalIgnoreCase));
            _recentUrls.Insert(0, (title.Length > 50 ? title.Substring(0, 50) + "…" : title, url, DateTime.UtcNow));
            while (_recentUrls.Count > MaxRecent) _recentUrls.RemoveAt(_recentUrls.Count - 1);
            try { File.WriteAllText(RecentPath(), JsonSerializer.Serialize(_recentUrls.Select(t => new { t.Title, t.Url, Visited = t.Visited.ToString("O") }))); } catch { }
        }
        private void SaveRecent()
        {
            if (_isPrivate) return;
            try { File.WriteAllText(RecentPath(), JsonSerializer.Serialize(_recentUrls.Select(t => new { t.Title, t.Url, Visited = t.Visited.ToString("O") }))); } catch { }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SystemThemeDetector.ThemeChanged -= OnSystemThemeChanged;
            if (_isPrivate) return;
            try
            {
                var urls = _tabs
                    .Select(t => t.Url?.Trim() ?? "")
                    .Where(u => u.Length > 0 && !u.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (urls.Count == 0) urls.Add("about:welcome");
                File.WriteAllText(SessionPath(), JsonSerializer.Serialize(urls));
            }
            catch { }
        }

        private async Task RestoreOrWelcomeAsync()
        {
            if (_isPrivate || string.Equals(_settings.Startup, "welcome", StringComparison.OrdinalIgnoreCase))
            {
                await CreateNewTab("about:welcome");
                return;
            }
            var sessionPath = SessionPath();
            List<string>? urls = null;
            if (File.Exists(sessionPath))
            {
                try
                {
                    var json = File.ReadAllText(sessionPath);
                    urls = JsonSerializer.Deserialize<List<string>>(json);
                }
                catch { }
            }
            if (urls == null || urls.Count == 0)
            {
                await CreateNewTab("about:welcome");
                return;
            }
            for (int i = 0; i < urls.Count; i++)
            {
                await CreateNewTab(urls[i]);
            }
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

        private void OnSystemThemeChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => ApplyTheme(SystemThemeDetector.IsDarkMode));
        }

        /// <summary>Apply light or dark theme: update tab palette and WebView2 preferred color scheme.</summary>
        private void ApplyTheme(bool isDark)
        {
            _isDarkTheme = isDark;
            // Tab bar and tab header palette
            TabBarBg = new SolidColorBrush(isDark ? Color.FromRgb(41, 42, 45) : Color.FromRgb(232, 236, 241));
            TabActiveBg = new SolidColorBrush(isDark ? Color.FromRgb(53, 54, 58) : Colors.White);
            TabActiveFg = new SolidColorBrush(isDark ? Color.FromRgb(232, 234, 237) : Color.FromRgb(15, 23, 42));
            TabInactiveBg = new SolidColorBrush(isDark ? Color.FromRgb(41, 42, 45) : Color.FromRgb(226, 232, 240));
            TabInactiveFg = new SolidColorBrush(isDark ? Color.FromRgb(154, 160, 166) : Color.FromRgb(71, 85, 105));
            PillActive = new SolidColorBrush(isDark ? Color.FromRgb(94, 184, 217) : Color.FromRgb(8, 145, 178));
            PillActiveFg = Brushes.White;
            PillInactive = Brushes.Transparent;
            PillInactiveFg = new SolidColorBrush(isDark ? Color.FromRgb(154, 160, 166) : Color.FromRgb(95, 99, 104));

            // Refresh all tab headers so they use the new brushes
            foreach (var t in _tabs)
                StyleTabHeader(t, t.Id == _activeTabId);

            // Native Windows title bar: dark when dark theme (Chrome-like)
            TrySetTitleBarDarkMode(isDark);

            // WebView2: set preferred color scheme so web content gets prefers-color-scheme: dark/light
            foreach (var tab in _tabs)
                SetWebView2ColorScheme(tab, isDark);

            // Address bar focus brush when not focused (in case we're re-applying)
            if (AddressBarBorder != null && !AddressBar.IsFocused)
            {
                AddressBarBorder.BorderBrush = new SolidColorBrush(isDark ? Color.FromRgb(95, 99, 104) : Color.FromRgb(226, 232, 240));
                AddressBarBorder.BorderThickness = new Thickness(1);
                AddressBarBorder.Effect = null;
            }
        }

        private void SetWebView2ColorScheme(BrowserTab tab, bool preferDark)
        {
            try
            {
                var cw = tab.WebView?.CoreWebView2;
                if (cw?.Profile == null) return;
                cw.Profile.PreferredColorScheme = preferDark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            }
            catch { }
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

            // Stats: one line (simple) vs full grid (expert)
            if (SimpleStatsBorder != null) SimpleStatsBorder.Visibility = simple;
            if (StatsGridBorder != null) StatsGridBorder.Visibility = expert;

            // Dashboard: hide technical blocks in simple mode
            if (CategoryCard != null) CategoryCard.Visibility = expert;
            BreakdownPanel.Visibility = expert;
            MitigationPanel.Visibility = simple;

            // Fingerprint panel: simple vs expert list
            FpSimpleList.Visibility = simple;
            FingerprintList.Visibility = expert;

            // Forensics: expert sections vs simplified
            ForensicExpertPanel.Visibility = expert;
            SimpleBehavioralCard.Visibility = simple;
        }

        // ================================================================
        //  PROTECTION CONTROLS (via app menu)
        // ================================================================
        private void MenuProtection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem mi || mi.Tag == null) return;
            if (!int.TryParse(mi.Tag.ToString(), out int idx) || idx < 0 || idx > 2) return;
            var mode = (ProtectionMode)idx;
            SetProtectionMenuChecked(mode);
            ApplyProtectionMode(mode);
        }

        private void SetProtectionMenuChecked(ProtectionMode mode)
        {
            if (ProtectionMonitorItem != null) ProtectionMonitorItem.IsChecked = mode == ProtectionMode.Monitor;
            if (ProtectionBlockItem != null) ProtectionBlockItem.IsChecked = mode == ProtectionMode.BlockKnown;
            if (ProtectionAggressiveItem != null) ProtectionAggressiveItem.IsChecked = mode == ProtectionMode.Aggressive;
            if (PillProtectionMonitor != null) PillProtectionMonitor.IsChecked = mode == ProtectionMode.Monitor;
            if (PillProtectionBlock != null) PillProtectionBlock.IsChecked = mode == ProtectionMode.BlockKnown;
            if (PillProtectionAggressive != null) PillProtectionAggressive.IsChecked = mode == ProtectionMode.Aggressive;
            if (ProtectionPillText != null)
                ProtectionPillText.Text = mode == ProtectionMode.Monitor ? "Monitor only" : mode == ProtectionMode.BlockKnown ? "Block known" : "Aggressive";
        }

        private void ProtectionPillBtn_Click(object sender, RoutedEventArgs e)
        {
            if (ProtectionPillBtn != null && ProtectionPillMenu != null)
            {
                ProtectionPillMenu.PlacementTarget = ProtectionPillBtn;
                ProtectionPillMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                ProtectionPillMenu.IsOpen = true;
            }
        }

        private void ApplyProtectionMode(ProtectionMode mode)
        {
            ProtectionEngine.GlobalDefaultMode = mode;
            var tab = ActiveTab;
            if (tab != null)
            {
                if (!string.IsNullOrEmpty(tab.CurrentHost))
                    ProtectionEngine.SetMode(tab.CurrentHost, mode, GetProfileStore());
                UpdateProtectionUI(tab);
                var profile = ProtectionEngine.GetProfile(tab.CurrentHost, GetProfileStore());
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
                var profile = ProtectionEngine.GetProfile(tab.CurrentHost, GetProfileStore());
                profile.AntiFingerprint = !profile.AntiFingerprint;
                ProtectionEngine.SetProfile(tab.CurrentHost, profile, GetProfileStore());
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
                var profile = ProtectionEngine.GetProfile(tab.CurrentHost, GetProfileStore());
                profile.BlockAdsTrackers = !profile.BlockAdsTrackers;
                ProtectionEngine.SetProfile(tab.CurrentHost, profile, GetProfileStore());
                _adBlockEnabled = profile.BlockAdsTrackers;
                UpdateAdBlockButton();
                _ = ApplyRuntimeBlockerAsync(tab, profile);
                _ = UpdatePerNavigationScriptsAsync(tab, profile);
                _dirty = true;
            }
        }

        private void UpdateProtectionUI(BrowserTab tab)
        {
            var mode = ProtectionEngine.GetEffectiveMode(tab.CurrentHost, GetProfileStore());
            SetProtectionMenuChecked(mode);

            // Update per-site toggles
            var profile = ProtectionEngine.GetProfile(tab.CurrentHost, GetProfileStore());
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
        private const string WebView2DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/#download-section";

        /// <summary>Directory containing the running .exe (so we find WebView2 folder next to it when single-file published).</summary>
        private static string GetExeDirectory()
        {
            try
            {
                string? path = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(path))
                {
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        return dir;
                }
            }
            catch { }
            try
            {
                var process = System.Diagnostics.Process.GetCurrentProcess();
                string? path = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                {
                    string? dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                        return dir;
                }
            }
            catch { }
            return AppContext.BaseDirectory;
        }

        private static CoreWebView2EnvironmentOptions? BuildEnvironmentOptions(string? proxyUrl)
        {
            if (string.IsNullOrWhiteSpace(proxyUrl)) return null;
            string arg = "--proxy-server=" + proxyUrl.Trim();
            return new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = arg };
        }

        /// <summary>Set when WebView2 started without proxy because creation with proxy failed (e.g. 0x8007139F). UI can show a message.</summary>
        private static bool _webView2StartedWithoutProxyFallback;

        private static async Task<CoreWebView2Environment?> CreateWebView2EnvironmentAsync(string? proxyUrl, string? userDataFolderOverride = null)
        {
            _webView2StartedWithoutProxyFallback = false;
            var options = BuildEnvironmentOptions(proxyUrl);
            var env = await CreateWebView2EnvironmentCoreAsync(options, userDataFolderOverride);
            // If proxy was set and creation failed (e.g. 0x8007139F), retry without proxy so the app can start
            if (env == null && options != null && userDataFolderOverride == null)
            {
                env = await CreateWebView2EnvironmentCoreAsync(null, null);
                if (env != null) _webView2StartedWithoutProxyFallback = true;
            }
            if (env != null) return env;
            throw new InvalidOperationException("WebView2 failed to start.");
        }

        private static async Task<CoreWebView2Environment?> CreateWebView2EnvironmentCoreAsync(CoreWebView2EnvironmentOptions? options, string? userDataFolderOverride = null)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userDataFolder = userDataFolderOverride ?? Path.Combine(localAppData, "PrivacyMonitor", "WebView2");
            string userDataFolderRecovery = Path.Combine(localAppData, "PrivacyMonitor", "WebView2_Recovery");

            async Task<CoreWebView2Environment?> TryCreateAsync(string? runtimePath, string dataFolder)
            {
                try
                {
                    if (runtimePath != null)
                        return options != null
                            ? await CoreWebView2Environment.CreateAsync(runtimePath, dataFolder, options)
                            : await CoreWebView2Environment.CreateAsync(runtimePath, dataFolder);
                    return options != null
                        ? await CoreWebView2Environment.CreateAsync(null, dataFolder, options)
                        : await CoreWebView2Environment.CreateAsync(null, dataFolder);
                }
                catch
                {
                    return null;
                }
            }

            async Task<CoreWebView2Environment?> TryWithFallbackFolderAsync(string? runtimePath)
            {
                var env = await TryCreateAsync(runtimePath, userDataFolder);
                if (env != null) return env;
                if (userDataFolderOverride != null) return null;
                return await TryCreateAsync(runtimePath, userDataFolderRecovery);
            }

            // 1) Try bundled fixed runtime: exe dir, then BaseDirectory, then current directory
            string exeDir = GetExeDirectory();
            string baseDir = AppContext.BaseDirectory;
            string currentDir = Directory.GetCurrentDirectory();
            foreach (string dir in new[] { exeDir, baseDir, currentDir })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string bundledWebView2 = Path.Combine(dir, "WebView2");
                string fixedRuntimePath = Path.Combine(dir, "Microsoft.Web.WebView2.FixedVersionRuntime.win-x64");
                if (Directory.Exists(bundledWebView2))
                {
                    var env = await TryWithFallbackFolderAsync(bundledWebView2);
                    if (env != null) return env;
                    break;
                }
                if (Directory.Exists(fixedRuntimePath))
                {
                    var env = await TryWithFallbackFolderAsync(fixedRuntimePath);
                    if (env != null) return env;
                    break;
                }
            }

            // 2) Try embedded WebView2 (extract from exe to LocalAppData on first run — single exe for any PC)
            string? extractedPath = await TryExtractEmbeddedWebView2Async();
            if (!string.IsNullOrEmpty(extractedPath))
            {
                var env = await TryWithFallbackFolderAsync(extractedPath);
                if (env != null) return env;
            }

            // 3) Fallback: system WebView2 (Evergreen - installed with Edge or standalone)
            return await TryWithFallbackFolderAsync(null);
        }

        private static async Task<string?> TryExtractEmbeddedWebView2Async()
        {
            string extractRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrivacyMonitor", "WebView2Runtime");
            string webView2Path = Path.Combine(extractRoot, "WebView2");
            if (Directory.Exists(webView2Path))
            {
                string exePath = Path.Combine(webView2Path, "msedgewebview2.exe");
                if (File.Exists(exePath)) return webView2Path;
            }

            Stream? zipStream = null;
            foreach (string name in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                if (name.EndsWith("WebView2Runtime.zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                    break;
                }
            }
            if (zipStream == null) return null;

            try
            {
                await Task.Run(() =>
                {
                    if (Directory.Exists(extractRoot)) Directory.Delete(extractRoot, true);
                    Directory.CreateDirectory(extractRoot);
                    using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false))
                    {
                        zip.ExtractToDirectory(extractRoot);
                    }
                }).ConfigureAwait(false);
                if (File.Exists(Path.Combine(webView2Path, "msedgewebview2.exe")))
                    return webView2Path;
            }
            catch { }
            return null;
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
                string? proxy = string.IsNullOrWhiteSpace(_settings.ProxyUrl) ? null : _settings.ProxyUrl.Trim();
                if (_isPrivate)
                {
                    _privateUserDataFolder ??= Path.Combine(Path.GetTempPath(), "PrivacyMonitor_Private", Guid.NewGuid().ToString("N"));
                    if (!Directory.Exists(_privateUserDataFolder)) Directory.CreateDirectory(_privateUserDataFolder);
                }
                var environment = await CreateWebView2EnvironmentAsync(proxy, _isPrivate ? _privateUserDataFolder : null);
                await tab.WebView.EnsureCoreWebView2Async(environment);
                tab.IsReady = true;
                if (_webView2StartedWithoutProxyFallback && StatusText != null)
                    StatusText.Text = "Started without proxy (WebView2 failed with proxy). Try clearing proxy in Settings or use a different proxy.";
                var cw = tab.WebView.CoreWebView2;
                cw.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                cw.WebResourceRequested += (s, e) => OnWebResourceRequested(tab, e);
                cw.WebResourceResponseReceived += (s, e) => OnWebResourceResponseReceived(tab, e);
                cw.WebMessageReceived += (s, e) => OnWebMessage(tab, e);
                try { cw.AddHostObjectToScript("settingsBridge", new SettingsSaveBridge(this, tab)); } catch { }
                cw.NavigationStarting += (s, e) => OnNavigationStarting(tab, e);
                cw.NavigationCompleted += (s, e) => OnNavigationCompleted(tab, e);
                cw.DownloadStarting += (s, e) => OnDownloadStarting(tab, e);
                cw.ProcessFailed += (s, e) => OnProcessFailed(tab, e);
                cw.DocumentTitleChanged += (s, e) => Dispatcher.Invoke(() => UpdateTabTitle(tab));
                cw.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    if (_settings.BlockPopups)
                        return; // Block pop-up entirely
                    Dispatcher.Invoke(async () => await CreateNewTab(e.Uri));
                };
                await cw.AddScriptToExecuteOnDocumentCreatedAsync(ProtectionEngine.ElementBlockerBootstrapScript);
                if (_settings.HideInPageAds)
                    await cw.AddScriptToExecuteOnDocumentCreatedAsync(ProtectionEngine.CosmeticFilterScript);

                SetWebView2ColorScheme(tab, _isDarkTheme);

                if (url == "about:welcome")
                    cw.NavigateToString(GetWelcomeHtml());
                else if (url.StartsWith("about:history", StringComparison.OrdinalIgnoreCase))
                    cw.NavigateToString(GetHistoryHtml());
                else if (url.StartsWith("about:settings", StringComparison.OrdinalIgnoreCase))
                    cw.NavigateToString(GetSettingsHtml());
                else if (url.StartsWith("about:shortcuts", StringComparison.OrdinalIgnoreCase))
                    cw.NavigateToString(GetShortcutsHtml());
                else
                    cw.Navigate(url);
                await UpdatePerNavigationScriptsAsync(tab, ProtectionEngine.GetProfile(tab.CurrentHost ?? "", GetProfileStore()));
            }
            catch (Exception ex)
            {
                string message = $"WebView2 failed to start.\n\n{ex.Message}\n\n" +
                    "Privacy Monitor needs the WebView2 Runtime. Install it from:\n" + WebView2DownloadUrl;
                MessageBox.Show(message, "Privacy Monitor - WebView2 Required", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (t.WebView != null)
                    t.WebView.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

                if (active)
                {
                    // Wake sleeping tab when it becomes active
                    if (t.IsSleeping && t.WebView != null)
                    {
                        ResumeWebView(t.WebView);
                        t.IsSleeping = false;
                    }

                    t.LastActivityUtc = DateTime.UtcNow;

                    bool isWelcome = string.IsNullOrEmpty(t.Url) || t.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
                    AddressBar.Text = isWelcome ? "" : t.Url;
                    UpdateAddressBarPlaceholder();
                    LoadingBar.Visibility = t.IsLoading ? Visibility.Visible : Visibility.Collapsed;
                    UpdateLockIcon(t);
                    StatusText.Text = t.CurrentHost.Length > 0 ? t.CurrentHost : "Ready — enter a URL above to start";
                    UpdateProtectionUI(t);
                    UpdateNavButtons();
                    UpdateStarButton();
                    if (CrashOverlay != null) CrashOverlay.Visibility = t.IsCrashed ? Visibility.Visible : Visibility.Collapsed;
                }

                StyleTabHeader(t, active);
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

        /// <summary>Switch to tab by 0-based index (Ctrl+1..8). No-op if index &gt;= tab count.</summary>
        private void SwitchToTabByIndex(int index)
        {
            if (index < 0 || index >= _tabs.Count) return;
            SwitchToTab(_tabs[index].Id);
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

            // Heavy tab indicator (small dot/flame for high resource usage)
            var heavyBadge = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(249, 115, 22)), // warm orange
                Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
                ToolTip = "This tab is using more resources than others."
            };
            tab.HeavyBadge = heavyBadge;

            // Layout
            var panel = new DockPanel();
            DockPanel.SetDock(closeBtn, Dock.Right);
            DockPanel.SetDock(blockedBadge, Dock.Right);
            DockPanel.SetDock(heavyBadge, Dock.Right);
            panel.Children.Add(closeBtn);
            panel.Children.Add(blockedBadge);
            panel.Children.Add(heavyBadge);
            panel.Children.Add(initialBorder);
            panel.Children.Add(title);

            var border = new Border
            {
                CornerRadius = new CornerRadius(10, 10, 0, 0),
                Padding = new Thickness(12, 8, 10, 8),
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 2, 0),
                Background = TabInactiveBg,
                Child = panel,
                MinWidth = 80, MaxWidth = 220,
                ToolTip = "New Tab"
            };

            // Delay tooltip like Chrome's hover cards (e.g., 2 seconds)
            ToolTipService.SetInitialShowDelay(border, 2000);

            // When user hovers a tab, request a fresh memory reading for that tab
            border.MouseEnter += (_, _) => RequestTabMemoryAsync(tab);

            border.MouseLeftButtonDown += (_, _) => SwitchToTab(cid);
            return border;
        }

        private void StyleTabHeader(BrowserTab tab, bool active)
        {
            tab.TabHeader.Background = active ? TabActiveBg : TabInactiveBg;
            tab.TitleBlock.Foreground = active ? TabActiveFg : TabInactiveFg;
            if (tab.TabHeader.Child is DockPanel dp && dp.Children.Count > 0 && dp.Children[0] is Button cb)
                cb.Foreground = active ? TabActiveFg : TabInactiveFg;

            // Active tab: subtle shadow and theme-aware border so it connects to nav bar
            if (active)
            {
                tab.TabHeader.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 0, Opacity = _isDarkTheme ? 0.12f : 0.06f, Color = Colors.Black };
                tab.TabHeader.BorderThickness = new Thickness(1, 1, 1, 0);
                tab.TabHeader.BorderBrush = TryFindResource("ActiveTabBorderBrush") is SolidColorBrush brush ? brush : new SolidColorBrush(Color.FromRgb(226, 232, 240));
            }
            else
            {
                tab.TabHeader.Effect = null;
                tab.TabHeader.BorderThickness = new Thickness(0);
                tab.TabHeader.BorderBrush = null;
            }

            // Sleeping tabs: slightly dimmed so users can see they are paused
            if (!active && tab.IsSleeping)
            {
                tab.TabHeader.Opacity = 0.6;
            }
            else
            {
                tab.TabHeader.Opacity = 1.0;
            }

            // Heavy-tab indicator visibility
            if (tab.HeavyBadge != null)
            {
                tab.HeavyBadge.Visibility = tab.IsHeavy ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TrySetTitleBarDarkMode(bool useDark)
        {
            try
            {
                const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(bool));
                var value = useDark;
                _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, size);
            }
            catch { }
        }

        // ================================================================
        //  TAB SLEEP / WAKE (PERFORMANCE)
        // ================================================================

        /// <summary>Periodically called from the UI timer to suspend long-idle background tabs.</summary>
        private async void CheckIdleTabsForSleep()
        {
            var now = DateTime.UtcNow;

            // Throttle to avoid running every 500ms
            if (now - _lastSleepCheckUtc < TimeSpan.FromSeconds(30))
                return;

            _lastSleepCheckUtc = now;

            foreach (var tab in _tabs)
            {
                // Never sleep the active tab
                if (tab.Id == _activeTabId)
                    continue;
                if (!tab.IsReady || tab.IsSleeping)
                    continue;

                // Skip very new tabs that haven't navigated yet
                if (now - tab.LastActivityUtc < _idleBeforeSleep)
                    continue;

                if (tab.WebView == null || tab.WebView.CoreWebView2 == null)
                    continue;

                bool suspended = await TrySuspendWebViewAsync(tab.WebView);
                if (!suspended)
                    continue;

                tab.IsSleeping = true;

                // Light UI hint so users know the tab is paused
                Dispatcher.Invoke(() =>
                {
                    tab.TabHeader.ToolTip = string.IsNullOrEmpty(tab.Title)
                        ? "Sleeping tab — click to wake"
                        : $"{tab.Title} (sleeping — click to wake)";
                    StyleTabHeader(tab, tab.Id == _activeTabId);
                });
            }
        }

        /// <summary>Best-effort WebView2 suspension using reflection so we work on older runtimes.</summary>
        private static async Task<bool> TrySuspendWebViewAsync(WebView2 webView)
        {
            try
            {
                var core = webView.CoreWebView2;
                if (core == null) return false;

                var method = core.GetType().GetMethod("TrySuspendAsync");
                if (method == null) return false;

                if (method.Invoke(core, null) is Task<bool> task)
                    return await task.ConfigureAwait(false);
            }
            catch
            {
                // Ignore failures – sleeping is a best-effort optimization.
            }
            return false;
        }

        /// <summary>Resume a previously suspended WebView2 instance if the API is available.</summary>
        private static void ResumeWebView(WebView2 webView)
        {
            try
            {
                var core = webView.CoreWebView2;
                if (core == null) return;
                var method = core.GetType().GetMethod("Resume");
                method?.Invoke(core, null);
            }
            catch
            {
                // Ignore failures – if resume is not available we just keep the tab as-is.
            }
        }

        /// <summary>
        /// Ask the tab's page to report its approximate memory usage (via JS performance APIs).
        /// Result comes back through OnWebMessage with cat = "memory".
        /// </summary>
        private async void RequestTabMemoryAsync(BrowserTab tab)
        {
            try
            {
                if (!tab.IsReady) return;
                var cw = tab.WebView?.CoreWebView2;
                if (cw == null) return;

                const string script = @"
(async function(){
  try {
    if (performance && performance.measureUserAgentSpecificMemory) {
      const r = await performance.measureUserAgentSpecificMemory();
      window.chrome && window.chrome.webview && window.chrome.webview.postMessage({ cat: 'memory', bytes: r.bytes || 0 });
    } else if (performance && performance.memory && (performance.memory.usedJSHeapSize || performance.memory.totalJSHeapSize)) {
      const m = performance.memory;
      const bytes = m.usedJSHeapSize || m.totalJSHeapSize || 0;
      window.chrome && window.chrome.webview && window.chrome.webview.postMessage({ cat: 'memory', bytes: bytes });
    } else {
      window.chrome && window.chrome.webview && window.chrome.webview.postMessage({ cat: 'memory', bytes: 0 });
    }
  } catch(e) {
    try {
      window.chrome && window.chrome.webview && window.chrome.webview.postMessage({ cat: 'memory', bytes: 0 });
    } catch(_) {}
  }
})();";

                await cw.ExecuteScriptAsync(script);
            }
            catch
            {
                // Best-effort only; ignore failures.
            }
        }

        // ================================================================
        //  TAB TASK MANAGER
        // ================================================================

        private void RefreshTaskManager()
        {
            if (TaskManagerPopup == null || TaskManagerList == null || !TaskManagerPopup.IsOpen)
                return;

            UpdateHeavyTabs();

            var rows = new List<TaskManagerRow>();
            foreach (var tab in _tabs)
            {
                string title = string.IsNullOrWhiteSpace(tab.Title) ? "New Tab" : tab.Title;
                string url = string.IsNullOrWhiteSpace(tab.Url) ? "" : tab.Url;

                string memLabel = "";
                if (tab.LastMemoryBytes is long bytes && bytes > 0)
                {
                    double mb = bytes / (1024.0 * 1024.0);
                    if (mb >= 1024)
                    {
                        double gb = mb / 1024.0;
                        memLabel = $"~{gb:F2} GB";
                    }
                    else
                    {
                        memLabel = $"~{mb:F1} MB";
                    }
                }
                else
                {
                    memLabel = "";
                }

                string requestsLabel = $"{tab.Requests.Count} request" + (tab.Requests.Count == 1 ? "" : "s");
                string blockedLabel = $"{tab.BlockedCount} blocked";
                string sleepLabel = tab.IsSleeping ? "Sleeping" : "Sleep";

                rows.Add(new TaskManagerRow
                {
                    Id = tab.Id,
                    Title = title,
                    Url = url,
                    Memory = memLabel,
                    RequestsLabel = requestsLabel,
                    BlockedLabel = blockedLabel,
                    SleepLabel = sleepLabel
                });
            }

            TaskManagerList.ItemsSource = rows;
        }

        /// <summary>Mark tabs as heavy when they are using far more resources than the others.</summary>
        private void UpdateHeavyTabs()
        {
            if (_tabs.Count == 0) return;

            double avgRequests = _tabs.Average(t => t.Requests.Count);
            double avgBlocked = _tabs.Average(t => t.BlockedCount);
            double avgMemBytes = _tabs.Where(t => t.LastMemoryBytes is long b && b > 0).Select(t => (double)t.LastMemoryBytes!).DefaultIfEmpty(0).Average();

            foreach (var tab in _tabs)
            {
                bool heavy = false;

                // Heuristic 1: very high request volume
                if (tab.Requests.Count > 200 && tab.Requests.Count > avgRequests * 1.5)
                    heavy = true;

                // Heuristic 2: many blocked trackers / ads
                if (tab.BlockedCount > 50 && tab.BlockedCount > avgBlocked * 1.5)
                    heavy = true;

                // Heuristic 3: high memory vs peers (if we have data)
                if (tab.LastMemoryBytes is long bytes && bytes > 0 && avgMemBytes > 0)
                {
                    double factor = bytes / avgMemBytes;
                    if (bytes > 200 * 1024 * 1024 && factor > 1.5) // >200MB and 1.5x average
                        heavy = true;
                }

                tab.IsHeavy = heavy;
                if (tab.HeavyBadge != null)
                    tab.HeavyBadge.Visibility = heavy ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TaskManagerClosePopup_Click(object sender, RoutedEventArgs e)
        {
            if (TaskManagerPopup != null)
                TaskManagerPopup.IsOpen = false;
        }

        private async void TaskManagerSleep_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not TaskManagerRow row)
                return;

            var tab = _tabs.FirstOrDefault(t => t.Id == row.Id);
            if (tab == null || tab.WebView == null || tab.IsSleeping)
                return;

            bool suspended = await TrySuspendWebViewAsync(tab.WebView);
            if (suspended)
            {
                tab.IsSleeping = true;
                RefreshTaskManager();
                StyleTabHeader(tab, tab.Id == _activeTabId);
            }
        }

        private void TaskManagerCloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not TaskManagerRow row)
                return;

            CloseTab(row.Id);
            RefreshTaskManager();
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref bool attrValue, int attrSize);

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
            tab.LastActivityUtc = DateTime.UtcNow;
            if (e.Uri != null && e.Uri.StartsWith("about:history", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(() => { try { tab.WebView?.CoreWebView2?.NavigateToString(GetHistoryHtml()); } catch { } });
                return;
            }
            if (e.Uri != null && e.Uri.StartsWith("about:settings", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                string uri = e.Uri;
                var tabCapture = tab;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        bool saved = false;
                        if (uri.IndexOf("save=1", StringComparison.OrdinalIgnoreCase) >= 0 || uri.IndexOf("save=true", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            int q = uri.IndexOf('?');
                            if (q >= 0 && q < uri.Length - 1)
                            {
                                string query = uri.Substring(q + 1);
                                foreach (var pair in query.Split('&'))
                                {
                                    var parts = pair.Split('=', 2);
                                    if (parts.Length != 2) continue;
                                    string key = Uri.UnescapeDataString(parts[0].Trim());
                                    string val = Uri.UnescapeDataString(parts[1].Replace('+', ' ').Trim());
                                    if (key.Equals("homePage", StringComparison.OrdinalIgnoreCase)) _settings.HomePage = val ?? "about:welcome";
                                    else if (key.Equals("startup", StringComparison.OrdinalIgnoreCase)) _settings.Startup = val ?? "restore";
                                    else if (key.Equals("searchEngineUrl", StringComparison.OrdinalIgnoreCase)) _settings.SearchEngineUrl = val ?? "https://duckduckgo.com/?q=";
                                    else if (key.Equals("blockPopups", StringComparison.OrdinalIgnoreCase)) _settings.BlockPopups = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                                    else if (key.Equals("hideInPageAds", StringComparison.OrdinalIgnoreCase)) _settings.HideInPageAds = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                                    else if (key.Equals("allowUsageData", StringComparison.OrdinalIgnoreCase)) _settings.AllowUsageData = val.Equals("true", StringComparison.OrdinalIgnoreCase);
                                    else if (key.Equals("proxyUrl", StringComparison.OrdinalIgnoreCase)) _settings.ProxyUrl = val ?? "";
                                }
                                SaveSettings();
                                saved = true;
                            }
                        }
                        tabCapture.WebView?.CoreWebView2?.NavigateToString(GetSettingsHtml(showSavedMessage: saved));
                    }
                    catch { try { tabCapture.WebView?.CoreWebView2?.NavigateToString(GetSettingsHtml()); } catch { } }
                }));
                return;
            }
            if (e.Uri != null && e.Uri.StartsWith("about:shortcuts", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(() => { try { tab.WebView?.CoreWebView2?.NavigateToString(GetShortcutsHtml()); } catch { } });
                return;
            }
            if (e.Uri != null && e.Uri.StartsWith("about:clearhistory", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(() =>
                {
                    _recentUrls.Clear();
                    SaveRecent();
                    try { tab.WebView?.CoreWebView2?.NavigateToString(GetHistoryHtml()); } catch { }
                    if (StatusText != null) StatusText.Text = "History cleared.";
                });
                return;
            }
            if (e.Uri != null && (e.Uri.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase)))
            {
                e.Cancel = true;
                if (e.Uri.StartsWith("edge://history", StringComparison.OrdinalIgnoreCase) || e.Uri.StartsWith("chrome://history", StringComparison.OrdinalIgnoreCase))
                    Dispatcher.BeginInvoke(() => { try { tab.WebView?.CoreWebView2?.NavigateToString(GetHistoryHtml()); } catch { } });
                return;
            }

            if (string.IsNullOrEmpty(e.Uri)) return;

            Dispatcher.Invoke(() =>
            {
                try
                {
                    var uri = new Uri(e.Uri!);
                    if (!uri.Host.Equals(tab.CurrentHost, StringComparison.OrdinalIgnoreCase))
                        tab.ResetDetection();
                    tab.CurrentHost = uri.Host; tab.Url = e.Uri; tab.IsLoading = true;
                    if (!string.IsNullOrEmpty(tab.CurrentHost))
                    {
                        var profile = ProtectionEngine.GetProfile(tab.CurrentHost, GetProfileStore());
                        profile.LastVisit = DateTime.UtcNow;
                        ProtectionEngine.SetProfile(tab.CurrentHost, profile, GetProfileStore());
                    }
                    if (tab.Id == _activeTabId)
                    {
                        bool isAbout = string.IsNullOrEmpty(e.Uri) || e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
                        AddressBar.Text = isAbout ? "" : e.Uri;
                        UpdateAddressBarPlaceholder();
                        LoadingBar.Visibility = Visibility.Visible;
                        StatusText.Text = "Loading…";
                        UpdateLockIcon(tab);
                        UpdateProtectionUI(tab);
                        UpdateNavButtons();
                    }
                    _dirty = true;
                }
                catch { }
            });

            try
            {
                if (tab.IsReady)
                {
                    var profile = ProtectionEngine.GetProfile(tab.CurrentHost, GetProfileStore());
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
                        try { cw.Settings.UserAgent = ProtectionEngine.BlendInUserAgent; } catch { }
                    }
                    else
                    {
                        tab.AntiFpScriptId = null;
                        tab.AntiFingerprintInjected = false;
                        try { cw.Settings.UserAgent = ""; } catch { }
                    }
                }
                catch { tab.AntiFpScriptId = null; }
            }
            else
            {
                tab.AntiFpScriptId = null;
                tab.AntiFingerprintInjected = false;
                try { cw.Settings.UserAgent = ""; } catch { }
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
            tab.LastActivityUtc = DateTime.UtcNow;
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
                    UpdateNavButtons();
                    UpdateStarButton();
                }
                AddRecent(tab.Title ?? "", tab.Url ?? "");
            });
            if (!tab.IsReady) return;

            try
            {
                var profile = ProtectionEngine.GetProfile(tab.CurrentHost, GetProfileStore());
                await ApplyRuntimeBlockerAsync(tab, profile);
            }
            catch { }

            try { await tab.WebView.CoreWebView2.ExecuteScriptAsync(PrivacyEngine.StorageEnumerationScript); } catch { }
            try { await tab.WebView.CoreWebView2.ExecuteScriptAsync(PrivacyEngine.WebRtcLeakScript); } catch { }
            _dirty = true;
        }

        private void OnDownloadStarting(BrowserTab tab, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                var op = e.DownloadOperation;
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloads)) Directory.CreateDirectory(downloads);
                string? name = null;
                try { name = Path.GetFileName(new Uri(op.Uri).LocalPath); } catch { }
                if (string.IsNullOrEmpty(name) || name.Length < 2) name = "download";
                foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                if (string.IsNullOrEmpty(Path.GetExtension(name))) name += ".bin";
                string path = Path.Combine(downloads, name);
                int n = 0;
                while (File.Exists(path))
                    path = Path.Combine(downloads, Path.GetFileNameWithoutExtension(name) + $" ({++n})" + Path.GetExtension(name));
                e.ResultFilePath = path;
                string fileName = Path.GetFileName(path);
                Dispatcher.Invoke(() => { if (StatusText != null) StatusText.Text = "Downloading: " + fileName; });
                op.StateChanged += (_, _) =>
                {
                    if (op.State == CoreWebView2DownloadState.Completed || op.State == CoreWebView2DownloadState.Interrupted)
                        Dispatcher.Invoke(() => { if (StatusText != null) StatusText.Text = op.State == CoreWebView2DownloadState.Completed ? "Download complete." : "Ready"; });
                };
            }
            catch { }
        }

        private void OnProcessFailed(BrowserTab tab, CoreWebView2ProcessFailedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                tab.IsCrashed = true;
                if (tab.Id == _activeTabId && CrashOverlay != null)
                {
                    CrashOverlay.Visibility = Visibility.Visible;
                }
            });
        }

        private void CrashReload_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab == null) return;
            tab.IsCrashed = false;
            if (CrashOverlay != null) CrashOverlay.Visibility = Visibility.Collapsed;
            try
            {
                if (!string.IsNullOrEmpty(tab.Url) && !tab.Url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                    tab.WebView.CoreWebView2?.Navigate(tab.Url);
                else
                    tab.WebView.CoreWebView2?.NavigateToString(GetWelcomeHtml());
            }
            catch { }
        }

        private void UpdateNavButtons()
        {
            var tab = ActiveTab;
            try
            {
                var cw = tab?.WebView?.CoreWebView2;
                if (NavBack != null) NavBack.IsEnabled = cw?.CanGoBack == true;
                if (NavForward != null) NavForward.IsEnabled = cw?.CanGoForward == true;
            }
            catch
            {
                if (NavBack != null) NavBack.IsEnabled = false;
                if (NavForward != null) NavForward.IsEnabled = false;
            }
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

                // ── INTERCEPTOR PAUSE (Burp-style): hold request at network level ──
                if (_interceptorSink is NetworkInterceptor.NetworkInterceptorService svc && svc.IsPaused && _interceptorTabId == tab.Id)
                {
                    CoreWebView2Deferral deferral = e.GetDeferral();
                    lock (_pausedInterceptorLock)
                    {
                        _pausedInterceptorDeferred.Add((deferral, entry, tab));
                        svc.SetPausedPendingCount(_pausedInterceptorDeferred.Count);
                    }
                    return;
                }

                // ── ACTIVE PROTECTION: Evaluate blocking decision ──
                var profile = ProtectionEngine.GetProfile(tab.CurrentHost, GetProfileStore());
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
                else if (profile.AntiFingerprint && profile.Mode != ProtectionMode.Monitor)
                {
                    try
                    {
                        e.Request.Headers.SetHeader("Sec-CH-UA", "\"Chromium\";v=\"131\", \"Google Chrome\";v=\"131\", \"Not_A Brand\";v=\"24\"");
                        e.Request.Headers.SetHeader("Sec-CH-UA-Mobile", "?0");
                        e.Request.Headers.SetHeader("Sec-CH-UA-Platform", "\"Windows\"");
                    }
                    catch { }
                }

                // Strip Referer on third-party requests to reduce cross-site leakage (stronger privacy).
                if (!shouldCancelInBrowser && isThirdParty && profile.Mode != ProtectionMode.Monitor)
                {
                    try { e.Request.Headers.SetHeader("Referer", ""); } catch { }
                }

                // Do Not Track: signal to sites that user prefers not to be tracked (stronger privacy).
                if (!shouldCancelInBrowser && profile.Mode != ProtectionMode.Monitor)
                {
                    try { e.Request.Headers.SetHeader("DNT", "1"); } catch { }
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

                // Adaptive learning: skip in private window so we don't persist
                if (!_isPrivate)
                {
                    foreach (var sig in trackerSignals)
                        if (!isMedia)
                            ProtectionEngine.ObserveTrackerSignal(uri.Host, sig.SignalType, sig.Confidence);

                    if (isThirdParty && trackerSignals.Count > 0 && !isMedia)
                        ProtectionEngine.ObserveCrossSiteAppearance(uri.Host, tab.CurrentHost);
                }

                // Enqueue for batched drain (avoids per-request Dispatcher.Invoke)
                tab.PendingRequests.Enqueue(entry);
                if (_interceptorSink != null && _interceptorTabId == tab.Id)
                    _interceptorSink.RecordRequest(tab.Id, entry);
                MarkDirtyThrottled();
            }
            catch { }
        }

        private void MarkDirtyThrottled()
        {
            long now = Environment.TickCount64;
            if (now - _lastDirtySetTicks >= 300) { _dirty = true; _lastDirtySetTicks = now; }
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
                            PrivacyEngine.AddResponseSignals(tab.Requests[i]); // ETag/cache-cookie tracking now that response is in
                            // Capture content-type and response size for evidence
                            if (respHeaders.TryGetValue("content-type", out var ct))
                                tab.Requests[i].ContentType = ct;
                            if (respHeaders.TryGetValue("content-length", out var cl) && long.TryParse(cl, out var clVal))
                                tab.Requests[i].ResponseSize = clVal;
                            if (tab.Requests[i].Host == tab.CurrentHost && tab.SecurityHeaders.Count == 0 && statusCode >= 200 && statusCode < 400)
                                tab.SecurityHeaders = PrivacyEngine.AnalyzeSecurityHeaders(respHeaders);
                            if (_interceptorSink != null && _interceptorTabId == tab.Id)
                                _interceptorSink.RecordResponse(tab.Id, uri, statusCode, respHeaders, tab.Requests[i].ContentType, tab.Requests[i].ResponseSize);
                            break;
                        }
                    }
                    MarkDirtyThrottled();
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
                    // Feed runtime signals to interceptor RiskScoring (sandboxed JS → IRuntimeFingerprintSignalProvider)
                    string signal = NetworkInterceptor.TabScopedRuntimeFingerprintProvider.MapFingerprintTypeToSignal(type);
                    if (!string.IsNullOrEmpty(signal))
                    {
                        lock (_fingerprintSignalsByTab)
                        {
                            if (!_fingerprintSignalsByTab.TryGetValue(tab.Id, out var list))
                            {
                                list = new List<string>();
                                _fingerprintSignalsByTab[tab.Id] = list;
                            }
                            if (!list.Contains(signal))
                                list.Add(signal);
                        }
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
                else if (cat == "settings")
                {
                    ApplySettingsFromJson(e.WebMessageAsJson);
                    SaveSettings();
                    var tabCapture = tab;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { tabCapture.WebView?.CoreWebView2?.NavigateToString(GetSettingsHtml(showSavedMessage: true)); } catch { }
                    }));
                }
                else if (cat == "clearHistory")
                {
                    Dispatcher.Invoke(() =>
                    {
                        _recentUrls.Clear();
                        SaveRecent();
                        try { tab.WebView?.CoreWebView2?.NavigateToString(GetHistoryHtml()); } catch { }
                        if (StatusText != null) StatusText.Text = "History cleared.";
                    });
                }
                else if (cat == "memory")
                {
                    long bytes = 0;
                    if (root.TryGetProperty("bytes", out var b))
                    {
                        try { bytes = b.GetInt64(); } catch { }
                    }

                    tab.LastMemoryBytes = bytes > 0 ? bytes : null;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        string title = string.IsNullOrWhiteSpace(tab.Title) ? "Tab" : tab.Title;

                        if (bytes > 0)
                        {
                            double mb = bytes / (1024.0 * 1024.0);
                            string memLabel;
                            if (mb >= 1024)
                            {
                                double gb = mb / 1024.0;
                                memLabel = $"{gb:F2} GB";
                            }
                            else
                            {
                                memLabel = $"{mb:F1} MB";
                            }

                            // Two clean lines: title + memory
                            tab.TabHeader.ToolTip = $"{title}\nMemory: ~{memLabel}";
                        }
                        else
                        {
                            // Fall back to just the title – no noisy extra text
                            tab.TabHeader.ToolTip = title;
                        }
                    }));
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
            RefreshTaskManager();

            var tab = ActiveTab; if (tab == null) return;
            _lastDirtySetTicks = Environment.TickCount64;

            // Drain pending requests from all tabs (batched, reduces UI thread contention)
            foreach (var t in _tabs) t.DrainPending();

            // Update per-tab blocked badges even if sidebar is hidden
            foreach (var t in _tabs) UpdateTabBlockedBadge(t);

            // Update heavy-tab indicators across all tabs
            UpdateHeavyTabs();

            if (!_sidebarOpen) return; // skip heavy panel work if hidden

            var scan = BuildScanResult(tab);
            // Collect all signals across requests for aggregate analysis
            scan.AllSignals = tab.Requests.SelectMany(r => r.Signals).ToList();
            var score = PrivacyEngine.CalculateScore(scan);
            scan.Score = score; scan.GdprFindings = PrivacyEngine.MapToGdpr(scan);

            // One-line summary for everyone
            if (SimpleSummaryText != null) SimpleSummaryText.Text = GetSimpleSummary(score, tab);

            // Threat Tier banner
            UpdateTierBanner(score);

            // Shield badge
            ShieldGrade.Text = score.Grade;
            ShieldBadge.Background = ScoreBadgeBg(score.NumericScore);

            // Score banner: grade letter in grade color, ring colored by grade
            GradeText.Text = score.Grade;
            GradeText.Foreground = score.GradeColor ?? ScoreBadgeBg(score.NumericScore);
            ScoreNum.Text = $"{score.NumericScore} / 100";
            ScoreSummary.Text = _expertMode ? score.Summary : (score.NumericScore >= 80 ? "Good. Few trackers." : score.NumericScore >= 55 ? "Okay. Some other companies involved." : "Lots of tracking. You're protected.");
            ScoreChip.Text = $"Score {score.NumericScore}";
            TierChip.Text = score.TierLabel;
            // High-risk warning (F or very low score)
            bool highRisk = score.Grade == "F" || score.NumericScore < 40;
            if (HighRiskWarningBorder != null)
            {
                HighRiskWarningBorder.Visibility = highRisk ? Visibility.Visible : Visibility.Collapsed;
                if (HighRiskWarningText != null) HighRiskWarningText.Text = "High risk – consider leaving this site or avoid entering sensitive data.";
            }
            if (ReportPlainSummary != null) ReportPlainSummary.Text = GetReportPlainSummary(score, tab);
            UpdateScoreRing(score.NumericScore);
            try { var parent = ScoreBarFill.Parent as Grid; if (parent != null && parent.ActualWidth > 0) ScoreBarFill.Width = parent.ActualWidth * score.NumericScore / 100.0; else ScoreBarFill.Width = 0; } catch { }
            ScoreBarFill.Background = ScoreBadgeBg(score.NumericScore);

            // Category score bars
            UpdateCategoryBars(score.CategoryScores);

            // Stats
            int allTrackingCookies = PrivacyEngine.CountAllTrackingCookies(scan);
            int thirdCount = tab.Requests.Count(r => r.IsThirdParty);
            int trackerCount = tab.Requests.Count(r => !string.IsNullOrEmpty(r.TrackerLabel) && !r.IsBlocked);
            StatTotal.Text = tab.Requests.Count.ToString();
            StatBlocked.Text = tab.BlockedCount.ToString();
            StatThirdParty.Text = thirdCount.ToString();
            StatTrackers.Text = trackerCount.ToString();
            StatFingerprints.Text = tab.Fingerprints.Count.ToString();
            StatCookies.Text = allTrackingCookies.ToString();
            // Simple view: one line
            if (SimpleStatsText != null)
            {
                SimpleStatsText.Text = tab.Requests.Count == 0 ? "Load a page to see numbers." : string.Join("  ·  ", new List<string> { $"{tab.Requests.Count} connections", $"{tab.BlockedCount} blocked", $"{thirdCount} from other companies" }.Concat(trackerCount > 0 ? new[] { $"{trackerCount} trackers" } : Array.Empty<string>()));
            }

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

            // Top trackers (fewer in simple mode)
            int topN = _expertMode ? 8 : 5;
            var topT = tab.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerLabel)).GroupBy(r => r.TrackerLabel)
                .OrderByDescending(g => g.Count()).Take(topN).Select(g => {
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

            int liveN = _expertMode ? 18 : 8;
            LiveFeed.ItemsSource = liveItems.TakeLast(liveN).Reverse().Select(r => {
                string label; SolidColorBrush color;
                if (r.IsBlocked)
                {
                    if (_expertMode)
                    {
                        string shortLabel = ConfidenceShortLabel(r.BlockConfidence > 0 ? r.BlockConfidence : r.ThreatConfidence);
                        string baseLabel = r.BlockCategory == "Ad" ? "BLOCKED AD" : r.BlockCategory == "Behavioral" ? "BLOCKED BEHAVIOR" : "BLOCKED TRACKER";
                        label = shortLabel.Length > 0 ? $"{baseLabel} ({shortLabel})" : baseLabel;
                    }
                    else label = "Blocked";
                    color = new SolidColorBrush(Color.FromRgb(217, 48, 37));
                }
                else if (!string.IsNullOrEmpty(r.TrackerLabel))
                {
                    label = _expertMode ? "TRACKER" : "Tracker";
                    color = new SolidColorBrush(Color.FromRgb(217, 48, 37));
                }
                else if (r.IsThirdParty) { label = _expertMode ? "3RD" : "Other"; color = new SolidColorBrush(Color.FromRgb(227, 116, 0)); }
                else { label = _expertMode ? "1ST" : "This site"; color = new SolidColorBrush(Color.FromRgb(24, 128, 56)); }
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
            RequestList.ItemsSource = visible.Reverse().Take(300).Select(r => {
                string typeLabel = r.IsBlocked ? (_expertMode ? (r.BlockCategory == "Ad" ? "BLOCKED AD" : r.BlockCategory == "Behavioral" ? "BLOCKED BEHAVIOR" : "BLOCKED TRACKER") : "Blocked") :
                    !string.IsNullOrEmpty(r.TrackerLabel) ? (_expertMode ? "TRACKER" : "Tracker") : r.IsThirdParty ? (_expertMode ? "THIRD-PARTY" : "Other company") : (_expertMode ? "FIRST-PARTY" : "This site");
                return new RequestListItem {
                Host = r.Host, Path = r.Path.Length > 50 ? r.Path[..50] + "..." : r.Path, Method = r.Method,
                Status = r.IsBlocked ? "BLK" : r.StatusCode > 0 ? r.StatusCode.ToString() : "...",
                TypeLabel = typeLabel,
                TypeColor = r.IsBlocked ? new SolidColorBrush(Color.FromRgb(217, 48, 37)) :
                    !string.IsNullOrEmpty(r.TrackerLabel) ? new SolidColorBrush(Color.FromRgb(217, 48, 37)) :
                    r.IsThirdParty ? new SolidColorBrush(Color.FromRgb(227, 116, 0)) : new SolidColorBrush(Color.FromRgb(24, 128, 56)),
                ConfidenceLabel = r.IsBlocked ? ConfidenceShortLabel(r.BlockConfidence > 0 ? r.BlockConfidence : r.ThreatConfidence) : r.ThreatConfidence > 0 ? $"{r.ThreatConfidence:P0}" : "",
                ToolTip = BuildBlockedTooltip(r),
                Entry = r };
            }).ToList();
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
        private static string GetSimpleSummary(PrivacyScore score, BrowserTab tab)
        {
            if (tab.Requests.Count == 0) return "";
            if (score.NumericScore >= 80) return "This page is mostly calm. Few trackers.";
            if (score.NumericScore >= 55) return "This page talks to several other companies. Normal for many sites.";
            if (tab.BlockedCount > 0) return "Lots of tracking here — we blocked some so you're protected.";
            return "This page has a lot of tracking. Use protection or browse elsewhere if you prefer.";
        }

        private static string GetReportPlainSummary(PrivacyScore score, BrowserTab tab)
        {
            if (tab.Requests.Count == 0) return "Load a page to see a plain-language summary.";
            if (score.NumericScore >= 80) return "For humans: This site is okay for casual browsing; few trackers detected.";
            if (score.NumericScore >= 60) return "For humans: This site talks to several other companies; normal for many sites.";
            if (score.NumericScore >= 40) return "For humans: Significant tracking here; consider avoiding for sensitive tasks.";
            return "For humans: Very invasive – high tracking and fingerprinting. Consider leaving or avoiding sensitive data.";
        }

        private void UpdateTierBanner(PrivacyScore score)
        {
            if (_expertMode)
            {
                TierLabel.Text = score.TierLabel;
                TierDetail.Text = score.Tier switch
                {
                    ThreatTier.SurveillanceGrade => "Extensive tracking infrastructure with identity stitching, session replay, or data broker activity.",
                    ThreatTier.AggressiveTracking => "Multiple advertising trackers, fingerprinting, or session replay detected.",
                    ThreatTier.TypicalWebTracking => "Standard analytics and third-party tracking present.",
                    _ => "Minimal or no tracking detected. Basic web infrastructure only."
                };
            }
            else
            {
                TierLabel.Text = score.Tier switch
                {
                    ThreatTier.SurveillanceGrade => "Worth being aware",
                    ThreatTier.AggressiveTracking => "Lots of tracking",
                    ThreatTier.TypicalWebTracking => "Normal for many sites",
                    _ => "Mostly calm"
                };
                TierDetail.Text = score.Tier switch
                {
                    ThreatTier.SurveillanceGrade => "This page has extensive tracking. You're still in control.",
                    ThreatTier.AggressiveTracking => "Several ad or tracking services here. We blocked some.",
                    ThreatTier.TypicalWebTracking => "Some other companies are involved. Common on the web.",
                    _ => "Few or no trackers. This page is relatively private."
                };
            }
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
            SolidColorBrush gradeBrush = ScoreBadgeBg(score);
            ScoreRing.Stroke = gradeBrush;
            // Base ring tinted with grade color so the whole circle reads as that grade
            Color c = gradeBrush.Color;
            ScoreRingBase.Stroke = new SolidColorBrush(Color.FromArgb(0x4D, c.R, c.G, c.B)); // ~30% opacity
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
        private void Back_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab?.IsReady == true)
            {
                tab.LastActivityUtc = DateTime.UtcNow;
                tab.WebView.CoreWebView2.GoBack();
            }
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab?.IsReady == true)
            {
                tab.LastActivityUtc = DateTime.UtcNow;
                tab.WebView.CoreWebView2.GoForward();
            }
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab?.IsReady == true)
            {
                tab.LastActivityUtc = DateTime.UtcNow;
                tab.WebView.CoreWebView2.Reload();
            }
        }

        private void Go_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab != null)
                Navigate();
        }

        private void AddressBar_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                Navigate();
        }

        private void AddressBar_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
            => UpdateAddressBarPlaceholder();

        private void AddressBar_GotFocus(object sender, RoutedEventArgs e)
        {
            UpdateAddressBarPlaceholder();
            if (AddressBarBorder != null)
            {
                AddressBarBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(8, 145, 178));
                AddressBarBorder.BorderThickness = new Thickness(2);
                AddressBarBorder.Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 0, Opacity = 0.15, Color = Color.FromRgb(8, 145, 178) };
            }
        }

        private void AddressBar_LostFocus(object sender, RoutedEventArgs e)
        {
            UpdateAddressBarPlaceholder();
            if (AddressBarBorder != null)
            {
                AddressBarBorder.BorderBrush = new SolidColorBrush(_isDarkTheme ? Color.FromRgb(95, 99, 104) : Color.FromRgb(226, 232, 240));
                AddressBarBorder.BorderThickness = new Thickness(1);
                AddressBarBorder.Effect = null;
            }
        }

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
            if (tab?.IsReady != true) return;
            tab.LastActivityUtc = DateTime.UtcNow;
            tab.WebView.CoreWebView2.NavigateToString(GetWelcomeHtml());
        }

        private void Star_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            var url = tab?.Url?.Trim() ?? "";
            if (string.IsNullOrEmpty(url) || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase)) return;
            var title = (tab?.Title?.Trim() ?? "").Length > 0 ? tab!.Title.Trim() : new Uri(url).Host;
            var existing = _bookmarks.FirstOrDefault(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { _bookmarks.Remove(existing); SaveBookmarks(); if (StatusText != null) StatusText.Text = "Bookmark removed."; }
            else { _bookmarks.Add(new BookmarkEntry { Title = title, Url = url }); SaveBookmarks(); if (StatusText != null) StatusText.Text = "Bookmark added."; }
            UpdateStarButton();
        }

        private void CopyUrl_Click(object sender, RoutedEventArgs e)
        {
            var url = AddressBar?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(url)) return;
            try { Clipboard.SetText(url); if (StatusText != null) StatusText.Text = "URL copied."; } catch { }
        }

        private void Bookmarks_Click(object sender, RoutedEventArgs e)
        {
            if (BookmarksBtn != null) BookmarksPopup.PlacementTarget = BookmarksBtn;
            RefreshBookmarksList();
            BookmarksPopup.IsOpen = true;
        }

        private void MenuBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MenuBtn != null && AppMenu != null)
            {
                AppMenu.PlacementTarget = MenuBtn;
                AppMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                AppMenu.IsOpen = true;
            }
        }

        private async void MenuNewTab_Click(object sender, RoutedEventArgs e) => await CreateNewTab();

        private void MenuNewPrivateWindow_Click(object sender, RoutedEventArgs e)
        {
            var w = new MainWindow(isPrivate: true);
            w.Title = "Privacy Monitor (Private)";
            w.Show();
        }

        private void MenuBookmarks_Click(object sender, RoutedEventArgs e) => Bookmarks_Click(BookmarksBtn, e);

        private void MenuTaskManager_Click(object sender, RoutedEventArgs e)
        {
            if (TaskManagerPopup == null) return;
            if (MenuBtn != null)
            {
                TaskManagerPopup.PlacementTarget = MenuBtn;
            }
            RefreshTaskManager();
            TaskManagerPopup.IsOpen = true;

            // Kick off fresh memory sampling for all ready tabs
            foreach (var t in _tabs)
            {
                if (t.IsReady)
                    RequestTabMemoryAsync(t);
            }
        }

        private void MenuNetworkInterceptor_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab == null) return;
            var runtimeProvider = new NetworkInterceptor.TabScopedRuntimeFingerprintProvider(tab.Id, _fingerprintSignalsByTab);
            var service = new NetworkInterceptor.NetworkInterceptorService(null, runtimeProvider, null);
            service.AttachToTab(tab.Id);
            _interceptorSink = service;
            _interceptorService = service;
            _interceptorTabId = tab.Id;
            void FlushPaused() => FlushPausedInterceptorRequests();
            service.Resumed += FlushPaused;
            var win = new NetworkInterceptor.NetworkInterceptorWindow(service, tab.Id)
            {
                Owner = this
            };
            win.Closed += (_, _) =>
            {
                service.Resumed -= FlushPaused;
                _interceptorSink = null;
                _interceptorService = null;
                _interceptorTabId = null;
            };
            win.Show();
        }

        private void FlushPausedInterceptorRequests()
        {
            List<(CoreWebView2Deferral Deferral, RequestEntry Entry, BrowserTab Tab)> copy;
            lock (_pausedInterceptorLock)
            {
                if (_pausedInterceptorDeferred.Count == 0) return;
                copy = new List<(CoreWebView2Deferral, RequestEntry, BrowserTab)>(_pausedInterceptorDeferred);
                _pausedInterceptorDeferred.Clear();
            }
            if (_interceptorService != null)
                _interceptorService.SetPausedPendingCount(0);
            foreach (var (deferral, entry, tab) in copy)
            {
                try
                {
                    tab.PendingRequests.Enqueue(entry);
                    _interceptorSink?.RecordRequest(tab.Id, entry);
                    deferral.Complete();
                }
                catch (Exception ex)
                {
                    try { deferral.Complete(); } catch { }
                    System.Diagnostics.Debug.WriteLine($"FlushPausedInterceptorRequests: {ex.Message}");
                }
            }
        }

        private void MenuHistory_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab?.IsReady != true) return;
            try { tab.WebView.CoreWebView2.NavigateToString(GetHistoryHtml()); } catch { }
        }

        private void MenuClearBrowsingHistory_Click(object sender, RoutedEventArgs e)
        {
            _recentUrls.Clear();
            SaveRecent();
            var tab = ActiveTab;
            if (tab?.IsReady == true)
            {
                try { tab.WebView.CoreWebView2.NavigateToString(GetHistoryHtml()); } catch { }
            }
            if (StatusText != null) StatusText.Text = "Browsing history cleared.";
        }

        private void MenuClearSiteData_Click(object sender, RoutedEventArgs e) => ClearSiteData_Click(sender, e);
        private void MenuAllowThisSite_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab?.IsReady != true || string.IsNullOrEmpty(tab.CurrentHost)) { if (StatusText != null) StatusText.Text = "Open a site first."; return; }
            ProtectionEngine.SetMode(tab.CurrentHost, ProtectionMode.Monitor, GetProfileStore());
            SetProtectionMenuChecked(ProtectionMode.Monitor);
            if (StatusText != null) StatusText.Text = $"Stopped blocking on {tab.CurrentHost}. Reload the page to apply.";
        }
        private void MenuOpenDownloads_Click(object sender, RoutedEventArgs e)
        {
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (!Directory.Exists(downloads)) Directory.CreateDirectory(downloads);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(downloads) { UseShellExecute = true }); } catch { }
        }
        private void MenuZoomIn_Click(object sender, RoutedEventArgs e) { var tab = ActiveTab; if (tab?.IsReady == true) tab.WebView.ZoomFactor = Math.Min(3.0, tab.WebView.ZoomFactor + 0.25); }
        private void MenuZoomOut_Click(object sender, RoutedEventArgs e) { var tab = ActiveTab; if (tab?.IsReady == true) tab.WebView.ZoomFactor = Math.Max(0.25, tab.WebView.ZoomFactor - 0.25); }
        private void MenuZoomReset_Click(object sender, RoutedEventArgs e) { var tab = ActiveTab; if (tab?.IsReady == true) tab.WebView.ZoomFactor = 1.0; }
        private void MenuShortcuts_Click(object sender, RoutedEventArgs e) { var tab = ActiveTab; if (tab?.IsReady == true) try { tab.WebView.CoreWebView2.NavigateToString(GetShortcutsHtml()); } catch { } }
        private void MenuSettings_Click(object sender, RoutedEventArgs e) { var tab = ActiveTab; if (tab?.IsReady == true) try { tab.WebView.CoreWebView2.NavigateToString(GetSettingsHtml()); } catch { } }
        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            string version = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.0.0";
            const string github = "https://github.com/NullSec8/PrivacyMonitor";
            string msg = $"Privacy Monitor\nVersion {version}\n\nAgjencia per Informim dhe Privatesi. Built for Windows.\n\n" +
                "All data stays on your PC. No telemetry.\n\n" +
                "Open source: " + github;
            MessageBox.Show(this, msg, "About Privacy Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void MenuCheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            string current = UpdateService.CurrentVersion;
            try
            {
                var (latestInfo, errorMessage) = await UpdateService.GetLatestWithErrorAsync().ConfigureAwait(true);
                if (latestInfo == null)
                {
                    var msg = string.IsNullOrEmpty(errorMessage)
                        ? "Could not reach the update server. Make sure you're online and that the server is available."
                        : "Could not reach the update server.\n\nReason: " + errorMessage;
                    msg += "\n\nTip: If you see 'timed out' or 'blocked', allow PrivacyMonitor.exe through Windows Firewall (outbound to the internet).";
                    MessageBox.Show(this, msg, "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                string latest = latestInfo.Version;
                if (UpdateService.CompareVersions(current, latest) < 0)
                {
                    var result = MessageBox.Show(this,
                        $"A new version ({latest}) is available.\n\nCurrent: {current}\n\nDownload and restart now to update?",
                        "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
                    if (result != MessageBoxResult.Yes) return;
                    await DownloadAndApplyUpdateAsync(latest).ConfigureAwait(true);
                }
                else
                    MessageBox.Show(this, $"You're up to date.\nVersion {current}", "Check for updates", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not check for updates. The update server may be unreachable.\n\n" + ex.Message, "Check for updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async System.Threading.Tasks.Task DownloadAndApplyUpdateAsync(string version)
        {
            var progress = new Progress<double>(p => { });
            Dispatcher.Invoke(() => { if (StatusText != null) StatusText.Text = "Downloading update..."; });
            var (extractDir, errorMessage) = await UpdateService.DownloadUpdateAsync(version, progress).ConfigureAwait(true);
            if (string.IsNullOrEmpty(extractDir))
            {
                Dispatcher.Invoke(() =>
                {
                    if (StatusText != null) StatusText.Text = "";
                    var msg = string.IsNullOrEmpty(errorMessage) ? "Update download failed." : "Update download failed.\n\n" + errorMessage;
                    MessageBox.Show(this, msg, "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                return;
            }
            Dispatcher.Invoke(() =>
            {
                if (StatusText != null) StatusText.Text = "Restarting to apply update...";
                UpdateService.ApplyUpdateAndRestart(extractDir);
                _ = UpdateService.LogInstallAsync(version);
                Application.Current.Shutdown(0);
            });
        }

        private void RefreshBookmarksList()
        {
            if (BookmarksListBox == null) return;
            BookmarksListBox.ItemsSource = _bookmarks
                .OrderByDescending(b => b.Added)
                .Select(b => new { Display = b.Title.Length > 0 ? b.Title : b.Url, b.Url })
                .ToList();
        }

        private void OpenSelectedBookmark(object? sourceItem = null)
        {
            if (BookmarksListBox == null) return;
            var item = sourceItem ?? BookmarksListBox.SelectedItem;
            if (item == null) return;
            var urlProp = item.GetType().GetProperty("Url");
            var url = urlProp?.GetValue(item)?.ToString() ?? "";
            if (string.IsNullOrEmpty(url)) return;
            BookmarksPopup.IsOpen = false;
            var tab = ActiveTab;
            if (tab?.IsReady == true)
            {
                try { tab.WebView.CoreWebView2.Navigate(url); } catch { }
            }
        }

        private void RemoveSelectedBookmark(object? sourceItem = null)
        {
            if (BookmarksListBox == null) return;
            var item = sourceItem ?? BookmarksListBox.SelectedItem;
            if (item == null) return;
            var urlProp = item.GetType().GetProperty("Url");
            var url = urlProp?.GetValue(item)?.ToString() ?? "";
            if (string.IsNullOrEmpty(url)) return;

            var existing = _bookmarks.FirstOrDefault(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
            if (existing == null) return;

            _bookmarks.Remove(existing);
            SaveBookmarks();
            RefreshBookmarksList();
            UpdateStarButton();
            if (StatusText != null) StatusText.Text = "Bookmark removed.";
        }

        private void BookmarksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Keep selection in sync; open/delete is handled via Enter, Delete, or context menu.
        }

        private void BookmarksList_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OpenSelectedBookmark();
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                RemoveSelectedBookmark();
                e.Handled = true;
            }
        }

        private void BookmarksOpen_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext != null)
                OpenSelectedBookmark(fe.DataContext);
            else
                OpenSelectedBookmark();
        }

        private void BookmarksRemove_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext != null)
                RemoveSelectedBookmark(fe.DataContext);
            else
                RemoveSelectedBookmark();
        }

        private void UpdateStarButton()
        {
            if (StarBtn == null) return;
            var tab = ActiveTab;
            var url = tab?.Url?.Trim() ?? "";
            bool isBookmarked = !string.IsNullOrEmpty(url) && _bookmarks.Any(b => string.Equals(b.Url, url, StringComparison.OrdinalIgnoreCase));
            StarBtn.Content = isBookmarked ? "\u2605" : "\u2606"; // ★ / ☆
            StarBtn.ToolTip = isBookmarked ? "Remove bookmark" : "Bookmark this page";
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
            var tab = ActiveTab;
            if (tab == null)
            {
                if (StatusText != null) StatusText.Text = "No tab — try opening a new tab.";
                return;
            }
            if (!tab.IsReady)
            {
                if (StatusText != null) StatusText.Text = "Browser is still loading… try again in a moment.";
                return;
            }
            var input = AddressBar?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(input))
            {
                if (StatusText != null) StatusText.Text = "Type a search or URL above.";
                return;
            }

            string url;
            string searchUrl = !string.IsNullOrWhiteSpace(_settings.SearchEngineUrl) ? _settings.SearchEngineUrl : "https://duckduckgo.com/?q=";
            if (LooksLikeUrl(input))
                url = input.Contains("://") ? input : "https://" + input;
            else
                url = searchUrl + Uri.EscapeDataString(input);
            try
            {
                tab.LastActivityUtc = DateTime.UtcNow;
                tab.WebView.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                if (StatusText != null) StatusText.Text = "Navigation failed: " + ex.Message;
            }
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
                    case Key.H: MenuHistory_Click(this, e); e.Handled = true; break;
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
                    case Key.D1: case Key.NumPad1: SwitchToTabByIndex(0); e.Handled = true; break;
                    case Key.D2: case Key.NumPad2: SwitchToTabByIndex(1); e.Handled = true; break;
                    case Key.D3: case Key.NumPad3: SwitchToTabByIndex(2); e.Handled = true; break;
                    case Key.D4: case Key.NumPad4: SwitchToTabByIndex(3); e.Handled = true; break;
                    case Key.D5: case Key.NumPad5: SwitchToTabByIndex(4); e.Handled = true; break;
                    case Key.D6: case Key.NumPad6: SwitchToTabByIndex(5); e.Handled = true; break;
                    case Key.D7: case Key.NumPad7: SwitchToTabByIndex(6); e.Handled = true; break;
                    case Key.D8: case Key.NumPad8: SwitchToTabByIndex(7); e.Handled = true; break;
                }
            }
            if (e.Key == Key.Enter && AddressBar != null && AddressBar.IsFocused)
            {
                Navigate();
                e.Handled = true;
            }
            if (e.Key == Key.F5) { Reload_Click(this, new RoutedEventArgs()); e.Handled = true; }
            if (e.Key == Key.Escape)
            {
                var t = ActiveTab;
                if (t?.IsLoading == true && t.WebView?.CoreWebView2 != null) { try { t.WebView.CoreWebView2.Stop(); } catch { } e.Handled = true; }
            }
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

        private async void ClearSiteData_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab?.IsReady != true || string.IsNullOrEmpty(tab.CurrentHost)) return;
            try
            {
                const string clearScript = @"
(function(){
 try { if (typeof localStorage !== 'undefined') localStorage.clear(); } catch(e){}
 try { if (typeof sessionStorage !== 'undefined') sessionStorage.clear(); } catch(e){}
 try {
   var c = document.cookie.split(';');
   for (var i = 0; i < c.length; i++) {
     var name = c[i].split('=')[0].trim();
     document.cookie = name + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/';
     document.cookie = name + '=;expires=Thu, 01 Jan 1970 00:00:00 GMT;path=/;domain=' + window.location.hostname;
   }
 } catch(e){}
})();
";
                await tab.WebView.CoreWebView2.ExecuteScriptAsync(clearScript);
                tab.Cookies.Clear();
                tab.Storage.Clear();
                try { await tab.WebView.CoreWebView2.ExecuteScriptAsync(PrivacyEngine.StorageEnumerationScript); } catch { }
                _dirty = true;
                if (StatusText != null) StatusText.Text = "Cleared cookies and storage for this site.";
            }
            catch (Exception ex)
            {
                if (StatusText != null) StatusText.Text = "Clear failed: " + ex.Message;
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
