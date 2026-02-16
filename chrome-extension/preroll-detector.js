/**
 * Universal pre-roll video ad detector. Pattern-based only (no site-specific selectors).
 * - Detects ad video by: URL patterns (vast, vpaid, preroll, etc.), DOM keywords (class/id/data-*),
 *   and optional duration heuristic (short + other signal).
 * - Actions: mute, skip to end (so 'ended' fires and player advances), and rely on preroll-skip for button click.
 * - MutationObserver for dynamically inserted videos. MV3 compatible.
 */
(function() {
  'use strict';

  var STORAGE_KEY = 'privacyMonitorMode';
  var MARK_ATTR = 'data-pm-preroll-handled';

  // URL substrings (lowercase) that indicate ad video or ad request
  var AD_URL_PATTERNS = [
    'vast', 'vpaid', 'ad_creative', 'ad_break', 'preroll', 'postroll', 'midroll',
    'pagead', 'doubleclick', 'googlesyndication', 'adservice', 'adsense',
    'ptracking', 'ad_serving', 'videoad', '/ad/', '_ad_', '-ad-', 'ad_creative',
    'ima-ad', 'google_ima', 'video-ad', 'ad-container', 'adcontainer', 'adsystem',
    'get_video_info', 'adformat=', 'ad_type=', 'instream', 'companion_ad'
  ];

  // Keywords in class, id, or data-* attributes (lowercase substring match)
  var AD_KEYWORDS = [
    'ad', 'preroll', 'midroll', 'postroll', 'commercial', 'sponsor', 'vpaid', 'vast',
    'ima', 'promo', 'video-ad', 'video_ad', 'ads-video', 'ad-container', 'ad-overlay',
    'ad-showing', 'ad-playing', 'ad-slot', 'ad-break', 'adbreak', 'ad_break'
  ];

  var MAX_AD_DURATION_SEC = 90;
  var MIN_DURATION_FOR_HEURISTIC_SEC = 5;

  function getVideoSrc(video) {
    try {
      var s = (video.currentSrc || video.src || '').trim().toLowerCase();
      if (s) return s;
      var src = video.querySelector && video.querySelector('source[src]');
      return (src && src.getAttribute && src.getAttribute('src')) ? src.getAttribute('src').trim().toLowerCase() : '';
    } catch (e) { return ''; }
  }

  function urlMatchesAdPatterns(url) {
    if (!url) return false;
    var u = url.toLowerCase();
    for (var i = 0; i < AD_URL_PATTERNS.length; i++) {
      if (u.indexOf(AD_URL_PATTERNS[i]) !== -1) return true;
    }
    return false;
  }

  function elementHasAdSignal(el) {
    if (!el || !el.getAttribute) return false;
    var cls = (el.className && typeof el.className === 'string' ? el.className : '').toLowerCase();
    var id = (el.id || '').toLowerCase();
    var data = '';
    try {
      for (var i = 0; i < el.attributes.length; i++) {
        var a = el.attributes[i];
        if (a && a.name && a.name.indexOf('data-') === 0 && a.value)
          data += ' ' + a.value.toLowerCase();
      }
    } catch (e) {}
    var combined = cls + ' ' + id + ' ' + data;
    for (var j = 0; j < AD_KEYWORDS.length; j++) {
      if (combined.indexOf(AD_KEYWORDS[j]) !== -1) return true;
    }
    return false;
  }

  function containerHasAdSignal(video) {
    var el = video;
    for (var i = 0; i < 15 && el; i++) {
      if (elementHasAdSignal(el)) return true;
      el = el.parentElement || el.parentNode;
      if (el && el.nodeType !== 1) el = el.parentElement;
    }
    return false;
  }

  function isShortDuration(video) {
    try {
      var d = video.duration;
      if (!Number.isFinite(d) || d <= 0) return false;
      return d >= MIN_DURATION_FOR_HEURISTIC_SEC && d <= MAX_AD_DURATION_SEC;
    } catch (e) { return false; }
  }

  function isAdVideo(video) {
    if (!video || video.nodeName !== 'VIDEO') return false;
    if (video.hasAttribute(MARK_ATTR)) return false;

    var urlSignal = urlMatchesAdPatterns(getVideoSrc(video));
    var domSignal = elementHasAdSignal(video) || containerHasAdSignal(video);
    if (urlSignal || domSignal) return true;
    if (isShortDuration(video) && containerHasAdSignal(video)) return true;
    return false;
  }

  function skipToEnd(video) {
    try {
      video.muted = true;
      if (Number.isFinite(video.duration) && video.duration > 0) {
        video.currentTime = video.duration - 0.1;
      } else {
        video.currentTime = 1e9;
      }
    } catch (e) {}
  }

  function tryClickSkipInContainer(video) {
    try {
      var root = video.closest && video.closest('video') ? video.parentElement : video;
      while (root && root !== document.body) {
        var btn = root.querySelector && root.querySelector('button[class*="skip" i], button[class*="ad" i], [role="button"]');
        if (btn) {
          var t = (btn.textContent || '').toLowerCase();
          if (t.indexOf('skip') !== -1 && t.indexOf('ad') !== -1) {
            btn.click();
            return;
          }
        }
        root = root.parentElement;
      }
    } catch (e) {}
  }

  function processVideo(video) {
    if (!video || video.hasAttribute(MARK_ATTR)) return;
    if (!isAdVideo(video)) return;

    video.setAttribute(MARK_ATTR, '1');
    tryClickSkipInContainer(video);
    skipToEnd(video);

    if (!Number.isFinite(video.duration) || video.duration <= 0) {
      video.addEventListener('loadedmetadata', function onMeta() {
        video.removeEventListener('loadedmetadata', onMeta);
        skipToEnd(video);
      }, { once: true });
    }
  }

  function scanVideos(root) {
    try {
      var videos = (root || document).querySelectorAll ? (root || document).querySelectorAll('video') : [];
      for (var i = 0; i < videos.length; i++) processVideo(videos[i]);
    } catch (e) {}
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

  function init() {
    runWhenReady(function() {
      if (!document.body) return;
      scanVideos(document.body);

      var observer = new MutationObserver(function(mutations) {
        for (var m = 0; m < mutations.length; m++) {
          var list = mutations[m].addedNodes;
          for (var i = 0; i < list.length; i++) {
            var node = list[i];
            if (node && node.nodeType === 1) {
              if (node.nodeName === 'VIDEO') processVideo(node);
              else if (node.querySelector) scanVideos(node);
            }
          }
        }
      });
      observer.observe(document.body, { childList: true, subtree: true });

      document.body.addEventListener('loadedmetadata', function(e) {
        if (e.target && e.target.nodeName === 'VIDEO') processVideo(e.target);
      }, true);
    });
  }

  chrome.storage.local.get(STORAGE_KEY, function(o) {
    if (o[STORAGE_KEY] === 'off') return;
    init();
  });
})();
