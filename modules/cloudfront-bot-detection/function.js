// CloudFront Function: lightweight bot detection at the edge.
//
// Responsibilities:
//   - Inspect the User-Agent header against known bot patterns
//   - Whitelist legitimate search engine crawlers (Googlebot, Bingbot, etc.)
//   - Add X-Is-Bot header (boolean string) for downstream filtering
//   - Add X-Bot-Score header (0 = human, 1 = suspected bot)
//   - Optionally block requests with empty or missing User-Agent
//
// Configuration: edit CONFIG below to change behavior without redeploying
// the request-normalization function. The two functions compose: normalizer
// runs first, then bot detection.
//
// Bot pattern categories:
//   WHITELIST — known legitimate crawlers that must never be blocked.
//               These bots index content for search engines and need
//               clean 200 responses.
//   BLACKLIST — headless browsers, scrapers, SEO/marketing crawlers,
//               security scanners, and programmatic HTTP libraries.
//
// To add a pattern: append a /pattern/i regex to the appropriate list.
// Use /i (case-insensitive) for robustness against UA string variations.

// ── Configuration ─────────────────────────────────────────────────────────

var CONFIG = {
    // When true, requests with no User-Agent header receive a 403 Forbidden.
    // Set to false to pass them through (flagged with X-Is-Bot: true instead).
    blockEmptyUA: true
};

// ── Whitelist: legitimate search engine crawlers ──────────────────────────

var WHITELIST = [
    // Google
    /googlebot/i,
    /google-inspectiontool/i,
    /adsbot-google/i,

    // Microsoft / Bing
    /bingbot/i,
    /msnbot/i,
    /adidxbot/i,

    // Yahoo
    /slurp/i,

    // DuckDuckGo
    /duckduckbot/i,

    // Baidu
    /baiduspider/i,

    // Yandex
    /yandex(?:bot|images|video|news|blogs|webmaster)/i,

    // Apple
    /applebot/i,

    // Social / messaging link previews
    /twitterbot/i,
    /facebookexternalhit/i,
    /linkedinbot/i,
    /discordbot/i,
    /slackbot-linkexpanding/i,
    /telegrambot/i,
    /whatsapp/i,

    // Social sharing
    /pinterest/i,
    /redditbot/i,
    /tumblr/i,

    // Preservation / archive
    /ia_archiver/i
];

// ── Blacklist: known bots, scrapers, and automated traffic ────────────────

var BLACKLIST = [
    // Headless browsers / automation frameworks
    /headless/i,
    /phantomjs/i,
    /puppeteer/i,
    /playwright/i,
    /selenium/i,
    /webdriver/i,

    // Programmatic HTTP libraries
    /python-requests/i,
    /python-urllib/i,
    /libwww-perl/i,
    /wget/i,
    /curl\//i,
    /axios/i,
    /node-fetch/i,
    /got\//i,
    /superagent/i,
    /go-http-client/i,
    /guzzlehttp/i,
    /apache-httpclient/i,
    /okhttp/i,
    /java\/[\d.]+/i,
    /scrapy/i,
    /httpie/i,
    /insomnia/i,
    /postmanruntime/i,

    // SEO / marketing crawlers
    /semrushbot/i,
    /ahrefsbot/i,
    /dotbot/i,
    /bytespider/i,
    /petalbot/i,
    /blexbot/i,
    /dataforseobot/i,
    /mj12bot/i,
    /rogerbot/i,
    /exabot/i,
    /coccocbot/i,
    /seznambot/i,
    /sogou/i,
    /yisouspider/i,
    /zoominfobot/i,
    /crawler4j/i,
    /magpie-crawler/i,
    /screamingfrog/i,
    /sitebulb/i,

    // Security scanners / vulnerability probes
    /netcraft/i,
    /nessus/i,
    /nikto/i,
    /sqlmap/i,
    /nmap/i,
    /masscan/i,
    /acunetix/i,
    /burpsuite/i,
    /openvas/i,
    /zap/i
];

// ── Handler ───────────────────────────────────────────────────────────────

function handler(event) {
    var request = event.request;
    var userAgentHeader = request.headers["user-agent"];
    var userAgent = userAgentHeader ? userAgentHeader.value : "";

    // ── Whitelist check ───────────────────────────────────────────────────
    // Must run before blacklist to ensure legitimate crawlers are
    // never flagged. Only applies when a UA is present.
    if (userAgent !== "") {
        for (var i = 0; i < WHITELIST.length; i++) {
            if (WHITELIST[i].test(userAgent)) {
                markHuman(request);
                return request;
            }
        }
    }

    // ── Empty/missing User-Agent ──────────────────────────────────────────
    if (userAgent === "") {
        if (CONFIG.blockEmptyUA) {
            return {
                statusCode: 403,
                statusDescription: "Forbidden",
                headers: { "content-type": { value: "text/plain" } },
                body: ""
            };
        }
        markBot(request);
        return request;
    }

    // ── Blacklist check ───────────────────────────────────────────────────
    for (var j = 0; j < BLACKLIST.length; j++) {
        if (BLACKLIST[j].test(userAgent)) {
            markBot(request);
            return request;
        }
    }

    // ── Default: treat as human ───────────────────────────────────────────
    markHuman(request);
    return request;
}

// ── Helpers ───────────────────────────────────────────────────────────────

function markBot(request) {
    request.headers["x-is-bot"] = { value: "true" };
    request.headers["x-bot-score"] = { value: "1" };
}

function markHuman(request) {
    request.headers["x-is-bot"] = { value: "false" };
    request.headers["x-bot-score"] = { value: "0" };
}
