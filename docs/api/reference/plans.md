# Plans

The plans endpoint returns the available subscription plans and their limits. It is public — no authentication required.

**Endpoint:** `GET /api/v1/plans`

---

## Available plans

| Plan | Price | Domains | Links/domain | Tracked clicks/mo | Analytics | Trial |
|------|-------|---------|-------------|-------------------|-----------|-------|
| **free** | $0 | 3 | 100 | 1,000 | 30 days | — |
| **starter** | $9/mo | 10 | 1,000 | 10,000 | 90 days | 14 days |
| **pro** | $29/mo | 50 | 10,000 | 100,000 | 180 days | 14 days |
| **team** | $79/mo | 100 | 50,000 | 500,000 | 365 days | 14 days |
| **business** | $249/mo | 500 | 200,000 | 2,000,000 | 730 days | — |
| **enterprise** | Custom | 1,000 | 1,000,000 | Unlimited | 1,095 days | — |

### Features by plan

| Feature | free | starter | pro | team | business | enterprise |
|---------|------|---------|-----|------|----------|------------|
| API access | — | ✓ | ✓ | ✓ | ✓ | ✓ |
| Analytics export | — | ✓ | ✓ | ✓ | ✓ | ✓ |
| Advanced analytics | — | — | ✓ | ✓ | ✓ | ✓ |
| Bulk import | — | — | ✓ | ✓ | ✓ | ✓ |
| Custom QR codes | — | — | ✓ | ✓ | ✓ | ✓ |
| Password-protected links | — | — | ✓ | ✓ | ✓ | ✓ |
| Team seats | — | — | — | ✓ | ✓ | ✓ |
| SSO | — | — | — | — | ✓ | ✓ |
| White label | — | — | — | — | ✓ | ✓ |
| Priority support | — | — | — | — | ✓ | ✓ |

> **Note:** API access requires the **starter plan or above**. Free plan workspaces cannot use the API.

### Free trial

New workspaces automatically start a **14-day Pro trial** with full Pro features. After the trial ends, the workspace is downgraded to the Free plan unless a paid subscription is added.

Plans marked **Trial** in the table above are eligible for a 14-day trial when upgrading to them.

---

## List plans

```
GET /api/v1/plans
```

No authentication required.

### Example

```bash
curl https://api.short.io/api/v1/plans
```

```python
import requests
plans = requests.get("https://api.short.io/api/v1/plans").json()
for plan in plans["items"]:
    print(f"{plan['id']}: ${plan['monthlyPriceCents'] / 100 if plan['monthlyPriceCents'] else 'Custom'}/mo")
```

```javascript
const { items: plans } = await fetch("https://api.short.io/api/v1/plans").then(r => r.json());
plans.forEach(plan => {
  const price = plan.monthlyPriceCents ? `$${plan.monthlyPriceCents / 100}/mo` : "Custom";
  console.log(`${plan.id}: ${price}`);
});
```

### Response

```json
{
  "items": [
    {
      "id": "free",
      "name": "Free",
      "monthlyPriceCents": 0,
      "isCustomPricing": false,
      "limits": {
        "maxDomains": 3,
        "maxLinksPerDomain": 100,
        "maxTrackedClicksPerMonth": 1000,
        "analyticsRetentionDays": 30
      },
      "features": [],
      "trialAvailable": false,
      "stripePriceId": null
    },
    {
      "id": "starter",
      "name": "Starter",
      "monthlyPriceCents": 900,
      "isCustomPricing": false,
      "limits": {
        "maxDomains": 10,
        "maxLinksPerDomain": 1000,
        "maxTrackedClicksPerMonth": 10000,
        "analyticsRetentionDays": 90
      },
      "features": ["ApiAccess", "AnalyticsExport"],
      "trialAvailable": true,
      "stripePriceId": "price_..."
    }
  ]
}
```

### Plan object

| Field | Type | Description |
|-------|------|-------------|
| `id` | string | Plan identifier: `free`, `starter`, `pro`, `team`, `business`, `enterprise` |
| `name` | string | Display name |
| `monthlyPriceCents` | integer \| null | Monthly price in cents (USD). `null` for enterprise (custom pricing). |
| `isCustomPricing` | boolean | `true` for enterprise plans with negotiated pricing |
| `limits.maxDomains` | integer | Maximum custom domains |
| `limits.maxLinksPerDomain` | integer | Maximum links per domain |
| `limits.maxTrackedClicksPerMonth` | integer | Monthly click tracking limit |
| `limits.analyticsRetentionDays` | integer | How long analytics data is retained |
| `features` | string[] | Feature flags enabled on this plan |
| `trialAvailable` | boolean | Whether a free trial is available when upgrading to this plan |
| `stripePriceId` | string \| null | Stripe Price ID (for client-side checkout integration) |
