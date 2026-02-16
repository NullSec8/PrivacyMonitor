/**
 * Navigation guard (page context). Overrides window.open and location to block
 * non-http(s) protocols and script-triggered redirects. Only http/https allowed.
 */
(function() {
  'use strict';
  var TRUST_MS = 800;
  var lastTrustedClickTime = 0;

  function allowedProtocol(url) {
    if (!url || typeof url !== 'string') return true;
    var t = url.trim().toLowerCase();
    if (t === '' || t[0] === '#' || t[0] === '/' || t[0] === '?') return true;
    if (t.indexOf('http://') === 0 || t.indexOf('https://') === 0) return true;
    return false;
  }

  function report(type, url) {
    try {
      document.dispatchEvent(new CustomEvent('privacyMonitorNavBlocked', {
        detail: { type: type, url: (url && String(url).substring(0, 200)) || '' }
      }));
    } catch (e) {}
  }

  document.addEventListener('click', function(e) {
    if (e.isTrusted) lastTrustedClickTime = Date.now();
  }, true);

  function trustedRecently() {
    return (Date.now() - lastTrustedClickTime) < TRUST_MS;
  }

  try {
    var origOpen = window.open;
    window.open = function(url, target, features) {
      if (!allowedProtocol(url)) {
        report('window.open', url);
        return null;
      }
      if (!trustedRecently()) {
        report('window.open', url);
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
        if (!allowedProtocol(url)) { report('location.replace', url); return; }
        if (!trustedRecently()) { report('location.replace', url); return; }
        origReplace.call(location, url);
      };
    }
    if (typeof origAssign === 'function') {
      location.assign = function(url) {
        if (!allowedProtocol(url)) { report('location.assign', url); return; }
        if (!trustedRecently()) { report('location.assign', url); return; }
        origAssign.call(location, url);
      };
    }
  } catch (e) {}
})();
