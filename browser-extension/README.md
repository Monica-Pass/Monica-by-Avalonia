# Monica Password Autofill Extension

This Manifest V3 extension connects Chrome or Edge to the authenticated Monica desktop loopback bridge.

## Local installation

1. Open `chrome://extensions` or `edge://extensions`.
2. Enable developer mode and choose **Load unpacked**.
3. Select this `browser-extension` directory.
4. Enable **Browser extension bridge** in Monica desktop settings after unlocking the vault.
5. Copy the session access token from Monica into the extension options page and verify the configured port.

The token exists only for the current unlocked Monica session. Locking Monica, disabling the bridge, exiting the app, or restarting the app revokes it. Credential queries are accepted only from extension origins and only for the HTTPS origin supplied by the browser tab.
