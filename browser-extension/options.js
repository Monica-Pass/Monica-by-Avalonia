const DEFAULT_PORT = 49152;
const form = document.querySelector("#settings-form");
const portInput = document.querySelector("#port");
const tokenInput = document.querySelector("#token");
const status = document.querySelector("#status");

initialize();

async function initialize() {
  const settings = await chrome.storage.local.get(["port", "token"]);
  portInput.value = settings.port || DEFAULT_PORT;
  tokenInput.value = settings.token || "";
}

form.addEventListener("submit", async (event) => {
  event.preventDefault();
  const port = Number(portInput.value);
  const token = tokenInput.value.trim();
  if (!Number.isInteger(port) || port < 1024 || port > 65535 || token.length < 43) return;

  status.textContent = "Testing connection...";
  try {
    const response = await fetch(`http://127.0.0.1:${port}/v1/session/check`, {
      method: "POST",
      cache: "no-store",
      headers: { "Authorization": `Bearer ${token}` }
    });
    if (!response.ok) throw new Error("Monica rejected the token.");
    await chrome.storage.local.set({ port, token });
    status.textContent = "Connected to Monica.";
  } catch (error) {
    status.textContent = error.message || "Unable to connect to Monica.";
  }
});
