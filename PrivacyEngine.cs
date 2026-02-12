using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace PrivacyMonitor
{
    public static class PrivacyEngine
    {
        // ════════════════════════════════════════════════════════════
        //  STRUCTURED TRACKER DATABASE  (~220 services)
        //  Each entry: Domain, Label, Company, Category, RiskWeight(1-5)
        // ════════════════════════════════════════════════════════════

        private static readonly TrackerInfo[] TrackerDatabase;
        private static readonly Dictionary<string, TrackerInfo> TrackerLookup; // O(1) suffix match cache
        private static readonly string[] TrackerDomainSuffixes; // sorted for binary search

        static PrivacyEngine()
        {
            // Build structured tracker database from compact tuples
            var raw = new (string D, string L, string C, TrackerCategory Cat, int R)[]
            {
                // ── Google / Alphabet ──
                ("google-analytics.com",    "Google Analytics",         "Google",   TrackerCategory.Analytics,    4),
                ("googletagmanager.com",    "Google Tag Manager",       "Google",   TrackerCategory.Analytics,    4),
                ("doubleclick.net",         "Google DoubleClick",       "Google",   TrackerCategory.Advertising,  5),
                ("googlesyndication.com",   "Google AdSense",           "Google",   TrackerCategory.Advertising,  5),
                ("googleadservices.com",    "Google Ads Conversion",    "Google",   TrackerCategory.Advertising,  5),
                ("adservice.google.com",    "Google Ad Service",        "Google",   TrackerCategory.Advertising,  5),
                ("googletagservices.com",   "Google Tag Services",      "Google",   TrackerCategory.Analytics,    4),
                ("googleoptimize.com",      "Google Optimize",          "Google",   TrackerCategory.Analytics,    3),
                ("firebaseinstallations.googleapis.com","Firebase Tracking","Google",TrackerCategory.Analytics,   3),
                ("firebase-settings.crashlytics.com","Firebase Crashlytics","Google",TrackerCategory.Analytics,  2),
                ("firebaselogging.googleapis.com","Firebase Logging",   "Google",   TrackerCategory.Analytics,    3),
                ("app-measurement.com",     "Firebase Analytics",       "Google",   TrackerCategory.Analytics,    4),
                ("pagead2.googlesyndication.com","Google Ads",          "Google",   TrackerCategory.Advertising,  5),

                // ── Meta / Facebook ──
                ("facebook.net",            "Facebook SDK",             "Meta",     TrackerCategory.Social,       5),
                ("facebook.com/tr",         "Facebook Pixel",           "Meta",     TrackerCategory.Advertising,  5),
                ("connect.facebook.net",    "Facebook Connect",         "Meta",     TrackerCategory.Social,       5),
                ("graph.facebook.com",      "Facebook Graph API",       "Meta",     TrackerCategory.Social,       4),
                ("pixel.facebook.com",      "Facebook Pixel",           "Meta",     TrackerCategory.Advertising,  5),
                ("an.facebook.com",         "Facebook Audience Network","Meta",     TrackerCategory.Advertising,  5),

                // ── Microsoft ──
                ("clarity.ms",              "Microsoft Clarity",        "Microsoft",TrackerCategory.SessionReplay,4),
                ("bat.bing.com",            "Bing UET",                 "Microsoft",TrackerCategory.Advertising,  4),
                ("c.bing.com",              "Bing Tracking",            "Microsoft",TrackerCategory.Analytics,    3),
                ("c.msn.com",               "MSN Tracking",             "Microsoft",TrackerCategory.Analytics,    3),

                // ── Twitter / X ──
                ("t.co",                    "Twitter Click Tracking",   "X Corp",   TrackerCategory.Social,       4),
                ("analytics.twitter.com",   "Twitter Analytics",        "X Corp",   TrackerCategory.Analytics,    4),
                ("ads-twitter.com",         "Twitter Ads",              "X Corp",   TrackerCategory.Advertising,  5),
                ("static.ads-twitter.com",  "Twitter Ads SDK",          "X Corp",   TrackerCategory.Advertising,  4),

                // ── TikTok ──
                ("analytics.tiktok.com",    "TikTok Analytics",         "ByteDance",TrackerCategory.Analytics,    5),
                ("analytics-sg.tiktok.com", "TikTok Analytics SG",      "ByteDance",TrackerCategory.Analytics,    5),
                ("mon.tiktokv.com",         "TikTok Monitoring",        "ByteDance",TrackerCategory.Analytics,    4),

                // ── LinkedIn ──
                ("ads.linkedin.com",        "LinkedIn Ads",             "Microsoft",TrackerCategory.Advertising,  4),
                ("snap.licdn.com",          "LinkedIn Insight",         "Microsoft",TrackerCategory.Analytics,    4),
                ("px.ads.linkedin.com",     "LinkedIn Conversion",      "Microsoft",TrackerCategory.Advertising,  4),

                // ── Pinterest ──
                ("ct.pinterest.com",        "Pinterest Tag",            "Pinterest",TrackerCategory.Advertising,  3),
                ("trk.pinterest.com",       "Pinterest Tracking",       "Pinterest",TrackerCategory.Advertising,  3),

                // ── Snapchat ──
                ("sc-static.net",           "Snapchat SDK",             "Snap Inc",TrackerCategory.Social,        3),
                ("tr.snapchat.com",         "Snapchat Tracking",        "Snap Inc",TrackerCategory.Advertising,   4),

                // ── Amazon ──
                ("amazon-adsystem.com",     "Amazon Advertising",       "Amazon",  TrackerCategory.Advertising,   4),
                ("aax.amazon-adsystem.com", "Amazon AAX",               "Amazon",  TrackerCategory.Advertising,   4),
                ("fls-na.amazon.com",       "Amazon Tracking",          "Amazon",  TrackerCategory.Analytics,     3),
                ("assoc-amazon.com",        "Amazon Associates",        "Amazon",  TrackerCategory.Affiliate,     3),

                // ── Adobe ──
                ("omtrdc.net",              "Adobe Analytics",          "Adobe",   TrackerCategory.Analytics,      4),
                ("demdex.net",              "Adobe Audience Mgr",       "Adobe",   TrackerCategory.DMP,            5),
                ("2o7.net",                 "Adobe Analytics Legacy",   "Adobe",   TrackerCategory.Analytics,      4),
                ("everesttech.net",         "Adobe Ad Cloud",           "Adobe",   TrackerCategory.Advertising,    4),
                ("tt.omtrdc.net",           "Adobe Target",             "Adobe",   TrackerCategory.Analytics,      3),

                // ── Session Replay ──
                ("hotjar.com",              "Hotjar Heatmaps",          "Hotjar",      TrackerCategory.SessionReplay, 5),
                ("hotjar.io",               "Hotjar",                   "Hotjar",      TrackerCategory.SessionReplay, 5),
                ("fullstory.com",           "FullStory Replay",         "FullStory",   TrackerCategory.SessionReplay, 5),
                ("rs.fullstory.com",        "FullStory",                "FullStory",   TrackerCategory.SessionReplay, 5),
                ("mouseflow.com",           "Mouseflow Replay",         "Mouseflow",   TrackerCategory.SessionReplay, 5),
                ("crazyegg.com",            "Crazy Egg Heatmaps",       "Crazy Egg",   TrackerCategory.SessionReplay, 4),
                ("inspectlet.com",          "Inspectlet Replay",        "Inspectlet",  TrackerCategory.SessionReplay, 5),
                ("logrocket.io",            "LogRocket Replay",         "LogRocket",   TrackerCategory.SessionReplay, 5),
                ("smartlook.com",           "Smartlook Replay",         "Smartlook",   TrackerCategory.SessionReplay, 5),
                ("luckyorange.com",         "Lucky Orange",             "Lucky Orange",TrackerCategory.SessionReplay, 4),
                ("luckyorange.net",         "Lucky Orange",             "Lucky Orange",TrackerCategory.SessionReplay, 4),

                // ── Analytics Platforms ──
                ("newrelic.com",            "New Relic APM",            "New Relic",   TrackerCategory.Analytics, 2),
                ("nr-data.net",             "New Relic Data",           "New Relic",   TrackerCategory.Analytics, 2),
                ("bam.nr-data.net",         "New Relic Browser",        "New Relic",   TrackerCategory.Analytics, 3),
                ("segment.io",              "Segment CDP",              "Twilio",      TrackerCategory.Analytics, 4),
                ("segment.com",             "Segment CDP",              "Twilio",      TrackerCategory.Analytics, 4),
                ("api.segment.io",          "Segment CDP",              "Twilio",      TrackerCategory.Analytics, 4),
                ("mixpanel.com",            "Mixpanel",                 "Mixpanel",    TrackerCategory.Analytics, 4),
                ("api.mixpanel.com",        "Mixpanel",                 "Mixpanel",    TrackerCategory.Analytics, 4),
                ("amplitude.com",           "Amplitude",                "Amplitude",   TrackerCategory.Analytics, 4),
                ("api.amplitude.com",       "Amplitude",                "Amplitude",   TrackerCategory.Analytics, 4),
                ("heapanalytics.com",       "Heap Analytics",           "Heap",        TrackerCategory.Analytics, 4),
                ("plausible.io",            "Plausible",                "Plausible",   TrackerCategory.Analytics, 1),
                ("matomo.cloud",            "Matomo",                   "Matomo",      TrackerCategory.Analytics, 1),
                ("sentry.io",              "Sentry Error",              "Sentry",      TrackerCategory.Analytics, 2),
                ("bugsnag.com",            "Bugsnag",                   "Bugsnag",     TrackerCategory.Analytics, 2),
                ("chartbeat.com",          "Chartbeat",                 "Chartbeat",   TrackerCategory.Analytics, 3),
                ("static.chartbeat.com",   "Chartbeat",                 "Chartbeat",   TrackerCategory.Analytics, 3),
                ("scorecardresearch.com",  "comScore",                  "comScore",    TrackerCategory.Analytics, 4),
                ("sb.scorecardresearch.com","comScore Beacon",          "comScore",    TrackerCategory.Analytics, 4),
                ("quantserve.com",         "Quantcast",                 "Quantcast",   TrackerCategory.Analytics, 4),
                ("parsely.com",            "Parse.ly",                  "Automattic",  TrackerCategory.Analytics, 3),

                // ── A/B Testing ──
                ("optimizely.com",          "Optimizely",               "Optimizely",  TrackerCategory.Analytics, 3),
                ("cdn.optimizely.com",      "Optimizely",               "Optimizely",  TrackerCategory.Analytics, 3),
                ("vwo.com",                 "VWO A/B Testing",          "Wingify",     TrackerCategory.Analytics, 3),

                // ── Attribution / Mobile ──
                ("appsflyer.com",          "AppsFlyer",                 "AppsFlyer",   TrackerCategory.Attribution, 4),
                ("adjust.com",             "Adjust",                    "Adjust",      TrackerCategory.Attribution, 4),
                ("app.adjust.com",         "Adjust",                    "Adjust",      TrackerCategory.Attribution, 4),
                ("branch.io",              "Branch",                    "Branch",      TrackerCategory.Attribution, 3),
                ("app.link",               "Branch Links",              "Branch",      TrackerCategory.Attribution, 3),
                ("kochava.com",            "Kochava",                   "Kochava",     TrackerCategory.Attribution, 4),
                ("singular.net",           "Singular",                  "Singular",    TrackerCategory.Attribution, 4),

                // ── Ad Exchanges / SSPs / DSPs ──
                ("criteo.com",             "Criteo Retargeting",        "Criteo",      TrackerCategory.Advertising, 5),
                ("criteo.net",             "Criteo",                    "Criteo",      TrackerCategory.Advertising, 5),
                ("outbrain.com",           "Outbrain Native Ads",       "Outbrain",    TrackerCategory.Advertising, 4),
                ("outbrainimg.com",        "Outbrain",                  "Outbrain",    TrackerCategory.Advertising, 4),
                ("taboola.com",            "Taboola Native Ads",        "Taboola",     TrackerCategory.Advertising, 4),
                ("adnxs.com",              "Xandr/AppNexus",            "Microsoft",   TrackerCategory.Advertising, 5),
                ("ib.adnxs.com",           "AppNexus",                  "Microsoft",   TrackerCategory.Advertising, 5),
                ("rubiconproject.com",     "Rubicon Project",           "Magnite",     TrackerCategory.Advertising, 4),
                ("pubmatic.com",           "PubMatic",                  "PubMatic",    TrackerCategory.Advertising, 4),
                ("ads.pubmatic.com",       "PubMatic",                  "PubMatic",    TrackerCategory.Advertising, 4),
                ("openx.net",              "OpenX",                     "OpenX",       TrackerCategory.Advertising, 4),
                ("casalemedia.com",        "Index Exchange",            "Index Exch",  TrackerCategory.Advertising, 4),
                ("indexexchange.com",      "Index Exchange",            "Index Exch",  TrackerCategory.Advertising, 4),
                ("bidswitch.net",          "BidSwitch",                 "IPONWEB",     TrackerCategory.Advertising, 4),
                ("media.net",              "Media.net",                 "Media.net",   TrackerCategory.Advertising, 4),
                ("sharethrough.com",       "Sharethrough",              "Sharethrough",TrackerCategory.Advertising, 4),
                ("33across.com",           "33Across",                  "33Across",    TrackerCategory.Advertising, 4),
                ("triplelift.com",         "TripleLift",                "TripleLift",  TrackerCategory.Advertising, 4),
                ("yieldmo.com",            "Yieldmo",                   "Yieldmo",     TrackerCategory.Advertising, 4),
                ("sovrn.com",              "Sovrn",                     "Sovrn",       TrackerCategory.Advertising, 3),
                ("lijit.com",              "Sovrn/Lijit",               "Sovrn",       TrackerCategory.Advertising, 3),
                ("smartadserver.com",      "Smart AdServer",            "Equativ",     TrackerCategory.Advertising, 4),
                ("advertising.com",        "Verizon Advertising",       "Verizon",     TrackerCategory.Advertising, 4),

                // ── DSPs ──
                ("adsrvr.org",             "The Trade Desk",            "TTD",         TrackerCategory.Advertising, 5),
                ("mathtag.com",            "MediaMath",                 "MediaMath",   TrackerCategory.Advertising, 4),
                ("rfihub.com",             "Sizmek",                    "Amazon",      TrackerCategory.Advertising, 4),

                // ── DMPs / Data Brokers ──
                ("bluekai.com",            "Oracle BlueKai",            "Oracle",      TrackerCategory.DMP, 5),
                ("addthis.com",            "Oracle AddThis",            "Oracle",      TrackerCategory.DMP, 4),
                ("krxd.net",               "Salesforce Krux",           "Salesforce",  TrackerCategory.DMP, 5),
                ("exelator.com",           "Nielsen eXelate",           "Nielsen",     TrackerCategory.DMP, 5),
                ("rlcdn.com",              "LiveRamp Identity",         "LiveRamp",    TrackerCategory.DMP, 5),
                ("pippio.com",             "LiveRamp",                  "LiveRamp",    TrackerCategory.DMP, 5),
                ("eyeota.net",             "Eyeota",                    "Dun & Brad",  TrackerCategory.DMP, 5),
                ("liadm.com",              "LiveIntent",                "LiveIntent",  TrackerCategory.DMP, 4),
                ("lotame.com",             "Lotame DMP",                "Lotame",      TrackerCategory.DMP, 5),
                ("crwdcntrl.net",          "Lotame",                    "Lotame",      TrackerCategory.DMP, 5),
                ("tapad.com",              "Tapad Cross-Device",        "Experian",    TrackerCategory.DMP, 5),
                ("intentiq.com",           "Intent IQ",                 "Intent IQ",   TrackerCategory.DMP, 5),
                ("agkn.com",               "Neustar",                   "TransUnion",  TrackerCategory.DMP, 5),

                // ── Ad Verification ──
                ("moatads.com",            "Moat Viewability",          "Oracle",      TrackerCategory.AdVerification, 3),
                ("moatpixel.com",          "Moat Pixel",                "Oracle",      TrackerCategory.AdVerification, 3),
                ("doubleverify.com",       "DoubleVerify",              "DoubleVerify",TrackerCategory.AdVerification, 3),
                ("adsafeprotected.com",    "IAS Verification",          "IAS",         TrackerCategory.AdVerification, 3),
                ("serving-sys.com",        "Sizmek Ad Server",          "Amazon",      TrackerCategory.Advertising, 3),
                ("flashtalking.com",       "Flashtalking",              "Mediaocean",  TrackerCategory.Advertising, 3),

                // ── Social Widgets ──
                ("sharethis.com",          "ShareThis",                 "ShareThis",   TrackerCategory.Social, 3),
                ("disqus.com",             "Disqus Tracking",           "Disqus",      TrackerCategory.Social, 4),
                ("disquscdn.com",          "Disqus",                    "Disqus",      TrackerCategory.Social, 3),
                ("addtoany.com",           "AddToAny",                  "AddToAny",    TrackerCategory.Social, 2),

                // ── WordPress ──
                ("pixel.wp.com",           "WordPress Pixel",           "Automattic",  TrackerCategory.Analytics, 3),
                ("stats.wp.com",           "WordPress Stats",           "Automattic",  TrackerCategory.Analytics, 3),

                // ── Yandex ──
                ("mc.yandex.ru",           "Yandex Metrica",            "Yandex",      TrackerCategory.Analytics, 4),

                // ── Consent Management (track consent itself) ──
                ("cookiebot.com",          "Cookiebot CMP",             "Usercentrics",TrackerCategory.CMP, 2),
                ("onetrust.com",           "OneTrust CMP",              "OneTrust",    TrackerCategory.CMP, 2),
                ("cookielaw.org",          "OneTrust/Cookielaw",        "OneTrust",    TrackerCategory.CMP, 2),
                ("trustarc.com",           "TrustArc CMP",              "TrustArc",    TrackerCategory.CMP, 2),
                ("consensu.org",           "IAB TCF",                   "IAB",         TrackerCategory.CMP, 1),

                // ── Affiliate Networks ──
                ("awin1.com",              "Awin Affiliate",            "Awin",        TrackerCategory.Affiliate, 3),
                ("shareasale.com",         "ShareASale",                "Awin",        TrackerCategory.Affiliate, 3),
                ("dpbolvw.net",            "CJ Affiliate",              "Publicis",    TrackerCategory.Affiliate, 3),
                ("impact.com",             "Impact",                    "Impact",      TrackerCategory.Affiliate, 3),
                ("partnerize.com",         "Partnerize",                "Partnerize",  TrackerCategory.Affiliate, 3),
                ("clickbank.net",          "ClickBank",                 "ClickBank",   TrackerCategory.Affiliate, 3),

                // ── Marketing Automation ──
                ("hs-analytics.net",       "HubSpot Analytics",         "HubSpot",     TrackerCategory.Analytics,  3),
                ("hubspot.com",            "HubSpot Tracking",          "HubSpot",     TrackerCategory.Analytics,  3),
                ("hs-scripts.com",         "HubSpot Scripts",           "HubSpot",     TrackerCategory.Analytics,  3),
                ("marketo.net",            "Marketo",                   "Adobe",       TrackerCategory.Analytics,  3),
                ("mktoresp.com",           "Marketo",                   "Adobe",       TrackerCategory.Analytics,  3),
                ("pardot.com",             "Salesforce Pardot",         "Salesforce",  TrackerCategory.Analytics,  3),

                // ── Chat / Support (with tracking) ──
                ("drift.com",              "Drift Chat",                "Salesloft",   TrackerCategory.Analytics,  2),
                ("intercom.io",            "Intercom",                  "Intercom",    TrackerCategory.Analytics,  3),
                ("intercomcdn.com",        "Intercom CDN",              "Intercom",    TrackerCategory.Analytics,  2),
                ("zendesk.com",            "Zendesk",                   "Zendesk",     TrackerCategory.Analytics,  2),
                ("zdassets.com",           "Zendesk Assets",            "Zendesk",     TrackerCategory.Analytics,  1),
                ("tawk.to",                "Tawk.to",                   "Tawk.to",     TrackerCategory.Analytics,  2),
                ("crisp.chat",             "Crisp Chat",                "Crisp",       TrackerCategory.Analytics,  2),

                // ── Tag Managers ──
                ("tealiumiq.com",          "Tealium",                   "Tealium",     TrackerCategory.Analytics,  3),
                ("tags.tiqcdn.com",        "Tealium CDN",               "Tealium",     TrackerCategory.Analytics,  3),
                ("ensighten.com",          "Ensighten",                 "Ensighten",   TrackerCategory.Analytics,  3),

                // ── Native / content ad networks ──
                ("mgid.com",               "MGID Native Ads",           "MGID",        TrackerCategory.Advertising, 5),
                ("mgid.io",                "MGID",                      "MGID",        TrackerCategory.Advertising,  5),
                ("revcontent.com",         "Revcontent",                "Revcontent",   TrackerCategory.Advertising, 4),
                ("revcontent.io",          "Revcontent",                "Revcontent",   TrackerCategory.Advertising, 4),
                ("zergnet.com",            "Zergnet",                   "Zergnet",     TrackerCategory.Advertising, 4),
                ("content.ad",             "Content.ad",                "Content.ad",   TrackerCategory.Advertising, 4),
                ("undertone.com",          "Undertone",                 "Undertone",   TrackerCategory.Advertising, 4),
                ("gravity.com",            "Gravity",                   "AOL",         TrackerCategory.Advertising, 3),
                ("adcolony.com",           "AdColony",                  "AdColony",    TrackerCategory.Advertising, 4),
                ("unity3d.com",            "Unity Ads",                 "Unity",       TrackerCategory.Advertising, 4),
                ("vungle.com",             "Vungle",                    "Liftoff",     TrackerCategory.Advertising, 4),
                ("inmobi.com",             "InMobi",                    "InMobi",      TrackerCategory.Advertising, 4),
                ("chartboost.com",         "Chartboost",                "Chartboost",   TrackerCategory.Advertising, 3),
                ("fyber.com",              "Fyber",                     "Digital Turbine", TrackerCategory.Advertising, 4),
                ("tapjoy.com",             "Tapjoy",                    "Tapjoy",      TrackerCategory.Advertising, 4),
                ("leadbolt.net",           "Leadbolt",                  "Leadbolt",    TrackerCategory.Advertising, 3),
                ("spotxchange.com",        "SpotX",                     "Magnite",     TrackerCategory.Advertising, 4),
                ("spotx.tv",               "SpotX",                     "Magnite",     TrackerCategory.Advertising, 4),
                ("freewheel.tv",           "FreeWheel",                 "Comcast",     TrackerCategory.Advertising, 4),
                ("stickyadstv.com",        "Sticky Ads",                "Comcast",     TrackerCategory.Advertising, 4),
                ("teads.tv",               "Teads",                     "Teads",       TrackerCategory.Advertising, 4),
                ("outbrain.com",           "Outbrain",                  "Outbrain",    TrackerCategory.Advertising, 4),
                ("zemanta.com",            "Zemanta",                   "Outbrain",    TrackerCategory.Advertising, 3),
                ("connatix.com",           "Connatix",                  "Connatix",   TrackerCategory.Advertising, 3),
                ("taboola.com",            "Taboola",                   "Taboola",    TrackerCategory.Advertising, 4),
                ("taboolasyndication.com", "Taboola Syndication",       "Taboola",    TrackerCategory.Advertising, 4),
                ("connextra.com",          "Connextra",                 "Connextra",   TrackerCategory.Advertising, 3),
                ("contentrecommendation.net", "Content Rec",            "Various",     TrackerCategory.Advertising, 3),
                ("bidtellect.com",         "BidTellect",                "BidTellect",   TrackerCategory.Advertising, 4),
                ("advertising.amazon.com", "Amazon DSP",                "Amazon",      TrackerCategory.Advertising, 4),
                ("ads.tiktok.com",         "TikTok Ads",                "ByteDance",   TrackerCategory.Advertising, 5),
                ("ads-api.tiktok.com",     "TikTok Ads API",            "ByteDance",   TrackerCategory.Advertising, 5),
                ("business-api.tiktok.com","TikTok Business",           "ByteDance",   TrackerCategory.Advertising, 4),
                ("reddit.com/static/ads",   "Reddit Ads",                "Reddit",      TrackerCategory.Advertising, 4),
                ("ads.reddit.com",         "Reddit Ads",                "Reddit",      TrackerCategory.Advertising, 4),
                ("events.redditmedia.com", "Reddit Analytics",         "Reddit",      TrackerCategory.Analytics,  4),
                ("branch.io",              "Branch",                    "Branch",      TrackerCategory.Attribution, 3),
                ("app.link",               "Branch Deep Links",         "Branch",      TrackerCategory.Attribution, 3),
                ("singular.net",           "Singular",                  "Singular",    TrackerCategory.Attribution, 4),
                ("appsflyer.com",          "AppsFlyer",                 "AppsFlyer",   TrackerCategory.Attribution, 4),
                ("adjust.com",             "Adjust",                    "Adjust",      TrackerCategory.Attribution, 4),
                ("kochava.com",            "Kochava",                   "Kochava",     TrackerCategory.Attribution, 4),
                ("braze.com",              "Braze",                     "Braze",       TrackerCategory.Analytics,  4),
                ("cdn.braze.com",          "Braze CDN",                 "Braze",       TrackerCategory.Analytics,  4),
                ("clevertap.com",          "CleverTap",                 "CleverTap",   TrackerCategory.Analytics,  4),
                ("wizrocket.com",          "WizRocket",                 "CleverTap",   TrackerCategory.Analytics,  3),

                // ── Additional DMPs / identity / regional ──
                ("zeotap.com",             "Zeotap CDP",                "Zeotap",      TrackerCategory.DMP, 5),
                ("zeotap.io",              "Zeotap",                    "Zeotap",      TrackerCategory.DMP, 5),
                ("improvedigital.com",     "Improve Digital",          "Improve",     TrackerCategory.Advertising, 4),
                ("liveramp.com",           "LiveRamp",                  "LiveRamp",    TrackerCategory.DMP, 5),
                ("synacor.com",            "Synacor",                   "Synacor",     TrackerCategory.Advertising, 3),
                ("audiencescience.com",    "AudienceScience",          "AudienceScience", TrackerCategory.DMP, 5),
                ("omnicomgroup.com",       "Omnicom",                   "Omnicom",     TrackerCategory.Advertising, 3),

                // ── More CMPs / consent ──
                ("sourcepoint.com",        "Sourcepoint CMP",           "Sourcepoint", TrackerCategory.CMP, 2),
                ("didomi.io",              "Didomi CMP",                "Didomi",      TrackerCategory.CMP, 2),
                ("quantcast.com",          "Quantcast Choice",          "Quantcast",   TrackerCategory.CMP, 3),
                ("iab.eu",                 "IAB Europe TCF",           "IAB",         TrackerCategory.CMP, 1),

                // ── More analytics / CDP ──
                ("rudderstack.com",       "RudderStack",               "RudderStack", TrackerCategory.Analytics, 4),
                ("rs.rudderstack.com",     "RudderStack",               "RudderStack", TrackerCategory.Analytics, 4),
                ("posthog.com",            "PostHog",                   "PostHog",     TrackerCategory.Analytics, 3),
                ("us.posthog.com",         "PostHog",                   "PostHog",     TrackerCategory.Analytics, 3),
                ("launchdarkly.com",       "LaunchDarkly",              "LaunchDarkly", TrackerCategory.Analytics, 2),
                ("cdn.launchdarkly.com",   "LaunchDarkly CDN",          "LaunchDarkly", TrackerCategory.Analytics, 2),
            };

            TrackerDatabase = raw.Select(r => new TrackerInfo
            {
                Domain = r.D, Label = r.L, Company = r.C, Category = r.Cat, RiskWeight = r.R,
                DataTypes = InferDataTypes(r.Cat)
            }).ToArray();

            // Build O(1) lookup dictionary keyed by domain suffix
            TrackerLookup = new Dictionary<string, TrackerInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in TrackerDatabase)
                TrackerLookup.TryAdd(t.Domain, t);
            TrackerDomainSuffixes = TrackerLookup.Keys.OrderBy(k => k).ToArray();
        }

        /// <summary>
        /// Expose the structured tracker database for other engines (e.g. ProtectionEngine blocklist seeding).
        /// Read-only; do not mutate returned instances.
        /// </summary>
        public static IEnumerable<TrackerInfo> GetTrackerDatabase() => TrackerDatabase;

        private static string[] InferDataTypes(TrackerCategory cat) => cat switch
        {
            TrackerCategory.Advertising => new[] { "Browsing history", "Device info", "IP address", "Ad interactions", "Cross-site identity" },
            TrackerCategory.Analytics => new[] { "Page views", "Device info", "IP address", "User behavior", "Session data" },
            TrackerCategory.Social => new[] { "Social identity", "Browsing history", "IP address", "Device info" },
            TrackerCategory.SessionReplay => new[] { "Mouse movements", "Keystrokes", "Form inputs", "Full session recording", "PII risk" },
            TrackerCategory.DMP => new[] { "Cross-site profile", "Browsing history", "Purchase intent", "Demographic data", "Device graph" },
            TrackerCategory.Attribution => new[] { "Install source", "Device ID", "IP address", "Campaign data" },
            TrackerCategory.Affiliate => new[] { "Click source", "Purchase data", "Referral chain" },
            TrackerCategory.AdVerification => new[] { "Ad viewability", "Page context", "IP address" },
            TrackerCategory.Fingerprinting => new[] { "Device fingerprint", "Hardware specs", "Font list", "Canvas hash" },
            TrackerCategory.CMP => new[] { "Consent choices", "Vendor list" },
            _ => new[] { "Unknown" }
        };

        // ════════════════════════════════════════════
        //  TRACKING URL PARAMETERS
        // ════════════════════════════════════════════

        private static readonly string[] TrackingParamPrefixes = {
            "utm_", "fbclid", "gclid", "dclid", "msclkid", "ttclid",
            "mc_eid", "yclid", "twclid", "_ga", "srsltid",
            "igshid", "li_fat_id", "epik", "s_kwcid", "wickedid",
            "cmpid", "irclickid", "zanpid", "gbraid", "wbraid",
            "vero_id", "oly_enc_id", "oly_anon_id", "_hsenc", "_hsmi",
            "mc_cid", "mc_eid", "mkt_tok", "trk_contact", "trk_msg",
            "si", "ict", "_kx", "hsa_", "ref_",
            "sid", "uid", "cid", "_gac_", "_gl", "oaid", "idt", "_pk_", "_sp_", "distinct_id", "ajs_",
            "trk", "trk_", "track", "tracking", "clickid", "campaign", "affiliate", "ref",
            "_ref", "source", "medium", "content", "term", "campaign_id", "ad_id", "placement",
            "adset", "ad_id", "fb_action_ids", "fb_source", "sc_cid", "mc_cid", "_hsq",
            "redirect_mid", "redirect_log_mid", "_bta_c", "_bta_t", "vero_conv", "_branch_match_id",
            "tapad_id", "lr_id", "lr_enc", "om_campaign", "om_channel", "_gl", "gclsrc",
            "__n", "__imp", "vgo_ee", "ns_", "ti_", "_pxl", "_px", "px_", "conv_", "click_id",
            "rnd", "rand", "cache_bust", "cb_", "_reqid", "req_id", "request_id", "correlation",
            "session_id", "visitor_id", "user_id", "device_id", "client_id", "customer_id",
            "_hjid", "_hj", "intercom-id", "drift_", "hubspotutk", "__hs", "mp_", "s_vi", "s_fid"
        };

        // ════════════════════════════════════════════
        //  TRACKING COOKIE PATTERNS
        // ════════════════════════════════════════════

        private static readonly string[] TrackingCookiePatterns = {
            "_ga", "_gid", "_gat", "_gcl", "_gac_", "_gat_gtag",
            "_fbp", "_fbc", "fr", "_ttp",
            "_uetsid", "_uetvid",
            "IDE", "DSID", "FLC", "AID", "TAID", "exchange_uid",
            "MUID", "ANONCHK", "_uetmsclkid",
            "_hjid", "_hjSession", "_hjAbsoluteSessionInProgress", "_hjFirstSeen",
            "_clsk", "_clck",
            "mp_", "ajs_", "ajs_anonymous_id", "ajs_user_id",
            "amplitude_id",
            "optimizelyEndUserId", "optimizelySegments",
            "s_cc", "s_sq", "s_vi", "s_fid", "s_ecid",
            "__utma", "__utmb", "__utmc", "__utmz", "__utmv", "__utmt",
            "datr", "sb", "wd", "c_user", "xs", "presence",
            "_pin_unauth", "_pinterest_sess",
            "__qca", "mc_",
            "_dc_gtm_", "_gads", "test_cookie",
            "NID", "SID", "HSID", "SSID", "APISID", "SAPISID", "1P_JAR", "CONSENT",
            "YSC", "VISITOR_INFO1_LIVE", "LOGIN_INFO",
            "_mkto_trk", "hubspotutk", "__hs",
            "intercom-id", "intercom-session",
            "drift_aid", "drift_campaign_refresh",
            "personalization_id", "guest_id", "ct0",
            "bcookie", "bscookie", "li_sugr", "UserMatchHistory", "AnalyticsSyncHistory", "li_gc",
            "ANID", "SNID", "OGPC",
            "_rdt_uuid", "_scid", "_sctr",
            "cto_bundle", "cto_bidid",
            "__adroll", "__adroll_fpc",
            "__cf_bm", "cf_clearance",
            "_abck", "bm_sv", "ak_bmsc",
        };

        // ════════════════════════════════════════════
        //  ANTI-EVASION: CNAME CLOAKING HEURISTICS
        // ════════════════════════════════════════════

        private static readonly string[] SuspiciousSubdomains = {
            "metrics", "analytics", "track", "pixel", "data", "log", "tag",
            "collect", "beacon", "telemetry", "stats", "measure", "report",
            "event", "hit", "counter", "insight", "monitor", "pulse", "signal",
            "trk", "t", "px", "img", "srv", "cdn-cgi", "rum",
            "ad", "ads", "adserver", "adservice", "adsystem", "tracking", "tracker",
            "imp", "view", "click", "conv", "sync", "match", "bid", "delivery",
            "gtm", "gtag", "ga", "fbevents", "fbq", "tr", "gdm", "pagead"
        };

        private static readonly Regex HighEntropyPattern = new(@"[A-Za-z0-9+/=_-]{24,}", RegexOptions.Compiled);
        private static readonly Regex Base64Payload = new(@"^[A-Za-z0-9+/]{20,}={0,2}$", RegexOptions.Compiled);
        private static readonly Regex HexPayload = new(@"^[0-9a-fA-F]{24,}$", RegexOptions.Compiled);

        // ════════════════════════════════════════════════════════════
        //  JAVASCRIPT INJECTION: FINGERPRINT DETECTION (expanded)
        // ════════════════════════════════════════════════════════════

        public static string FingerprintDetectionScript => @"
(function() {
    const _src = () => { try { return (new Error()).stack.split('\n').slice(2,4).map(s=>s.trim()).join(' | ').substring(0,200); } catch(e){ return ''; } };
    const _post = (type, detail) => {
        try { chrome.webview.postMessage(JSON.stringify({cat:'fp', type:type, detail:detail, source:_src(), ts:Date.now()})); } catch(e){}
    };

    // ── Canvas Fingerprinting ──
    const _toDataURL = HTMLCanvasElement.prototype.toDataURL;
    const _toBlob = HTMLCanvasElement.prototype.toBlob;
    const _getImageData = CanvasRenderingContext2D.prototype.getImageData;
    let canvasFlagged = false;
    HTMLCanvasElement.prototype.toDataURL = function() {
        if (!canvasFlagged && this.width > 16 && this.height > 16) { canvasFlagged = true; _post('Canvas Fingerprinting', 'toDataURL() on ' + this.width + 'x' + this.height + ' canvas'); }
        return _toDataURL.apply(this, arguments);
    };
    HTMLCanvasElement.prototype.toBlob = function() {
        if (!canvasFlagged && this.width > 16 && this.height > 16) { canvasFlagged = true; _post('Canvas Fingerprinting', 'toBlob() on hidden canvas'); }
        return _toBlob.apply(this, arguments);
    };
    CanvasRenderingContext2D.prototype.getImageData = function() {
        if (!canvasFlagged && this.canvas && this.canvas.width > 16 && this.canvas.height > 16) { canvasFlagged = true; _post('Canvas Fingerprinting', 'getImageData() reading pixel data'); }
        return _getImageData.apply(this, arguments);
    };

    // ── WebGL Fingerprinting ──
    try {
        const wgl = WebGLRenderingContext.prototype;
        const _getParam = wgl.getParameter; const _getExt = wgl.getExtension;
        let wglCount = 0, wglFlagged = false;
        wgl.getParameter = function(p) { wglCount++; if (!wglFlagged && wglCount >= 5) { wglFlagged = true; _post('WebGL Fingerprinting', 'Bulk getParameter() — GPU enumeration (' + wglCount + ' calls)'); } return _getParam.apply(this, arguments); };
        wgl.getExtension = function(name) { if (name === 'WEBGL_debug_renderer_info') _post('WebGL Fingerprinting', 'WEBGL_debug_renderer_info — GPU model identification'); return _getExt.apply(this, arguments); };
    } catch(e) {}

    // ── AudioContext Fingerprinting ──
    try {
        const AC = window.AudioContext || window.webkitAudioContext;
        if (AC) {
            const _createOsc = AC.prototype.createOscillator;
            const _createComp = AC.prototype.createDynamicsCompressor;
            let audioFlag = false;
            AC.prototype.createDynamicsCompressor = function() { if (!audioFlag) { audioFlag = true; _post('Audio Fingerprinting', 'DynamicsCompressor + OscillatorNode pattern — audio hash generation'); } return _createComp.apply(this, arguments); };
            AC.prototype.createOscillator = function() { if (!audioFlag) { audioFlag = true; _post('Audio Fingerprinting', 'OscillatorNode created — possible audio fingerprint'); } return _createOsc.apply(this, arguments); };
        }
    } catch(e) {}

    // ── Navigator Properties Enumeration ──
    try {
        const navProps = ['hardwareConcurrency','deviceMemory','platform','languages','maxTouchPoints','vendor','appVersion','product','productSub'];
        let navCount = 0, navFlagged = false;
        for (const prop of navProps) { try {
            const desc = Object.getOwnPropertyDescriptor(Navigator.prototype, prop);
            if (desc && desc.get) { const orig = desc.get; Object.defineProperty(Navigator.prototype, prop, { get: function() { navCount++; if (!navFlagged && navCount >= 3) { navFlagged = true; _post('Navigator Fingerprinting', 'Reading ' + navCount + ' hardware properties: ' + navProps.slice(0,5).join(', ')); } return orig.call(this); }, configurable: true }); }
        } catch(e) {} }
    } catch(e) {}

    // ── Screen Properties ──
    try {
        const screenProps = ['width','height','colorDepth','pixelDepth','availWidth','availHeight'];
        let scrCount = 0, scrFlagged = false;
        for (const prop of screenProps) { try {
            const desc = Object.getOwnPropertyDescriptor(Screen.prototype, prop);
            if (desc && desc.get) { const orig = desc.get; Object.defineProperty(Screen.prototype, prop, { get: function() { scrCount++; if (!scrFlagged && scrCount >= 3) { scrFlagged = true; _post('Screen Fingerprinting', 'Enumerating screen dimensions and color depth'); } return orig.call(this); }, configurable: true }); }
        } catch(e) {} }
    } catch(e) {}

    // ── Font Enumeration ──
    try {
        const origOW = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth');
        if (origOW && origOW.get) {
            const origGet = origOW.get; let fc = 0, ff = false;
            Object.defineProperty(HTMLElement.prototype, 'offsetWidth', { get: function() { if (this.style && this.style.fontFamily && this.style.position === 'absolute') { fc++; if (!ff && fc >= 20) { ff = true; _post('Font Fingerprinting', 'Measuring offsetWidth for ' + fc + '+ font families'); } } return origGet.call(this); }, configurable: true });
        }
    } catch(e) {}

    // ── Battery API ──
    try {
        if (navigator.getBattery) {
            const _getBattery = navigator.getBattery.bind(navigator);
            navigator.getBattery = function() { _post('Battery Fingerprinting', 'navigator.getBattery() — battery level reveals device identity'); return _getBattery(); };
        }
    } catch(e) {}

    // ── Media Devices Enumeration ──
    try {
        if (navigator.mediaDevices && navigator.mediaDevices.enumerateDevices) {
            const _enum = navigator.mediaDevices.enumerateDevices.bind(navigator.mediaDevices);
            let mdFlag = false;
            navigator.mediaDevices.enumerateDevices = function() { if (!mdFlag) { mdFlag = true; _post('MediaDevice Fingerprinting', 'enumerateDevices() — camera/mic list reveals hardware'); } return _enum(); };
        }
    } catch(e) {}

    // ── Timezone / Locale Probing ──
    try {
        const _resolvedOptions = Intl.DateTimeFormat.prototype.resolvedOptions;
        let tzFlag = false;
        Intl.DateTimeFormat.prototype.resolvedOptions = function() { if (!tzFlag) { tzFlag = true; _post('Timezone Fingerprinting', 'Intl.DateTimeFormat.resolvedOptions() — timezone and locale probing'); } return _resolvedOptions.apply(this, arguments); };
    } catch(e) {}

    // ── Performance Timing Abuse ──
    try {
        const _getEntries = Performance.prototype.getEntriesByType;
        let perfFlag = false;
        Performance.prototype.getEntriesByType = function(type) { if (!perfFlag && (type === 'navigation' || type === 'resource')) { perfFlag = true; _post('Performance Timing', 'getEntriesByType(""' + type + '"") — timing data can fingerprint network/hardware'); } return _getEntries.apply(this, arguments); };
    } catch(e) {}

    // ── Plugin Enumeration ──
    try {
        const pluginDesc = Object.getOwnPropertyDescriptor(Navigator.prototype, 'plugins');
        if (pluginDesc && pluginDesc.get) {
            const origPlugins = pluginDesc.get; let plFlag = false;
            Object.defineProperty(Navigator.prototype, 'plugins', { get: function() { if (!plFlag) { plFlag = true; _post('Plugin Fingerprinting', 'navigator.plugins enumeration — installed plugins list'); } return origPlugins.call(this); }, configurable: true });
        }
    } catch(e) {}

    // ── Connection / Network Info ──
    try {
        const conn = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
        if (conn) {
            const connProps = ['effectiveType','downlink','rtt','saveData'];
            let connCount = 0, connFlagged = false;
            for (const prop of connProps) { try {
                const desc = Object.getOwnPropertyDescriptor(conn.__proto__, prop);
                if (desc && desc.get) { const orig = desc.get; Object.defineProperty(conn.__proto__, prop, { get: function() { connCount++; if (!connFlagged && connCount >= 2) { connFlagged = true; _post('Network Fingerprinting', 'Reading connection info: type, speed, RTT'); } return orig.call(this); }, configurable: true }); }
            } catch(e) {} }
        }
    } catch(e) {}
})();";

        // ════════════════════════════════════════════════════════════
        //  JAVASCRIPT INJECTION: BEHAVIORAL MONITORING
        //  Detects scripts collecting user interaction patterns,
        //  dynamic script injection, and runtime obfuscation.
        // ════════════════════════════════════════════════════════════

        public static string BehavioralMonitorScript => @"
(function() {
    const _post = (type, detail) => {
        try { chrome.webview.postMessage(JSON.stringify({cat:'fp', type:type, detail:detail, source:'', ts:Date.now()})); } catch(e){}
    };

    // ── Event Listener Tracking: detect behavioral surveillance ──
    let mouseListeners = 0, scrollListeners = 0, keyListeners = 0, touchListeners = 0;
    let mouseFlagged = false, scrollFlagged = false, keyFlagged = false, touchFlagged = false;
    const _addEvt = EventTarget.prototype.addEventListener;
    EventTarget.prototype.addEventListener = function(type, handler, options) {
        if (type === 'mousemove' || type === 'mousedown' || type === 'mouseup') {
            mouseListeners++;
            if (!mouseFlagged && mouseListeners >= 3) { mouseFlagged = true; _post('Behavioral: Mouse Tracking', mouseListeners + ' mouse event listeners — recording mouse movement patterns'); }
        }
        if (type === 'scroll' || type === 'wheel') {
            scrollListeners++;
            if (!scrollFlagged && scrollListeners >= 3) { scrollFlagged = true; _post('Behavioral: Scroll Tracking', scrollListeners + ' scroll listeners — monitoring scroll depth and velocity'); }
        }
        if (type === 'keydown' || type === 'keyup' || type === 'keypress') {
            keyListeners++;
            if (!keyFlagged && keyListeners >= 3) { keyFlagged = true; _post('Behavioral: Keystroke Tracking', keyListeners + ' keyboard listeners — monitoring typing patterns and cadence'); }
        }
        if (type === 'touchstart' || type === 'touchmove' || type === 'touchend') {
            touchListeners++;
            if (!touchFlagged && touchListeners >= 3) { touchFlagged = true; _post('Behavioral: Touch Tracking', touchListeners + ' touch event listeners — mobile interaction monitoring'); }
        }
        return _addEvt.apply(this, arguments);
    };

    // ── Dynamic Script Injection Detection ──
    let dynamicScripts = 0, dynamicFlagged = false;
    const _createElement = document.createElement.bind(document);
    document.createElement = function(tag) {
        const el = _createElement(tag);
        if (tag.toLowerCase() === 'script') {
            dynamicScripts++;
            const origSrc = Object.getOwnPropertyDescriptor(HTMLScriptElement.prototype, 'src');
            if (origSrc && origSrc.set) {
                const _set = origSrc.set;
                Object.defineProperty(el, 'src', {
                    set: function(v) {
                        if (!dynamicFlagged && dynamicScripts >= 3) { dynamicFlagged = true; _post('Dynamic Script Loading', dynamicScripts + ' scripts injected at runtime — possible tracker chain loading: ' + (v||'').substring(0,100)); }
                        return _set.call(this, v);
                    },
                    get: origSrc.get ? origSrc.get : undefined,
                    configurable: true
                });
            }
        }
        return el;
    };

    // ── eval() / Function() Obfuscation Detection ──
    let evalFlag = false;
    const _eval = window.eval;
    window.eval = function(code) {
        if (!evalFlag && code && code.length > 200) { evalFlag = true; _post('Script Obfuscation', 'eval() called with ' + code.length + ' chars — possible runtime code obfuscation'); }
        return _eval.apply(this, arguments);
    };

    // ── MutationObserver abuse (session replay signature) ──
    let mutationFlag = false;
    const _MO = window.MutationObserver;
    window.MutationObserver = function(callback) {
        const mo = new _MO(callback);
        const _observe = mo.observe.bind(mo);
        mo.observe = function(target, config) {
            if (!mutationFlag && config && config.childList && config.subtree && (config.attributes || config.characterData)) {
                mutationFlag = true;
                _post('Session Replay Signature', 'MutationObserver watching full DOM tree — session replay or form monitoring active');
            }
            return _observe(target, config);
        };
        return mo;
    };
    window.MutationObserver.prototype = _MO.prototype;

    // ── postMessage cross-frame tracking ──
    let pmCount = 0, pmFlagged = false;
    const _pm = window.postMessage.bind(window);
    window.postMessage = function(msg, origin) {
        pmCount++;
        if (!pmFlagged && pmCount >= 5 && origin && origin !== window.location.origin) { pmFlagged = true; _post('Cross-Frame Communication', pmCount + ' postMessage calls to ' + origin + ' — possible cross-origin data sharing'); }
        return _pm(msg, origin);
    };

    // ── sendBeacon tracking ──
    if (navigator.sendBeacon) {
        let beaconFlag = false;
        const _beacon = navigator.sendBeacon.bind(navigator);
        navigator.sendBeacon = function(url, data) {
            if (!beaconFlag) { beaconFlag = true; _post('Beacon Data Exfil', 'navigator.sendBeacon() to ' + (url||'').substring(0,100) + ' — silent data transmission'); }
            return _beacon(url, data);
        };
    }
})();";

        public static string StorageEnumerationScript => @"
(function() {
    const result = {cat:'storage', cookies:[], localStorage:[], sessionStorage:[], indexedDB:[]};
    try { result.cookies = document.cookie.split(';').filter(c => c.trim()).map(c => { const p = c.trim().split('='); return {name: p[0].trim(), value: p.slice(1).join('=').substring(0, 200)}; }); } catch(e) {}
    try { for (let i = 0; i < localStorage.length; i++) { const k = localStorage.key(i); result.localStorage.push({key: k, size: (localStorage.getItem(k)||'').length}); } } catch(e) {}
    try { for (let i = 0; i < sessionStorage.length; i++) { const k = sessionStorage.key(i); result.sessionStorage.push({key: k, size: (sessionStorage.getItem(k)||'').length}); } } catch(e) {}
    if (window.indexedDB && indexedDB.databases) { indexedDB.databases().then(dbs => { result.indexedDB = dbs.map(d => ({name: d.name, version: d.version})); chrome.webview.postMessage(JSON.stringify(result)); }).catch(() => chrome.webview.postMessage(JSON.stringify(result))); }
    else { chrome.webview.postMessage(JSON.stringify(result)); }
})();";

        public static string WebRtcLeakScript => @"
(function() {
    try {
        const pc = new RTCPeerConnection({iceServers:[{urls:'stun:stun.l.google.com:19302'}]});
        pc.createDataChannel(''); const seen = {};
        pc.createOffer().then(o => pc.setLocalDescription(o));
        pc.onicecandidate = function(e) {
            if (e.candidate) { const m = e.candidate.candidate.match(/([0-9]{1,3}(\.[0-9]{1,3}){3})/); if (m && !seen[m[1]] && m[1] !== '0.0.0.0') { seen[m[1]] = true; chrome.webview.postMessage(JSON.stringify({cat:'webrtc', ip:m[1], type:e.candidate.type||'unknown'})); } }
            if (!e.candidate) { try { pc.close(); } catch(x){} }
        };
        setTimeout(() => { try { pc.close(); } catch(x){} }, 5000);
    } catch(e) {}
})();";

        // ════════════════════════════════════════════════════════════
        //  PRIMARY DETECTION: ANALYZE REQUEST (signal-based)
        // ════════════════════════════════════════════════════════════

        public static TrackerMatch? DetectTrackerFull(string host, string url)
        {
            // 1) Direct O(1) domain lookup
            foreach (var kv in TrackerLookup)
            {
                if (host.Equals(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith("." + kv.Key, StringComparison.OrdinalIgnoreCase))
                    return new TrackerMatch { Info = kv.Value, MatchType = "domain", Confidence = 0.95 };
            }
            // 1b) Static blocklist domain lookup (ads/trackers)
            if (ProtectionEngine.TryGetBlocklistEntry(host, out var bl))
            {
                var info = new TrackerInfo
                {
                    Domain = bl.Domain,
                    Label = bl.Label,
                    Company = "Blocklist",
                    Category = MapBlocklistCategory(bl.Category),
                    RiskWeight = bl.Category == "Ad" ? 4 : 3,
                    DataTypes = bl.Category == "Ad" ? new[] { "Ad interactions", "Browsing history" } : new[] { "Browsing history", "Device info" }
                };
                return new TrackerMatch { Info = info, MatchType = "domain", Confidence = Math.Max(0.90, bl.Confidence) };
            }
            // 2) URL-path match (e.g., facebook.com/tr)
            var lower = url.ToLowerInvariant();
            foreach (var t in TrackerDatabase)
            {
                if (t.Domain.Contains('/') && lower.Contains(t.Domain))
                    return new TrackerMatch { Info = t, MatchType = "domain", Confidence = 0.90 };
            }
            // 3) Heuristic path patterns (expanded for stronger detection)
            if (lower.Contains("/collect?") || lower.Contains("/pixel?") || lower.Contains("/beacon?") ||
                lower.Contains("/track?") || lower.Contains("/log?") || lower.Contains("/event?") ||
                lower.Contains("/__imp?") || lower.Contains("/t.gif") || lower.Contains("/p.gif") ||
                lower.Contains("/pixel.gif") || lower.Contains("/1x1.gif") || lower.Contains("/pixel.png") ||
                lower.Contains("/tr?") || lower.Contains("/impression?") || lower.Contains("/imp?") ||
                lower.Contains("/analytics.js") || lower.Contains("/gtag/js") || lower.Contains("/beacon/") || lower.Contains("/telemetry") ||
                lower.Contains("/g/collect") || lower.Contains("/j/collect") || lower.Contains("/r/collect") || lower.Contains("/s/collect") ||
                lower.Contains("/gtm.js") || lower.Contains("/gtm-") || lower.EndsWith("/ga.js") || lower.Contains("/ga/") ||
                lower.Contains("/fbevents") || lower.Contains("/tr?id=") || lower.Contains("/px.") || lower.Contains("/b/collect") ||
                lower.Contains("/view?") || lower.Contains("/click?") || lower.Contains("/conv?") || lower.Contains("/sync?") ||
                lower.Contains("/match?") || lower.Contains("/delivery") || lower.Contains("/pagead/") || lower.Contains("/pagead2/") ||
                lower.Contains("/ad.js") || lower.Contains("/ads.js") || lower.Contains("/adrequest") || lower.Contains("/adcall") ||
                lower.Contains("/vast") || lower.Contains("/vpaid") || lower.Contains("/hb_pb") || lower.Contains("/hb_bidder") ||
                lower.Contains("/__n") || lower.Contains("/blank.gif") || lower.Contains("/transparent.gif") ||
                lower.Contains("/v2/collect") || lower.Contains("/v2/e") || lower.Contains("/ingest") || lower.Contains("/e?") ||
                lower.Contains("/i.ve") || lower.Contains("/identity") || lower.Contains("/sync/identity") || lower.Contains("/setuid") ||
                lower.Contains("/usersync") || lower.Contains("/match/id") || lower.Contains("/id/sync") || lower.Contains("/gdpr_consent") ||
                lower.Contains("/cm/sync") || lower.Contains("/csync") || lower.Contains("/ups") || lower.Contains("/__imp"))
            {
                var heuristic = new TrackerInfo { Domain = host, Label = "Suspected Tracker (heuristic)", Company = "Unknown", Category = TrackerCategory.Other, RiskWeight = 3 };
                return new TrackerMatch { Info = heuristic, MatchType = "heuristic", Confidence = 0.72 };
            }
            return null;
        }

        // Backward-compatible wrapper
        public static string DetectTracker(string host, string url)
        {
            var m = DetectTrackerFull(host, url);
            return m?.Info.Label ?? "";
        }

        /// <summary>
        /// Produce all detection signals for a single request.
        /// This is the core analysis pipeline run per-request.
        /// </summary>
        public static List<DetectionSignal> AnalyzeRequest(RequestEntry req, string pageHost)
        {
            var signals = new List<DetectionSignal>();

            // 0) Cross-site request signal (low confidence, used for heuristics only)
            if (req.IsThirdParty && !string.IsNullOrEmpty(pageHost))
            {
                signals.Add(new DetectionSignal
                {
                    SignalType = "cross_site_request",
                    Source = req.Host,
                    Detail = $"Third-party request from {pageHost} to {req.Host}",
                    Confidence = 0.30,
                    Risk = RiskType.Tracking,
                    Severity = 2,
                    Evidence = $"Page: {pageHost}, Host: {req.Host}",
                    GdprArticle = "Art. 5(1)(a)"
                });
            }

            // 1) Tracker match
            var trackerMatch = DetectTrackerFull(req.Host, req.FullUrl);
            if (trackerMatch != null)
            {
                signals.Add(new DetectionSignal
                {
                    SignalType = trackerMatch.MatchType == "domain" ? "known_tracker" : "heuristic_tracker",
                    Source = req.Host,
                    Detail = $"{trackerMatch.Info.Label} ({trackerMatch.Info.Company}) [{trackerMatch.Info.Category}]",
                    Confidence = trackerMatch.Confidence,
                    Risk = RiskType.Tracking,
                    Severity = trackerMatch.Info.RiskWeight,
                    Evidence = $"Domain: {req.Host} matched tracker DB entry: {trackerMatch.Info.Domain}",
                    GdprArticle = "Art. 6"
                });
                req.TrackerLabel = trackerMatch.Info.Label;
                req.TrackerCompany = trackerMatch.Info.Company;
                req.TrackerCategoryName = trackerMatch.Info.Category.ToString();
            }

            // 2) Tracking URL parameters
            req.TrackingParams = DetectTrackingParams(req.FullUrl);
            if (req.TrackingParams.Count > 0)
            {
                signals.Add(new DetectionSignal
                {
                    SignalType = "tracking_param",
                    Source = req.Host,
                    Detail = $"{req.TrackingParams.Count} tracking parameter(s): {string.Join(", ", req.TrackingParams.Take(5).Select(p => p.Split('=')[0]))}",
                    Confidence = 0.90,
                    Risk = RiskType.Tracking,
                    Severity = 3,
                    Evidence = string.Join("; ", req.TrackingParams.Take(5)),
                    GdprArticle = "Art. 5(1)(b)"
                });
            }

            // 3) High-entropy parameters (obfuscated tracking IDs)
            DetectHighEntropyParams(req, signals);

            // 3b) Cookie sync detection (identifier handoff)
            DetectCookieSync(req, signals);

            // 4) CNAME cloaking suspect
            if (!req.IsThirdParty && !string.IsNullOrEmpty(pageHost))
                DetectCnameSuspect(req, pageHost, signals);

            // 5) Pixel/beacon detection (small third-party requests)
            if (req.IsThirdParty)
                DetectPixelTracking(req, signals);

            // 5b) Third-party script: executable code from external domain (tracking vector)
            if (req.IsThirdParty && string.Equals(req.ResourceContext, "Script", StringComparison.OrdinalIgnoreCase))
            {
                signals.Add(new DetectionSignal
                {
                    SignalType = "third_party_script",
                    Source = req.Host,
                    Detail = $"Third-party script loaded from {req.Host} — can track behavior and fingerprint",
                    Confidence = 0.66,
                    Risk = RiskType.Tracking,
                    Severity = 2,
                    Evidence = $"Script from {req.Host}",
                    GdprArticle = "Art. 5(1)(a)"
                });
            }

            // 5c) ETag-based tracking (cache cookie)
            DetectEtagTracking(req, signals);

            // 6) Data classification
            req.DataClassifications = ClassifyRequestData(req);

            // 7) Data exfiltration signals
            if (req.IsThirdParty && req.HasBody)
            {
                signals.Add(new DetectionSignal
                {
                    SignalType = "data_exfil",
                    Source = req.Host,
                    Detail = $"POST data sent to third-party: {req.Host}",
                    Confidence = 0.85,
                    Risk = RiskType.DataLeakage,
                    Severity = 4,
                    Evidence = $"Method: {req.Method}, Host: {req.Host}",
                    GdprArticle = "Art. 5(1)(a)"
                });
            }

            // Compute aggregate threat confidence
            req.Signals = signals;
            req.ThreatConfidence = signals.Count > 0 ? Math.Min(1.0, signals.Max(s => s.Confidence) + signals.Count * 0.02) : 0;
            return signals;
        }

        // ════════════════════════════════════════════
        //  ANTI-EVASION: HIGH ENTROPY DETECTION
        // ════════════════════════════════════════════

        public static double ShannonEntropy(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 8) return 0;
            var freq = new Dictionary<char, int>();
            foreach (char c in s) freq[c] = freq.GetValueOrDefault(c) + 1;
            double len = s.Length;
            return -freq.Values.Sum(f => { double p = f / len; return p * Math.Log2(p); });
        }

        private static void DetectHighEntropyParams(RequestEntry req, List<DetectionSignal> signals)
        {
            try
            {
                var uri = new Uri(req.FullUrl);
                if (string.IsNullOrEmpty(uri.Query)) return;
                foreach (var pair in uri.Query.TrimStart('?').Split('&'))
                {
                    var parts = pair.Split('=', 2);
                    if (parts.Length < 2 || parts[1].Length < 16) continue;
                    string val = parts[1];
                    double entropy = ShannonEntropy(val);
                    bool isBase64 = Base64Payload.IsMatch(val);
                    bool isHex = HexPayload.IsMatch(val);

                    if (entropy > 4.0 || isBase64 || isHex)
                    {
                        double conf = entropy > 4.5 ? 0.85 : entropy > 4.0 ? 0.70 : 0.55;
                        if (isBase64 || isHex) conf = Math.Max(conf, 0.75);
                        signals.Add(new DetectionSignal
                        {
                            SignalType = "high_entropy_param",
                            Source = req.Host,
                            Detail = $"Param '{parts[0]}' has high entropy ({entropy:F1}) — possible obfuscated tracking ID" + (isBase64 ? " [Base64]" : "") + (isHex ? " [Hex]" : ""),
                            Confidence = conf,
                            Risk = RiskType.Tracking,
                            Severity = 3,
                            Evidence = $"{parts[0]}={val[..Math.Min(40, val.Length)]}... (entropy: {entropy:F2})",
                            GdprArticle = "Art. 5(1)(c)"
                        });
                        break; // one signal per request is enough
                    }
                }
            }
            catch { }
        }

        // ════════════════════════════════════════════
        //  ANTI-EVASION: CNAME CLOAKING HEURISTIC
        // ════════════════════════════════════════════

        private static void DetectCnameSuspect(RequestEntry req, string pageHost, List<DetectionSignal> signals)
        {
            // First-party request to a suspicious subdomain
            string sub = req.Host.Replace("." + pageHost, "", StringComparison.OrdinalIgnoreCase);
            if (sub == req.Host || sub == pageHost) return; // not a subdomain
            var subLower = sub.ToLowerInvariant();

            foreach (var pattern in SuspiciousSubdomains)
            {
                if (subLower == pattern || subLower.StartsWith(pattern + ".") || subLower.StartsWith(pattern + "-"))
                {
                    signals.Add(new DetectionSignal
                    {
                        SignalType = "cname_suspect",
                        Source = req.Host,
                        Detail = $"First-party subdomain '{req.Host}' has suspicious name '{sub}' — possible CNAME cloaking for a tracking service",
                        Confidence = 0.50,
                        Risk = RiskType.Tracking,
                        Severity = 3,
                        Evidence = $"Subdomain: {sub}, pattern: {pattern}",
                        GdprArticle = "Art. 5(1)(a)"
                    });
                    return;
                }
            }
        }

        // ════════════════════════════════════════════
        //  ANTI-EVASION: PIXEL / BEACON DETECTION
        // ════════════════════════════════════════════

        private static void DetectPixelTracking(RequestEntry req, List<DetectionSignal> signals)
        {
            var lower = req.FullUrl.ToLowerInvariant();
            bool isPixelUrl = lower.EndsWith(".gif") || lower.EndsWith(".png") || lower.EndsWith("pixel") ||
                              lower.Contains("/pixel") || lower.Contains("/1x1") || lower.Contains("/p.gif") ||
                              lower.Contains("/t.gif") || lower.Contains("/beacon") || lower.Contains(".gif?") ||
                              lower.Contains("/imp?") || lower.Contains("/view?") || lower.Contains("/blank.gif") ||
                              lower.Contains("/transparent.gif") || lower.Contains("/__imp") || lower.Contains("/tr?");
            if (isPixelUrl)
            {
                signals.Add(new DetectionSignal
                {
                    SignalType = "pixel_tracking",
                    Source = req.Host,
                    Detail = $"Tracking pixel/beacon to {req.Host} — 1x1 image or beacon used to transmit data silently",
                    Confidence = 0.75,
                    Risk = RiskType.Tracking,
                    Severity = 3,
                    Evidence = $"URL pattern: {req.Path[..Math.Min(80, req.Path.Length)]}",
                    GdprArticle = "Art. 5(1)(a)"
                });
            }
        }

        /// <summary>Call when response headers are available (e.g. after GetResponse) to add ETag/cache-cookie signals.</summary>
        public static void AddResponseSignals(RequestEntry req)
        {
            if (req?.Signals == null) return;
            DetectEtagTracking(req, req.Signals);
        }

        private static void DetectEtagTracking(RequestEntry req, List<DetectionSignal> signals)
        {
            if (!req.IsThirdParty) return;
            if (req.ResponseHeaders == null || req.ResponseHeaders.Count == 0) return;
            if (!req.ResponseHeaders.TryGetValue("etag", out var etag) && !req.ResponseHeaders.TryGetValue("ETag", out etag))
                return;
            etag = etag?.Trim(' ', '"');
            if (string.IsNullOrEmpty(etag) || etag.Length < 20) return;
            double entropy = ShannonEntropy(etag);
            if (entropy >= 3.5 || Base64Payload.IsMatch(etag) || HexPayload.IsMatch(etag))
            {
                signals.Add(new DetectionSignal
                {
                    SignalType = "etag_tracking",
                    Source = req.Host,
                    Detail = "ETag looks like a tracking identifier — cache-based re-identification (cache cookie)",
                    Confidence = 0.72,
                    Risk = RiskType.Tracking,
                    Severity = 4,
                    Evidence = $"ETag length={etag.Length}, entropy={entropy:F2}",
                    GdprArticle = "Art. 5(1)(c)"
                });
            }
        }

        private static void DetectCookieSync(RequestEntry req, List<DetectionSignal> signals)
        {
            if (!req.IsThirdParty) return;
            if (!req.RequestHeaders.TryGetValue("cookie", out var cookieVal)) return;
            if (string.IsNullOrWhiteSpace(cookieVal)) return;

            bool hasIds = req.TrackingParams.Count > 0 ||
                          signals.Any(s => s.SignalType == "high_entropy_param") ||
                          req.Path.Contains("sync", StringComparison.OrdinalIgnoreCase) ||
                          req.Path.Contains("match", StringComparison.OrdinalIgnoreCase);

            if (hasIds)
            {
                signals.Add(new DetectionSignal
                {
                    SignalType = "cookie_sync",
                    Source = req.Host,
                    Detail = "Cookie sync detected — identifiers passed to third-party ad-tech",
                    Confidence = 0.70,
                    Risk = RiskType.Tracking,
                    Severity = 4,
                    Evidence = $"Cookie header present with tracking params to {req.Host}",
                    GdprArticle = "Art. 5(1)(a)"
                });
            }
        }

        private static TrackerCategory MapBlocklistCategory(string category)
        {
            return category == "Ad" ? TrackerCategory.Advertising : TrackerCategory.Analytics;
        }

        // ════════════════════════════════════════════
        //  TRACKING URL PARAMETERS
        // ════════════════════════════════════════════

        public static List<string> DetectTrackingParams(string url)
        {
            var found = new List<string>();
            try
            {
                var uri = new Uri(url);
                if (string.IsNullOrEmpty(uri.Query)) return found;
                foreach (var pair in uri.Query.TrimStart('?').Split('&'))
                {
                    var key = pair.Split('=')[0].ToLowerInvariant();
                    if (TrackingParamPrefixes.Any(tp => key.StartsWith(tp) || key == tp))
                        found.Add(pair);
                }
            }
            catch { }
            return found;
        }

        // ════════════════════════════════════════════
        //  COOKIE / STORAGE CLASSIFICATION
        // ════════════════════════════════════════════

        public static string ClassifyCookie(string name)
        {
            var lower = name.ToLowerInvariant();
            if (TrackingCookiePatterns.Any(p => lower.StartsWith(p.ToLowerInvariant()) || lower == p.ToLowerInvariant()))
                return "Tracking / Analytics";
            if (lower.Contains("session") || lower.Contains("sid") || lower.Contains("csrf") || lower.Contains("token") || lower.Contains("auth"))
                return "Session / Security";
            if (lower.Contains("consent") || lower.Contains("gdpr") || lower.Contains("ccpa") || lower.Contains("cookie") || lower.Contains("optanon"))
                return "Consent";
            if (lower.Contains("lang") || lower.Contains("locale") || lower.Contains("theme") || lower.Contains("pref") || lower.Contains("currency"))
                return "Preference";
            return "Unknown";
        }

        public static string ClassifyStorageKey(string key)
        {
            var lower = key.ToLowerInvariant();
            if (TrackingCookiePatterns.Any(p => lower.Contains(p.ToLowerInvariant())))
                return "Tracking / Analytics";
            if (lower.Contains("token") || lower.Contains("auth") || lower.Contains("session"))
                return "Authentication";
            if (lower.Contains("cache") || lower.Contains("sw-") || lower.Contains("workbox"))
                return "Cache / Service Worker";
            if (lower.Contains("consent") || lower.Contains("gdpr") || lower.Contains("cookie"))
                return "Consent";
            return "Application Data";
        }

        // ════════════════════════════════════════════
        //  COUNT ALL TRACKING COOKIES (JS + HttpOnly)
        // ════════════════════════════════════════════

        public static int CountAllTrackingCookies(ScanResult scan)
        {
            int jsTracking = scan.Cookies.Count(c => c.Classification == "Tracking / Analytics");
            var setCookieNames = scan.Requests
                .SelectMany(r => r.ResponseHeaders.Where(h => h.Key.Equals("set-cookie", StringComparison.OrdinalIgnoreCase))
                    .Select(h => h.Value.Split(';')[0].Split('=')[0].Trim()))
                .Distinct().Where(n => !scan.Cookies.Any(c => c.Name == n))
                .Count(n => ClassifyCookie(n) == "Tracking / Analytics");
            return jsTracking + setCookieNames;
        }

        // ════════════════════════════════════════════
        //  DATA CLASSIFICATION
        // ════════════════════════════════════════════

        public static List<string> ClassifyRequestData(RequestEntry req)
        {
            var tags = new List<string>();
            var headers = req.RequestHeaders;
            if (headers.Any(h => h.Key.Equals("cookie", StringComparison.OrdinalIgnoreCase))) tags.Add("Cookies (identity)");
            if (headers.Any(h => h.Key.Equals("authorization", StringComparison.OrdinalIgnoreCase))) tags.Add("Authentication credentials");
            if (headers.Any(h => h.Key.Equals("referer", StringComparison.OrdinalIgnoreCase))) tags.Add("Browsing history (referer)");
            if (headers.Any(h => h.Key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))) tags.Add("Device/browser info");
            if (req.TrackingParams.Count > 0) tags.Add("Cross-site tracking IDs");
            if (req.HasBody) tags.Add("Form/POST data");
            var url = req.FullUrl.ToLowerInvariant();
            if (url.Contains("geo") || url.Contains("location") || url.Contains("lat=") || url.Contains("lng=")) tags.Add("Location data");
            if (url.Contains("email") || url.Contains("phone") || url.Contains("name=") || url.Contains("address")) tags.Add("Possible PII");
            return tags;
        }

        // ════════════════════════════════════════════
        //  SECURITY HEADERS
        // ════════════════════════════════════════════

        public static List<SecurityHeaderResult> AnalyzeSecurityHeaders(Dictionary<string, string> headers)
        {
            var results = new List<SecurityHeaderResult>();
            var h = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
            results.Add(CheckHeader(h, "Strict-Transport-Security", "Forces HTTPS connections", "Detyron lidhje HTTPS", 10));
            results.Add(CheckHeader(h, "Content-Security-Policy", "Controls which resources can load", "Kontrollon cilat burime mund te ngarkohen", 15));
            results.Add(CheckHeader(h, "X-Content-Type-Options", "Prevents MIME-type sniffing", "Parandalon pergjimin e llojit MIME", 5));
            results.Add(CheckHeader(h, "X-Frame-Options", "Prevents clickjacking attacks", "Parandalon sulmet clickjacking", 8));
            results.Add(CheckHeader(h, "Referrer-Policy", "Controls how much URL info is shared", "Kontrollon sa informacion URL ndahet", 8));
            results.Add(CheckHeader(h, "Permissions-Policy", "Restricts browser features (camera, mic)", "Kufizon funksionet e shfletuesit", 10));
            results.Add(CheckHeader(h, "Cross-Origin-Embedder-Policy", "Isolates resources from other origins", "Izolon burimet nga origjina te tjera", 5));
            results.Add(CheckHeader(h, "Cross-Origin-Opener-Policy", "Isolates browsing context", "Izolon kontekstin e shfletimit", 5));
            results.Add(CheckHeader(h, "X-XSS-Protection", "Legacy XSS filter", "Filtri i vjeter XSS", 2));
            return results;
        }

        private static SecurityHeaderResult CheckHeader(Dictionary<string, string> h, string name, string exp, string expSq, int impact)
        {
            if (h.TryGetValue(name, out var value))
            {
                bool weak = false;
                if (name == "Referrer-Policy" && value.Contains("unsafe-url")) weak = true;
                if (name == "X-Frame-Options" && !value.Contains("DENY") && !value.Contains("SAMEORIGIN")) weak = true;
                if (name == "Content-Security-Policy" && value.Contains("unsafe-inline") && value.Contains("unsafe-eval")) weak = true;
                return new SecurityHeaderResult { Header = name, Status = weak ? "Weak" : "Present", Value = value.Length > 120 ? value[..120] + "..." : value, Explanation = exp, ExplanationSq = expSq, ScoreImpact = weak ? impact / 2 : 0 };
            }
            return new SecurityHeaderResult { Header = name, Status = "Missing", Value = "--", Explanation = exp, ExplanationSq = expSq, ScoreImpact = impact };
        }

        // ════════════════════════════════════════════════════════════
        //  GDPR ARTICLE MAPPING
        // ════════════════════════════════════════════════════════════

        public static List<GdprFinding> MapToGdpr(ScanResult scan)
        {
            var findings = new List<GdprFinding>();
            int trackers = scan.Requests.Count(r => !string.IsNullOrEmpty(r.TrackerLabel));
            int uniqueTrackerServices = scan.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerLabel)).Select(r => r.TrackerLabel).Distinct().Count();
            int uniqueCompanies = scan.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerCompany)).Select(r => r.TrackerCompany).Distinct().Count();
            int thirdParty = scan.Requests.Count(r => r.IsThirdParty);
            int uniqueTpDomains = scan.Requests.Where(r => r.IsThirdParty).Select(r => r.Host).Distinct().Count();
            int fps = scan.Fingerprints.Count;
            int trackingCookies = CountAllTrackingCookies(scan);
            int leaks = scan.WebRtcLeaks.Count;
            int missingHeaders = scan.SecurityHeaders.Count(h => h.Status == "Missing");
            int postTp = scan.Requests.Count(r => r.IsThirdParty && r.HasBody);
            int cnameSuspects = scan.AllSignals.Count(s => s.SignalType == "cname_suspect");
            int pixelBeacons = scan.AllSignals.Count(s => s.SignalType == "pixel_tracking");
            int highEntropy = scan.AllSignals.Count(s => s.SignalType == "high_entropy_param");

            if (uniqueTrackerServices > 0)
                findings.Add(new GdprFinding { Article = "Art. 6", Title = "Lawfulness of Processing", TitleSq = "Ligjshmeria e Perpunimit",
                    Description = $"{uniqueTrackerServices} tracking service(s) from {uniqueCompanies} companies detected ({trackers} requests). Each requires a lawful basis under GDPR.",
                    Severity = uniqueTrackerServices >= 10 ? "Critical" : uniqueTrackerServices >= 3 ? "High" : "Medium", Count = uniqueTrackerServices });

            if (trackingCookies > 0)
                findings.Add(new GdprFinding { Article = "Art. 7 + ePrivacy", Title = "Conditions for Consent", TitleSq = "Kushtet per Pelqim",
                    Description = $"{trackingCookies} tracking cookie(s) detected. Tracking cookies require informed, specific consent BEFORE being set.",
                    Severity = trackingCookies >= 5 ? "Critical" : trackingCookies >= 2 ? "High" : "Medium", Count = trackingCookies });

            if (fps > 0)
                findings.Add(new GdprFinding { Article = "Art. 5(1)(c)", Title = "Data Minimisation", TitleSq = "Minimizimi i te Dhenave",
                    Description = $"{fps} fingerprinting technique(s) detected. Browser fingerprinting collects excessive device data, violating the minimisation principle.",
                    Severity = "Critical", Count = fps });

            if (thirdParty > 3)
                findings.Add(new GdprFinding { Article = "Art. 5(1)(b)", Title = "Purpose Limitation", TitleSq = "Kufizimi i Qellimit",
                    Description = $"{thirdParty} third-party requests to {uniqueTpDomains} external domains. Data shared with external parties must be limited to the stated purpose.",
                    Severity = thirdParty > 50 ? "Critical" : thirdParty > 20 ? "High" : "Medium", Count = thirdParty });

            if (uniqueTrackerServices > 0 || fps > 0 || trackingCookies > 0)
                findings.Add(new GdprFinding { Article = "Art. 13", Title = "Information Obligation", TitleSq = "Detyrimi per Informim",
                    Description = "Users must be informed about ALL data collection at the time it occurs. Trackers, fingerprinting, and tracking cookies must be disclosed in the privacy notice.",
                    Severity = "High", Count = uniqueTrackerServices + fps + trackingCookies });

            if (missingHeaders > 2)
                findings.Add(new GdprFinding { Article = "Art. 25 + Art. 32", Title = "Data Protection by Design", TitleSq = "Mbrojtja sipas Dizajnit",
                    Description = $"{missingHeaders} security headers missing. Insufficient technical measures may fail to protect personal data.",
                    Severity = missingHeaders > 5 ? "High" : "Medium", Count = missingHeaders });

            if (leaks > 0)
                findings.Add(new GdprFinding { Article = "Art. 5(1)(f)", Title = "Integrity and Confidentiality", TitleSq = "Integriteti dhe Konfidencialiteti",
                    Description = $"WebRTC leaked {leaks} IP address(es). Exposes real location without consent.",
                    Severity = "Critical", Count = leaks });

            if (postTp > 0)
                findings.Add(new GdprFinding { Article = "Art. 5(1)(a)", Title = "Transparency Principle", TitleSq = "Parimi i Transparences",
                    Description = $"{postTp} POST request(s) sent data to third-party servers without transparency.",
                    Severity = postTp > 5 ? "High" : "Medium", Count = postTp });

            if (cnameSuspects > 0)
                findings.Add(new GdprFinding { Article = "Art. 5(1)(a)", Title = "CNAME Cloaking Suspected", TitleSq = "Dyshim per CNAME Cloaking",
                    Description = $"{cnameSuspects} first-party subdomain(s) with suspicious names that may disguise tracking services as first-party resources.",
                    Severity = "High", Count = cnameSuspects });

            if (highEntropy > 5)
                findings.Add(new GdprFinding { Article = "Art. 5(1)(c)", Title = "Obfuscated Tracking Identifiers", TitleSq = "Identifikues Gjurmimi te Fshehur",
                    Description = $"{highEntropy} URL parameters contain high-entropy data consistent with obfuscated tracking IDs.",
                    Severity = "Medium", Count = highEntropy });

            int etagTracking = scan.AllSignals.Count(s => s.SignalType == "etag_tracking");
            if (etagTracking > 0)
                findings.Add(new GdprFinding { Article = "Art. 5(1)(c)", Title = "ETag / Cache Tracking", TitleSq = "Gjurmim me ETag/Cache",
                    Description = $"{etagTracking} response(s) use ETag that resembles a tracking identifier (cache cookie). Re-identification without consent.",
                    Severity = "High", Count = etagTracking });

            int extDomains = scan.Requests.Where(r => r.IsThirdParty).Select(r => r.Host)
                .Where(h => h.EndsWith(".com") || h.EndsWith(".net") || h.EndsWith(".io") || h.EndsWith(".org")).Distinct().Count();
            if (extDomains > 3)
                findings.Add(new GdprFinding { Article = "Art. 44-49", Title = "International Data Transfers", TitleSq = "Transferimi Nderkombetar",
                    Description = $"Data sent to {extDomains} external domains. Transfers outside EU/EEA require adequate safeguards.",
                    Severity = extDomains > 10 ? "High" : "Medium", Count = extDomains });

            return findings;
        }

        // ════════════════════════════════════════════════════════════
        //  PRIVACY SCORE (confidence-weighted, diminishing returns)
        // ════════════════════════════════════════════════════════════

        // ════════════════════════════════════════════════════════════
        //  BASELINE NORMALIZATION
        //  Adjusts tracker penalty based on category: CMP/CDN/low-risk
        //  analytics get reduced weight, DMPs/SessionReplay get increased.
        // ════════════════════════════════════════════════════════════

        private static double BaselineWeight(TrackerCategory cat) => cat switch
        {
            TrackerCategory.CMP => 0.2,             // Consent tools are infrastructure, very low risk
            TrackerCategory.CDN => 0.1,             // CDN is expected
            TrackerCategory.AdVerification => 0.5,  // Verification is semi-expected
            TrackerCategory.Affiliate => 0.6,       // Affiliate tracking is common
            TrackerCategory.Analytics => 0.7,       // Standard analytics — normal web behavior
            TrackerCategory.Social => 0.8,          // Social widgets carry moderate risk
            TrackerCategory.Attribution => 0.9,     // Attribution is aggressive mobile tracking
            TrackerCategory.Advertising => 1.0,     // Full penalty for ad trackers
            TrackerCategory.SessionReplay => 1.3,   // Session replay is invasive — amplified penalty
            TrackerCategory.DMP => 1.5,             // Data brokers are highest risk — amplified
            TrackerCategory.Fingerprinting => 1.4,  // Fingerprinting evades consent — amplified
            _ => 1.0
        };

        public static PrivacyScore CalculateScore(ScanResult scan)
        {
            int score = 100;
            var breakdown = new Dictionary<string, int>();
            var catScores = new Dictionary<string, int> { ["Tracking"] = 100, ["Fingerprinting"] = 100, ["DataLeakage"] = 100, ["Security"] = 100, ["Behavioral"] = 100 };

            // ── Trackers: baseline-normalized diminishing returns ──
            var trackerEntries = scan.Requests.Where(r => !string.IsNullOrEmpty(r.TrackerLabel)).ToList();
            int uniqueTrackers = trackerEntries.Select(r => r.TrackerLabel).Distinct().Count();
            // Weighted tracker penalty: each tracker penalized by its baseline weight
            double weightedTrackerSum = 0;
            foreach (var group in trackerEntries.GroupBy(r => r.TrackerLabel))
            {
                var sample = group.First();
                Enum.TryParse<TrackerCategory>(sample.TrackerCategoryName, out var cat);
                weightedTrackerSum += BaselineWeight(cat);
            }
            int trackerPenalty = (int)Math.Min(50, 7 * Math.Sqrt(weightedTrackerSum));
            score -= trackerPenalty; catScores["Tracking"] -= trackerPenalty;
            breakdown["Trackers"] = -trackerPenalty;

            // ── Third-party domains: diminishing ──
            int uniqueTpDomains = scan.Requests.Where(r => r.IsThirdParty).Select(r => r.Host).Distinct().Count();
            int domainPenalty = (int)Math.Min(20, 2.5 * Math.Sqrt(Math.Max(0, uniqueTpDomains - 2)));
            score -= domainPenalty; catScores["Tracking"] -= domainPenalty;
            breakdown["Third-party domains"] = -domainPenalty;

            // ── Fingerprinting: steep penalty ──
            int fpPenalty = Math.Min(35, scan.Fingerprints.Count * 10);
            score -= fpPenalty; catScores["Fingerprinting"] -= fpPenalty;
            breakdown["Fingerprinting"] = -fpPenalty;

            // ── Tracking cookies: softer diminishing curve to prevent alarm fatigue ──
            int allTrackingCookies = CountAllTrackingCookies(scan);
            int cookiePenalty = (int)Math.Min(22, 4 * Math.Sqrt(allTrackingCookies));
            score -= cookiePenalty; catScores["Tracking"] -= cookiePenalty;
            breakdown["Tracking cookies"] = -cookiePenalty;

            // ── Tracking params: diminishing ──
            int paramTotal = scan.Requests.Sum(r => r.TrackingParams.Count);
            int paramPenalty = (int)Math.Min(15, 3 * Math.Sqrt(paramTotal));
            score -= paramPenalty; catScores["Tracking"] -= paramPenalty;
            breakdown["Tracking URL params"] = -paramPenalty;

            // ── Security headers: capped to prevent over-penalizing common configs ──
            int headerPenalty = Math.Min(18, scan.SecurityHeaders.Sum(h => h.ScoreImpact));
            score -= headerPenalty; catScores["Security"] -= headerPenalty;
            breakdown["Security headers"] = -headerPenalty;

            // ── WebRTC leaks ──
            int rtcPenalty = Math.Min(15, scan.WebRtcLeaks.Count * 8);
            score -= rtcPenalty; catScores["DataLeakage"] -= rtcPenalty;
            breakdown["WebRTC leaks"] = -rtcPenalty;

            // ── POST to third-party ──
            int postTp = scan.Requests.Count(r => r.IsThirdParty && r.HasBody);
            int postPenalty = Math.Min(12, postTp * 4);
            score -= postPenalty; catScores["DataLeakage"] -= postPenalty;
            breakdown["POST to third-party"] = -postPenalty;

            // ── Excessive cookies ──
            int totalCookies = scan.Cookies.Count + scan.Requests
                .SelectMany(r => r.ResponseHeaders.Where(h => h.Key.Equals("set-cookie", StringComparison.OrdinalIgnoreCase)))
                .Select(h => h.Value.Split(';')[0].Split('=')[0].Trim()).Distinct().Count();
            int excessPenalty = totalCookies > 20 ? 10 : totalCookies > 10 ? 5 : 0;
            score -= excessPenalty;
            if (excessPenalty > 0) breakdown["Excessive cookies"] = -excessPenalty;

            // ── Behavioral tracking (mouse/scroll/keystroke/session replay/obfuscation) ──
            int behavioralCount = scan.Fingerprints.Count(f =>
                f.Type.StartsWith("Behavioral:") || f.Type.Contains("Session Replay") ||
                f.Type.Contains("Obfuscation") || f.Type.Contains("Dynamic Script") ||
                f.Type.Contains("Beacon Data") || f.Type.Contains("Cross-Frame"));
            int behavioralPenalty = Math.Min(25, behavioralCount * 6);
            score -= behavioralPenalty; catScores["Behavioral"] -= behavioralPenalty;
            if (behavioralPenalty > 0) breakdown["Behavioral tracking"] = -behavioralPenalty;

            // ── Anti-evasion signals ──
            int cnamePenalty = Math.Min(10, scan.AllSignals.Count(s => s.SignalType == "cname_suspect") * 3);
            if (cnamePenalty > 0) { score -= cnamePenalty; breakdown["CNAME suspects"] = -cnamePenalty; }
            int entropyPenalty = Math.Min(8, scan.AllSignals.Count(s => s.SignalType == "high_entropy_param") / 3);
            if (entropyPenalty > 0) { score -= entropyPenalty; breakdown["Obfuscated IDs"] = -entropyPenalty; }

            // ── Third-party scripts (tracking vector) ──
            int thirdPartyScripts = scan.AllSignals.Count(s => s.SignalType == "third_party_script");
            int scriptPenalty = Math.Min(10, (int)(2.5 * Math.Sqrt(Math.Max(0, thirdPartyScripts))));
            if (scriptPenalty > 0) { score -= scriptPenalty; catScores["Tracking"] -= scriptPenalty; breakdown["Third-party scripts"] = -scriptPenalty; }

            // ── ETag / cache-cookie tracking ──
            int etagCount = scan.AllSignals.Count(s => s.SignalType == "etag_tracking");
            int etagPenalty = Math.Min(8, etagCount * 4);
            if (etagPenalty > 0) { score -= etagPenalty; breakdown["ETag tracking"] = -etagPenalty; }

            // Clamp
            score = Math.Max(0, Math.Min(100, score));
            foreach (var k in catScores.Keys.ToList()) catScores[k] = Math.Max(0, Math.Min(100, catScores[k]));

            // Signal stats
            int totalSignals = scan.AllSignals.Count;
            int highConf = scan.AllSignals.Count(s => s.Confidence >= 0.80);

            string grade;
            SolidColorBrush color;
            if (score >= 90) { grade = "A"; color = new SolidColorBrush(Color.FromRgb(24, 128, 56)); }
            else if (score >= 75) { grade = "B"; color = new SolidColorBrush(Color.FromRgb(26, 115, 232)); }
            else if (score >= 60) { grade = "C"; color = new SolidColorBrush(Color.FromRgb(227, 116, 0)); }
            else if (score >= 40) { grade = "D"; color = new SolidColorBrush(Color.FromRgb(217, 48, 37)); }
            else { grade = "F"; color = new SolidColorBrush(Color.FromRgb(165, 14, 14)); }

            string summary = grade switch
            {
                "A" => "Excellent privacy. Minimal data collection.",
                "B" => "Good privacy with some minor concerns.",
                "C" => "Moderate issues. Multiple tracking mechanisms.",
                "D" => "Poor privacy. Significant tracking detected.",
                "F" => "Severe violations. Extensive tracking and data leakage.",
                _ => ""
            };
            string summarySq = grade switch
            {
                "A" => "Privatesi e shkelqyer.",
                "B" => "Privatesi e mire me disa shqetesime.",
                "C" => "Probleme mesatare privatetisie.",
                "D" => "Privatesi e dobet.",
                "F" => "Shkelje te renda privatetisie.",
                _ => ""
            };

            // ── Threat Tier Classification ──
            bool hasDMP = trackerEntries.Any(r => r.TrackerCategoryName == "DMP");
            bool hasSessionReplay = trackerEntries.Any(r => r.TrackerCategoryName == "SessionReplay");
            bool hasFingerprinting = scan.Fingerprints.Count >= 2;
            bool hasBehavioral = scan.Fingerprints.Any(f => f.Type.StartsWith("Behavioral:") || f.Type.Contains("Session Replay"));
            bool hasIdentityStitching = scan.AllSignals.Any(s => s.SignalType == "high_entropy_param" && s.Confidence >= 0.8);
            int adTrackers = trackerEntries.Count(r => r.TrackerCategoryName == "Advertising");

            ThreatTier tier;
            string tierLabel;
            if (score <= 35 || (hasDMP && hasFingerprinting) || (hasSessionReplay && hasFingerprinting && hasBehavioral) || (hasIdentityStitching && hasDMP))
            {
                tier = ThreatTier.SurveillanceGrade;
                tierLabel = "Surveillance-Grade";
            }
            else if (score <= 55 || (adTrackers >= 3 && hasFingerprinting) || hasSessionReplay || (hasDMP && uniqueTrackers >= 5))
            {
                tier = ThreatTier.AggressiveTracking;
                tierLabel = "Aggressive Tracking";
            }
            else if (score <= 80 || uniqueTrackers >= 3 || (scan.Fingerprints.Count >= 2 && uniqueTrackers >= 1))
            {
                tier = ThreatTier.TypicalWebTracking;
                tierLabel = "Typical Web Tracking";
            }
            else
            {
                tier = ThreatTier.SafeIsh;
                tierLabel = "Safe-ish";
            }

            return new PrivacyScore
            {
                NumericScore = score, Grade = grade, GradeColor = color,
                Summary = summary, SummarySq = summarySq, Breakdown = breakdown,
                CategoryScores = catScores, TotalSignals = totalSignals, HighConfidenceSignals = highConf,
                Tier = tier, TierLabel = tierLabel
            };
        }
    }
}
