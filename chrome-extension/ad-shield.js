/**
 * AdShield – hard defensive anti-navigation content script.
 * Prevents forced external navigation: synthetic events, bad protocols, invisible overlays.
 * Single content script; injects ad-shield-page.js for window.open/location overrides.
 * No UI; logs to console only; production-safe; no memory leaks (no dangling refs).
 */
(function() {
  'use strict';

  var PREFIX = '[AdShield]';
  var OVERLAY_MIN_COVER = 0.4;
  var OVERLAY_Z_INDEX_MIN = 500;

  // --- Protocol: allow only http, https, relative ---
  function allowedProtocol(href) {
    if (!href || typeof href !== 'string') return true;
    var t = String(href).trim().toLowerCase();
    if (t === '' || t[0] === '#' || t[0] === '/' || t[0] === '?') return true;
    if (t.indexOf('http://') === 0 || t.indexOf('https://') === 0) return true;
    return false;
  }

  function getHref(el) {
    if (!el || !el.getAttribute) return '';
    return el.getAttribute('href') || el.href || '';
  }

  function logBlock(type, detail) {
    try {
      console.log(PREFIX, 'blocked navigation attempt', type, detail || '');
    } catch (e) {}
  }

  // --- Layer 1: Inject page script first so overrides run before site scripts ---
  function injectPageScript() {
    var s = document.createElement('script');
    s.src = chrome.runtime.getURL('ad-shield-page.js');
    s.dataset.adshield = '1';
    (document.head || document.documentElement).appendChild(s);
  }
  injectPageScript();

  // --- Layer 2: Intercept click, mousedown, pointerdown, touchstart (capture) ---
  function handlePointerEvent(e) {
    // Reject synthetic events – block immediately, no navigation
    if (!e.isTrusted) {
      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();
      logBlock('synthetic event', e.type);
      return false;
    }

    var a = e.target && e.target.closest && e.target.closest('a');
    if (!a || !a.href) return;

    var href = getHref(a);
    if (!href) return;

    // Block bad protocols: javascript:, intent:, data:, blob:, file:, mailto:, tel:, etc.
    if (!allowedProtocol(href)) {
      e.preventDefault();
      e.stopPropagation();
      e.stopImmediatePropagation();
      logBlock('protocol', href);
      return false;
    }
  }

  ['click', 'mousedown', 'pointerdown', 'touchstart'].forEach(function(ev) {
    document.addEventListener(ev, handlePointerEvent, true);
  });

  // --- Layer 3: Neutralize invisible full-screen anchor overlays ---
  function isOverlayAnchor(a) {
    try {
      var style = window.getComputedStyle(a);
      var rect = a.getBoundingClientRect();
      var vw = window.innerWidth;
      var vh = window.innerHeight;
      var area = rect.width * rect.height;
      var viewArea = vw * vh;
      if (area < viewArea * OVERLAY_MIN_COVER) return false;
      if (style.position !== 'fixed' && style.position !== 'absolute') return false;
      var z = parseInt(style.zIndex, 10);
      if (isNaN(z) || z < OVERLAY_Z_INDEX_MIN) return false;
      return true;
    } catch (e) { return false; }
  }

  function neutralizeOverlay(a) {
    if (!allowedProtocol(getHref(a))) {
      try {
        a.style.setProperty('pointer-events', 'none', 'important');
        a.setAttribute('data-adshield-neutralized', '1');
        logBlock('overlay neutralized', getHref(a));
      } catch (e) {}
    }
  }

  function scanOverlays() {
    try {
      var links = document.querySelectorAll('a[href]:not([data-adshield-neutralized])');
      for (var i = 0; i < links.length; i++) {
        if (isOverlayAnchor(links[i])) neutralizeOverlay(links[i]);
      }
    } catch (e) {}
  }

  var observer = new MutationObserver(function() {
    scanOverlays();
  });
  observer.observe(document.documentElement, { childList: true, subtree: true });
  if (document.body) scanOverlays();
  else document.addEventListener('DOMContentLoaded', scanOverlays, { once: true });

  // --- Layer 4: Log blocks from page script (window.open / location overrides) ---
  document.addEventListener('privacyMonitorNavBlocked', function(e) {
    var d = e.detail || {};
    logBlock(d.type || 'script', d.url);
  }, true);
})();
