/**
 * Click-guard content script. Injects page script for window.open/location overrides;
 * intercepts clicks (capture phase) to block non-trusted and ad/tracker links.
 * Data flow: reads storage local only; optional local log of blocked redirects (last 50, for debugging).
 */
(function() {
  'use strict';

  var blocklist = [];
  var ready = false;
  var lastMousedownHref = '';
  var lastMousedownTime = 0;
  var BLOCK_LOG_MAX = 50;
  var BLOCK_LOG_KEY = 'privacyMonitorBlockLog';

  var maliciousHosts = [
    'support-online.net', 'secure-support.net', 'pc-cleaner.com', 'virus-removal.net', 'fix-your-pc.com',
    'system-alert.net', 'browser-update.net', 'flash-update.net', 'adobe-update.net', 'win-update.net',
    'security-alert.net', 'antivirus-alert.com', 'trojan-removal.net', 'malware-fix.net', 'click2redirect.com',
    'adf.ly', 'bc.vc', 'sh.st', 'adfly.com', 'linkshrink.net', 'shortest.link', 'clk.ink', 'ouo.io', 'exe.io',
    'shink.in', 'clks.pro', 'linkbucks.com', 'adfoc.us', 'cpmlink.net', 'adlock.in', 'megaurl.in', 'urlz.fr',
    'admy.link', 'adshort.co', 'safefilelink.com', 'safelinkconverter.com', 'safelink.icu', 'safelink.xyz',
    'safelink.one', 'urlz.work', 'shorte.st', 'linksfly.me', 'winactivator.com', 'crack-keygen.com',
    'serial-key.net', 'cracks.me', 'tech-support-scam.com', 'microsoft-support.net', 'apple-support.net',
    'amazon-support.net', 'secure-check.net', 'virus-scan.net', 'pc-fix.net', 'driver-update.net',
    'update-flash.com', 'java-update.net', 'browser-check.net', 'antivirus-download.net', 'clean-pc.net',
    'free-soft.com', 'download-fix.net', 'pc-speedup.net', 'registry-cleaner.net', 'error-fix.net'
  ];

  function getHost(url) {
    try { return new URL(url, location.href).hostname.toLowerCase(); } catch (e) { return ''; }
  }

  function isMalicious(host) {
    if (!host) return false;
    for (var i = 0; i < maliciousHosts.length; i++) {
      if (host === maliciousHosts[i] || host.endsWith('.' + maliciousHosts[i])) return true;
    }
    return false;
  }

  function hostBlocked(host) {
    if (!host || !blocklist.length) return false;
    host = host.toLowerCase();
    for (var i = 0; i < blocklist.length; i++) {
      if (host === blocklist[i] || host.endsWith('.' + blocklist[i])) return true;
    }
    return false;
  }

  function logBlocked(type, url) {
    chrome.storage.local.get(BLOCK_LOG_KEY, function(o) {
      var log = Array.isArray(o[BLOCK_LOG_KEY]) ? o[BLOCK_LOG_KEY] : [];
      log.push({ type: type, url: url || '', time: Date.now() });
      if (log.length > BLOCK_LOG_MAX) log = log.slice(-BLOCK_LOG_MAX);
      chrome.storage.local.set({ [BLOCK_LOG_KEY]: log });
    });
  }

  function injectPageScript(mode, blockClickHijacking) {
    var config = {
      mode: mode,
      blockClickHijacking: blockClickHijacking,
      blocklist: blocklist,
      maliciousHosts: maliciousHosts
    };
    document.documentElement.setAttribute('data-privacy-monitor-click-guard', JSON.stringify(config));
    var s = document.createElement('script');
    s.src = chrome.runtime.getURL('click-guard-page.js');
    s.dataset.privacyMonitor = '1';
    (document.head || document.documentElement).appendChild(s);
  }

  function setupClickInterceptor() {
    document.addEventListener('mousedown', function(e) {
      var a = e.target && (e.target.closest ? e.target.closest('a') : null);
      if (a && a.href) {
        lastMousedownHref = a.href;
        lastMousedownTime = Date.now();
      } else {
        lastMousedownHref = '';
      }
    }, true);

    document.addEventListener('click', function(e) {
      if (!ready) return;

      if (!e.isTrusted) {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        logBlocked('click.untrusted', (e.target && e.target.closest && e.target.closest('a')) ? e.target.closest('a').href : '');
        return false;
      }

      var a = e.target && (e.target.closest ? e.target.closest('a') : null);
      if (!a || !a.href) return;

      var href = a.href;
      var host = getHost(href);

      if (isMalicious(host)) {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        logBlocked('click.malicious', href);
        return false;
      }

      if (hostBlocked(host)) {
        e.preventDefault();
        e.stopPropagation();
        e.stopImmediatePropagation();
        logBlocked('click.blocklist', href);
        return false;
      }

      if (lastMousedownHref && (Date.now() - lastMousedownTime) < 2000) {
        if (a.href !== lastMousedownHref) {
          var newHost = getHost(a.href);
          if (isMalicious(newHost) || hostBlocked(newHost)) {
            e.preventDefault();
            e.stopPropagation();
            e.stopImmediatePropagation();
            logBlocked('click.rewrite', a.href);
            return false;
          }
        }
      }
    }, true);

    document.addEventListener('privacyMonitorBlocked', function(e) {
      var d = e.detail || {};
      logBlocked(d.type || 'page', d.url);
    }, true);
  }

  chrome.storage.local.get(['privacyMonitorMode', 'privacyMonitorBlocklistHosts', 'blockClickHijacking'], function(o) {
    var mode = o.privacyMonitorMode;
    blocklist = o.privacyMonitorBlocklistHosts || [];
    var blockClickHijacking = o.blockClickHijacking !== false;
    ready = true;

    if (mode === 'off' || !blockClickHijacking) return;

    injectPageScript(mode, blockClickHijacking);
    setupClickInterceptor();
  });

  chrome.storage.onChanged.addListener(function(changes, area) {
    if (area !== 'local') return;
    if (changes.privacyMonitorBlocklistHosts)
      blocklist = (changes.privacyMonitorBlocklistHosts.newValue) || [];
  });
})();
