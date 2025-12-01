# Invoices

The invoices endpoints give you programmatic access to your billing history and upcoming charges. Invoice data is sourced from Stripe.

**Base path:** `/api/v1/tenants/{tenantId}/invoices`

**Required permission:** `billing:read`

---

## The invoice object

```json
{
  "id": "in_1ABC123...",
  "status": "paid",
  "amountDue": 2900,
  "amountPaid": 2900,
  "subtotal": 2900,
  "total": 2900,
  "currency": "usd",
  "createdAt": "2024-11-01T00:00:00Z",
  "dueDate": "2024-11-01T00:00:00Z",
  "periodStart": "2024-11-01T00:00:00Z",
  "periodEnd": "2024-11-30T23:59:59Z",
  "invoicePdfUrl": "https://pay.stripe.com/invoice/...",
  "hostedInvoiceUrl": "https://invoice.stripe.com/i/...",
  "description": "Short.io Pro — November 2024",
  "lineItems": [
    {
      "id": "il_1ABC...",
      "description": "Short.io Pro × 1",
      "amount": 2900,
      "currency": "usd",
      "type": "plan",
      "periodStart": "2024-11-01T00:00:00Z",
      "periodEnd": "2024-11-30T23:59:59Z"
    }
  ]
}
```

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Stripe invoice ID |
| `status` | string | `paid`, `open`, `overdue`, or `void` |
| `amountDue` | integer | Amount due in cents |
| `amountPaid` | integer | Amount paid in cents |
| `subtotal` | integer | Subtotal before credits/discounts in cents |
| `total` | integer | Total after credits/discounts in cents |
| `currency` | string | Currency code (lowercase ISO 4217, e.g. `usd`) |
| `createdAt` | datetime | Invoice creation date (UTC) |
| `dueDate` | datetime \| null | Payment due date (UTC) |
| `periodStart` | datetime \| null | Billing period start (UTC) |
| `periodEnd` | datetime \| null | Billing period end (UTC) |
| `invoicePdfUrl` | string \| null | Direct link to the invoice PDF |
| `hostedInvoiceUrl` | string \| null | Stripe-hosted invoice page |
| `description` | string \| null | Invoice description |
| `lineItems` | array | Individual line items (see below) |

### Line item object

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Stripe line item ID |
| `description` | string | Line item description |
| `amount` | integer | Amount in cents |
| `currency` | string | Currency code |
| `type` | string | `plan`, `overage`, or `credit` |
| `periodStart` | datetime \| null | Period covered by this line item |
| `periodEnd` | datetime \| null | Period end |

---

## List invoices

```
GET /api/v1/tenants/{tenantId}/invoices
```

**Required permission:** `billing:read`

Returns invoices in reverse chronological order (most recent first).

### Query parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `limit` | integer | `10` | Items per page. Range 1–100. |
| `startingAfter` | string | — | Pagination cursor: Stripe invoice ID. Returns invoices created after this one. |

### Example

```bash
# Fetch most recent 10 invoices
curl "https://api.short.io/api/v1/tenants/{tenantId}/invoices" \
  -H "X-API-Key: sk_your_key_here"

# Next page
curl "https://api.short.io/api/v1/tenants/{tenantId}/invoices?startingAfter=in_1ABC123" \
  -H "X-API-Key: sk_your_key_here"
```

```python
# Fetch all invoices (paginate automatically)
def list_all_invoices(tenant_id, api_key):
    invoices = []
    cursor = None
    while True:
        params = {"limit": 100}
        if cursor:
            params["startingAfter"] = cursor
        response = requests.get(
            f"https://api.short.io/api/v1/tenants/{tenant_id}/invoices",
            headers={"X-API-Key": api_key},
            params=params,
        )
        data = response.json()
        invoices.extend(data["items"])
        if not data["hasMore"]:
            break
        cursor = data["nextCursor"]
    return invoices
```

### Response

```json
{
  "items": [ ...invoice objects... ],
  "hasMore": true,
  "nextCursor": "in_1ABC123..."
}
```

---

## Get an invoice

```
GET /api/v1/tenants/{tenantId}/invoices/{invoiceId}
```

**Required permission:** `billing:read`

### Example

```bash
curl "https://api.short.io/api/v1/tenants/{tenantId}/invoices/in_1ABC123" \
  -H "X-API-Key: sk_your_key_here"
```

### Response

`200 OK` with the full [invoice object](#the-invoice-object) including `lineItems`.

---

## Get the upcoming invoice

```
GET /api/v1/tenants/{tenantId}/invoices/upcoming
```

**Required permission:** `billing:read`

Returns a preview of the next scheduled invoice. Useful for showing customers what they will be charged before the billing cycle ends.

### Example

```bash
curl "https://api.short.io/api/v1/tenants/{tenantId}/invoices/upcoming" \
  -H "X-API-Key: sk_your_key_here"
```

### Response

`200 OK` with the upcoming invoice object. The invoice has no `id` since it has not yet been finalized by Stripe.

### Errors

| Status | Cause |
|--------|-------|
| `404` | No active subscription — workspace is on the free plan |

---

## Download invoice PDF

```
GET /api/v1/tenants/{tenantId}/invoices/{invoiceId}/pdf
```

**Required permission:** `billing:read`

Returns a `302 Found` redirect to the Stripe-hosted invoice PDF. The redirect URL is short-lived.

### Example

```bash
# Follow the redirect to download the PDF
curl -L "https://api.short.io/api/v1/tenants/{tenantId}/invoices/in_1ABC123/pdf" \
  -H "X-API-Key: sk_your_key_here" \
  --output invoice.pdf
```

### Response

`302 Found` with `Location` header pointing to the Stripe invoice PDF URL.
