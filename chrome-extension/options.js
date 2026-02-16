/**
 * Options page – privacy-focused.
 * Data flow: reads/writes only chrome.storage.local. No external requests.
 * Clear data: clears local storage, resets to defaults via background.
 */

(function() {
  'use strict';

  var STORAGE_KEY_SHOW_IP = 'showPublicIp';
  var STORAGE_KEY_PRIVACY_MODE = 'privacyMode';
  var STORAGE_KEY_BLOCK_CLICK_HIJACKING = 'blockClickHijacking';
  var BLOCK_LOG_KEY = 'privacyMonitorBlockLog';
  var IPIFY_ORIGIN = 'https://api.ipify.org/';

  var privacyModeCb = document.getElementById('privacyMode');
  var showPublicIpCb = document.getElementById('showPublicIp');
  var blockClickHijackingCb = document.getElementById('blockClickHijacking');
  var clearDataBtn = document.getElementById('clearDataBtn');
  var clearStatus = document.getElementById('clearStatus');
  var blockLogEl = document.getElementById('blockLog');
  var clearBlockLogBtn = document.getElementById('clearBlockLogBtn');

  function setStatus(msg, isError) {
    clearStatus.textContent = msg || '';
    clearStatus.className = 'clear-status' + (msg ? (isError ? ' error' : ' success') : '');
  }

  function load() {
    chrome.storage.local.get([STORAGE_KEY_SHOW_IP, STORAGE_KEY_PRIVACY_MODE, STORAGE_KEY_BLOCK_CLICK_HIJACKING], function(o) {
      showPublicIpCb.checked = !!o[STORAGE_KEY_SHOW_IP];
      privacyModeCb.checked = o[STORAGE_KEY_PRIVACY_MODE] !== false;
      blockClickHijackingCb.checked = o[STORAGE_KEY_BLOCK_CLICK_HIJACKING] !== false;
    });
    loadBlockLog();
  }

  function loadBlockLog() {
    chrome.storage.local.get(BLOCK_LOG_KEY, function(o) {
      var log = Array.isArray(o[BLOCK_LOG_KEY]) ? o[BLOCK_LOG_KEY] : [];
      blockLogEl.innerHTML = '';
      blockLogEl.classList.toggle('empty', log.length === 0);
      if (log.length === 0) {
        blockLogEl.textContent = 'No blocked redirects logged.';
      } else {
        log.slice().reverse().forEach(function(entry) {
          var d = document.createElement('div');
          d.textContent = new Date(entry.time).toLocaleString() + ' – ' + (entry.type || '') + (entry.url ? ' ' + entry.url : '');
          blockLogEl.appendChild(d);
        });
      }
    });
  }

  blockClickHijackingCb.addEventListener('change', function() {
    chrome.storage.local.set({ [STORAGE_KEY_BLOCK_CLICK_HIJACKING]: blockClickHijackingCb.checked });
  });

  privacyModeCb.addEventListener('change', function() {
    chrome.storage.local.set({ [STORAGE_KEY_PRIVACY_MODE]: privacyModeCb.checked });
  });

  showPublicIpCb.addEventListener('change', function() {
    var enabled = showPublicIpCb.checked;
    if (enabled) {
      chrome.permissions.request({ origins: [IPIFY_ORIGIN] }, function(granted) {
        chrome.storage.local.set({ [STORAGE_KEY_SHOW_IP]: granted });
        if (!granted) showPublicIpCb.checked = false;
      });
    } else {
      chrome.storage.local.set({ [STORAGE_KEY_SHOW_IP]: false });
    }
  });

  clearBlockLogBtn.addEventListener('click', function() {
    chrome.storage.local.remove(BLOCK_LOG_KEY, function() {
      loadBlockLog();
    });
  });

  clearDataBtn.addEventListener('click', function() {
    clearDataBtn.disabled = true;
    setStatus('');
    chrome.runtime.sendMessage({ action: 'clearData' }, function(res) {
      clearDataBtn.disabled = false;
      if (chrome.runtime.lastError) {
        setStatus('Failed: ' + chrome.runtime.lastError.message, true);
        return;
      }
      if (res && res.ok) {
        setStatus('All local data cleared. Defaults restored.');
        load();
        loadBlockLog();
      } else {
        setStatus((res && res.error) ? res.error : 'Failed', true);
      }
    });
  });

  load();
})();
