# URL Shortener

# Proposal: CloudFront-based Short.io-equivalent platform

## 1) Core idea

Build a **multi-tenant branded link management platform**: customers connect domains like `go.customer.com`, create short links like `/summer-sale`, and the system redirects visitors while collecting analytics.

Equivalent capability target:

```text
Customer domain → short path → rules engine → redirect → analytics/event pipeline
```

This is technically feasible. The hard parts are not the redirect itself; they are **custom-domain onboarding, certificate automation, analytics cost control, uptime, abuse prevention, and product polish**.

---

## 2) Recommended AWS foundation

Use **Amazon CloudFront SaaS Manager** as the domain/CDN layer. AWS launched it specifically for SaaS providers and platforms that manage many customer domains; it provides reusable configurations, TLS certificate management, DDoS protection, and observability for multi-domain SaaS delivery. ([Amazon Web Services, Inc.][1])

CloudFront SaaS Manager is better than manually creating standard CloudFront distributions because standard CloudFront has awkward scaling limits for this use case: default 100 alternate domain names per distribution, 500 distributions per AWS account, and only one certificate attached per distribution. ([AWS Documentation][2])

For 20,000 customer domains, the key AWS quota is **distribution tenants**. CloudFront’s documented default quota is **10,000 distribution tenants per AWS account**, and AWS marks it as requestable for increase; so the clean path is either a quota increase to 20,000+ or two AWS accounts under AWS Organizations. ([AWS Documentation][3])

---

## 3) High-level architecture

```text
Visitor
  ↓
Customer DNS: go.customer.com CNAME → CloudFront routing endpoint
  ↓
CloudFront SaaS Manager
  ↓
CloudFront Function / routing layer
  ↓
Redirect API origin
  ↓
DynamoDB / Redis lookup
  ↓
302 / 307 redirect
  ↓
CloudFront real-time logs → Kinesis / Firehose → S3 + ClickHouse/Redshift
```

### Core AWS services

| Layer            | Service                                           | Purpose                                            |
| ---------------- | ------------------------------------------------- | -------------------------------------------------- |
| Edge/domain      | CloudFront SaaS Manager                           | 20k+ domains, tenant routing, TLS, WAF integration |
| TLS              | ACM via CloudFront managed certs                  | Customer-domain HTTPS                              |
| Redirect compute | Lambda / ECS Fargate / API Gateway + Lambda       | Redirect lookup and rule evaluation                |
| Fast lookup      | DynamoDB, optionally Redis/ElastiCache            | `host + slug → redirect config`                    |
| Analytics stream | CloudFront real-time logs → Kinesis / Firehose    | Per-click event ingestion                          |
| Analytics store  | S3 + Athena, ClickHouse, Redshift, or OpenSearch  | Dashboards and exports                             |
| Control plane    | Postgres/Aurora + service API                     | users, workspaces, domains, billing, permissions   |
| Security         | AWS WAF, Shield, GuardDuty, URL reputation checks | abuse prevention                                   |

---

## 4) Tenant/domain onboarding flow

1. Customer creates workspace.
2. Customer adds domain, for example `go.customer.com`.
3. Platform creates a **CloudFront distribution tenant** for that customer/domain.
4. CloudFront/ACM handles certificate issuance or validation.
5. Customer adds DNS records:
   - `go.customer.com CNAME <cloudfront-routing-endpoint>`
   - validation record if required

6. Platform polls domain + certificate status.
7. Domain becomes active.
8. Customer creates links.

CloudFront’s distribution tenants can inherit shared TLS/security config or attach tenant-specific certificates, and each tenant can have one or more domains. AWS also documents `_cf-challenge` TXT validation and CNAME-to-routing-endpoint setup for tenant domains. ([AWS Documentation][4])

---

## 5) Redirect engine design

### Request example

```text
GET https://go.customer.com/fCkaML
```

Lookup key:

```text
host = go.customer.com
slug = fCkaML
lookup_key = go.customer.com#fCkaML
```

Redirect record:

```json
{
  "tenant_id": "tenant_123",
  "domain": "go.customer.com",
  "slug": "fCkaML",
  "destination_url": "https://customer.com/landing-page",
  "status": "active",
  "redirect_type": 302,
  "rules": {
    "geo": [],
    "device": [],
    "ab_test": []
  },
  "expires_at": null
}
```

### Engine responsibilities

The redirect service should handle:

- link lookup
- destination validation
- geo/device/referrer rules
- expiration
- click-limit expiration
- A/B routing
- bot filtering
- UTM appending
- fallback destination
- 404 or branded error page

### Redirect code

Use:

- `302` for mutable campaign links
- `307` when method preservation matters
- `301` only for truly permanent links

For a Short.io-style product, default to `302` because customers often edit destinations after publishing a link.

---

## 6) Why not put everything in CloudFront Functions?

CloudFront Functions are useful for lightweight edge logic, but they are not enough for the full redirect platform. AWS documents that CloudFront Functions have restricted runtime access, including restricted network access, and their KeyValueStore has small limits such as 5 MB per store and 1 KB values. ([AWS Documentation][5])

Use CloudFront Functions for:

- host/path normalization
- blocking obvious bad requests
- reading a tiny hot-link cache
- adding headers
- routing to the right origin

Do **not** rely on CloudFront KVS as the full link database. At 20,000 customers, the platform will likely have millions of links and many rule payloads. Use DynamoDB/Redis/Postgres instead.

---

## 7) Analytics design

There are two viable analytics modes.

### Mode A: Origin-counted analytics

Every click hits the redirect API, and the API emits an event.

Pros:

- exact event control
- easy enrichment
- simple mental model

Cons:

- no edge caching
- more origin load
- higher latency and cost

### Mode B: Edge-log analytics — recommended

Cache simple redirect responses at CloudFront and use **CloudFront real-time logs** for click analytics. AWS real-time logs can deliver records to Kinesis Data Streams within seconds, with configurable sampling from 1–100%. ([AWS Documentation][6])

Recommended setup:

```text
CloudFront real-time logs
  → Kinesis Data Streams / Firehose
  → S3 raw event lake
  → ClickHouse / Redshift / Athena aggregates
  → Dashboard API
```

Track:

- timestamp
- tenant ID
- domain
- slug
- destination
- country/region
- device type
- browser
- referrer
- UTM values
- bot score
- status code
- latency

For paid plans, store raw click data longer. For free plans, keep only aggregates after 30 days.

---

## 8) Data model

### `tenants`

```text
tenant_id
name
plan
billing_customer_id
status
created_at
```

### `domains`

```text
domain_id
tenant_id
hostname
cloudfront_tenant_id
certificate_status
dns_status
routing_endpoint
status
created_at
```

### `links`

```text
link_id
tenant_id
domain_id
slug
destination_url
redirect_type
rules_json
status
expires_at
created_by
created_at
updated_at
```

### `click_events`

Raw events should go to S3/object storage first, not only a relational DB.

```text
event_id
timestamp
tenant_id
domain
slug
country
device
referrer
ip_hash
user_agent_hash
bot_score
```

### `click_aggregates`

```text
tenant_id
link_id
bucket_time
country
device
referrer_domain
click_count
unique_count_estimate
```

Use approximate unique counting rather than storing raw user identities wherever possible.

---

## 9) Scaling plan for 20,000 customers/domains

### CloudFront layer

Use:

- 2–5 multi-tenant distributions by product tier or region
- 20,000 distribution tenants
- connection groups per tier or shard
- WAF policies by tier

Default CloudFront quota is 10,000 distribution tenants per account, so the target design should either request a 20,000+ quota or split tenants across at least two AWS accounts. ([AWS Documentation][3])

### Database layer

Use DynamoDB for redirect lookups:

```text
PK = hostname#slug
```

This gives single-read lookup for the hot path.

Use Postgres/Aurora for:

- user accounts
- billing
- workspace permissions
- audit logs
- dashboard metadata

Use S3 + ClickHouse/Redshift/Athena for analytics.

### Traffic assumptions

A reasonable initial scale model:

```text
20,000 domains
5 links/customer average at launch = 100,000 links
100 links/customer mature = 2,000,000 links
50,000 clicks/customer/month at high usage = 1B clicks/month
```

The architecture should support 1B clicks/month, but the MVP does not need to start there.

---

## 10) Product capabilities

### MVP

- custom domains
- short links
- editable destinations
- QR codes
- basic analytics
- 302/301 redirects
- link expiration
- API keys
- CSV import/export
- branded 404 page

### Pro features

- geo routing
- device routing
- language routing
- A/B destination split
- UTM builder
- bulk link creation
- webhooks
- campaign grouping
- team members
- role-based permissions

### Enterprise features

- SSO/SAML
- audit logs
- raw data export to S3
- custom retention
- dedicated WAF rules
- SLA
- abuse review workflow
- dedicated support
- data-processing agreement
- custom domain migration tooling

Short.io’s current public pricing page shows that customers expect custom domains, analytics, dashboards, targeting, expiration, password protection, mobile deep links, SSO, and S3 export across higher tiers. ([Short.io][7])

---

## 11) Abuse, trust, and safety

This is not optional. URL shorteners attract abuse.

Required controls:

- block known malware/phishing domains
- scan destination URLs at creation and periodically
- rate-limit link creation
- rate-limit redirects by domain/IP pattern
- quarantine suspicious links
- abuse reporting page
- tenant suspension
- destination change audit trail
- domain ownership verification
- no anonymous high-volume usage

You should assume abuse operations become a major part of the business once free signup exists.

---

## 12) Reliability design

Target:

```text
Public SLA: 99.9% initially
Internal target: 99.95%+
Enterprise target later: 99.99%
```

Reliability rules:

- redirect path must not depend on Postgres
- redirect path should use DynamoDB/Redis/cache-first design
- analytics pipeline can lag without breaking redirects
- dashboard can degrade without affecting links
- expired/suspended links must still resolve to controlled error pages
- all link changes must be versioned

Failure behaviour:

| Failure             | Behaviour                         |
| ------------------- | --------------------------------- |
| Analytics down      | Redirects continue                |
| Dashboard down      | Redirects continue                |
| Postgres down       | Redirects continue using DynamoDB |
| DynamoDB degraded   | serve cached links where possible |
| Customer DNS bad    | show diagnostic in dashboard      |
| Certificate pending | domain stays inactive             |

---

## 13) Pricing strategy

Competitor anchors: Short.io lists Free, Hobby, Pro, Team, and Enterprise tiers, with custom domains even on Free and Team at $48/month; Bitly lists Core at $10/month annually, Growth at $29/month annually / $35 monthly, and Premium at $199/month annually / $300 monthly; Rebrandly lists a free plan and Essentials around $11/month annually. ([Short.io][7])

Recommended pricing:

| Plan       |            Price | Target user         | Limits                                                         |
| ---------- | ---------------: | ------------------- | -------------------------------------------------------------- |
| Free       |               $0 | trial / hobby       | 1 domain, 1,000 links, 10k tracked clicks/mo, 30-day analytics |
| Starter    |            $9/mo | creators            | 3 domains, 10k links, 100k clicks/mo                           |
| Pro        |           $29/mo | small business      | 10 domains, 100k links, 1M clicks/mo, rules engine             |
| Team       |           $79/mo | agencies/teams      | 50 domains, 500k links, 5M clicks/mo, RBAC                     |
| Business   |          $249/mo | serious SaaS/agency | 200 domains, 25M clicks/mo, webhooks, exports                  |
| Enterprise | custom, $750+/mo | high-volume         | custom domains/clicks/SLA/SSO/S3 export                        |

Overages:

```text
Extra tracked clicks: $2–$5 per million
Extra custom domains: $0.25–$1/domain/month at scale
Raw event retention: paid add-on
Dedicated WAF/custom compliance: enterprise add-on
```

Do not compete only on being cheaper. Compete on **developer experience, faster custom-domain onboarding, better analytics, and safer abuse handling**.

---

## 14) Cost model

Main cost drivers:

1. CloudFront tenant/domain management
2. CloudFront requests
3. CloudFront Functions or Lambda@Edge invocations
4. DynamoDB reads/writes
5. Kinesis/Firehose ingestion
6. analytics warehouse
7. WAF
8. support and abuse operations

CloudFront data transfer between CloudFront and AWS origins is currently waived when serving through CloudFront, which helps this architecture because redirects are low-payload and most cost is request/event processing rather than bandwidth. ([Amazon Web Services, Inc.][8])

A simple COGS formula:

```text
monthly_cost =
  edge_requests
+ redirect_compute
+ lookup_reads
+ analytics_ingestion
+ analytics_storage/query
+ domain/TLS/tenant management
+ WAF/security
+ support
```

Keep gross margin healthy by limiting free analytics retention and charging for high click volume.

---

## 15) Build phases

### Phase 1 — MVP

- one multi-tenant CloudFront setup
- domain onboarding
- basic redirect API
- DynamoDB lookup
- Postgres control plane
- basic dashboard
- CloudFront logs to S3
- Stripe billing
- abuse report page

### Phase 2 — Competitive product

- real-time analytics
- QR codes
- CSV bulk import
- API keys
- geo/device rules
- webhooks
- team permissions
- branded 404 pages

### Phase 3 — Scale to 20,000 domains

- quota increase or multi-account sharding
- tenant sharding automation
- ClickHouse/Redshift analytics
- WAF tiering
- automated certificate/DNS diagnostics
- enterprise exports
- high-volume usage-based billing

---

## 16) Main risks

| Risk                                | Mitigation                                            |
| ----------------------------------- | ----------------------------------------------------- |
| AWS tenant quota                    | request increase early; design multi-account sharding |
| abuse/spam                          | scanning, throttling, manual review, reporting        |
| analytics cost explosion            | sampling, retention tiers, aggregation                |
| certificate/DNS onboarding friction | guided setup, automated checks                        |
| latency                             | cache simple redirects; keep lookup single-read       |
| competitor parity pressure          | focus on niche positioning and UX                     |

---

## 17) Positioning strategy

Do not launch as “another Bitly.” Better positioning:

> **Developer-friendly branded link infrastructure for creators, agencies, and SaaS teams that need custom domains, clean analytics, and reliable redirects without enterprise pricing.**

Best initial wedge:

- agencies managing many client domains
- creators/newsletters who need branded tracking
- SaaS teams needing white-labelled campaign links
- developer teams needing API-first link management

---

## Final recommendation

Build it on **CloudFront SaaS Manager + DynamoDB + real-time logs + analytics warehouse**.

The technically correct architecture is:

```text
CloudFront SaaS Manager for 20k domains
DynamoDB for redirect lookups
CloudFront real-time logs for click analytics
S3/ClickHouse/Redshift for reporting
Postgres for control plane
WAF + scanning for abuse prevention
```

This can become a real competitor, but the winning edge will be **domain onboarding, analytics quality, trust/safety, and focused product positioning**, not merely the redirect engine.

[1]: https://aws.amazon.com/about-aws/whats-new/2025/04/saas-manager-amazon-cloudfront/ 'Announcing SaaS Manager for Amazon CloudFront - AWS'
[2]: https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/cloudfront-limits.html 'Quotas - Amazon CloudFront'
[3]: https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/cloudfront-limits.html?utm_source=chatgpt.com 'Quotas - Amazon CloudFront'
[4]: https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/managed-cloudfront-certificates.html 'Request certificates for your CloudFront distribution tenant - Amazon CloudFront'
[5]: https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/cloudfront-function-restrictions.html 'Restrictions on CloudFront Functions - Amazon CloudFront'
[6]: https://docs.aws.amazon.com/AmazonCloudFront/latest/DeveloperGuide/real-time-logs.html?utm_source=chatgpt.com 'Use real-time access logs - Amazon CloudFront'
[7]: https://short.io/pricing/ 'Short.io pricing: link shortener plans from free to enterprise'
[8]: https://aws.amazon.com/cloudfront/pricing/ 'Amazon CloudFront CDN - Plans & Pricing - Try For Free'
