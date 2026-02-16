/**
 * Click-guard page script. Runs in page context to override window.open and location.
 * Only allows navigation/popups when there was a recent trusted user gesture (event.isTrusted).
 * Blocked redirects are reported via custom event for optional local logging.
 */
(function() {
  'use strict';

  var TRUST_MS = 800;
  var lastTrustedClickTime = 0;
  var config = { mode: 'off', blockClickHijacking: false, blocklist: [], maliciousHosts: [] };

  try {
    var raw = document.documentElement.getAttribute('data-privacy-monitor-click-guard');
    if (raw) {
      var parsed = JSON.parse(raw);
      config.mode = parsed.mode || 'off';
      config.blockClickHijacking = !!parsed.blockClickHijacking;
      config.blocklist = Array.isArray(parsed.blocklist) ? parsed.blocklist : [];
      config.maliciousHosts = Array.isArray(parsed.maliciousHosts) ? parsed.maliciousHosts : [];
    }
  } catch (e) {}

  if (config.mode === 'off' || !config.blockClickHijacking) return;

  function getHost(url) {
    try { return new URL(url, location.href).hostname.toLowerCase(); } catch (e) { return ''; }
  }

  function hostInList(host, list) {
    if (!host || !list.length) return false;
    for (var i = 0; i < list.length; i++) {
      var d = list[i];
      if (host === d || host.endsWith('.' + d)) return true;
    }
    return false;
  }

  function blockNavigate(url) {
    var host = getHost(url);
    return hostInList(host, config.maliciousHosts) || hostInList(host, config.blocklist);
  }

  function trustedRecently() {
    return (Date.now() - lastTrustedClickTime) < TRUST_MS;
  }

  function reportBlocked(type, url) {
    try {
      document.dispatchEvent(new CustomEvent('privacyMonitorBlocked', {
        detail: { type: type, url: url || '', time: Date.now() }
      }));
    } catch (e) {}
  }

  document.addEventListener('click', function(e) {
    if (e.isTrusted) lastTrustedClickTime = Date.now();
  }, true);

  try {
    var origOpen = window.open;
    window.open = function(url, target, features) {
      if (config.mode === 'aggressive') {
        reportBlocked('window.open', url);
        return null;
      }
      if (!trustedRecently()) {
        reportBlocked('window.open', url);
        return null;
      }
      if (url && blockNavigate(url)) {
        reportBlocked('window.open', url);
        return null;
      }
      return origOpen.apply(window, arguments);
    };
  } catch (e) {}

  try {
    var origReplace = location.replace;
    var origAssign = location.assign;
    if (typeof origReplace === 'function') {
      location.replace = function(url) {
        if (!trustedRecently()) {
          reportBlocked('location.replace', url);
          return;
        }
        if (url && blockNavigate(url)) {
          reportBlocked('location.replace', url);
          return;
        }
        origReplace.call(location, url);
      };
    }
    if (typeof origAssign === 'function') {
      location.assign = function(url) {
        if (!trustedRecently()) {
          reportBlocked('location.assign', url);
          return;
        }
        if (url && blockNavigate(url)) {
          reportBlocked('location.assign', url);
          return;
        }
        origAssign.call(location, url);
      };
    }
  } catch (e) {}
})();
