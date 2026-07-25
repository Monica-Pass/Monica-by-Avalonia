# Monica Password Autofill Extension

This Manifest V3 extension connects Chrome or Edge to Monica's authenticated
desktop loopback bridge. It is a local development and pairing build, not a
browser-store release.

## Install locally

1. Open `chrome://extensions` or `edge://extensions`.
2. Enable developer mode.
3. Choose **Load unpacked**.
4. Select this `browser-extension` directory.

## Pair with Monica

1. Unlock the Monica desktop vault.
2. Open **Settings > Desktop integrations**.
3. Enable **Browser extension bridge**.
4. Confirm the loopback port.
5. Copy the masked session access token through Monica's secure copy command.
6. Paste the token and port into the extension options page.
7. Open an HTTPS sign-in page and invoke the extension.

## Security boundary

- Monica listens only on the IPv4 loopback interface.
- The access token is a 256-bit random base64url value created for the current
  unlocked vault session.
- Locking Monica, disabling the bridge, changing the port, exiting, or
  restarting Monica revokes the token.
- The service worker derives the target origin from the active browser tab;
  content scripts cannot choose an arbitrary target.
- Monica accepts only supported extension callers and HTTPS target origins.
- Returned credentials must match the requested host or an allowed parent
  domain.
- The extension does not receive the vault master password, Bitwarden account
  keys, or the complete Monica database.

## Current limits

- Chrome and Edge Manifest V3 are the validated targets.
- Installation currently uses developer mode; no browser-store signing or
  distribution claim is made.
- The bridge requires Monica to be running and unlocked.
- HTTP pages are deliberately rejected.
- The extension queries credentials for the active site; it does not perform
  vault synchronization, export, or background decryption.

Protocol details are documented in
[`docs/browser-bridge-protocol.md`](../docs/browser-bridge-protocol.md).
