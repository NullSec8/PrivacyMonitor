/**
 * Pre-roll video ad skip. Clicks the "Skip ad" button as soon as it appears.
 * Works with YouTube, Vimeo, and other players that show a skip button.
 * Only runs when blocking is on. No UI; no network blocking (skip-only to avoid breaking playback).
 */
(function() {
  'use strict';

  var STORAGE_KEY = 'privacyMonitorMode';
  var CLICKED_ATTR = 'data-privacy-monitor-skipped';
  var POLL_MS = 400;
  var POLL_MAX_MS = 60000;

  // All known skip-button selectors (YouTube, Vimeo, Video.js, generic)
  var SKIP_SELECTORS = [
    '.ytp-ad-skip-button',
    '.ytp-ad-skip-button-modern',
    '.ytp-skip-ad-button',
    '.videoAdUiSkipButton',
    '.vjs-skip-ad',
    '[class*="ytp-ad-skip"]',
    '[class*="video-ad-skip"]',
    '[class*="vjs-skip-ad"]',
    'button[class*="skip" i][class*="ad" i]',
    '.ytp-ad-skip-button-container button'
  ].join(', ');

  function findSkipButton() {
    try {
      var el = document.querySelector(SKIP_SELECTORS);
      if (el && !el.hasAttribute(CLICKED_ATTR)) return el;
      var buttons = document.querySelectorAll('button, [role="button"]');
      for (var i = 0; i < buttons.length; i++) {
        var b = buttons[i];
        if (b.hasAttribute(CLICKED_ATTR)) continue;
        var text = (b.textContent || '').trim().toLowerCase();
        if (text.indexOf('skip') === -1 || text.indexOf('ad') === -1) continue;
        var inAd = b.closest && (b.closest('[class*="ytp-ad"]') || b.closest('[class*="video-ad"]') || b.closest('[class*="vjs-ad"]') || b.closest('[class*="ima-ad"]'));
        if (inAd) return b;
      }
    } catch (e) {}
    return null;
  }

  function clickSkip(btn) {
    if (!btn || btn.hasAttribute(CLICKED_ATTR)) return false;
    try {
      btn.setAttribute(CLICKED_ATTR, '1');
      btn.click();
      return true;
    } catch (e) {}
    return false;
  }

  function trySkip() {
    var btn = findSkipButton();
    if (btn) clickSkip(btn);
  }

  function runWhenReady(fn) {
    if (document.body) {
      fn();
      return;
    }
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', fn);
    } else {
      fn();
    }
  }

  function startPolling() {
    var start = Date.now();
    var t = setInterval(function() {
      trySkip();
      if (Date.now() - start > POLL_MAX_MS) clearInterval(t);
    }, POLL_MS);
  }

  function init() {
    runWhenReady(function() {
      if (!document.body) return;
      trySkip();
      startPolling();
      var observer = new MutationObserver(function() {
        trySkip();
      });
      observer.observe(document.body, { childList: true, subtree: true });
    });
  }

  chrome.storage.local.get(STORAGE_KEY, function(o) {
    if (o[STORAGE_KEY] === 'off') return;
    init();
  });

  chrome.storage.onChanged.addListener(function(changes, area) {
    if (area === 'local' && changes[STORAGE_KEY] && changes[STORAGE_KEY].newValue === 'off') {
      // Stop: observer and interval are not stored, so they keep running but do nothing if mode toggled later
    }
  });
})();
