# Authentication

The API supports two authentication methods. For server-to-server integrations, use **API keys**. The dashboard uses **JWT bearer tokens** for session-based access.

## API keys

### Format

All API keys begin with `sk_` followed by a URL-safe base64-encoded random value:

```
sk_ABC123def456GHI789jkl012MNO345pqr678STU901vwx234
```

**The full key is shown exactly once — at creation time.** After that, only a masked preview (`sk_ABC123...`) is stored and returned in list operations. Store the key securely immediately after creation.

### Creating an API key

```bash
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/api-keys \
  -H "X-API-Key: sk_existing_key_with_apikey_manage_permission" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Production integration",
    "permissions": ["link:read", "link:write", "domain:read"]
  }'
```

Response:

```json
{
  "id": "a1b2c3d4-...",
  "name": "Production integration",
  "key": "sk_ABC123def456GHI789jkl012MNO345pqr678STU901vwx234",
  "keyPrefix": "sk_ABC123",
  "permissions": ["link:read", "link:write", "domain:read"],
  "createdAt": "2024-11-01T14:32:00Z"
}
```

### Using an API key

Send the key in the `X-API-Key` header on every request:

```
X-API-Key: sk_ABC123def456GHI789jkl012MNO345pqr678STU901vwx234
```

**Example:**

```bash
curl https://api.short.io/api/v1/tenants/{tenantId}/links \
  -H "X-API-Key: sk_your_key_here"
```

### Revoking an API key

```bash
curl -X DELETE https://api.short.io/api/v1/tenants/{tenantId}/api-keys/{keyId} \
  -H "X-API-Key: sk_your_key_here"
```

Revocation is immediate. Any in-flight requests using the revoked key will receive `401 Unauthorized`.

---

## JWT bearer tokens

JWT tokens are issued by the Short.io authentication service and are used by the dashboard. They expire after **60 minutes**.

```
Authorization: Bearer eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9...
```

For integrations, prefer API keys over JWT tokens. JWT tokens are intended for interactive, session-based flows.

---

## Permissions model

Every API key is created with an explicit set of permissions. Requests made with a key are limited to those permissions regardless of the requesting user's role.

### Available permissions

| Permission | What it allows |
|-----------|---------------|
| `link:read` | List and retrieve links |
| `link:write` | Create, update, delete, and restore links |
| `domain:read` | List and retrieve domains |
| `domain:write` | Register, update, verify, and delete domains |
| `tenant:read` | Read workspace details and settings |
| `tenant:write` | Update workspace settings, suspend, reactivate |
| `billing:read` | View invoices and plan details |
| `apikey:manage` | Create, list, and revoke API keys |

### Roles

You can also assign a role as a shorthand for a group of permissions:

| Role | Permissions granted |
|------|-------------------|
| `owner` | All permissions |
| `admin` | All permissions |
| `member` | `tenant:read`, `domain:read`, `link:read`, `link:write` |
| `viewer` | `tenant:read`, `domain:read`, `link:read` |

When creating an API key, you can specify either individual permissions or a role name:

```json
{ "name": "Read-only bot", "permissions": ["viewer"] }
{ "name": "Link manager", "permissions": ["link:read", "link:write", "domain:read"] }
```

### Principle of least privilege

Grant only the permissions a key actually needs. Examples:

| Use case | Recommended permissions |
|----------|------------------------|
| Analytics dashboard (read-only) | `link:read`, `domain:read` |
| Campaign link creation | `link:read`, `link:write`, `domain:read` |
| Full integration | `link:read`, `link:write`, `domain:read`, `domain:write` |
| Billing dashboard | `billing:read` |

---

## Security best practices

- **Never expose API keys in client-side code.** Browsers, mobile apps, and public repositories are unsafe. Always call the API from your server.
- **Rotate keys regularly.** Treat API keys like passwords.
- **Use scoped keys.** Create one key per integration with only the required permissions.
- **Revoke keys immediately** when they are no longer needed or may have been compromised.
- **Monitor last-used timestamps.** Unused keys returned by `GET /api-keys` are candidates for revocation.

---

## Authentication errors

| Status | Cause |
|--------|-------|
| `401 Unauthorized` | No key provided, key is invalid, or key has been revoked |
| `403 Forbidden` | Key is valid but lacks the required permission for this endpoint |

See [Error handling](errors.md) for the full error response format.
