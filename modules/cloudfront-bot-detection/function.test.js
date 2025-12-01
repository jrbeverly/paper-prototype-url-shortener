// Tests for the CloudFront bot detection function.
// Run: node modules/cloudfront-bot-detection/function.test.js

var assert = require("assert");
var fs = require("fs");
var path = require("path");

// ── Test harness ──────────────────────────────────────────────────────────

var rawSource = fs.readFileSync(path.join(__dirname, "function.js"), "utf8");

function buildHandler(overrides) {
    var source = rawSource;
    if (overrides) {
        for (var key in overrides) {
            if (overrides.hasOwnProperty(key)) {
                source = source.replace(
                    key + ": " + (key === "blockEmptyUA" ? "true" : "undefined"),
                    key + ": " + overrides[key]
                );
            }
        }
    }
    return new Function(source + "; return handler;")();
}

// Default handler with blockEmptyUA = true
var handler = buildHandler();

// Handler that passes through empty UAs instead of blocking
var handlerPermissive = buildHandler({ blockEmptyUA: "false" });

// ── Helpers ───────────────────────────────────────────────────────────────

function makeEvent(userAgent) {
    var headers = {};
    if (userAgent !== undefined) {
        headers["user-agent"] = { value: userAgent };
    }
    return {
        request: {
            uri: "/slug",
            method: "GET",
            headers: headers,
            querystring: "",
        }
    };
}

function assertNotBot(req, label) {
    assert.strictEqual(req.headers["x-is-bot"].value, "false",
        (label || "") + ": x-is-bot should be false");
    assert.strictEqual(req.headers["x-bot-score"].value, "0",
        (label || "") + ": x-bot-score should be 0");
}

function assertBot(req, label) {
    assert.strictEqual(req.headers["x-is-bot"].value, "true",
        (label || "") + ": x-is-bot should be true");
    assert.strictEqual(req.headers["x-bot-score"].value, "1",
        (label || "") + ": x-bot-score should be 1");
}

function assertBlocked(response, expectedStatus, label) {
    assert.strictEqual(typeof response.statusCode, "number",
        (label || "") + ": response should have statusCode");
    assert.strictEqual(response.statusCode, expectedStatus,
        (label || "") + ": statusCode should be " + expectedStatus);
}

// ── Human traffic ─────────────────────────────────────────────────────────

(function testHumanChrome() {
    var event = makeEvent(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
    );
    var req = handler(event);
    assertNotBot(req, "Chrome browser");
    console.log("PASS: Chrome browser → not flagged");
})();

(function testHumanFirefox() {
    var event = makeEvent(
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10.15; rv:127.0) Gecko/20100101 Firefox/127.0"
    );
    var req = handler(event);
    assertNotBot(req, "Firefox browser");
    console.log("PASS: Firefox browser → not flagged");
})();

(function testHumanSafari() {
    var event = makeEvent(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1"
    );
    var req = handler(event);
    assertNotBot(req, "Safari on iPhone");
    console.log("PASS: Safari on iPhone → not flagged");
})();

(function testHumanEdge() {
    var event = makeEvent(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0"
    );
    var req = handler(event);
    assertNotBot(req, "Edge browser");
    console.log("PASS: Edge browser → not flagged");
})();

// ── Headless browser detection ────────────────────────────────────────────

(function testBotHeadlessChrome() {
    var event = makeEvent(
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "HeadlessChrome/110.0.5481.177 Safari/537.36"
    );
    var req = handler(event);
    assertBot(req, "HeadlessChrome");
    console.log("PASS: HeadlessChrome → flagged as bot");
})();

(function testBotPhantomJS() {
    var event = makeEvent(
        "Mozilla/5.0 (Unknown; Linux x86_64) AppleWebKit/538.1 " +
        "(KHTML, like Gecko) PhantomJS/2.1.1 Safari/538.1"
    );
    var req = handler(event);
    assertBot(req, "PhantomJS");
    console.log("PASS: PhantomJS → flagged as bot");
})();

(function testBotPuppeteer() {
    var event = makeEvent(
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) " +
        "HeadlessChrome/110.0.0.0 Safari/537.36"
    );
    var req = handler(event);
    // "HeadlessChrome" is in the blacklist via /headless/i
    assertBot(req, "Puppeteer (headless match)");
    console.log("PASS: Puppeteer default UA (headless) → flagged as bot");
})();

(function testBotPlaywright() {
    var event = makeEvent("Playwright/1.40.0");
    var req = handler(event);
    assertBot(req, "Playwright");
    console.log("PASS: Playwright → flagged as bot");
})();

(function testBotSelenium() {
    var event = makeEvent("Mozilla/5.0 Safari/537.36 Selenium/4.15");
    var req = handler(event);
    assertBot(req, "Selenium");
    console.log("PASS: Selenium → flagged as bot");
})();

(function testBotWebDriver() {
    var event = makeEvent("Mozilla/5.0 WebDriver/1.0");
    var req = handler(event);
    assertBot(req, "WebDriver");
    console.log("PASS: WebDriver → flagged as bot");
})();

// ── Programmatic HTTP library detection ───────────────────────────────────

(function testBotPythonRequests() {
    var event = makeEvent("python-requests/2.31.0");
    var req = handler(event);
    assertBot(req, "python-requests");
    console.log("PASS: python-requests → flagged as bot");
})();

(function testBotCurl() {
    var event = makeEvent("curl/8.4.0");
    var req = handler(event);
    assertBot(req, "curl");
    console.log("PASS: curl → flagged as bot");
})();

(function testBotWget() {
    var event = makeEvent("Wget/1.21.4");
    var req = handler(event);
    assertBot(req, "Wget");
    console.log("PASS: Wget → flagged as bot");
})();

(function testBotAxios() {
    var event = makeEvent("axios/1.6.0");
    var req = handler(event);
    assertBot(req, "Axios");
    console.log("PASS: Axios → flagged as bot");
})();

(function testBotGoHttpClient() {
    var event = makeEvent("Go-http-client/2.0");
    var req = handler(event);
    assertBot(req, "Go HTTP client");
    console.log("PASS: Go HTTP client → flagged as bot");
})();

(function testBotOkHttp() {
    var event = makeEvent("okhttp/4.12.0");
    var req = handler(event);
    assertBot(req, "OkHttp");
    console.log("PASS: OkHttp → flagged as bot");
})();

(function testBotJavaClient() {
    var event = makeEvent("Java/17.0.2");
    var req = handler(event);
    assertBot(req, "Java HTTP client");
    console.log("PASS: Java HTTP client → flagged as bot");
})();

(function testBotScrapy() {
    var event = makeEvent("Scrapy/2.11.0 (+https://scrapy.org)");
    var req = handler(event);
    assertBot(req, "Scrapy");
    console.log("PASS: Scrapy → flagged as bot");
})();

(function testBotPostman() {
    var event = makeEvent("PostmanRuntime/7.36.0");
    var req = handler(event);
    assertBot(req, "Postman");
    console.log("PASS: Postman → flagged as bot");
})();

// ── SEO / marketing crawler detection ─────────────────────────────────────

(function testBotSemrush() {
    var event = makeEvent("SemrushBot/7~bl (+http://www.semrush.com/bot.html)");
    var req = handler(event);
    assertBot(req, "SemrushBot");
    console.log("PASS: SemrushBot → flagged as bot");
})();

(function testBotAhrefs() {
    var event = makeEvent("AhrefsBot/7.0 (+http://ahrefs.com/robot/)");
    var req = handler(event);
    assertBot(req, "AhrefsBot");
    console.log("PASS: AhrefsBot → flagged as bot");
})();

(function testBotDotbot() {
    var event = makeEvent("Mozilla/5.0 (compatible; DotBot/1.2; +https://moz.com/dotbot)");
    var req = handler(event);
    assertBot(req, "DotBot");
    console.log("PASS: DotBot → flagged as bot");
})();

(function testBotBytespider() {
    var event = makeEvent("Mozilla/5.0 (compatible; Bytespider; +https://zhanzhang.toutiao.com/)");
    var req = handler(event);
    assertBot(req, "Bytespider");
    console.log("PASS: Bytespider → flagged as bot");
})();

(function testBotMJ12bot() {
    var event = makeEvent("MJ12bot/v1.4.8 (http://mj12bot.com/)");
    var req = handler(event);
    assertBot(req, "MJ12bot");
    console.log("PASS: MJ12bot → flagged as bot");
})();

// ── Security scanner detection ────────────────────────────────────────────

(function testBotNessus() {
    var event = makeEvent("Nessus SOAP");
    var req = handler(event);
    assertBot(req, "Nessus");
    console.log("PASS: Nessus → flagged as bot");
})();

(function testBotSqlmap() {
    var event = makeEvent("sqlmap/1.7#stable (https://sqlmap.org)");
    var req = handler(event);
    assertBot(req, "sqlmap");
    console.log("PASS: sqlmap → flagged as bot");
})();

(function testBotNikto() {
    var event = makeEvent("Nikto/2.1.6");
    var req = handler(event);
    assertBot(req, "Nikto");
    console.log("PASS: Nikto → flagged as bot");
})();

// ── Whitelist: legitimate search crawlers ─────────────────────────────────

(function testWhitelistGooglebot() {
    var event = makeEvent(
        "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)"
    );
    var req = handler(event);
    assertNotBot(req, "Googlebot");
    console.log("PASS: Googlebot → whitelisted (not flagged)");
})();

(function testWhitelistGooglebotSmartphone() {
    var event = makeEvent(
        "Mozilla/5.0 (Linux; Android 6.0.1; Nexus 5X Build/MMB29P) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/125.0.6422.175 Mobile Safari/537.36 (compatible; Googlebot/2.1)"
    );
    var req = handler(event);
    assertNotBot(req, "Googlebot smartphone");
    console.log("PASS: Googlebot smartphone → whitelisted");
})();

(function testWhitelistBingbot() {
    var event = makeEvent(
        "Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)"
    );
    var req = handler(event);
    assertNotBot(req, "Bingbot");
    console.log("PASS: Bingbot → whitelisted (not flagged)");
})();

(function testWhitelistDuckDuckBot() {
    var event = makeEvent("DuckDuckBot/1.0; (+http://duckduckgo.com/duckduckbot.html)");
    var req = handler(event);
    assertNotBot(req, "DuckDuckBot");
    console.log("PASS: DuckDuckBot → whitelisted");
})();

(function testWhitelistBaiduspider() {
    var event = makeEvent(
        "Mozilla/5.0 (compatible; Baiduspider/2.0; +http://www.baidu.com/search/spider.html)"
    );
    var req = handler(event);
    assertNotBot(req, "Baiduspider");
    console.log("PASS: Baiduspider → whitelisted");
})();

(function testWhitelistYandexBot() {
    var event = makeEvent(
        "Mozilla/5.0 (compatible; YandexBot/3.0; +http://yandex.com/bots)"
    );
    var req = handler(event);
    assertNotBot(req, "YandexBot");
    console.log("PASS: YandexBot → whitelisted");
})();

(function testWhitelistApplebot() {
    var event = makeEvent(
        "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1 (Applebot/0.1)"
    );
    var req = handler(event);
    assertNotBot(req, "Applebot");
    console.log("PASS: Applebot → whitelisted");
})();

(function testWhitelistTwitterbot() {
    var event = makeEvent("Twitterbot/1.0");
    var req = handler(event);
    assertNotBot(req, "Twitterbot");
    console.log("PASS: Twitterbot → whitelisted");
})();

(function testWhitelistFacebookExternalHit() {
    var event = makeEvent("facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)");
    var req = handler(event);
    assertNotBot(req, "Facebook external hit");
    console.log("PASS: FacebookExternalHit → whitelisted");
})();

(function testWhitelistLinkedInBot() {
    var event = makeEvent("LinkedInBot/1.0 (compatible; Mozilla/5.0; +http://www.linkedin.com/bot)");
    var req = handler(event);
    assertNotBot(req, "LinkedInBot");
    console.log("PASS: LinkedInBot → whitelisted");
})();

(function testWhitelistSlackbot() {
    var event = makeEvent("Slackbot-LinkExpanding 1.0 (+https://api.slack.com/robots)");
    var req = handler(event);
    assertNotBot(req, "Slackbot");
    console.log("PASS: Slackbot → whitelisted");
})();

(function testWhitelistDiscordbot() {
    var event = makeEvent("Mozilla/5.0 (compatible; Discordbot/2.0; +https://discordapp.com)");
    var req = handler(event);
    assertNotBot(req, "Discordbot");
    console.log("PASS: Discordbot → whitelisted");
})();

(function testWhitelistInternetArchive() {
    var event = makeEvent("ia_archiver (+http://web.archive.org/)");
    var req = handler(event);
    assertNotBot(req, "ia_archiver");
    console.log("PASS: Internet Archive → whitelisted");
})();

// ── Empty / missing User-Agent — blocking mode ───────────────────────────

(function testBlockEmptyUA() {
    var event = makeEvent("");
    var resp = handler(event);
    assertBlocked(resp, 403, "empty UA");
    assert.strictEqual(resp.statusDescription, "Forbidden");
    console.log("PASS: empty User-Agent → 403 Forbidden");
})();

(function testBlockMissingUAHeader() {
    var event = {
        request: {
            uri: "/slug",
            method: "GET",
            headers: {},
            querystring: "",
        }
    };
    var resp = handler(event);
    assertBlocked(resp, 403, "missing UA header");
    console.log("PASS: missing User-Agent header → 403 Forbidden");
})();

// ── Empty / missing User-Agent — permissive mode ─────────────────────────

(function testPermissiveEmptyUAPassesThrough() {
    var event = makeEvent("");
    var req = handlerPermissive(event);
    assertBot(req, "empty UA permissive");
    console.log("PASS: empty UA in permissive mode → passes through flagged");
})();

(function testPermissiveMissingUAPassesThrough() {
    var event = {
        request: {
            uri: "/slug",
            method: "GET",
            headers: {},
            querystring: "",
        }
    };
    var req = handlerPermissive(event);
    assertBot(req, "missing UA permissive");
    console.log("PASS: missing UA in permissive mode → passes through flagged");
})();

// ── Whitelist takes precedence over blacklist ─────────────────────────────

(function testWhitelistWinsWhenOverlapping() {
    // "AdsBot-Google" contains "googlebot" in its full UA string but should
    // still be whitelisted because /adsbot-google/i is in the whitelist.
    var event = makeEvent("AdsBot-Google (+http://www.google.com/adscbot.html)");
    var req = handler(event);
    assertNotBot(req, "AdsBot-Google");
    console.log("PASS: AdsBot-Google → whitelisted (precedence check)");
})();

// ── Edge cases ────────────────────────────────────────────────────────────

(function testCaseInsensitiveBotMatch() {
    var event = makeEvent("CURL/8.4.0");
    var req = handler(event);
    assertBot(req, "CURL uppercase");
    console.log("PASS: case-insensitive bot pattern match");
})();

(function testRealisticBogusUA() {
    // Some legitimate browsers send unusual UA strings — they should not match
    var event = makeEvent(
        "Mozilla/5.0 (X11; CrOS x86_64 14541.0.0) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36"
    );
    var req = handler(event);
    assertNotBot(req, "Chrome OS");
    console.log("PASS: Chrome OS UA → not flagged (no false positive)");
})();

(function testHeadersAddedForHuman() {
    var event = makeEvent("Safari/604.1");
    var req = handler(event);
    assert.strictEqual(req.headers["x-is-bot"].value, "false");
    assert.strictEqual(req.headers["x-bot-score"].value, "0");
    console.log("PASS: human request gets both X-Is-Bot and X-Bot-Score headers");
})();

(function testHeadersAddedForBot() {
    var event = makeEvent("curl/8.0.0");
    var req = handler(event);
    assert.strictEqual(req.headers["x-is-bot"].value, "true");
    assert.strictEqual(req.headers["x-bot-score"].value, "1");
    console.log("PASS: bot request gets both X-Is-Bot and X-Bot-Score headers");
})();

(function testDoesNotAlterUri() {
    var event = makeEvent("Chrome");
    event.request.uri = "/My-Slug";
    var req = handler(event);
    assert.strictEqual(req.uri, "/My-Slug");
    console.log("PASS: original URI is preserved");
})();

(function testDoesNotAlterQuerystring() {
    var event = makeEvent("Chrome");
    event.request.querystring = "utm_source=twitter";
    var req = handler(event);
    assert.strictEqual(req.querystring, "utm_source=twitter");
    console.log("PASS: querystring is preserved");
})();

// ── Summary ───────────────────────────────────────────────────────────────

console.log("\nAll tests passed.");
