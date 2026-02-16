/**
 * Redirect unwrapping and malicious-link blocking. Runs in page context; overrides location/link behavior.
 * Data flow: reads privacyMonitorMode and blocklist from chrome.storage.local. No URLs or behavior sent out; no logging.
 */
(function() {
  'use strict';
  var blocklist = [];
  var ready = false;

  var redirectGatewayHosts = [
    'redirectingat.com', 'go.redirectingat.com', 'viglink.com', 'skimlinks.com', 'skimresources.com',
    'propellerads.com', 'popads.net', 'adsterra.com', 'exoclick.com', 'clickadu.com', 'hilltopads.net',
    'adskeeper.com', 'popmyads.com', 'onclkds.com', 'link.media', 'doubleclick.net', 'googleadservices.com',
    'googlesyndication.com', 'adnxs.com', 'criteo.com', 'outbrain.com', 'taboola.com', 'revcontent.com',
    'mgid.com', 'zergnet.com', 'content.ad', 'trafficjunky.com', 'popcash.net', 'adblade.com', 'adbreak.com',
    'redirect.viglink.com', 'redirect.skimlinks.com', 'clicksor.com'
  ];

  var allowlistHosts = [
    'google.com', 'www.google.com', 'google.co.uk', 'google.de', 'google.fr', 'google.it', 'google.es', 'google.ca', 'google.com.au',
    'bing.com', 'www.bing.com', 'duckduckgo.com', 'www.duckduckgo.com', 'startpage.com', 'yahoo.com', 'ecosia.org',
    'chrome.google.com', 'accounts.google.com', 'mail.google.com', 'drive.google.com', 'maps.google.com', 'play.google.com'
  ];

  var maliciousHosts = [
    'support-online.net', 'secure-support.net', 'pc-cleaner.com', 'virus-removal.net', 'fix-your-pc.com',
    'system-alert.net', 'browser-update.net', 'flash-update.net', 'adobe-update.net', 'win-update.net',
    'security-alert.net', 'antivirus-alert.com', 'trojan-removal.net', 'malware-fix.net', 'click2redirect.com',
    'adf.ly', 'bc.vc', 'sh.st', 'adfly.com', 'linkshrink.net', 'shortest.link', 'clk.ink', 'ouo.io', 'exe.io',
    'shink.in', 'clks.pro', 'linkbucks.com', 'adfoc.us', 'cpmlink.net', 'adlock.in', 'megaurl.in', 'urlz.fr',
    'admy.link', 'adshort.co', 'safefilelink.com', 'safelinkconverter.com', 'safelink.icu', 'safelink.xyz',
    'safelink.one', 'urlz.work', 'shorte.st', 'linksfly.me', 'winactivator.com', 'crack-keygen.com',
    'serial-key.net', 'cracks.me', 'tech-support-scam.com', 'microsoft-support.net', 'apple-support.net',
    'amazon-support.net', 'secure-check.net', 'virus-scan.net', 'pc-fix.net', 'driver-update.net',
    'update-flash.com', 'java-update.net', 'browser-check.net', 'antivirus-download.net', 'clean-pc.net',
    'free-soft.com', 'download-fix.net', 'pc-speedup.net', 'registry-cleaner.net', 'error-fix.net'
  ];

  function isMalicious(host) {
    if (!host) return false;
    host = host.toLowerCase();
    for (var i = 0; i < maliciousHosts.length; i++) {
      if (host === maliciousHosts[i] || host.endsWith('.' + maliciousHosts[i])) return true;
    }
    return false;
  }

  function isAllowlisted(host) {
    if (!host) return false;
    host = host.toLowerCase();
    for (var i = 0; i < allowlistHosts.length; i++) {
      if (host === allowlistHosts[i] || host.endsWith('.' + allowlistHosts[i])) return true;
    }
    return false;
  }

  function getHost(url) {
    try { return new URL(url, location.href).hostname.toLowerCase(); } catch (e) { return ''; }
  }

  function isRedirectGateway(host) {
    if (!host) return false;
    for (var i = 0; i < redirectGatewayHosts.length; i++) {
      if (host === redirectGatewayHosts[i] || host.endsWith('.' + redirectGatewayHosts[i])) return true;
    }
    return false;
  }

  function hostBlocked(host) {
    if (!host || !blocklist.length) return false;
    host = host.toLowerCase();
    for (var i = 0; i < blocklist.length; i++) {
      if (host === blocklist[i] || host.endsWith('.' + blocklist[i])) return true;
    }
    return false;
  }

  function tryUnwrapRedirect(href) {
    try {
      var u = new URL(href, location.href);
      var params = u.searchParams;
      var paramNames = ['url', 'u', 'dest', 'destination', 'to', 'target', 'redir', 'redirect', 'go', 'link', 'out', 'ref', 'returnUrl', 'return_to', 'next', 'rurl', 'targetUrl', 'realurl', 'target_url'];
      for (var i = 0; i < paramNames.length; i++) {
        var val = params.get(paramNames[i]);
        if (val && val.indexOf('http') === 0) return val;
        if (val) try { var d = decodeURIComponent(val); if (d.indexOf('http') === 0) return d; } catch (_) {}
      }
      if (u.hash) {
        var idx = u.hash.indexOf('http');
        if (idx !== -1) {
          var rest = u.hash.slice(idx);
          var end = rest.indexOf('&');
          var t = end !== -1 ? rest.slice(0, end) : rest;
          if (t.indexOf('http') === 0) return t;
        }
      }
    } catch (e) {}
    return null;
  }

  function blockNavigate(url) {
    var host = getHost(url);
    if (isAllowlisted(host)) return false;
    if (isMalicious(host)) return true;
    if (hostBlocked(host)) return true;
    return false;
  }

  chrome.storage.local.get(['privacyMonitorMode', 'privacyMonitorBlocklistHosts'], function(o) {
    var mode = o.privacyMonitorMode;
    blocklist = o.privacyMonitorBlocklistHosts || [];
    ready = true;
    if (mode === 'off') return;

    try {
      var origReplace = location.replace;
      var origAssign = location.assign;
      if (typeof origReplace === 'function') {
        location.replace = function(url) {
          if (blockNavigate(url)) return;
          origReplace.call(location, url);
        };
      }
      if (typeof origAssign === 'function') {
        location.assign = function(url) {
          if (blockNavigate(url)) return;
          origAssign.call(location, url);
        };
      }
    } catch (e) {}

    var origOpen = window.open;
    try {
      window.open = function(url, target, features) {
        if (url && blockNavigate(url)) return null;
        return origOpen.call(window, url, target, features);
      };
    } catch (e) {}

    document.addEventListener('click', function(e) {
      if (!ready) return;
      var a = e.target && (e.target.closest ? e.target.closest('a') : e.target.tagName === 'A' ? e.target : null);
      if (!a || !a.href) return;
      var host = getHost(a.href);
      if (isAllowlisted(host)) return;
      if (isMalicious(host)) {
        e.preventDefault();
        e.stopPropagation();
        return false;
      }
      var blocked = blocklist.length && hostBlocked(host);
      var isGateway = isRedirectGateway(host);
      if (!blocked && !isGateway) return;
      var unwrapped = tryUnwrapRedirect(a.href);
      if (unwrapped) {
        var unwrapHost = getHost(unwrapped);
        e.preventDefault();
        e.stopPropagation();
        if (isMalicious(unwrapHost) || (!isAllowlisted(unwrapHost) && blocklist.length && hostBlocked(unwrapHost)))
          return false;
        if ((a.target === '_blank' || a.target === 'blank') && !a.hasAttribute('download'))
          window.open(unwrapped, '_blank', 'noopener');
        else
          location.href = unwrapped;
        return false;
      }
      if (blocked || isGateway) {
        e.preventDefault();
        e.stopPropagation();
        return false;
      }
    }, true);

    function checkMetaRefresh() {
      var meta = document.querySelector('meta[http-equiv="refresh" i]');
      if (meta && meta.content && blocklist.length) {
        var m = meta.content.match(/url\s*=\s*(.+)/i);
        if (m && blockNavigate(m[1].trim())) meta.remove();
      }
    }
    if (document.documentElement) checkMetaRefresh();
    var root = document.documentElement || document.body;
    if (root) {
      var mo = new MutationObserver(checkMetaRefresh);
      mo.observe(root, { childList: true, subtree: true });
    }
  });

  chrome.storage.onChanged.addListener(function(changes, area) {
    if (area === 'local' && changes.privacyMonitorBlocklistHosts)
      blocklist = changes.privacyMonitorBlocklistHosts.newValue || [];
  });
})();
