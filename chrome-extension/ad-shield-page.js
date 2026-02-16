/**
 * AdShield – page context. Overrides window.open, location, top.location, history.pushState.
 * Navigation allowed only: (1) recent trusted user gesture, (2) http/https or relative.
 * Logs blocked attempts to console; no alerts/popups.
 */
(function() {
  'use strict';

  var TRUST_MS = 800;
  var lastTrustedClickTime = 0;

  function allowedProtocol(url) {
    if (!url || typeof url !== 'string') return true;
    var t = String(url).trim().toLowerCase();
    if (t === '' || t[0] === '#' || t[0] === '/' || t[0] === '?') return true;
    if (t.indexOf('http://') === 0 || t.indexOf('https://') === 0) return true;
    return false;
  }

  function sameOrigin(url) {
    try {
      return new URL(url, location.href).origin === location.origin;
    } catch (e) { return false; }
  }

  function trustedRecently() {
    return (Date.now() - lastTrustedClickTime) < TRUST_MS;
  }

  function block(type, url) {
    try {
      console.log('[AdShield] blocked navigation attempt', type, url || '');
    } catch (e) {}
    try {
      document.dispatchEvent(new CustomEvent('privacyMonitorNavBlocked', {
        detail: { type: type, url: (url && String(url).substring(0, 200)) || '' }
      }));
    } catch (e) {}
  }

  // Layer 1: Track trusted user gestures (click, mousedown, pointerdown, touchstart)
  ['click', 'mousedown', 'pointerdown', 'touchstart'].forEach(function(ev) {
    document.addEventListener(ev, function(e) {
      if (e.isTrusted) lastTrustedClickTime = Date.now();
    }, true);
  });

  // Layer 2: Override window.open – block unless trusted gesture + allowed protocol
  try {
    var origOpen = window.open;
    window.open = function(url, target, features) {
      if (!allowedProtocol(url)) {
        block('window.open', url);
        return null;
      }
      if (!trustedRecently()) {
        block('window.open', url);
        return null;
      }
      return origOpen.apply(window, arguments);
    };
  } catch (e) {}

  // Layer 3: Override location.assign and location.replace
  try {
    var origReplace = location.replace.bind(location);
    var origAssign = location.assign.bind(location);
    location.replace = function(url) {
      if (!allowedProtocol(url)) { block('location.replace', url); return; }
      if (!trustedRecently()) { block('location.replace', url); return; }
      origReplace(url);
    };
    location.assign = function(url) {
      if (!allowedProtocol(url)) { block('location.assign', url); return; }
      if (!trustedRecently()) { block('location.assign', url); return; }
      origAssign(url);
    };
  } catch (e) {}

  // Layer 4: Override location.href setter (catches location.href = url and timed redirects)
  try {
    var loc = window.location;
    var proto = loc.constructor && loc.constructor.prototype;
    var desc = proto ? Object.getOwnPropertyDescriptor(proto, 'href') : null;
    if (desc && desc.set) {
      var origSet = desc.set;
      Object.defineProperty(loc, 'href', {
        set: function(v) {
          if (!allowedProtocol(v)) { block('location.href', v); return; }
          if (!trustedRecently()) { block('location.href', v); return; }
          origSet.call(loc, v);
        },
        get: desc.get,
        configurable: true,
        enumerable: desc.enumerable
      });
    }
  } catch (e) {}

  // Layer 5: top.location – when we are top frame, top.location === location (already overridden above)

  // Layer 6: history.pushState abuse – allow only same-origin URL (block external redirect via pushState)
  try {
    var origPushState = history.pushState.bind(history);
    history.pushState = function(state, title, url) {
      if (url != null && url !== '' && !sameOrigin(url)) {
        block('history.pushState', url);
        return;
      }
      origPushState(state, title, url);
    };
  } catch (e) {}
})();
