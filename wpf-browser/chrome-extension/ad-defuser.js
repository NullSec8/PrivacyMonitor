/**
 * Injects ad-defuser-page.js only when blocking is ON. When OFF, does nothing (100% off).
 * Data flow: reads privacyMonitorMode from chrome.storage.local only. No external requests; no logging.
 */
(function() {
  'use strict';
  function run() {
    chrome.storage.local.get('privacyMonitorMode', function(o) {
      if (o.privacyMonitorMode === 'off') return;
      try {
        var root = document.documentElement || document.head || document.body;
        if (root) {
          var s = document.createElement('script');
          s.src = chrome.runtime.getURL('ad-defuser-page.js');
          s.dataset.privacyMonitor = '1';
          root.appendChild(s);
        }
      } catch (e) {}
    });
  }
  run();
})();
