/**
 * YouTube-specific ad blocking. Handles pre-rolls, mid-rolls, overlay ads,
 * companion banners, and masthead promotions. Skips unskippable ads by
 * seeking to end. Removes ad UI containers.
 */
(function() {
  'use strict';
  var STORAGE_KEY = 'privacyMonitorMode';
  var HANDLED = 'data-pm-yt-handled';
  var CHECK_MS = 250;

  var AD_SELECTORS = [
    '.ytp-ad-module',
    '.ytp-ad-overlay-container',
    '.ytp-ad-text-overlay',
    '.ytp-ad-skip-button-container',
    '.ytp-ad-feedback-dialog-container',
    '.ytp-ad-survey-questions',
    '.ytp-ad-action-interstitial',
    '.ytp-ad-image-overlay',
    '#player-ads',
    '#masthead-ad',
    '#ad_creative_3',
    'ytd-promoted-sparkles-web-renderer',
    'ytd-promoted-video-renderer',
    'ytd-display-ad-renderer',
    'ytd-companion-slot-renderer',
    'ytd-action-companion-ad-renderer',
    'ytd-in-feed-ad-layout-renderer',
    'ytd-ad-slot-renderer',
    'ytd-banner-promo-renderer',
    'ytd-statement-banner-renderer',
    'ytd-brand-video-singleton-renderer',
    'ytd-brand-video-shelf-renderer',
    'ytd-search-pyv-renderer',
    'tp-yt-paper-dialog:has(yt-mealbar-promo-renderer)',
    '.ytd-merch-shelf-renderer',
    '.ytd-in-feed-ad-layout-renderer',
    '#offer-module',
    '.ytp-suggested-action',
    '.ytp-cards-teaser',
    '.iv-branding'
  ];

  var SKIP_SELECTORS = [
    '.ytp-ad-skip-button',
    '.ytp-ad-skip-button-modern',
    '.ytp-skip-ad-button',
    'button.ytp-ad-skip-button',
    'button.ytp-ad-skip-button-modern',
    '.ytp-ad-skip-button-container button',
    '[class*="ytp-ad-skip"] button'
  ];

  function hideAds() {
    for (var i = 0; i < AD_SELECTORS.length; i++) {
      try {
        var els = document.querySelectorAll(AD_SELECTORS[i]);
        for (var j = 0; j < els.length; j++) {
          if (els[j].getAttribute(HANDLED)) continue;
          els[j].setAttribute(HANDLED, '1');
          els[j].style.setProperty('display', 'none', 'important');
        }
      } catch(e) {}
    }
  }

  function clickSkip() {
    for (var i = 0; i < SKIP_SELECTORS.length; i++) {
      try {
        var btn = document.querySelector(SKIP_SELECTORS[i]);
        if (btn && btn.offsetParent !== null) {
          btn.click();
          return true;
        }
      } catch(e) {}
    }
    return false;
  }

  function skipVideoAd() {
    try {
      var video = document.querySelector('.html5-main-video');
      if (!video) return;
      var player = document.querySelector('.html5-video-player');
      if (!player) return;
      var isAd = player.classList.contains('ad-showing') ||
                 player.classList.contains('ad-interrupting') ||
                 document.querySelector('.ytp-ad-player-overlay') !== null;
      if (!isAd) return;

      if (clickSkip()) return;

      video.muted = true;
      video.playbackRate = 16;
      if (Number.isFinite(video.duration) && video.duration > 0) {
        video.currentTime = video.duration - 0.1;
      }
    } catch(e) {}
  }

  function removeHomepageAds() {
    try {
      var selectors = [
        'ytd-rich-item-renderer:has(ytd-ad-slot-renderer)',
        'ytd-rich-section-renderer:has(ytd-ad-slot-renderer)',
        'ytd-rich-item-renderer:has(ytd-promoted-video-renderer)',
        'ytd-rich-item-renderer:has(ytd-display-ad-renderer)',
        'ytd-video-renderer:has(.ytd-promoted-sparkles-web-renderer)',
        'ytd-compact-promoted-video-renderer',
        'ytd-promoted-sparkles-text-search-renderer'
      ];
      for (var i = 0; i < selectors.length; i++) {
        var els = document.querySelectorAll(selectors[i]);
        for (var j = 0; j < els.length; j++) {
          if (els[j].getAttribute(HANDLED)) continue;
          els[j].setAttribute(HANDLED, '1');
          els[j].style.setProperty('display', 'none', 'important');
        }
      }
    } catch(e) {}
  }

  function run() {
    hideAds();
    skipVideoAd();
    removeHomepageAds();
  }

  function init() {
    run();
    setInterval(run, CHECK_MS);

    if (document.body) {
      var obs = new MutationObserver(function() { run(); });
      obs.observe(document.body, { childList: true, subtree: true });
    } else {
      document.addEventListener('DOMContentLoaded', function() {
        var obs = new MutationObserver(function() { run(); });
        obs.observe(document.body, { childList: true, subtree: true });
      });
    }
  }

  chrome.storage.local.get(STORAGE_KEY, function(o) {
    if (o[STORAGE_KEY] === 'off') return;
    if (location.hostname === 'www.youtube.com' || location.hostname === 'youtube.com' ||
        location.hostname === 'm.youtube.com' || location.hostname === 'music.youtube.com') {
      init();
    }
  });
})();
