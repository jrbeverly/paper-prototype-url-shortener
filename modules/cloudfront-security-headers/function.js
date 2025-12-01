// CloudFront Function: injects security and observability headers on viewer responses.
//
// Responsibilities:
//   - Inject X-Content-Type-Options: nosniff
//   - Inject X-Frame-Options: DENY
//   - Inject Strict-Transport-Security on HTTPS connections
//   - Add X-Request-Id from CloudFront's requestId for end-to-end tracing
//   - Add X-Response-Time (epoch ms at edge) for observability
//   - Apply Cache-Control: no-store default for error responses (4xx/5xx)
//   - Never overrides headers already set by the origin
//
// Trigger: viewer-response
// Runtime: CloudFront Functions (ES5-compatible JavaScript subset)

function handler(event) {
    var request = event.request;
    var response = event.response;
    var headers = response.headers;

    // ── Security headers ──────────────────────────────────────────────────────
    // Each header is set only when the origin has not already provided it.

    if (!headers["x-content-type-options"]) {
        headers["x-content-type-options"] = { value: "nosniff" };
    }

    if (!headers["x-frame-options"]) {
        headers["x-frame-options"] = { value: "DENY" };
    }

    // HSTS: only inject on HTTPS connections.
    // cloudfront-forwarded-proto is present when the distribution's origin
    // request policy includes it.  x-forwarded-proto is checked as a fallback.
    // When neither header is present the connection is assumed to be HTTPS —
    // CloudFront distributions should redirect HTTP to HTTPS at the viewer level.
    if (!headers["strict-transport-security"]) {
        var protoHeader = request.headers["cloudfront-forwarded-proto"] ||
            request.headers["x-forwarded-proto"];
        var isHttps = !protoHeader || protoHeader.value === "https";
        if (isHttps) {
            headers["strict-transport-security"] = {
                value: "max-age=31536000; includeSubDomains"
            };
        }
    }

    // ── Observability headers ─────────────────────────────────────────────────

    // Propagate CloudFront's own requestId so every response carries a
    // traceable identifier that correlates access logs, Lambda logs, and the
    // viewer experience.
    if (!headers["x-request-id"]) {
        headers["x-request-id"] = { value: event.context.requestId || "" };
    }

    // Epoch milliseconds when the edge processed this response.
    // If the origin (e.g. Lambda) already set X-Response-Time, it is preserved.
    if (!headers["x-response-time"]) {
        headers["x-response-time"] = { value: String(Date.now()) };
    }

    // ── Cache-Control defaults for error responses ────────────────────────────
    // Prevent transient error pages from being cached by browsers or intermediate
    // proxies.  Success and redirect responses inherit whatever the origin set.

    if (!headers["cache-control"] && response.statusCode >= 400) {
        headers["cache-control"] = { value: "no-store" };
    }

    return response;
}
