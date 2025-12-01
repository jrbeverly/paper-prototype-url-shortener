// Tests for the CloudFront security headers function.
// Run: node modules/cloudfront-security-headers/function.test.js

const assert = require("assert");
const fs = require("fs");
const path = require("path");

// ── Test harness ──────────────────────────────────────────────────────────

const source = fs.readFileSync(path.join(__dirname, "function.js"), "utf8");
const handler = new Function(source + "; return handler;")();

// ── Helpers ───────────────────────────────────────────────────────────────

function makeEvent(opts) {
    opts = opts || {};
    return {
        context: {
            distributionDomainName: "d1234567890.cloudfront.net",
            distributionId: "EDFDVBD6EXAMPLE",
            eventType: "viewer-response",
            requestId: opts.requestId !== undefined ? opts.requestId : "test-request-id-abc"
        },
        request: {
            uri: opts.uri || "/slug",
            method: "GET",
            headers: opts.requestHeaders || {},
            querystring: opts.querystring || ""
        },
        response: {
            statusCode: opts.statusCode !== undefined ? opts.statusCode : 302,
            statusDescription: opts.statusDescription || "Found",
            headers: opts.responseHeaders || {}
        }
    };
}

function httpsEvent(opts) {
    opts = opts || {};
    opts.requestHeaders = opts.requestHeaders || {};
    opts.requestHeaders["cloudfront-forwarded-proto"] = { value: "https" };
    return makeEvent(opts);
}

function httpEvent(opts) {
    opts = opts || {};
    opts.requestHeaders = opts.requestHeaders || {};
    opts.requestHeaders["cloudfront-forwarded-proto"] = { value: "http" };
    return makeEvent(opts);
}

function hval(response, name) {
    var h = response.headers[name];
    return h ? h.value : undefined;
}

// ── AC1: Security headers are present on all responses ────────────────────

(function testSecurityHeadersOnRedirect() {
    var resp = handler(httpsEvent({ statusCode: 302 }));
    assert.strictEqual(hval(resp, "x-content-type-options"), "nosniff");
    assert.strictEqual(hval(resp, "x-frame-options"), "DENY");
    assert.ok(hval(resp, "strict-transport-security"));
    console.log("PASS: security headers present on 302 redirect");
})();

(function testSecurityHeadersOnOk() {
    var resp = handler(httpsEvent({ statusCode: 200, statusDescription: "OK" }));
    assert.strictEqual(hval(resp, "x-content-type-options"), "nosniff");
    assert.strictEqual(hval(resp, "x-frame-options"), "DENY");
    console.log("PASS: security headers present on 200 OK");
})();

(function testXContentTypeOptionsValue() {
    var resp = handler(httpsEvent({}));
    assert.strictEqual(hval(resp, "x-content-type-options"), "nosniff");
    console.log("PASS: X-Content-Type-Options is 'nosniff'");
})();

(function testXFrameOptionsValue() {
    var resp = handler(httpsEvent({}));
    assert.strictEqual(hval(resp, "x-frame-options"), "DENY");
    console.log("PASS: X-Frame-Options is 'DENY'");
})();

// ── AC2: HSTS header is present on HTTPS responses ────────────────────────

(function testHstsOnHttpsViaForwardedProto() {
    var resp = handler(httpsEvent({}));
    assert.strictEqual(
        hval(resp, "strict-transport-security"),
        "max-age=31536000; includeSubDomains"
    );
    console.log("PASS: HSTS set when cloudfront-forwarded-proto is https");
})();

(function testHstsOnHttpsViaXForwardedProto() {
    var event = makeEvent({
        requestHeaders: { "x-forwarded-proto": { value: "https" } }
    });
    var resp = handler(event);
    assert.strictEqual(
        hval(resp, "strict-transport-security"),
        "max-age=31536000; includeSubDomains"
    );
    console.log("PASS: HSTS set when x-forwarded-proto is https (fallback)");
})();

(function testHstsAssumedWhenNoProtoHeader() {
    // No protocol header → assume HTTPS (CloudFront should redirect HTTP to HTTPS)
    var resp = handler(makeEvent({}));
    assert.strictEqual(
        hval(resp, "strict-transport-security"),
        "max-age=31536000; includeSubDomains"
    );
    console.log("PASS: HSTS set when no protocol header (HTTPS assumed)");
})();

(function testHstsNotSetOnHttp() {
    var resp = handler(httpEvent({}));
    assert.strictEqual(hval(resp, "strict-transport-security"), undefined);
    console.log("PASS: HSTS not set when cloudfront-forwarded-proto is http");
})();

(function testHstsMaxAge() {
    var resp = handler(httpsEvent({}));
    var hsts = hval(resp, "strict-transport-security");
    assert.ok(hsts.indexOf("max-age=31536000") !== -1, "HSTS must include max-age=31536000");
    assert.ok(hsts.indexOf("includeSubDomains") !== -1, "HSTS must include includeSubDomains");
    console.log("PASS: HSTS max-age=31536000 and includeSubDomains are present");
})();

// ── AC3: Request ID enables end-to-end tracing ────────────────────────────

(function testRequestIdFromContext() {
    var resp = handler(makeEvent({ requestId: "cf-req-12345" }));
    assert.strictEqual(hval(resp, "x-request-id"), "cf-req-12345");
    console.log("PASS: X-Request-Id matches event.context.requestId");
})();

(function testRequestIdIsUnique() {
    var resp1 = handler(makeEvent({ requestId: "req-aaa" }));
    var resp2 = handler(makeEvent({ requestId: "req-bbb" }));
    assert.notStrictEqual(hval(resp1, "x-request-id"), hval(resp2, "x-request-id"));
    console.log("PASS: X-Request-Id differs across requests");
})();

(function testRequestIdFallsBackToEmptyString() {
    // Simulate missing requestId in context
    var event = makeEvent({});
    event.context.requestId = undefined;
    var resp = handler(event);
    assert.strictEqual(hval(resp, "x-request-id"), "");
    console.log("PASS: X-Request-Id falls back to empty string when context has no requestId");
})();

// ── X-Response-Time ───────────────────────────────────────────────────────

(function testResponseTimeIsNumericString() {
    var before = Date.now();
    var resp = handler(makeEvent({}));
    var after = Date.now();
    var value = hval(resp, "x-response-time");
    assert.ok(value !== undefined, "X-Response-Time must be present");
    var ms = parseInt(value, 10);
    assert.ok(!isNaN(ms), "X-Response-Time must be parseable as an integer");
    assert.ok(ms >= before && ms <= after, "X-Response-Time must be in the current epoch range");
    console.log("PASS: X-Response-Time is a valid epoch-ms timestamp");
})();

// ── Cache-Control defaults for error pages ────────────────────────────────

(function testCacheControlNoStoreOn400() {
    var resp = handler(httpsEvent({ statusCode: 400, statusDescription: "Bad Request" }));
    assert.strictEqual(hval(resp, "cache-control"), "no-store");
    console.log("PASS: Cache-Control: no-store added for 400");
})();

(function testCacheControlNoStoreOn404() {
    var resp = handler(httpsEvent({ statusCode: 404, statusDescription: "Not Found" }));
    assert.strictEqual(hval(resp, "cache-control"), "no-store");
    console.log("PASS: Cache-Control: no-store added for 404");
})();

(function testCacheControlNoStoreOn500() {
    var resp = handler(httpsEvent({ statusCode: 500, statusDescription: "Internal Server Error" }));
    assert.strictEqual(hval(resp, "cache-control"), "no-store");
    console.log("PASS: Cache-Control: no-store added for 500");
})();

(function testCacheControlNoStoreOn503() {
    var resp = handler(httpsEvent({ statusCode: 503, statusDescription: "Service Unavailable" }));
    assert.strictEqual(hval(resp, "cache-control"), "no-store");
    console.log("PASS: Cache-Control: no-store added for 503");
})();

(function testCacheControlNotAddedFor200() {
    var resp = handler(httpsEvent({ statusCode: 200, statusDescription: "OK" }));
    assert.strictEqual(hval(resp, "cache-control"), undefined);
    console.log("PASS: Cache-Control not added for 200 OK");
})();

(function testCacheControlNotAddedFor302() {
    var resp = handler(httpsEvent({ statusCode: 302 }));
    assert.strictEqual(hval(resp, "cache-control"), undefined);
    console.log("PASS: Cache-Control not added for 302 redirect");
})();

(function testCacheControlNotAddedFor301() {
    var resp = handler(httpsEvent({ statusCode: 301, statusDescription: "Moved Permanently" }));
    assert.strictEqual(hval(resp, "cache-control"), undefined);
    console.log("PASS: Cache-Control not added for 301 redirect");
})();

// ── AC5: Headers don't override origin-set values ─────────────────────────

(function testDoesNotOverrideXContentTypeOptions() {
    var resp = handler(httpsEvent({
        responseHeaders: { "x-content-type-options": { value: "nosniff; something-extra" } }
    }));
    assert.strictEqual(hval(resp, "x-content-type-options"), "nosniff; something-extra");
    console.log("PASS: origin X-Content-Type-Options is preserved");
})();

(function testDoesNotOverrideXFrameOptions() {
    var resp = handler(httpsEvent({
        responseHeaders: { "x-frame-options": { value: "SAMEORIGIN" } }
    }));
    assert.strictEqual(hval(resp, "x-frame-options"), "SAMEORIGIN");
    console.log("PASS: origin X-Frame-Options is preserved");
})();

(function testDoesNotOverrideHsts() {
    var originHsts = "max-age=63072000; includeSubDomains; preload";
    var resp = handler(httpsEvent({
        responseHeaders: { "strict-transport-security": { value: originHsts } }
    }));
    assert.strictEqual(hval(resp, "strict-transport-security"), originHsts);
    console.log("PASS: origin HSTS is preserved");
})();

(function testDoesNotOverrideXRequestId() {
    var resp = handler(makeEvent({
        requestId: "cf-id-from-context",
        responseHeaders: { "x-request-id": { value: "origin-request-id-xyz" } }
    }));
    assert.strictEqual(hval(resp, "x-request-id"), "origin-request-id-xyz");
    console.log("PASS: origin X-Request-Id is preserved");
})();

(function testDoesNotOverrideXResponseTime() {
    var resp = handler(makeEvent({
        responseHeaders: { "x-response-time": { value: "42" } }
    }));
    assert.strictEqual(hval(resp, "x-response-time"), "42");
    console.log("PASS: origin X-Response-Time is preserved");
})();

(function testDoesNotOverrideCacheControlOnError() {
    var resp = handler(httpsEvent({
        statusCode: 404,
        responseHeaders: { "cache-control": { value: "max-age=60" } }
    }));
    assert.strictEqual(hval(resp, "cache-control"), "max-age=60");
    console.log("PASS: origin Cache-Control on error response is preserved");
})();

// ── Response integrity ────────────────────────────────────────────────────

(function testReturnsResponseObject() {
    var event = httpsEvent({ statusCode: 302 });
    var resp = handler(event);
    assert.strictEqual(resp, event.response, "must return the same response object");
    console.log("PASS: handler returns the response object");
})();

(function testStatusCodeNotAltered() {
    var resp = handler(httpsEvent({ statusCode: 302 }));
    assert.strictEqual(resp.statusCode, 302);
    console.log("PASS: statusCode is not altered");
})();

(function testRequestUriNotAltered() {
    var event = httpsEvent({ uri: "/my-link" });
    handler(event);
    assert.strictEqual(event.request.uri, "/my-link");
    console.log("PASS: request URI is not altered");
})();

// ── AC4: Function completes in < 1ms ─────────────────────────────────────

(function testPerformance() {
    var event = httpsEvent({ statusCode: 302 });
    var iterations = 1000;
    var start = Date.now();
    for (var i = 0; i < iterations; i++) {
        // Fresh response headers each call to avoid the no-override short-circuit
        event.response.headers = {};
        handler(event);
    }
    var elapsedMs = Date.now() - start;
    var avgMs = elapsedMs / iterations;
    assert.ok(avgMs < 1,
        "Average per-call time " + avgMs.toFixed(3) + "ms should be < 1ms");
    console.log("PASS: function averages " + avgMs.toFixed(3) + "ms per call (< 1ms target)");
})();

// ── Summary ───────────────────────────────────────────────────────────────

console.log("\nAll tests passed.");
