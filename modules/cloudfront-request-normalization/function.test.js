// Tests for the CloudFront request normalization function.
// Run: node modules/cloudfront-request-normalization/function.test.js

const assert = require("assert");
const fs = require("fs");
const path = require("path");

// Load the handler from the source file (CloudFront Functions don't use CommonJS).
const source = fs.readFileSync(path.join(__dirname, "function.js"), "utf8");
const handler = new Function(source + "; return handler;")();

// Helpers ----------------------------------------------------------------

function makeEvent(uri, host) {
    return {
        request: {
            uri: uri,
            method: "GET",
            headers: host !== undefined
                ? { host: { value: host } }
                : { host: { value: "" } },
            querystring: "",
        }
    };
}

// Tests ------------------------------------------------------------------

// --- Host normalization ---

(function testLowercasesHostHeader() {
    var event = makeEvent("/slug", "Example.COM");
    var req = handler(event);
    assert.strictEqual(req.headers.host.value, "example.com");
    assert.strictEqual(req.headers["x-normalized-host"].value, "example.com");
    console.log("PASS: lowercases host header");
})();

(function testHandlesMissingHostHeader() {
    var event = { request: { uri: "/slug", method: "GET", headers: {}, querystring: "" } };
    var req = handler(event);
    assert.strictEqual(req.headers["x-normalized-host"].value, "");
    console.log("PASS: handles missing host header");
})();

// --- Path normalization ---

(function testLowercasesPath() {
    var event = makeEvent("/My-Slug", "example.com");
    var req = handler(event);
    assert.strictEqual(req.uri, "/my-slug");
    assert.strictEqual(req.headers["x-normalized-slug"].value, "my-slug");
    console.log("PASS: lowercases path");
})();

// --- Trailing slash ---

(function testStripsTrailingSlash() {
    var event = makeEvent("/slug/", "example.com");
    var req = handler(event);
    assert.strictEqual(req.uri, "/slug");
    assert.strictEqual(req.headers["x-normalized-slug"].value, "slug");
    console.log("PASS: strips trailing slash");
})();

(function testPreservesRootPath() {
    var event = makeEvent("/", "example.com");
    var req = handler(event);
    assert.strictEqual(req.uri, "/");
    assert.strictEqual(req.headers["x-normalized-slug"].value, "");
    console.log("PASS: preserves root path /");
})();

// --- URL decoding ---

(function testDecodesPercentEncoding() {
    var event = makeEvent("/my%20slug", "example.com");
    var req = handler(event);
    assert.strictEqual(req.uri, "/my slug");
    assert.strictEqual(req.headers["x-normalized-slug"].value, "my slug");
    console.log("PASS: decodes percent-encoded characters");
})();

(function testDecodesMultipleEncodedChars() {
    var event = makeEvent("/slug%2Dwith%2Ddashes", "example.com");
    var req = handler(event);
    assert.strictEqual(req.uri, "/slug-with-dashes");
    console.log("PASS: decodes multiple percent-encoded characters");
})();

(function testDecodesUnicode() {
    var event = makeEvent("/caf%C3%A9", "example.com");
    var req = handler(event);
    assert.strictEqual(req.uri, "/café");
    console.log("PASS: decodes UTF-8 percent-encoded characters");
})();

// --- Invalid encoding ---

(function testRejectsInvalidPercentEncoding() {
    var event = makeEvent("/bad%ZZ", "example.com");
    var resp = handler(event);
    assert.strictEqual(resp.statusCode, 400);
    console.log("PASS: rejects invalid percent encoding with 400");
})();

// --- Path too long ---

(function testRejectsExcessivelyLongPath() {
    var longPath = "/" + "a".repeat(256);
    var event = makeEvent(longPath, "example.com");
    var resp = handler(event);
    assert.strictEqual(resp.statusCode, 414);
    console.log("PASS: rejects path > 255 chars with 414");
})();

(function testAllowsMaxLengthPath() {
    var maxPath = "/" + "a".repeat(254);
    var event = makeEvent(maxPath, "example.com");
    var req = handler(event);
    assert.strictEqual(typeof req.statusCode, "undefined");
    assert.ok(req.uri);
    console.log("PASS: allows path exactly 255 chars");
})();

// --- Query string preservation ---

(function testPreservesQueryString() {
    var event = { request: { uri: "/slug", method: "GET", headers: { host: { value: "example.com" } }, querystring: "utm_source=twitter" } };
    var req = handler(event);
    assert.strictEqual(req.querystring, "utm_source=twitter");
    console.log("PASS: query string preserved unchanged");
})();

// --- Combined normalization ---

(function testCombinedNormalization() {
    var event = makeEvent("/My%20Link/", "WWW.EXAMPLE.COM");
    var req = handler(event);
    assert.strictEqual(req.uri, "/my link");
    assert.strictEqual(req.headers["x-normalized-host"].value, "www.example.com");
    assert.strictEqual(req.headers["x-normalized-slug"].value, "my link");
    console.log("PASS: combined normalization (decode + lower + trim)");
})();

// --- Health endpoint ---

(function testHealthEndpointPassesThrough() {
    var event = makeEvent("/health", "example.com");
    var req = handler(event);
    assert.strictEqual(req.uri, "/health");
    assert.strictEqual(req.headers["x-normalized-slug"].value, "health");
    console.log("PASS: /health endpoint passes through normalized");
})();

// --- Empty slug (root) ---

(function testRootSlugIsEmptyString() {
    var event = makeEvent("/", "example.com");
    var req = handler(event);
    assert.strictEqual(req.headers["x-normalized-slug"].value, "");
    console.log("PASS: root path yields empty slug");
})();

// --- Host with mixed case and trailing dot ---

(function testHostWithComplexCase() {
    var event = makeEvent("/slug", "Go.Customer.COM.");
    var req = handler(event);
    assert.strictEqual(req.headers["x-normalized-host"].value, "go.customer.com.");
    console.log("PASS: complex hostname lowercased");
})();

console.log("\nAll tests passed.");
