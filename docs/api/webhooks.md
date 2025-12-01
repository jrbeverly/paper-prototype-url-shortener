# Webhooks

> **Coming soon.** Custom webhook support is on the roadmap. This page will be updated when the feature is available.

## What to expect

When released, webhooks will let you subscribe to events from your Short.io workspace and receive real-time HTTP notifications at a URL you configure.

Planned events include:

| Event | Description |
|-------|-------------|
| `link.created` | A new short link was created |
| `link.updated` | A short link was modified |
| `link.deleted` | A short link was deleted |
| `domain.verified` | A custom domain completed DNS verification |
| `domain.verification_failed` | A domain verification attempt failed |
| `trial.expiring` | A trial is expiring within the configured threshold |
| `trial.expired` | A trial has expired and the workspace was downgraded |
| `subscription.updated` | A subscription plan change occurred |
| `payment.failed` | A payment attempt failed |

## Current status

At this time, the API processes **inbound Stripe webhooks** for billing events only. There is no outbound webhook support for workspace events yet.

If your integration requires event-driven updates today, use polling via the [Links](reference/links.md) and [Domains](reference/domains.md) list endpoints.

## Stay updated

Watch the [API changelog](changelog.md) for announcements when webhook support is added.
