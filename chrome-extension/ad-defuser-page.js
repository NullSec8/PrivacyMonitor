/**
 * Runs in page context. Neuters ad/tracking APIs (uBlock-style defuser).
 */
(function() {
  'use strict';
  var noop = function() {};
  var empty = function() { return {}; };
  try {
    if (typeof window === 'undefined') return;
    if (typeof window.adsbygoogle === 'undefined') {
      try {
        Object.defineProperty(window, 'adsbygoogle', {
          get: function() { return []; },
          set: function() {},
          configurable: false,
          enumerable: true
        });
      } catch (e) { window.adsbygoogle = []; }
    }
    if (window.adsbygoogle && window.adsbygoogle.push) window.adsbygoogle.push = noop;
    window.gtag = noop;
    window.dataLayer = window.dataLayer || [];
    window.ga = function() {};
    window.gaq = window.gaq || [];
    window.__ga = noop;
    window.__gaTracker = noop;
    window.GoogleAnalyticsObject = 'ga';
    window.gaplugins = window.gaplugins || empty();
    window.fbq = function() {};
    window._fbq = noop;
    window.twq = noop;
    window.pintrk = noop;
    window.uetq = noop;
    window.lintrk = noop;
    window.snaptr = noop;
    window._linkedin_data_partner_ids = [];
    window.optimizely = window.optimizely || [];
    window.__tcfapi = noop;
    window._paq = window._paq || [];
    window._tmr = window._tmr || noop;
    window.ym = window.ym || noop;
    window._qevents = window._qevents || [];
    window._hsq = window._hsq || [];
    window.NREUM = window.NREUM || empty();
    window.amzn_ads = window.amzn_ads || empty();
    window._pp = noop;
    window.uetq = noop;
    if (window.grecaptcha && !window.grecaptcha.execute) window.grecaptcha = empty();
  } catch (e) {}
})();
