# Quickstart

Create your first short link in under 5 minutes.

## Prerequisites

1. **API key** — Generate one from the dashboard under **Settings → API Keys**, or [create one via the API](reference/api-keys.md).
2. **Tenant ID** — Your workspace identifier (UUID). Available in the dashboard under **Settings → Workspace** or from the API key creation response.
3. **Domain ID** — A verified custom domain. If you don't have one yet, see [step 1](#step-1-add-a-domain) below.

## The 5-line version

If you already have a domain set up, creating a link is a single API call:

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/links \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"domainId": "your-domain-id", "destinationUrl": "https://example.com/long-path"}'
```

Response:

```json
{
  "id": "3f2e1d0c-...",
  "shortUrl": "https://go.yourbrand.com/aB3xYz",
  "destinationUrl": "https://example.com/long-path",
  "slug": "aB3xYz",
  "status": "active",
  "clickCount": 0,
  "createdAt": "2024-11-01T14:32:00Z"
}
```

The `shortUrl` is ready to use immediately.

---

## Step 1: Add a domain

Before creating links, register a custom domain. Skip this step if you already have a domain with status `active`.

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/domains \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"hostname": "go.yourbrand.com"}'
```

Response:

```json
{
  "id": "d1e2f3a4-...",
  "hostname": "go.yourbrand.com",
  "status": "pending_verification",
  "verificationInstructions": {
    "txtName": "_short-io-verify.go.yourbrand.com",
    "txtValue": "short-io-verify=abc123",
    "cnameName": "go.yourbrand.com",
    "cnameValue": "cname.short.io"
  }
}
```

Add both DNS records at your DNS provider, then verify:

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/domains/{domainId}/verify \
  -H "X-API-Key: sk_your_key_here"
```

Once verified, the domain status changes to `active` and you can create links on it.

## Step 2: Create a short link

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/links \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "domainId": "d1e2f3a4-...",
    "destinationUrl": "https://example.com/your/long/url"
  }'
```

The API auto-generates a slug. To use a custom slug:

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/links \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "domainId": "d1e2f3a4-...",
    "destinationUrl": "https://example.com/your/long/url",
    "slug": "summer-sale"
  }'
```

## Step 3: Use the short URL

The `shortUrl` in the response is immediately live. Share it anywhere — no warm-up or propagation delay.

```
https://go.yourbrand.com/summer-sale  →  https://example.com/your/long/url
```

---

## Code examples

### cURL

```bash
# Create a link
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/links \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"domainId": "d1e2f3a4-...", "destinationUrl": "https://example.com"}'
```

### Python

```python
import requests

API_KEY = "sk_your_key_here"
TENANT_ID = "your-tenant-id"
BASE_URL = f"https://api.short.io/api/v1/tenants/{TENANT_ID}"

headers = {"X-API-Key": API_KEY}

# Create a link
response = requests.post(
    f"{BASE_URL}/links",
    headers=headers,
    json={
        "domainId": "d1e2f3a4-...",
        "destinationUrl": "https://example.com",
    },
)
response.raise_for_status()
link = response.json()
print(link["shortUrl"])  # https://go.yourbrand.com/aB3xYz
```

### JavaScript (Node.js / browser)

> **Note:** Never expose your API key in client-side (browser) code. Make API calls from your server.

```javascript
const API_KEY = "sk_your_key_here";
const TENANT_ID = "your-tenant-id";
const BASE_URL = `https://api.short.io/api/v1/tenants/${TENANT_ID}`;

// Create a link
const response = await fetch(`${BASE_URL}/links`, {
  method: "POST",
  headers: {
    "X-API-Key": API_KEY,
    "Content-Type": "application/json",
  },
  body: JSON.stringify({
    domainId: "d1e2f3a4-...",
    destinationUrl: "https://example.com",
  }),
});

if (!response.ok) {
  const error = await response.json();
  throw new Error(error.detail ?? "API error");
}

const link = await response.json();
console.log(link.shortUrl); // https://go.yourbrand.com/aB3xYz
```

### C\#

```csharp
using System.Net.Http.Json;

var apiKey = "sk_your_key_here";
var tenantId = "your-tenant-id";

using var client = new HttpClient { BaseAddress = new Uri("https://api.short.io") };
client.DefaultRequestHeaders.Add("X-API-Key", apiKey);

// Create a link
var response = await client.PostAsJsonAsync(
    $"/api/v1/tenants/{tenantId}/links",
    new
    {
        domainId = "d1e2f3a4-...",
        destinationUrl = "https://example.com",
    });

response.EnsureSuccessStatusCode();

var link = await response.Content.ReadFromJsonAsync<JsonElement>();
Console.WriteLine(link.GetProperty("shortUrl").GetString());
// https://go.yourbrand.com/aB3xYz
```

### Ruby

```ruby
require "net/http"
require "json"
require "uri"

API_KEY  = "sk_your_key_here"
TENANT_ID = "your-tenant-id"

def short_io_post(path, body)
  uri  = URI("https://api.short.io#{path}")
  http = Net::HTTP.new(uri.host, uri.port)
  http.use_ssl = true

  req = Net::HTTP::Post.new(uri, {
    "X-API-Key"    => API_KEY,
    "Content-Type" => "application/json"
  })
  req.body = body.to_json

  response = http.request(req)
  raise "API error #{response.code}: #{response.body}" unless response.is_a?(Net::HTTPSuccess)
  JSON.parse(response.body)
end

# Create a link
link = short_io_post(
  "/api/v1/tenants/#{TENANT_ID}/links",
  { domainId: "d1e2f3a4-...", destinationUrl: "https://example.com" }
)
puts link["shortUrl"]  # https://go.yourbrand.com/aB3xYz
```

---

## What's next

- [Create multiple links at once](reference/links.md#bulk-create) — up to 10,000 in a single request
- [Import links from CSV](reference/links.md#import-csv)
- [Set link expiry dates](reference/links.md#create-a-link)
- [Choose your redirect type](reference/links.md#redirect-types) — 301, 302, 307, or 308
- [Understand rate limits](rate-limiting.md)
