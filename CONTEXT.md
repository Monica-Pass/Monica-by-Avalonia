# Monica Desktop Vault

Monica by Avalonia is a local-first desktop vault whose feature language follows the Monica Android product while adapting interaction patterns to desktop platforms.

## Language

**Vault**:
The encrypted local collection containing passwords, notes, authenticators, cards, documents, attachments, and history.
_Avoid_: Database, store

**Vault Access**:
The initialization, creation, and unlock flow that establishes an authenticated vault session.
_Avoid_: Login, authentication page

**Password Vault**:
The feature for organizing and using website and application credentials.
_Avoid_: Password page, accounts

**Secure Note**:
A vault item containing private plain-text or Markdown content.
_Avoid_: Memo

**Authenticator**:
A vault item that generates a time-based one-time password.
_Avoid_: TOTP account, token

**Wallet Item**:
A bank card or identity document stored in the vault.
_Avoid_: Card record

## Relationships

- **Vault Access** establishes the session required by all other vault features.
- A **Vault** contains zero or more **Password Vault** entries, **Secure Notes**, **Authenticators**, and **Wallet Items**.

## Example Dialogue

> **Dev:** "Should the Password Vault load while Vault Access is still verifying the master password?"
> **Domain expert:** "No. Vault Access must establish the vault session before any encrypted feature data is loaded."

## Flagged Ambiguities

- "Login" previously described local vault unlocking; use **Vault Access** because no remote account is involved.
