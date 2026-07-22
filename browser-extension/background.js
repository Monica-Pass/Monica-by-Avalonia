const DEFAULT_PORT = 49152;

chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message?.type !== "queryCredentials") return false;

  queryCredentials(sender)
    .then((items) => sendResponse({ ok: true, items }))
    .catch((error) => sendResponse({ ok: false, error: error.message }));
  return true;
});

async function queryCredentials(sender) {
  const tabUrl = sender.tab?.url;
  if (!tabUrl) throw new Error("The active page URL is unavailable.");

  const page = new URL(tabUrl);
  if (page.protocol !== "https:") throw new Error("Monica autofill requires HTTPS.");

  const { port = DEFAULT_PORT, token = "" } = await chrome.storage.local.get(["port", "token"]);
  if (!Number.isInteger(port) || port < 1024 || port > 65535 || token.length < 43) {
    throw new Error("Open the extension options and pair Monica first.");
  }

  const response = await fetch(`http://127.0.0.1:${port}/v1/credentials/query`, {
    method: "POST",
    cache: "no-store",
    headers: {
      "Authorization": `Bearer ${token}`,
      "Content-Type": "application/json"
    },
    body: JSON.stringify({ origin: page.origin })
  });
  if (!response.ok) throw new Error(response.status === 401 ? "The Monica session token has expired." : "Monica rejected the request.");

  const result = await response.json();
  return Array.isArray(result.items) ? result.items : [];
}
