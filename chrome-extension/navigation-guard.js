/**
 * Navigation guard – blocks forced navigation and non-http(s) protocols.
 * Intercepts clicks (capture phase); allows only http, https, and relative URLs.
 * Blocked: intent:, javascript:, data:, blob:, file:, mailto:, tel:, chrome:, msedge:, etc.
 * No UI; logs blocked attempts locally for debugging.
 */
(function() {
  'use strict';

  var BLOCK_LOG_MAX = 50;
  var BLOCK_LOG_KEY = 'privacyMonitorBlockLog';
  var lastMousedownHref = '';

  function allowedProtocol(href) {
    if (!href || typeof href !== 'string') return true;
    var t = href.trim().toLowerCase();
    if (t === '' || t[0] === '#' || t[0] === '/' || t[0] === '?') return true;
    if (t.indexOf('http://') === 0 || t.indexOf('https://') === 0) return true;
    return false;
  }

  function logBlocked(type, url) {
    try {
      chrome.storage.local.get(BLOCK_LOG_KEY, function(o) {
        var log = Array.isArray(o[BLOCK_LOG_KEY]) ? o[BLOCK_LOG_KEY] : [];
        log.push({ type: type, url: (url || '').substring(0, 300), time: Date.now() });
        if (log.length > BLOCK_LOG_MAX) log = log.slice(-BLOCK_LOG_MAX);
        chrome.storage.local.set({ [BLOCK_LOG_KEY]: log });
      });
    } catch (e) {}
  }

  function injectPageScript() {
    var s = document.createElement('script');
    s.src = chrome.runtime.getURL('navigation-guard-page.js');
    s.dataset.privacyMonitor = '1';
    (document.head || document.documentElement).appendChild(s);
  }

  document.addEventListener('mousedown', function(e) {
    var a = e.target && e.target.closest && e.target.closest('a');
    lastMousedownHref = (a && a.getAttribute && a.getAttribute('href')) || '';
  }, true);

  document.addEventListener('click', function(e) {
    var a = e.target && e.target.closest && e.target.closest('a');
    if (!a || !a.href) return;

    var href = (a.getAttribute && a.getAttribute('href')) || a.href || '';
    if (!href) return;

    if (!allowedProtocol(href)) {
      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();
      logBlocked('nav.protocol', href);
      return false;
    }

    var currentHref = (a.getAttribute && a.getAttribute('href')) || a.href || '';
    if (lastMousedownHref && currentHref !== lastMousedownHref && !allowedProtocol(currentHref)) {
      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();
      logBlocked('nav.rewrite', currentHref);
      return false;
    }
  }, true);

  document.addEventListener('privacyMonitorNavBlocked', function(e) {
    var d = e.detail || {};
    logBlocked(d.type || 'nav.script', d.url);
  }, true);

  injectPageScript();
})();
