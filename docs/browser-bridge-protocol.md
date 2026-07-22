# Browser Bridge Protocol

The Monica desktop browser bridge is a versioned, authenticated HTTP protocol exposed only on the IPv4 loopback interface. Version 1 supports Chrome and Edge Manifest V3 extensions.

## Trust boundary

The browser supplies the active tab URL to the extension service worker. The service worker derives the target origin from `sender.tab.url`; content scripts cannot choose it. Monica accepts only HTTPS target origins and returns credentials whose stored website host is equal to, or a parent domain of, the requested host.

The bridge token authorizes the installed extension for the current unlocked vault session. The token is a 256-bit random base64url value. It is never persisted by Monica and is revoked when the vault locks, browser integration is disabled, the listening port changes, Monica exits, or Monica restarts.

The extension stores the paired token in its private `chrome.storage.local` area. A revoked token has no value because a new token is generated whenever the bridge starts.

## Transport

- Address: `127.0.0.1`
- Configurable port: `1024` through `65535`
- Maximum request headers: 16 KiB
- Maximum request body: 8 KiB
- Request timeout: 5 seconds
- Maximum concurrent clients: 4
- Cache policy: `Cache-Control: no-store`
- Browser callers: `chrome-extension://`, `moz-extension://`, or `ms-browser-extension://` origins

Every authenticated request uses `Authorization: Bearer <session-token>`. Token comparison uses fixed-time byte comparison.

## Endpoints

### `POST /v1/session/check`

Validates pairing without reading vault records.

```json
{
  "ready": true,
  "protocolVersion": 1
}
```

### `POST /v1/credentials/query`

Requires `Content-Type: application/json`.

```json
{
  "origin": "https://accounts.example.com"
}
```

Successful responses contain only fields required for login selection and filling:

```json
{
  "items": [
    {
      "id": 7,
      "title": "Example",
      "username": "person@example.com",
      "password": "secret",
      "website": "https://example.com"
    }
  ]
}
```

Deleted and archived entries are excluded. Notes, custom fields, attachments, passkeys, TOTP seeds, card data, and password history are never returned.

## Rejection rules

The bridge rejects missing or invalid extension origins, missing or invalid bearer tokens, HTTP target origins, user-info components in target URLs, unsupported methods and paths, duplicate headers, transfer encoding, oversized payloads, malformed JSON, and requests exceeding the deadline. Error responses contain stable error codes without exception details.
