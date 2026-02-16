const fs = require('fs');
const path = require('path');
const js = fs.readFileSync(path.join(__dirname, 'tracker-domains.js'), 'utf8');
const m = js.match(/const BLOCK_KNOWN_DOMAINS = \[([\s\S]*?)\];/);
const domains = m ? m[1].match(/"([^"]+)"/g).map(s => s.slice(1, -1)) : [];

const redirectGateways = [
  'redirectingat.com', 'go.redirectingat.com', 'viglink.com', 'skimlinks.com', 'skimresources.com',
  'propellerads.com', 'popads.net', 'popcash.net', 'adsterra.com', 'exoclick.com', 'trafficjunky.com',
  'clickadu.com', 'hilltopads.net', 'adskeeper.com', 'popmyads.com', 'onclkds.com', 'link.media',
  'ad.doubleclick.net', 'pagead.l.doubleclick.net', 'googleadservices.com', 'googlesyndication.com',
  'doubleclick.net', 'adnxs.com', 'criteo.com', 'outbrain.com', 'taboola.com', 'revcontent.com',
  'mgid.com', 'zergnet.com', 'content.ad', 'taboolasyndication.com', 'outbrainimg.com'
];

const pushAdsDomains = [
  'propellerads.com', 'popads.net', 'popcash.net', 'adsterra.com', 'exoclick.com', 'trafficjunky.com',
  'clickadu.com', 'hilltopads.net', 'adskeeper.com', 'popmyads.com', 'onclkds.com', 'content.ad',
  'mgid.com', 'mgid.io', 'revcontent.com', 'revcontent.io', 'zergnet.com', 'pushcrew.com',
  'onesignal.com', 'izooto.com', 'webpushr.com', 'aimtell.com',
  'push.advertising.com',
  'adsterra.com', 'propellerads.com', 'popads.net', 'adskeeper.com', 'hilltopads.net',
  'adblade.com', 'adbreak.com', 'adbroker.de', 'adbrn.com', 'adbutter.net', 'adcanopus.com',
  'adcel.co', 'adcolony.com', 'adconion.com', 'adform.net', 'adform.com', 'adfox.ru',
  'adhaven.com', 'adhigh.net', 'adincube.com', 'adition.com', 'adkernel.com', 'adk2.com',
  'adlanding.com', 'adlantic.com', 'adlegend.com', 'admarvel.com', 'admedia.com',
  'adnium.com', 'adnxs.com', 'adocean.pl', 'adop.cc', 'adplugg.com', 'adroll.com',
  'adsco.re', 'adsfactor.net', 'adskeeper.com', 'adsnative.com', 'adspirit.de', 'adswizz.com',
  'adtheorent.com', 'advertising.com', 'adzerk.net', 'aerserv.com', 'affiliateb.com',
  'amobee.com', 'appnexus.com', 'atomx.com', 'bidswitch.net', 'brightcom.com', 'broadstreetads.com',
  'celtra.com', 'centro.net', 'chartboost.com', 'clicksor.com', 'connextra.com', 'criteo.com',
  'criteo.net', 'districtm.io', 'doubleclick.net', 'eadv.it', 'emetriq.com', 'engagemedia.org',
  'eplanning.net', 'exoclick.com', 'eyeviewdigital.com', 'faktor.io', 'freewheel.tv',
  'fyber.com', 'gemius.com', 'gumgum.com', 'improvedigital.com', 'inmobi.com', 'inneractive.com',
  'kargo.com', 'kiosked.com', 'lijit.com', 'liveramp.com', 'lockerdome.com', 'loopme.com',
  'madadsmedia.com', 'media.net', 'mediavine.com', 'mediamath.com', 'meetrics.net', 'mgid.com',
  'mobfox.com', 'mobileadtrading.com', 'mopub.com', 'nativo.com', 'nativeads.com', 'nexage.com',
  'onebyaol.com', 'openx.net', 'outbrain.com', 'outbrainimg.com', 'pixel.advertising.com',
  'playground.xyz', 'plista.com', 'postrelease.com', 'powerlinks.com', 'propellerads.com',
  'pubmatic.com', 'pulsepoint.com', 'revcontent.com', 'rhythmone.com', 'richaudience.com',
  'rubiconproject.com', 'sape.ru', 'sharethrough.com', 'smaato.com', 'smartadserver.com',
  'sovrn.com', 'spotxchange.com', 'spotx.tv', 'stackadapt.com', 'stickyadstv.com', 'taboola.com',
  'taboolasyndication.com', 'teads.tv', 'teads.tv', 'triplelift.com', 'tribalfusion.com',
  'truex.com', 'undertone.com', 'vungle.com', 'yieldmo.com', 'yieldbot.com', 'yieldlab.net',
  'zedo.com', 'zergnet.com'
];

// Extra strength: more ad/tracker domains (EasyList-style, public knowledge)
const strengthDomains = [
  'googletagmanager.com', 'googletagservices.com', 'google-analytics.com', 'googleadservices.com',
  'googlesyndication.com', 'doubleclick.net', '2mdn.net', 'googleadservices.com', 'pagead2.googlesyndication.com',
  'stats.g.doubleclick.net', 'tpc.googlesyndication.com', 'securepubads.g.doubleclick.net', 'cm.g.doubleclick.net',
  'adservice.google.com', 'fundingchoices.google.com', 'partner.googleadservices.com', 'www.googleadservices.com',
  'static.doubleclick.net', 'ad.doubleclick.net', 'pagead.l.doubleclick.net', 'pagead46.l.doubleclick.net',
  'static.googleadsserving.com', 'adclick.g.doubleclick.net', 'googleads.g.doubleclick.net',
  'facebook.net', 'connect.facebook.net', 'pixel.facebook.com', 'an.facebook.com', 'graph.facebook.com',
  'clarity.ms', 'bat.bing.com', 'c.bing.com', 'c.msn.com', 'ads-twitter.com', 'static.ads-twitter.com',
  'analytics.twitter.com', 'ads-api.twitter.com', 't.co', 'analytics.tiktok.com', 'ads.tiktok.com',
  'ads.linkedin.com', 'snap.licdn.com', 'px.ads.linkedin.com', 'hotjar.com', 'hotjar.io', 'fullstory.com',
  'mouseflow.com', 'crazyegg.com', 'segment.io', 'segment.com', 'api.segment.io', 'mixpanel.com',
  'amplitude.com', 'heapanalytics.com', 'quantserve.com', 'scorecardresearch.com', 'sb.scorecardresearch.com',
  'omtrdc.net', 'demdex.net', '2o7.net', 'everesttech.net', 'tt.omtrdc.net', 'criteo.com', 'criteo.net',
  'adnxs.com', 'ib.adnxs.com', 'rubiconproject.com', 'pubmatic.com', 'openx.net', 'indexexchange.com',
  'casalemedia.com', 'bidswitch.net', 'media.net', 'sovrn.com', 'smartadserver.com', 'advertising.com',
  'adsrvr.org', 'mathtag.com', 'rfihub.com', 'bluekai.com', 'addthis.com', 'rlcdn.com', 'lotame.com',
  'tapad.com', 'agkn.com', 'moatads.com', 'doubleverify.com', 'adsafeprotected.com', 'flashtalking.com',
  'serving-sys.com', 'amazon-adsystem.com', 'aax.amazon-adsystem.com', 'advertising.amazon.com',
  'sc-static.net', 'tr.snapchat.com', 'ct.pinterest.com', 'trk.pinterest.com', 'appsflyer.com',
  'adjust.com', 'app.adjust.com', 'kochava.com', 'singular.net', 'optimizely.com', 'cdn.optimizely.com',
  'vwo.com', 'sharethis.com', 'disqus.com', 'disquscdn.com', 'pixel.wp.com', 'stats.wp.com', 'mc.yandex.ru',
  'chartbeat.com', 'static.chartbeat.com', 'parsely.com', 'newrelic.com', 'nr-data.net', 'bam.nr-data.net',
  'sentry.io', 'bugsnag.com', 'branch.io', 'app.link', 'marketo.net', 'mktoresp.com', 'hubspot.com',
  'ensighten.com', 'addtoany.com', 'cookiebot.com', 'onetrust.com', 'cookielaw.org', 'trustarc.com',
  'consensu.org', 'sourcepoint.com', 'didomi.io', 'quantcast.com', 'iab.eu', 'zendesk.com', 'zdassets.com',
  'drift.com', 'intercom.io', 'intercomcdn.com', 'tawk.to', 'crisp.chat', 'tealiumiq.com', 'tags.tiqcdn.com',
  'awin1.com', 'shareasale.com', 'dpbolvw.net', 'impact.com', 'partnerize.com', 'clickbank.net',
  'mgid.io', 'revcontent.io', 'content.ad', 'gravity.com', 'adcolony.com', 'unity3d.com', 'vungle.com',
  'inmobi.com', 'chartboost.com', 'fyber.com', 'tapjoy.com', 'freewheel.tv', 'stickyadstv.com', 'teads.tv',
  'reddit.com', 'ads.reddit.com', 'events.redditmedia.com', 'braze.com', 'cdn.braze.com', 'clevertap.com',
  'zeotap.com', 'zeotap.io', 'liveramp.com', 'rudderstack.com', 'rs.rudderstack.com', 'posthog.com',
  'us.posthog.com', 'launchdarkly.com', 'cdn.launchdarkly.com', 'acsb.com', 'identity.liveintent.com',
  'idsync.rlcdn.com', 'ads.stickyadstv.com', 'contextweb.com', 'sitescout.com', 'nextroll.com', 'adroll.com',
  'smartyads.com', 'e-planning.net', 'widespace.com', 'adgrx.com', 'adhigh.net', 'adform.net', 'adform.com',
  'lijit.com', 'connextra.com', 'contentrecommendation.net', 'bidtellect.com', 'zemanta.com', 'connatix.com',
  'improvedigital.com', 'synacor.com', 'audiencescience.com', 'omnicomgroup.com', 'facebook.com',
  'firebase-settings.crashlytics.com', 'plausible.io', 'matomo.cloud', 'adtech.com', 'appnexus.com',
  'bidvertiser.com', 'bounceexchange.com', 'dotomi.com', 'exelator.com', 'fastclick.net', 'fwmrm.net',
  'gigya.com', 'imrworldwide.com', 'liveintent.com', 'livere.com', 'lkqd.net', 'loopme.com', 'mediamath.com',
  'mookie1.com', 'nexage.com', 'perfectaudience.com', 'reachlocal.com', 'richaudience.com', 'simpli.fi',
  'sonobi.com', 'spotxchange.com', 'spotx.tv', 'stackadapt.com', 'theadhost.com', 'tribalfusion.com',
  'turn.com', 'undertone.com', 'yieldoptimizer.com', 'zqtk.net', 'pippio.com', 'liadm.com', 'intentiq.com',
  'eyeota.net', 'moatpixel.com', 'fls-na.amazon.com', 'assoc-amazon.com', 'leadbolt.net',
  'business-api.tiktok.com', 'ads-api.tiktok.com', 'wizrocket.com', 'pardot.com', 'hs-analytics.net',
  'hs-scripts.com', 'improvedigital.com', 'video-ad-stats.googlesyndication.com', 'd.adroll.com',
  'adsymptotic.com', 'atomx.com', 'eyeviewdigital.com', 'insightexpressai.com', 'pulsead.ai',
  'nr-data.net', 'bam.nr-data.net', 'adblockanalytics.com', 'blockadblock.com', 'acceptableads.com',
  'pcookie.net', 'smartadserver.com', 'adzerk.net', 'advertising.com', 'criteo.com', 'outbrain.com',
  'taboola.com', 'yieldmo.com', 'triplelift.com', 'sharethrough.com', '33across.com', 'openx.net',
  'rubiconproject.com', 'pubmatic.com', 'spotx.tv', 'freewheel.tv', 'teads.tv', 'connextra.com',
  'lijit.com', 'sovrn.com', 'aerserv.com', 'tremorvideo.com', 'unruly.co', 'videohub.com', 'yieldlab.net',
  'adform.com', 'adform.net', 'smartyads.com', 'adroll.com', 'sitescout.com', 'adhigh.net', 'adgrx.com',
  'liveramp.com', 'liveintent.com', 'zeotap.com', 'krxd.net', 'bluekai.com', 'lotame.com', 'tapad.com',
  'crossix.com', 'datalogix.com', 'epsilon.com', 'acxiom.com', 'experian.com', 'targusinfo.com',
  'adsymptotic.com', 'adtech.com', 'advertising.com', 'collective.com', 'undertone.com', 'specificmedia.com',
  'invitemedia.com', 'rightmedia.com', 'admeld.com', 'admeta.com', 'adnxs.com', 'adroll.com',
  'advertising.amazon.com', 'amazon-adsystem.com', 'aax.amazon-adsystem.com', 'c.amazon-adsystem.com',
  'w.amazon-adsystem.com', 'ir-na.amazon-adsystem.com', 'ads.pubmatic.com', 'tracking.leadlander.com',
  'tracking.g2crowd.com', 'tracking.leadforensics.com', 'tracking.tracklead.com', 'trackcmp.net',
  'tracking.musixmatch.com', 'tracking.onefeed.co.uk', 'tracking.omgpm.com', 'tracking.parsely.com',
  'tracking.podtrac.com', 'tracking.simpli.fi', 'tracking.sojern.com', 'tracking.thinkbright.com',
  'tracking.vietnamnet.vn', 'tracking.waterfrontmedia.com', 'tracking.yellowpages.com', 'tracking101.com',
  'tracking202.com', 'trackingsoft.com', 'trackingtraffic.com', 'trackmyself.com', 'trackvoluum.com',
  'trafficadept.com', 'trafficforce.com', 'trafficjunky.com', 'trafficstars.com', 'trafficsynergy.com',
  'trafficvance.com', 'trafficzen.com', 'tribalfusion.com', 'triggit.com', 'truex.com', 'tubemogul.com',
  'turn.com', 'twenga.com', 'tynt.com', 'undertone.com', 'unrulymedia.com', 'valueclick.com',
  'valueclickmedia.com', 'videohub.com', 'viewablemedia.com', 'vindicosuite.com', 'visualdna.com',
  'vungle.com', 'w55c.net', 'webtrekk.net', 'webtrends.com', 'webtrendslive.com', 'widespace.com',
  'wunderloop.net', 'xad.com', 'xaxis.com', 'xe.com', 'xertive.com', 'xlive.com', 'yieldbot.com',
  'yieldlab.net', 'yieldmanager.com', 'yieldmo.com', 'yieldoptimizer.com', 'yieldpartners.com',
  'yieldbuild.com', 'yieldr.com', 'zedo.com', 'zergnet.com', 'zeotap.com', 'zqtk.net', '1und1.de',
  '2mdn.net', '4dsply.com', '33across.com', 'adform.net', 'adnxs.com', 'adroll.com', 'adsrvr.org',
  'advertising.com', 'amazon-adsystem.com', 'criteo.com', 'doubleclick.net', 'facebook.com',
  'google-analytics.com', 'googletagmanager.com', 'googlesyndication.com', 'googleadservices.com',
  'hotjar.com', 'linkedin.com', 'mathtag.com', 'media.net', 'openx.net', 'outbrain.com', 'pubmatic.com',
  'quantserve.com', 'rubiconproject.com', 'scorecardresearch.com', 'taboola.com', 'twitter.com',
  'yieldmo.com', 's0.2mdn.net', 'tpc.googlesyndication.com', 'pagead2.googlesyndication.com',
  'static.doubleclick.net', 'ad.doubleclick.net', 'pagead.l.doubleclick.net', 'cm.g.doubleclick.net',
  'securepubads.g.doubleclick.net', 'stats.g.doubleclick.net', 'googleads.g.doubleclick.net',
  'adclick.g.doubleclick.net', 'pagead46.l.doubleclick.net', 'static.googleadsserving.com',
  'partner.googleadservices.com', 'www.googleadservices.com', 'fundingchoices.google.com',
  'pub.doubleverify.com', 'doubleverify.com'
];

const easyListExtra = [
  'addthis.com', 'addthisedge.com', 'addthiscdn.com', 's7.addthis.com', 'm.addthis.com',
  'connect.facebook.net', 'static.ads-twitter.com', 'platform.twitter.com', 'syndication.twitter.com',
  'platform.linkedin.com', 'snap.licdn.com', 'bat.bing.com', 'c.bing.com', 'ads.pubmatic.com',
  'securepubads.g.doubleclick.net', 'tpc.googlesyndication.com', 'pagead2.googlesyndication.com',
  'static.ads-twitter.com', 'analytics.tiktok.com', 'ads.tiktok.com', 'business-api.tiktok.com',
  'px.ads.linkedin.com', 'px4.ads.linkedin.com', 'tracking.leadlander.com', 'tracking.parsely.com',
  'dpm.demdex.net', 'cm.everesttech.net', 'pixel.quantserve.com', 'edge.quantserve.com',
  'segment.io', 'cdn.segment.com', 'api.segment.io', 'cdn.amplitude.com', 'api.mixpanel.com',
  'cdn.heapanalytics.com', 'cdn.hotjar.com', 'static.hotjar.com', 'script.hotjar.com',
  'clarity.ms', 'www.clarity.ms', 'c.clarity.ms', 'chaturbate.com', 'livejasmin.com',
  'adserver.adtechus.com', 'adserver.adtech.de', 'ads.advertising.com', 'ads.pointroll.com',
  'ads.stickyadstv.com', 'adsymptotic.com', 'adtrgt.com', 'ag.innity.com', 'ag.umeng.com',
  'amung.us', 'api.adsymptotic.com', 'api.branch.io', 'api.rlcdn.com', 'b.scorecardresearch.com',
  'beacon.krxd.net', 'beacon.walmart.com', 'cdn.adsymptotic.com', 'cdn.branch.io', 'cdn.permutive.com',
  'd.agkn.com', 'd2c.adsymptotic.com', 'dc.adsymptotic.com',
  'eus.rubiconproject.com', 'exelator.com', 'fastlane.rubiconproject.com', 'geo.adsymptotic.com',
  'googleads.g.doubleclick.net', 'ib.adnxs.com', 'image2.pubmatic.com', 'image3.pubmatic.com',
  'js-sec.indexww.com', 'match.adsrvr.org', 'match.prod.bidr.io', 'ml314.com', 'pixel.adsafeprotected.com',
  'pixel.mathtag.com', 'pixel.rubiconproject.com', 'pixel.tapad.com', 'prebid.adnxs.com',
  'rtb.mfadsrvr.com', 'sb.scorecardresearch.com', 'secure-ds.serving-sys.com', 'sync.adsymptotic.com',
  'tag.advertising.com', 'track.adsymptotic.com', 'tracking.adsymptotic.com', 'us-u.openx.net',
  'video-ad-stats.googlesyndication.com', 'widgets.pinterest.com', 'www.googletagmanager.com',
  'x.dlx.addthis.com', 'cdn.onesignal.com', 'onesignal.com', 'log.optimizely.com'
];

const allDomains = [...new Set([...domains, ...redirectGateways, ...pushAdsDomains, ...strengthDomains, ...easyListExtra])];
const resourceTypesBlock = ['main_frame', 'sub_frame', 'image', 'stylesheet', 'media', 'other'];
const resourceTypesStealth = ['script', 'xmlhttprequest'];
const emptyJs = 'data:text/javascript,';
const emptyHtml = 'data:text/html,<!DOCTYPE html><html><head></head><body></body></html>';

const rulesBlock = allDomains.map((d, i) => ({
  id: i + 1,
  priority: 1,
  action: { type: 'block' },
  condition: {
    urlFilter: '||' + d + '^',
    resourceTypes: resourceTypesBlock,
    isUrlFilterCaseSensitive: false
  }
}));

const rulesStealth = allDomains.map((d, i) => ({
  id: i + 1,
  priority: 2,
  action: { type: 'redirect', redirect: { url: emptyJs } },
  condition: {
    urlFilter: '||' + d + '^',
    resourceTypes: resourceTypesStealth,
    isUrlFilterCaseSensitive: false
  }
}));

fs.writeFileSync(path.join(__dirname, 'rules.json'), JSON.stringify(rulesBlock));
fs.writeFileSync(path.join(__dirname, 'rules-stealth.json'), JSON.stringify(rulesStealth));
console.log('Wrote', rulesBlock.length, 'block rules to rules.json');
console.log('Wrote', rulesStealth.length, 'stealth (redirect) rules to rules-stealth.json');
