/**
 * Privacy Monitor – Background service worker
 *
 * DATA FLOWS (all local; no telemetry, no external send):
 * - Storage: chrome.storage.local only. Keys: privacyMonitorMode, privacyMonitorBlocklistHosts,
 *   showPublicIp, privacyMode. No sync storage; no data sent to any server.
 * - Tab analysis: Receives tabId from popup; runs executeScript on that tab to read resource URLs
 *   (script/img/iframe/link). Used only in memory to compute blocked host list; result sent back to
 *   popup. No logging, no history, no persistence of URLs or behavior.
 * - No analytics, fingerprinting, or hidden tracking. Network: none from background.
 */

try {
  importScripts('tracker-domains.js');
} catch (e) {
  console.warn('Privacy Monitor: tracker-domains.js failed', e);
  self.BLOCK_KNOWN_DOMAINS = [];
  self.AGGRESSIVE_EXTRA_DOMAINS = [];
  self.getDomainsForMode = function() { return []; };
}

const RULESET_IDS = ['blocklist', 'stealth', 'videoads'];
const STORAGE_KEY_MODE = 'privacyMonitorMode';
const STORAGE_KEY_SHOW_IP = 'showPublicIp';
const STORAGE_KEY_PRIVACY_MODE = 'privacyMode';
const STORAGE_KEY_BLOCK_CLICK_HIJACKING = 'blockClickHijacking';

function getDomainsForMode(mode) {
  if (typeof self.getDomainsForMode === 'function')
    return self.getDomainsForMode(mode);
  return self.BLOCK_KNOWN_DOMAINS || [];
}

async function getMode() {
  var o = await chrome.storage.local.get(STORAGE_KEY_MODE);
  var v = o[STORAGE_KEY_MODE];
  if (v === 'blockKnown' || v === 'aggressive') return v;
  if (v === 'off') return 'off';
  return 'blockKnown';
}

async function setMode(mode) {
  await chrome.storage.local.set({ [STORAGE_KEY_MODE]: mode });
  await syncRuleset();
  await updateBadge();
}

async function syncRuleset() {
  var mode = await getMode();
  var enable = (mode === 'blockKnown' || mode === 'aggressive');
  try {
    if (enable) {
      await chrome.declarativeNetRequest.updateEnabledRulesets({ enableRulesetIds: RULESET_IDS });
      var hosts = getDomainsForMode(mode);
      await chrome.storage.local.set({ privacyMonitorBlocklistHosts: hosts });
    } else {
      await chrome.declarativeNetRequest.updateEnabledRulesets({ disableRulesetIds: RULESET_IDS });
      await chrome.storage.local.set({ privacyMonitorBlocklistHosts: [] });
    }
  } catch (e) {
    console.error('Privacy Monitor: updateEnabledRulesets failed', e);
  }
}

async function updateBadge() {
  var mode = await getMode();
  var text = mode === 'off' ? 'OFF' : mode === 'aggressive' ? '2' : '1';
  var color = mode === 'off' ? '#6B7280' : mode === 'aggressive' ? '#0D9488' : '#0891B2';
  try {
    await chrome.action.setBadgeText({ text: text });
    await chrome.action.setBadgeBackgroundColor({ color: color });
  } catch (e) {}
}

chrome.runtime.onInstalled.addListener(async function() {
  await syncRuleset();
  await updateBadge();
});

chrome.runtime.onStartup.addListener(function() {
  updateBadge();
});

function hostMatchesBlockList(host, blockList) {
  if (!host || !blockList || !blockList.length) return false;
  host = host.toLowerCase();
  for (var i = 0; i < blockList.length; i++) {
    var d = blockList[i];
    if (host === d || host.endsWith('.' + d)) return true;
  }
  return false;
}

chrome.runtime.onMessage.addListener(function(msg, _sender, sendResponse) {
  if (msg.action === 'getState') {
    getMode().then(function(mode) {
      chrome.storage.local.get(STORAGE_KEY_BLOCK_CLICK_HIJACKING, function(o) {
        var known = (self.BLOCK_KNOWN_DOMAINS || []).length;
        var extra = (self.AGGRESSIVE_EXTRA_DOMAINS || []).length;
        sendResponse({
          mode: mode,
          knownCount: known,
          aggressiveCount: known + extra,
          blockClickHijacking: o[STORAGE_KEY_BLOCK_CLICK_HIJACKING] !== false
        });
      });
    });
    return true;
  }
  if (msg.action === 'setMode') {
    setMode(msg.mode).then(function() { sendResponse({ ok: true }); }).catch(function(e) { sendResponse({ ok: false, error: e.message }); });
    return true;
  }
  if (msg.action === 'getPrivacyPrefs') {
    chrome.storage.local.get([STORAGE_KEY_SHOW_IP, STORAGE_KEY_PRIVACY_MODE], function(o) {
      sendResponse({
        showPublicIp: o[STORAGE_KEY_SHOW_IP] !== false,
        privacyMode: o[STORAGE_KEY_PRIVACY_MODE] !== false
      });
    });
    return true;
  }
  if (msg.action === 'clearData') {
    (async function() {
      try {
        await chrome.storage.local.clear();
        await chrome.storage.local.set({
          [STORAGE_KEY_MODE]: 'blockKnown',
          [STORAGE_KEY_SHOW_IP]: false,
          [STORAGE_KEY_PRIVACY_MODE]: true,
          [STORAGE_KEY_BLOCK_CLICK_HIJACKING]: true
        });
        await syncRuleset();
        await updateBadge();
        sendResponse({ ok: true });
      } catch (e) {
        sendResponse({ ok: false, error: e.message });
      }
    })();
    return true;
  }
  if (msg.action === 'analyzeTab') {
    (async function() {
      var mode = await getMode();
      var blockList = getDomainsForMode(mode);
      var tabId = msg.tabId;
      if (!tabId || mode === 'off' || !blockList.length) {
        sendResponse({ blocked: [], count: 0, mode: mode });
        return;
      }
      try {
        var results = await chrome.scripting.executeScript({
          target: { tabId: tabId },
          func: function() {
            var out = [];
            var add = function(url) { if (url && url.startsWith('http')) out.push(url); };
            document.querySelectorAll('script[src]').forEach(function(s) { add(s.src); });
            document.querySelectorAll('img[src]').forEach(function(s) { add(s.src); });
            document.querySelectorAll('iframe[src]').forEach(function(s) { add(s.src); });
            document.querySelectorAll('link[href]').forEach(function(l) {
              var h = l.getAttribute('href');
              if (h && (l.rel === 'stylesheet' || l.rel === 'preload' || l.rel === 'script')) add(h);
            });
            return out;
          }
        });
        var urls = (results && results[0] && results[0].result) ? results[0].result : [];
        var blocked = [];
        var seen = new Set();
        for (var i = 0; i < urls.length; i++) {
          try {
            var host = new URL(urls[i]).hostname.toLowerCase();
            if (seen.has(host)) continue;
            if (hostMatchesBlockList(host, blockList)) {
              seen.add(host);
              blocked.push(host);
            }
          } catch (_) {}
        }
        sendResponse({ blocked: blocked, count: blocked.length, mode: mode });
      } catch (e) {
        sendResponse({ blocked: [], count: 0, mode: mode, error: e.message });
      }
    })();
    return true;
  }
});
