// CloudFront Function: normalizes incoming requests at the edge.
//
// Responsibilities:
//   - Lowercase host and path for case-insensitive matching
//   - Strip trailing slashes
//   - URL-decode percent-encoded characters in the path
//   - Add normalized values as x-normalized-host and x-normalized-slug headers
//   - Reject paths longer than 255 characters
//
// All strings must use ASCII-compatible encoding. Normalization never
// alters the query string (available separately as event.request.querystring).
function handler(event) {
    var request = event.request;
    var uri = request.uri;

    // Reject excessively long paths before any processing.
    if (uri.length > 255) {
        return {
            statusCode: 414,
            statusDescription: "URI Too Long",
            headers: { "content-type": { value: "text/plain" } },
            body: "URI Too Long"
        };
    }

    // URL-decode percent-encoded characters (e.g. %20 -> space).
    // Invalid encoding (e.g. %ZZ) throws URIError — treat as a bad request.
    var decoded;
    try {
        decoded = decodeURIComponent(uri);
    } catch (e) {
        return {
            statusCode: 400,
            statusDescription: "Bad Request",
            headers: { "content-type": { value: "text/plain" } },
            body: "Bad Request"
        };
    }

    // Strip a single trailing slash: /slug/ -> /slug, but preserve /.
    if (decoded.length > 1 && decoded.charAt(decoded.length - 1) === "/") {
        decoded = decoded.substring(0, decoded.length - 1);
    }

    // Case-fold the path.
    var normalizedPath = decoded.toLowerCase();

    // Extract the slug (path without the leading /).
    var slug = normalizedPath.charAt(0) === "/"
        ? normalizedPath.substring(1)
        : normalizedPath;

    // Case-fold the host header.
    var rawHost = request.headers.host ? request.headers.host.value : "";
    var normalizedHost = rawHost.toLowerCase();

    // Overwrite the Host header so downstream systems see the normalized value.
    request.headers.host = { value: normalizedHost };

    // Add custom headers for the origin to consume directly.
    request.headers["x-normalized-host"] = { value: normalizedHost };
    request.headers["x-normalized-slug"] = { value: slug };

    // Update the URI to the normalized path. The query string is maintained
    // separately by CloudFront and is not affected.
    request.uri = normalizedPath;

    return request;
}
