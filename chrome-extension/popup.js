/**
 * Popup – all data from extension storage or current tab; no analytics.
 * Network: only optional IP fetch when user opted in (Options);
 * uses api.ipify.org. IP is never stored; shown in UI only.
 */
(function() {
  var modeOff = document.getElementById('modeOff');
  var modeBlockKnown = document.getElementById('modeBlockKnown');
  var modeAggressive = document.getElementById('modeAggressive');
  var knownCountEl = document.getElementById('knownCount');
  var aggressiveCountEl = document.getElementById('aggressiveCount');
  var analysisLoading = document.getElementById('analysisLoading');
  var analysisResult = document.getElementById('analysisResult');
  var analysisError = document.getElementById('analysisError');
  var blockedCountEl = document.getElementById('blockedCount');
  var blockedListEl = document.getElementById('blockedList');
  var totalScriptsEl = document.getElementById('totalScripts');
  var totalIframesEl = document.getElementById('totalIframes');
  var refreshLink = document.getElementById('refreshLink');
  var optionsLink = document.getElementById('optionsLink');
  var ipValueEl = document.getElementById('ipValue');
  var ipRefreshBtn = document.getElementById('ipRefresh');
  var ipStatusEl = document.getElementById('ipStatus');
  var blockClickHijackingCb = document.getElementById('blockClickHijacking');

  var IPIFY_URL = 'https://api.ipify.org?format=json';
  var IP_TIMEOUT_MS = 8000;

  function setIpState(loading, value, statusText, isError) {
    ipRefreshBtn.disabled = loading;
    ipValueEl.textContent = value;
    ipStatusEl.hidden = !statusText;
    ipStatusEl.textContent = statusText || '';
    ipStatusEl.classList.toggle('error', !!isError);
  }

  function fetchPublicIp() {
    setIpState(true, '…', '', false);
    var controller = new AbortController();
    var timeoutId = setTimeout(function() { controller.abort(); }, IP_TIMEOUT_MS);
    fetch(IPIFY_URL, { method: 'GET', signal: controller.signal })
      .then(function(res) {
        clearTimeout(timeoutId);
        if (!res.ok) throw new Error('API error');
        return res.json();
      })
      .then(function(data) {
        var ip = (data && data.ip) ? String(data.ip).trim() : '';
        setIpState(false, ip || '—', ip ? '' : 'Could not get IP.', !ip);
      })
      .catch(function(err) {
        clearTimeout(timeoutId);
        var msg = (err && err.name === 'AbortError')
          ? 'Timed out.'
          : 'Unavailable.';
        setIpState(false, '—', msg, true);
      });
  }

  function initIpSection() {
    chrome.runtime.sendMessage({ action: 'getPrivacyPrefs' }, function(prefs) {
      if (chrome.runtime.lastError || !prefs) {
        setIpState(false, '—', 'Disabled.', false);
        return;
      }
      if (!prefs.showPublicIp) {
        setIpState(false, '—', 'Enable in Options.', false);
        ipRefreshBtn.onclick = function() { chrome.runtime.openOptionsPage(); };
        return;
      }
      ipRefreshBtn.onclick = function() { fetchPublicIp(); };
      fetchPublicIp();
    });
  }

  ipRefreshBtn.addEventListener('click', function(e) { e.preventDefault(); });
  initIpSection();

  function setMode(mode) {
    chrome.runtime.sendMessage({ action: 'setMode', mode: mode }, function() {
      if (chrome.runtime.lastError) return;
      runAnalysis();
    });
  }

  function showError(msg) {
    analysisLoading.hidden = true;
    analysisResult.hidden = true;
    analysisError.hidden = false;
    analysisError.textContent = msg;
  }

  function showAnalysis(blocked, count, extra) {
    analysisLoading.hidden = true;
    analysisError.hidden = true;
    analysisResult.hidden = false;
    blockedCountEl.textContent = count;
    totalScriptsEl.textContent = (extra && extra.scripts >= 0) ? extra.scripts : '-';
    totalIframesEl.textContent = (extra && extra.iframes >= 0) ? extra.iframes : '-';
    blockedListEl.innerHTML = '';
    (blocked || []).slice(0, 40).forEach(function(host) {
      var d = document.createElement('div');
      d.textContent = host;
      blockedListEl.appendChild(d);
    });
    if (blocked && blocked.length > 40) {
      var d = document.createElement('div');
      d.textContent = '… +' + (blocked.length - 40) + ' more';
      d.style.opacity = '0.6';
      blockedListEl.appendChild(d);
    }
  }

  function runAnalysis() {
    analysisLoading.hidden = false;
    analysisResult.hidden = true;
    analysisError.hidden = true;
    chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
      var tab = tabs && tabs[0];
      if (!tab || !tab.id) { showError('No active tab.'); return; }
      if (tab.url && (tab.url.startsWith('chrome://') || tab.url.startsWith('edge://') || tab.url.startsWith('about:'))) {
        showError('Not available on this page.');
        return;
      }
      chrome.runtime.sendMessage({ action: 'analyzeTab', tabId: tab.id }, function(res) {
        if (chrome.runtime.lastError || !res) { showError('Extension not ready.'); return; }
        if (res.error) { showError(res.error); return; }
        if (res.mode === 'off') {
          showAnalysis([], 0, res.extra);
          var statsDiv = analysisResult.querySelector('.analysis-stats');
          if (statsDiv) statsDiv.insertAdjacentHTML('afterend', '<div style="font-size:11px;opacity:0.6;margin-top:6px">Blocking is off.</div>');
          return;
        }
        showAnalysis(res.blocked || [], res.count || 0, res.extra);
      });
    });
  }

  function loadState(retries) {
    retries = retries || 0;
    chrome.runtime.sendMessage({ action: 'getState' }, function(state) {
      if (chrome.runtime.lastError) {
        if (retries < 2) setTimeout(function() { loadState(retries + 1); }, 300);
        return;
      }
      if (state) {
        knownCountEl.textContent = state.knownCount || '0';
        aggressiveCountEl.textContent = state.aggressiveCount || state.knownCount || '0';
        var mode = state.mode || 'blockKnown';
        modeOff.checked = mode === 'off';
        modeBlockKnown.checked = mode === 'blockKnown';
        modeAggressive.checked = mode === 'aggressive';
        blockClickHijackingCb.checked = state.blockClickHijacking !== false;
      }
    });
  }

  loadState();

  modeOff.addEventListener('change', function() { if (modeOff.checked) setMode('off'); });
  modeBlockKnown.addEventListener('change', function() { if (modeBlockKnown.checked) setMode('blockKnown'); });
  modeAggressive.addEventListener('change', function() { if (modeAggressive.checked) setMode('aggressive'); });
  blockClickHijackingCb.addEventListener('change', function() {
    chrome.storage.local.set({ blockClickHijacking: blockClickHijackingCb.checked });
  });

  refreshLink.addEventListener('click', function(e) { e.preventDefault(); runAnalysis(); });
  optionsLink.href = chrome.runtime.getURL('options.html');
  optionsLink.addEventListener('click', function(e) { e.preventDefault(); chrome.runtime.openOptionsPage(); });

  runAnalysis();
})();
