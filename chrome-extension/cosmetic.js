/**
 * Cosmetic hiding: injects CSS and observes DOM to hide ad/tracker elements.
 * Data flow: reads privacyMonitorMode and applies/removes styles locally. No data sent anywhere; no logging.
 */
(function() {
  'use strict';
  var STORAGE_KEY = 'privacyMonitorMode';
  var injectedStyle = null;
  var injectedLink = null;
  var observer = null;

  var critical = '[id*="google_ads"],[class*="adsbygoogle"],ins.adsbygoogle,[id*="div-gpt-ad"],[class*="div-gpt-ad"],' +
    '[id*="outbrain"],[class*="outbrain"],[id*="taboola"],[class*="taboola"],[class*="criteo"],[class*="revcontent"],[class*="mgid"],' +
    '[class*="ad-container"],[class*="ad-wrapper"],[class*="ad-slot"],[class*="ad-banner"],[data-ad-slot],[data-ad-unit],' +
    'iframe[src*="doubleclick"],iframe[src*="googlesyndication"],iframe[src*="googleadservices"],iframe[src*="facebook.com/tr"],' +
    '[class*="adblock" i],[class*="interstitial" i],[class*="ad-overlay" i],[class*="video-ad" i],[class*="ytp-ad"],' +
    '[class*="cookie-banner" i],[class*="consent-banner" i],[class*="cookie-consent" i],[class*="gdpr-banner" i],' +
    '[class*="promoted-content"],[class*="native-ad"],[class*="sponsored-content"],[class*="content-recommendation"]' +
    '{display:none!important}';

  function injectCSS() {
    try {
      var root = document.head || document.documentElement || document.body;
      if (!root) return;
      injectedStyle = document.createElement('style');
      injectedStyle.dataset.privacyMonitor = '1';
      injectedStyle.textContent = critical;
      root.appendChild(injectedStyle);
      injectedLink = document.createElement('link');
      injectedLink.rel = 'stylesheet';
      injectedLink.href = chrome.runtime.getURL('cosmetic.css');
      injectedLink.dataset.privacyMonitor = '1';
      root.insertBefore(injectedLink, root.firstChild);
    } catch (e) {}
  }

  function removeInjected() {
    if (injectedStyle && injectedStyle.parentNode) injectedStyle.parentNode.removeChild(injectedStyle);
    if (injectedLink && injectedLink.parentNode) injectedLink.parentNode.removeChild(injectedLink);
    injectedStyle = null;
    injectedLink = null;
    if (observer && document.body) {
      observer.disconnect();
      observer = null;
    }
  }

  injectCSS();

  chrome.storage.local.get(STORAGE_KEY, function(o) {
    if (o[STORAGE_KEY] === 'off') {
      removeInjected();
      return;
    }

    function hideAdLabels() {
      if (!document.body) return;
      var labels = ['Advertisement', 'Ad', 'Sponsored', 'Interstitial Ad', 'Banner Ads', 'Skip Ad', 'Skip ad', 'Ad Choices', 'Why this ad?', 'Close ad'];
      var tags = document.querySelectorAll('span, div, p, label');
      for (var i = 0; i < tags.length; i++) {
        var n = tags[i];
        if (n.dataset.privacyMonitorLabel) continue;
        var text = (n.textContent || '').trim();
        var isLabel = false;
        for (var j = 0; j < labels.length; j++) { if (text === labels[j]) { isLabel = true; break; } }
        if (!isLabel) continue;
        if (n.children.length > 0 || n.querySelector('a[href]')) continue;
        n.dataset.privacyMonitorLabel = '1';
        n.style.setProperty('display', 'none', 'important');
      }
    }

    function hideInterstitialOverlays() {
      if (!document.body) return;
      var all = document.querySelectorAll('div');
      for (var i = 0; i < all.length; i++) {
        var el = all[i];
        if (el.dataset.privacyMonitorInterstitial) continue;
        var style = window.getComputedStyle(el);
        if (style.position !== 'fixed') continue;
        var z = parseInt(style.zIndex, 10);
        if (isNaN(z) || z < 9999) continue;
        var text = (el.textContent || '').toLowerCase();
        var id = (el.id || '').toLowerCase();
        var cls = (el.className && typeof el.className === 'string' ? el.className : '').toLowerCase();
        if (text.indexOf('interstitial') !== -1 || id.indexOf('interstitial') !== -1 || cls.indexOf('interstitial') !== -1) {
          el.dataset.privacyMonitorInterstitial = '1';
          el.style.setProperty('display', 'none', 'important');
        }
      }
    }

    function tryClickSkipAd() {
      if (!document.body) return;
      var sel = document.querySelector('.ytp-ad-skip-button, .ytp-ad-skip-button-modern, .vjs-skip-ad, [class*="ytp-ad-skip"], [class*="video-ad-skip"]');
      if (sel && !sel.dataset.privacyMonitorSkipClicked) {
        sel.dataset.privacyMonitorSkipClicked = '1';
        try { sel.click(); } catch (e) {}
        return;
      }
      var btns = document.querySelectorAll('button[class*="ad"], button[class*="skip"]');
      for (var i = 0; i < btns.length; i++) {
        var b = btns[i];
        if (b.dataset.privacyMonitorSkipClicked) continue;
        var t = (b.textContent || '').trim();
        var tLower = t.toLowerCase();
        if (tLower.indexOf('skip') === -1 || tLower.indexOf('ad') === -1) continue;
        var inAd = b.closest('[class*="ytp-ad"]') || b.closest('[class*="video-ad"]') || b.closest('[class*="vjs-ad"]');
        if (!inAd) continue;
        b.dataset.privacyMonitorSkipClicked = '1';
        try { b.click(); } catch (e) {}
        return;
      }
    }

    function runHiders() {
      hideAdLabels();
      hideInterstitialOverlays();
      tryClickSkipAd();
    }

    function runWhenReady(fn) {
      if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', fn);
      else fn();
    }

    runWhenReady(runHiders);
    runWhenReady(function() {
      setTimeout(runHiders, 300);
      setTimeout(runHiders, 1000);
    });
    if (document.readyState !== 'loading') runHiders();

    runWhenReady(function() {
      if (!document.body) return;
      observer = new MutationObserver(runHiders);
      observer.observe(document.body, { childList: true, subtree: true });
    });
  });

  chrome.storage.onChanged.addListener(function(changes, area) {
    if (area === 'local' && changes[STORAGE_KEY] && changes[STORAGE_KEY].newValue === 'off')
      removeInjected();
  });
})();
