/**
 * Popup – all data from extension storage or current tab; no analytics.
 * Network: only optional IP fetch when user opted in (Options) and Privacy mode is off;
 * uses api.ipify.org. IP is never stored; shown in UI only.
 */
(function() {
  const modeOff = document.getElementById('modeOff');
  const modeBlockKnown = document.getElementById('modeBlockKnown');
  const modeAggressive = document.getElementById('modeAggressive');
  const knownCountEl = document.getElementById('knownCount');
  const aggressiveCountEl = document.getElementById('aggressiveCount');
  const analysisLoading = document.getElementById('analysisLoading');
  const analysisResult = document.getElementById('analysisResult');
  const analysisError = document.getElementById('analysisError');
  const blockedCountEl = document.getElementById('blockedCount');
  const blockedListEl = document.getElementById('blockedList');
  const refreshLink = document.getElementById('refreshLink');
  const optionsLink = document.getElementById('optionsLink');
  const ipValueEl = document.getElementById('ipValue');
  const ipRefreshBtn = document.getElementById('ipRefresh');
  const ipStatusEl = document.getElementById('ipStatus');
  const blockClickHijackingCb = document.getElementById('blockClickHijacking');

  const IPIFY_URL = 'https://api.ipify.org?format=json';
  const IPIFY_ORIGIN = 'https://api.ipify.org/';
  const IP_TIMEOUT_MS = 8000;

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
        if (ip) {
          setIpState(false, ip, '', false);
        } else {
          setIpState(false, '—', 'Could not get IP. Try again.', true);
        }
      })
      .catch(function(err) {
        clearTimeout(timeoutId);
        var msg = (err && err.name === 'AbortError')
          ? 'Request timed out. Check your connection.'
          : 'Unable to fetch IP. No internet or service unavailable.';
        setIpState(false, '—', msg, true);
      });
  }

  function initIpSection() {
    chrome.runtime.sendMessage({ action: 'getPrivacyPrefs' }, function(prefs) {
      if (chrome.runtime.lastError || !prefs) {
        setIpState(false, '—', 'Disabled. Enable in Options if desired.', false);
        return;
      }
      if (!prefs.showPublicIp) {
        setIpState(false, '—', 'Enable "Show public IP" in Options to display it here.', false);
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
    chrome.runtime.sendMessage({ action: 'setMode', mode }, function(res) {
      if (chrome.runtime.lastError) {
        showError('Extension error. Try reloading the extension.');
        return;
      }
      runAnalysis();
    });
  }

  function showError(msg) {
    analysisLoading.hidden = true;
    analysisResult.hidden = true;
    analysisError.hidden = false;
    analysisError.textContent = msg;
  }

  function showAnalysis(blocked, count, error) {
    analysisLoading.hidden = true;
    analysisResult.hidden = !(blocked !== null && blocked !== undefined);
    analysisError.hidden = !error;
    if (blocked !== null && blocked !== undefined) {
      blockedCountEl.textContent = count;
      blockedListEl.innerHTML = '';
      (blocked || []).slice(0, 30).forEach(function(host) {
        const d = document.createElement('div');
        d.textContent = host;
        blockedListEl.appendChild(d);
      });
      if (blocked.length > 30) {
        const d = document.createElement('div');
        d.textContent = '\u2026 +' + (blocked.length - 30) + ' more';
        d.style.color = '#6b7280';
        blockedListEl.appendChild(d);
      }
    }
    if (error) analysisError.textContent = error;
  }

  function runAnalysis() {
    analysisLoading.hidden = false;
    analysisResult.hidden = true;
    analysisError.hidden = true;
    chrome.tabs.query({ active: true, currentWindow: true }, function(tabs) {
      const tab = tabs && tabs[0];
      if (!tab || !tab.id) {
        showAnalysis([], 0, null);
        analysisError.hidden = false;
        analysisError.textContent = 'No active tab.';
        return;
      }
      if (tab.url && (tab.url.startsWith('chrome://') || tab.url.startsWith('edge://') || tab.url.startsWith('about:'))) {
        showAnalysis([], 0, null);
        analysisError.hidden = false;
        analysisError.textContent = 'Not available on this page.';
        return;
      }
      chrome.runtime.sendMessage({ action: 'analyzeTab', tabId: tab.id }, function(res) {
        if (chrome.runtime.lastError) {
          showError('Extension not ready. Reload the extension and try again.');
          return;
        }
        if (!res) {
          showError('No response. Reload the extension.');
          return;
        }
        if (res.error) {
          showAnalysis([], 0, null);
          analysisError.hidden = false;
          analysisError.textContent = res.error;
          return;
        }
        if (res.mode === 'off') {
          showAnalysis([], 0, null);
          analysisResult.hidden = false;
          var countEl = analysisResult.querySelector('.analysis-count');
          if (countEl) countEl.textContent = 'Blocking is Off. Enable Block known or Aggressive.';
          return;
        }
        showAnalysis(res.blocked || [], res.count || 0, null);
      });
    });
  }

  function loadState(retries) {
    retries = retries || 0;
    chrome.runtime.sendMessage({ action: 'getState' }, function(state) {
      if (chrome.runtime.lastError) {
        if (retries < 2) setTimeout(function() { loadState(retries + 1); }, 300);
        else {
          knownCountEl.textContent = '?';
          aggressiveCountEl.textContent = '?';
        }
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
