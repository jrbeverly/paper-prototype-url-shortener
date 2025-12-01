# API Keys

API keys are credentials used to authenticate programmatic access to the Short.io API. Each key is scoped to a workspace and has a fixed set of permissions.

**Base path:** `/api/v1/tenants/{tenantId}/api-keys`

**Required permission:** `apikey:manage`

---

## The API key object

API keys are returned in two forms depending on the endpoint:

### On creation (full key visible)

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "name": "Production integration",
  "key": "sk_ABC123def456GHI789jkl012MNO345pqr678STU901vwx234",
  "keyPrefix": "sk_ABC123",
  "permissions": ["link:read", "link:write", "domain:read"],
  "createdAt": "2024-11-01T14:32:00Z"
}
```

> **Important:** The `key` field is returned **only once, at creation time**. Copy and store it securely immediately — it cannot be retrieved again. If you lose it, revoke the key and create a new one.

### On list (masked)

```json
{
  "id": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
  "name": "Production integration",
  "keyPreview": "sk_ABC123...vwx234",
  "keyPrefix": "sk_ABC123",
  "permissions": ["link:read", "link:write", "domain:read"],
  "createdAt": "2024-11-01T14:32:00Z",
  "lastUsedAt": "2024-11-05T09:47:00Z",
  "isRevoked": false
}
```

| Field | Type | Description |
|-------|------|-------------|
| `id` | uuid | Unique identifier |
| `name` | string | Human-readable label for this key |
| `key` | string | Full key value (creation response only) |
| `keyPreview` | string | Masked version showing prefix and suffix (list response) |
| `keyPrefix` | string | Short prefix (`sk_` + first 8 chars) — useful for identifying keys in logs |
| `permissions` | string[] | Permissions or role names granted to this key |
| `createdAt` | datetime | Creation timestamp (UTC) |
| `lastUsedAt` | datetime \| null | Most recent successful authenticated request (UTC) |
| `isRevoked` | boolean | `true` if the key has been revoked |

---

## Create an API key

```
POST /api/v1/tenants/{tenantId}/api-keys
```

**Required permission:** `apikey:manage`

### Request body

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | Yes | Human-readable label. Max 100 characters. |
| `permissions` | string[] | No | Permissions or role names. Defaults to `["viewer"]`. |

Available permissions: `link:read`, `link:write`, `domain:read`, `domain:write`, `tenant:read`, `tenant:write`, `billing:read`, `apikey:manage`

Available roles: `owner`, `admin`, `member`, `viewer` (each expands to a preset set of permissions — see [Authentication](../authentication.md#permissions-model))

### Example

```bash
# Key with specific permissions
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/api-keys \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Link management bot",
    "permissions": ["link:read", "link:write", "domain:read"]
  }'

# Key with a role shorthand
curl -X POST https://api.short.io/api/v1/tenants/{tenantId}/api-keys \
  -H "X-API-Key: sk_your_key_here" \
  -H "Content-Type: application/json" \
  -d '{"name": "Read-only analytics dashboard", "permissions": ["viewer"]}'
```

```python
response = requests.post(
    f"{BASE_URL}/api-keys",
    headers={"X-API-Key": API_KEY},
    json={
        "name": "Link management bot",
        "permissions": ["link:read", "link:write", "domain:read"],
    },
)
data = response.json()
# Store data["key"] securely — it will not be shown again
print(data["key"])   # sk_ABC123def456...
print(data["id"])    # The key ID, used to revoke later
```

```javascript
const data = await fetch(`${BASE_URL}/api-keys`, {
  method: "POST",
  headers: { "X-API-Key": API_KEY, "Content-Type": "application/json" },
  body: JSON.stringify({
    name: "Link management bot",
    permissions: ["link:read", "link:write", "domain:read"],
  }),
}).then(r => r.json());

// Store data.key securely — it will not be shown again
console.log(data.key);  // sk_ABC123def456...
```

### Response

`201 Created` with the full key object (including `key`).

---

## List API keys

```
GET /api/v1/tenants/{tenantId}/api-keys
```

**Required permission:** `apikey:manage`

Returns all API keys for the workspace. The `key` field is not included — only the masked `keyPreview`.

### Example

```bash
curl https://api.short.io/api/v1/tenants/{tenantId}/api-keys \
  -H "X-API-Key: sk_your_key_here"
```

### Response

```json
[
  {
    "id": "a1b2c3d4-...",
    "name": "Production integration",
    "keyPreview": "sk_ABC123...vwx234",
    "keyPrefix": "sk_ABC123",
    "permissions": ["link:read", "link:write"],
    "createdAt": "2024-11-01T14:32:00Z",
    "lastUsedAt": "2024-11-05T09:47:00Z",
    "isRevoked": false
  },
  {
    "id": "b2c3d4e5-...",
    "name": "Old key (unused)",
    "keyPreview": "sk_XYZ789...abc123",
    "keyPrefix": "sk_XYZ789",
    "permissions": ["viewer"],
    "createdAt": "2024-09-01T08:00:00Z",
    "lastUsedAt": null,
    "isRevoked": false
  }
]
```

Use `lastUsedAt` to identify unused keys that can be safely revoked.

---

## Revoke an API key

```
DELETE /api/v1/tenants/{tenantId}/api-keys/{keyId}
```

**Required permission:** `apikey:manage`

Revocation is **immediate**. Any request currently in flight using this key will receive `401 Unauthorized`. Revocation cannot be undone.

### Example

```bash
curl -X DELETE https://api.short.io/api/v1/tenants/{tenantId}/api-keys/{keyId} \
  -H "X-API-Key: sk_your_key_here"
```

```python
response = requests.delete(
    f"{BASE_URL}/api-keys/{key_id}",
    headers={"X-API-Key": API_KEY},
)
response.raise_for_status()  # 204 No Content on success
```

### Response

`204 No Content`

### Errors

| Status | Cause |
|--------|-------|
| `404` | Key not found or belongs to a different workspace |

---

## Key rotation

To rotate an API key:

1. Create a new key with the same permissions
2. Update your integration to use the new key
3. Verify the new key is working correctly
4. Revoke the old key

```bash
# 1. Create new key
NEW_KEY=$(curl -s -X POST https://api.short.io/api/v1/tenants/{tenantId}/api-keys \
  -H "X-API-Key: sk_old_key" \
  -H "Content-Type: application/json" \
  -d '{"name": "Production v2", "permissions": ["link:read", "link:write"]}' \
  | jq -r '.key')

# 2. Test new key
curl https://api.short.io/api/v1/tenants/{tenantId}/links \
  -H "X-API-Key: $NEW_KEY"

# 3. Revoke old key (get old key ID first)
OLD_KEY_ID=$(curl -s https://api.short.io/api/v1/tenants/{tenantId}/api-keys \
  -H "X-API-Key: $NEW_KEY" \
  | jq -r '.[] | select(.name == "Production v1") | .id')

curl -X DELETE https://api.short.io/api/v1/tenants/{tenantId}/api-keys/$OLD_KEY_ID \
  -H "X-API-Key: $NEW_KEY"
```
