/**
 * Runs in page context. Neuters ad/tracking APIs (uBlock-style defuser).
 */
(function() {
  'use strict';
  if (typeof window === 'undefined') return;
  var noop = function() {};
  var noopPromise = function() { return Promise.resolve(); };
  var empty = function() { return {}; };
  var emptyArray = function() { return []; };

  function defProp(obj, name, val) {
    try {
      Object.defineProperty(obj, name, { get: function() { return val; }, set: noop, configurable: false, enumerable: true });
    } catch(e) { try { obj[name] = val; } catch(e2) {} }
  }

  try {
    // ── Google Ads ──
    defProp(window, 'adsbygoogle', []);
    if (window.adsbygoogle && window.adsbygoogle.push) window.adsbygoogle.push = noop;

    // ── Google Analytics / GTM ──
    window.gtag = noop;
    defProp(window, 'dataLayer', { push: noop, length: 0, slice: emptyArray, splice: emptyArray, indexOf: function() { return -1; } });
    window.ga = noop; window.ga.getAll = emptyArray; window.ga.getByName = noop;
    window.ga.create = noop; window.ga.remove = noop;
    window.gaq = []; window.gaq.push = noop;
    window.__ga = noop;
    window.__gaTracker = noop;
    window.GoogleAnalyticsObject = 'ga';
    window.gaplugins = empty();

    // ── Facebook ──
    defProp(window, 'fbq', noop);
    window._fbq = noop;

    // ── Twitter / X ──
    window.twq = noop;

    // ── Pinterest ──
    window.pintrk = noop;

    // ── Microsoft UET ──
    window.uetq = []; window.uetq.push = noop;

    // ── LinkedIn ──
    window.lintrk = noop;
    window._linkedin_data_partner_ids = [];
    window._linkedin_partner_id = '';

    // ── Snap ──
    window.snaptr = noop;

    // ── TikTok ──
    window.ttq = { track: noop, page: noop, identify: noop, instances: noop, debug: noop, on: noop, off: noop, once: noop, ready: noop, alias: noop, group: noop, enableCookie: noop, disableCookie: noop, load: noop };

    // ── Reddit ──
    window.rdt = noop;

    // ── Quora ──
    window.qp = noop;

    // ── Taboola ──
    window._tfa = []; window._tfa.push = noop;
    window.TRC = window.TRC || empty();

    // ── Outbrain ──
    window.OBR = window.OBR || empty();
    window.obApi = noop;

    // ── Criteo ──
    window.criteo_q = []; window.criteo_q.push = noop;

    // ── Optimizely ──
    window.optimizely = []; window.optimizely.push = noop;

    // ── TCF/CMP consent ──
    window.__tcfapi = noop;
    window.__cmp = noop;

    // ── Matomo ──
    defProp(window, '_paq', { push: noop, length: 0 });

    // ── Mail.ru / Tune TMR ──
    window._tmr = []; window._tmr.push = noop;

    // ── Yandex Metrika ──
    window.ym = noop;
    window.Ya = window.Ya || {};
    window.Ya.Metrika2 = noop;

    // ── Quantcast ──
    window._qevents = []; window._qevents.push = noop;

    // ── HubSpot ──
    window._hsq = []; window._hsq.push = noop;
    window.hbspt = window.hbspt || { forms: { create: noop } };

    // ── New Relic ──
    defProp(window, 'NREUM', { info: {}, init: {}, loader_config: {}, addRelease: noop, addPageAction: noop, setCustomAttribute: noop, finished: noop, noticeError: noop, setPageViewName: noop, setErrorHandler: noop });

    // ── Amazon Ads ──
    window.amzn_ads = empty();
    window.amznads = empty();

    // ── Segment ──
    window.analytics = { track: noop, identify: noop, page: noop, group: noop, alias: noop, ready: noop, on: noop, once: noop, off: noop, initialize: true, user: function() { return { id: noop, traits: noop, anonymousId: noop }; } };

    // ── Heap ──
    window.heap = { track: noop, identify: noop, resetIdentity: noop, addUserProperties: noop, addEventProperties: noop, removeEventProperty: noop, clearEventProperties: noop, appid: '', userId: '', config: {} };

    // ── Mixpanel ──
    window.mixpanel = { track: noop, identify: noop, alias: noop, set_config: noop, register: noop, register_once: noop, people: { set: noop, set_once: noop, increment: noop, track_charge: noop }, init: noop, get_distinct_id: function() { return ''; } };

    // ── Amplitude ──
    window.amplitude = { getInstance: function() { return { init: noop, logEvent: noop, setUserId: noop, setUserProperties: noop, identify: noop }; } };

    // ── Hotjar ──
    window.hj = noop;
    window._hjSettings = { hjid: 0, hjsv: 0 };

    // ── FullStory ──
    window.FS = { identify: noop, setUserVars: noop, event: noop, getCurrentSessionURL: noop, shutdown: noop, restart: noop, consent: noop };

    // ── Clarity ──
    window.clarity = noop;

    // ── Various ──
    window._pp = noop;
    window.Intercom = noop;
    try { window.Intercom.booted = true; } catch(e) {}
    window._satellite = { track: noop, getVar: noop, setVar: noop, pageBottom: noop };
    window.s_gi = noop;

    // ── Lotame ──
    window.lotame_sync = noop;
    window.LOTCC = { cmd: [], ready: noop, collect: noop };

    // ── Permutive ──
    window.permutive = { track: noop, identify: noop, trigger: noop, addon: noop, consent: noop, ready: noop };

    // ── Chartbeat ──
    window._sf_async_config = {};
    window.pSUPERFLY = { virtualPage: noop, activity: noop };

    // ── Parsely ──
    window.PARSELY = { autotrack: false, conversions: { trackPixel: noop } };

    // ── ComScore ──
    window._comscore = []; window._comscore.push = noop;
    window.COMSCORE = { beacon: noop };

    // ── IndexExchange ──
    window.headertag = { cmd: [], apiReady: true };

    // ── Moat ──
    window.moatYieldReady = true;
    window.moat = { record: noop, startTracking: noop };

    // ── IAS (Integral Ad Science) ──
    window.__iasPET = { queue: [], setTargetingForGPT: noop };

    // ── Prebid.js ──
    window.pbjs = window.pbjs || { que: [], cmd: [] };
    window.pbjs.que.push = noop;
    window.pbjs.cmd.push = noop;

    // ── SpotX ──
    window.spotx = { directAdOS: noop };

    // ── Snowplow ──
    window.snowplow = noop;

    // ── Bugsnag ──
    window.bugsnagClient = { notify: noop, use: noop, startSession: noop };

    // ── Drift ──
    window.drift = { api: { sidebar: { toggle: noop } }, on: noop, off: noop, track: noop, identify: noop, reset: noop };

    // ── Freshchat ──
    window.fcWidget = { init: noop, open: noop, close: noop, track: noop, identify: noop };

    // ── Mouseflow ──
    window._mfq = []; window._mfq.push = noop;

    // ── Lucky Orange ──
    window.__lo_cs_added = true;
    window.__lo_site_id = 0;

    // ── Adroll ──
    window.adroll = noop;
    window.__adroll_loaded = true;
    window.adroll_adv_id = '';
    window.adroll_pix_id = '';

    // ── Perfect Audience ──
    window._pa = { track: noop };

    // ── Block document.write ad injection ──
    var _docWrite = document.write.bind(document);
    document.write = function(s) {
      try {
        var lower = (s || '').toLowerCase();
        if (lower.indexOf('ad') !== -1 && (lower.indexOf('script') !== -1 || lower.indexOf('iframe') !== -1)) return;
        if (lower.indexOf('doubleclick') !== -1 || lower.indexOf('googlesyndication') !== -1) return;
        if (lower.indexOf('pagead') !== -1 || lower.indexOf('adservice') !== -1) return;
      } catch(e) {}
      return _docWrite(s);
    };

    // ── navigator.sendBeacon: Block known tracking endpoints ──
    if (navigator.sendBeacon) {
      var _sendBeacon = navigator.sendBeacon.bind(navigator);
      var beaconBlockPatterns = [
        'google-analytics', 'analytics', 'collect', '/r/collect', 'beacon', 'track', 'pixel',
        'facebook.com/tr', 'bat.bing.com', 'ad.doubleclick', 'stats.g.doubleclick',
        'sentry.io', 'logbucket', 'ingest.', '/log', '/telemetry', 'clarity.ms'
      ];
      navigator.sendBeacon = function(url, data) {
        try {
          var u = (url || '').toLowerCase();
          for (var i = 0; i < beaconBlockPatterns.length; i++) {
            if (u.indexOf(beaconBlockPatterns[i]) >= 0) return true;
          }
        } catch(e) {}
        return _sendBeacon(url, data);
      };
    }

    // ── Block tracking via fetch for known patterns ──
    if (window.fetch) {
      var _fetch = window.fetch;
      var fetchBlockPatterns = [
        'google-analytics.com', 'www.google-analytics.com', 'analytics.google.com',
        'stats.g.doubleclick.net', 'ad.doubleclick.net', 'pagead2.googlesyndication.com',
        'facebook.com/tr', 'connect.facebook.net', 'bat.bing.com',
        'clarity.ms', 'browser.sentry-cdn.com', '/collect?', '/r/collect',
        'cdn.mxpnl.com', 'api.mixpanel.com', 'api.amplitude.com',
        'api.segment.io', 'cdn.segment.com',
        'heapanalytics.com', 'rs.fullstory.com',
        'hotjar.com', 'static.hotjar.com',
        'script.hotjar.com', 'in.hotjar.com',
        'mouseflow.com', 'luckyorange.com', 'freshchat.com',
        'drift.com', 'chartbeat.com', 'parsely.com', 'b.scorecardresearch.com',
        'pixel.quantserve.com', 'c.amazon-adsystem.com', 'aax.amazon-adsystem.com',
        'sb.scorecardresearch.com', 'secure.quantserve.com', 'cdn.taboola.com',
        'trc.taboola.com', 'widgets.outbrain.com', 'log.outbrain.com',
        'ib.adnxs.com', 'secure.adnxs.com', 'pixel.facebook.com',
        'ct.pinterest.com', 'analytics.tiktok.com', 'an.facebook.com'
      ];
      window.fetch = function(input, init) {
        try {
          var url = typeof input === 'string' ? input : (input && input.url ? input.url : '');
          var u = url.toLowerCase();
          for (var i = 0; i < fetchBlockPatterns.length; i++) {
            if (u.indexOf(fetchBlockPatterns[i]) >= 0)
              return Promise.resolve(new Response('', { status: 200 }));
          }
        } catch(e) {}
        return _fetch.apply(this, arguments);
      };
    }

    // ── Block tracking via XMLHttpRequest for known analytics endpoints ──
    var _xhrOpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function(method, url) {
      try {
        var u = (url || '').toLowerCase();
        var xhrBlock = ['google-analytics.com', '/r/collect', 'facebook.com/tr', 'bat.bing.com',
          'clarity.ms', 'hotjar.com', 'sentry', 'mouseflow.com', 'luckyorange.com',
          'chartbeat.com', 'parsely.com', 'scorecardresearch.com', 'taboola.com',
          'outbrain.com', 'adnxs.com', 'tiktok.com/i18n', 'analytics.tiktok.com'];
        var blocked = false;
        for (var k = 0; k < xhrBlock.length; k++) { if (u.indexOf(xhrBlock[k]) >= 0) { blocked = true; break; } }
        if (blocked) {
          this.__pmBlocked = true;
          return;
        }
      } catch(e) {}
      return _xhrOpen.apply(this, arguments);
    };
    var _xhrSend = XMLHttpRequest.prototype.send;
    XMLHttpRequest.prototype.send = function(data) {
      if (this.__pmBlocked) return;
      return _xhrSend.apply(this, arguments);
    };

  } catch (e) {}
})();
