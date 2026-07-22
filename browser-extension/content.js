const attachedInputs = new WeakSet();

function attachAutofillButtons(root = document) {
  const passwordInputs = root.matches?.('input[type="password"]')
    ? [root, ...root.querySelectorAll('input[type="password"]')]
    : root.querySelectorAll('input[type="password"]');
  for (const passwordInput of passwordInputs) {
    if (attachedInputs.has(passwordInput) || passwordInput.disabled || passwordInput.readOnly) continue;
    attachedInputs.add(passwordInput);

    const button = document.createElement("button");
    button.type = "button";
    button.className = "monica-autofill-button";
    button.textContent = "M";
    button.title = "Fill with Monica";
    button.setAttribute("aria-label", "Fill with Monica");
    button.addEventListener("click", () => openCredentialMenu(button, passwordInput));
    passwordInput.insertAdjacentElement("afterend", button);
  }
}

async function openCredentialMenu(button, passwordInput) {
  closeCredentialMenus();
  button.disabled = true;
  try {
    const result = await chrome.runtime.sendMessage({ type: "queryCredentials" });
    if (!result?.ok) throw new Error(result?.error || "Monica is unavailable.");
    if (result.items.length === 0) return showMessage(button, "No matching logins");
    if (result.items.length === 1) return fillCredential(passwordInput, result.items[0]);

    const menu = document.createElement("div");
    menu.className = "monica-credential-menu";
    menu.setAttribute("role", "listbox");
    for (const item of result.items) {
      const option = document.createElement("button");
      option.type = "button";
      option.className = "monica-credential-option";
      option.textContent = item.username || item.title || "Login";
      option.title = item.title || item.username || "Monica login";
      option.addEventListener("click", () => {
        fillCredential(passwordInput, item);
        menu.remove();
      });
      menu.append(option);
    }
    button.insertAdjacentElement("afterend", menu);
  } catch (error) {
    showMessage(button, error.message);
  } finally {
    button.disabled = false;
  }
}

function fillCredential(passwordInput, credential) {
  const form = passwordInput.form || passwordInput.closest("form") || document;
  const usernameInput = form.querySelector('input[autocomplete="username"], input[type="email"], input[type="text"]');
  if (usernameInput && credential.username) setNativeValue(usernameInput, credential.username);
  setNativeValue(passwordInput, credential.password || "");
  passwordInput.focus();
}

function setNativeValue(input, value) {
  const setter = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, "value")?.set;
  setter?.call(input, value);
  input.dispatchEvent(new Event("input", { bubbles: true }));
  input.dispatchEvent(new Event("change", { bubbles: true }));
}

function closeCredentialMenus() {
  document.querySelectorAll(".monica-credential-menu,.monica-autofill-message").forEach((element) => element.remove());
}

function showMessage(button, text) {
  const message = document.createElement("div");
  message.className = "monica-autofill-message";
  message.textContent = text;
  button.insertAdjacentElement("afterend", message);
  window.setTimeout(() => message.remove(), 3500);
}

attachAutofillButtons();
new MutationObserver((records) => {
  for (const record of records) {
    for (const node of record.addedNodes) {
      if (node instanceof Element) attachAutofillButtons(node);
    }
  }
}).observe(document.documentElement, { childList: true, subtree: true });
