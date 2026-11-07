# Security Feature 001 — Human Authentication and Device Identity

**Status:** Approved design; implementation in progress  
**Branch:** `feat/household-device-security`  
**Scope:** Household users, household authorization, account onboarding, and ESP32 device identity.

This document records the approved security decisions. It is the implementation contract; unresolved engineering details must be documented rather than silently weakening a control.

## 1. Security boundaries

Human identities and ESP32 identities are independent security principals. A human session never authenticates a device, and a device credential never grants access to human or household administration APIs.

Every household-scoped operation must derive and validate household membership from the authenticated principal. Membership is stored separately from global Identity roles and is unique per user/household pair. Client-supplied household IDs are selectors, not proof of authorization. Cross-household access must fail closed. Device and household API reads and mutations, and SSR dashboard/device queries, are filtered by the caller's household memberships.

## 2. Human authentication

- Use ASP.NET Core Identity with email address and password.
- Public self-registration is disabled.
- The first household and Administrator account are provisioned through a one-time deployment bootstrap, not public registration.
- Bootstrap requires the deployment secrets `EMS_BOOTSTRAP_ADMIN_EMAIL`, `EMS_BOOTSTRAP_ADMIN_PASSWORD`, and `EMS_BOOTSTRAP_HOUSEHOLD_NAME`. The first account is email-confirmed as part of this trusted operator-controlled bootstrap.
- Bootstrap is disabled once any user exists unless the configured bootstrap account already exists; it is idempotent for that account and creates its initial household membership once.
- Identity schema migrations run independently from the EMS domain migration history. The API applies both migration sets before serving requests.
- Additional users join only through Administrator-issued email invitations. Invitation creation, email delivery, acceptance, revocation, household role management, member removal, and household-scoped security audit history are implemented.
- Invitations use cryptographically random single-use tokens; only SHA-256 hashes are stored. They expire after 24 hours, are revocable, and are bound to one household and one assigned role. New accounts are created only through a valid invitation; existing users must sign in with the invited email address to accept.
- Password recovery uses 30-minute, single-use Identity reset tokens delivered by email. Responses do not disclose whether an address exists. This flow is implemented.
- SMTP delivery is provider-independent and configured through deployment secrets/environment variables. Credentials must never be committed. Password reset is implemented. Configure `EMS_SMTP_HOST`, `EMS_SMTP_PORT` (default 587), `EMS_SMTP_USERNAME`, `EMS_SMTP_PASSWORD`, `EMS_SMTP_FROM`, `EMS_SMTP_ENABLE_SSL` (default true), and HTTPS `EMS_PUBLIC_BASE_URL`.
- Sign-in currently uses email and password only. MFA enforcement and enrollment have been disabled for all accounts. A database migration clears the Identity TwoFactorEnabled flag for every existing user; new account and invitation flows no longer redirect users into MFA setup. Legacy TOTP/recovery-code implementation remains in the repository but is not exposed through the account UI or authorization policy.
- The application cookie validates the persisted Identity security stamp on each request. Security-sensitive account changes and household role changes rotate the target account's stamp to invalidate existing sessions. Account pages set no-referrer, nosniff, frame-denial, and restrictive browser-permissions headers to reduce token leakage and clickjacking.
- Generate ten one-time recovery codes at MFA enrollment. Show them once and persist only hashes. There is no email-only MFA bypass.
- Login sessions are non-persistent, limited to eight hours maximum, and expire after fifteen minutes of inactivity. Cookies are HTTP-only, Secure, and SameSite protected. Sensitive actions require password reauthentication and administrator MFA where enabled; the fresh-authentication window is five minutes.
- Rate-limit login, invitation acceptance, password reset, MFA verification, and setup attempts. Emit security audit events without recording secrets or raw tokens.

## 3. Household roles

Roles are scoped to household membership:

| Permission | Administrator | Household Member | Read-only Viewer |
|---|---:|---:|---:|
| View dashboards, telemetry, devices, alerts | Yes | Yes | Yes |
| Operate configured loads | Yes | Yes | No |
| Acknowledge alerts | Yes | Yes | No |
| Register, edit, deactivate devices | Yes | No | No |
| Invite users and manage membership/roles | Yes | No | No |
| Change household settings | Yes | No | No |

Authorization must be enforced in endpoint/service code, not only by hiding UI controls. Remote operations never override firmware safety interlocks or local electrical protection.

## 4. Device provisioning and identity

- Every registered ESP32 has a separate device identity and revocable credential.
- An Administrator creates a short-lived, single-use provisioning code for a specific registered device.
- The ESP32 exchanges the code over HTTPS for a unique device secret.
- Support both serial provisioning and a temporary local Wi-Fi setup portal. Both use the same backend provisioning contract.
- The setup portal closes after successful provisioning or timeout.
- The backend stores a SHA-256 verifier and an AES-GCM-protected copy of the device secret. The plaintext secret is returned only during provisioning/rotation and is never persisted in plaintext.
- Configure `EMS_DEVICE_CREDENTIAL_ENCRYPTION_KEY` as base64 for exactly 32 random bytes. Keep it stable and secret across deployments; loss or replacement makes stored device credentials unusable.
- Firmware stores the secret in standard ESP32 NVS for this initial release. Flash extraction remains a documented physical-access risk.
- Device credentials are never shared between devices or with human accounts.

## 5. Device request authentication

- Sign every device request with HMAC-SHA-256 using the device-specific secret.
- Current canonical input is the uppercase HTTP method, path plus query string, Unix timestamp in seconds, unique nonce, and uppercase hexadecimal SHA-256 body digest, separated by newline characters.
- Send `X-EMS-Device`, `X-EMS-Timestamp`, `X-EMS-Nonce`, and `X-EMS-Signature` headers. The signature is 64 hexadecimal characters.
- Verify signatures using constant-time comparison and verify the decrypted secret against its stored SHA-256 verifier.
- Current timestamp tolerance is 60 seconds. Nonces must be 16–64 URL-safe characters, are unique per device, and are retained for at least ten minutes. Reuse is rejected atomically.
- Request bodies are limited to 1 MiB for HMAC verification.
- The API applies HTTPS redirection. Device firmware must use HTTPS and must never downgrade to HTTP.
- Synchronize device time using NTP/SNTP. A device must have valid synchronized time before signing requests; do not fall back to an untrusted local clock or backend-time shortcut.
- Device authentication endpoints are rate-limited per source IP and device. Failures do not automatically revoke or permanently lock the device.

## 6. Device capabilities and command transport

An authenticated device may:
- Submit telemetry.
- Send heartbeat and diagnostic information.
- Retrieve pending commands addressed to itself.
- Acknowledge command receipt and report execution results.
- Report observed equipment state and fault conditions.

It may not manage users, households, registrations, or other devices.

Command delivery initially uses HTTPS polling at a fixed five-second interval. Firmware configuration may change the interval, but the initial default is five seconds. Requests must not overlap. The backend must support command expiry, device scoping, idempotency, acknowledgement, and retry-safe result reporting. Command delivery is not proof of physical execution or state change.

## 7. Device credential rotation

- An Administrator with recent authentication can initiate rotation from the device console or API.
- Backend creates a replacement credential while the current credential remains valid. The replacement secret is shown once and expires after 24 hours.
- Both credentials are temporarily accepted during rotation.
- A valid device request signed with the replacement credential confirms adoption. The backend then revokes the previous credential within the same database transaction that accepts the nonce.
- If confirmation does not arrive before expiry, the pending credential is ignored and the existing credential remains usable.
- A new rotation attempt invalidates any previous pending replacement.
- Administrators can revoke all current and pending credentials, and unused provisioning codes, after recent authentication. Revocation is logged without recording secret material.

## 8. Current implementation status

Implemented on `feat/household-device-security`:
- Separate ASP.NET Core Identity persistence and migration history.
- Email/password sign-in and sign-out pages; public self-registration is not mapped.
- Secure application cookies with a 15-minute inactivity timeout and an eight-hour absolute session limit.
- Administrator bootstrap through deployment environment secrets.
- Household membership persistence, administrator-issued invitations, single-use invitation acceptance, membership role changes, and last-administrator protection.
- Account security profile, password change with recent authentication, recovery-code regeneration, household user management, administrator-assisted MFA recovery, member removal, household creation UI, and household-scoped audit history.
- Role-aware device registration/deactivation UI, account security pages, device provisioning and dual-key rotation UI, emergency credential revocation, and five-minute recent-authentication checks for sensitive household/device operations.
- Backend device provisioning-code issuance and exchange: administrator-scoped, single-use codes expire after ten minutes and only their hashes are stored.
- Device credentials are returned once; the backend stores a SHA-256 verifier and AES-GCM-protected secret, not plaintext.
- HMAC-SHA-256 device request authentication, timestamp freshness, body hashing, constant-time signature verification, per-device nonce replay protection, heartbeat endpoint, and device/IP rate limiting.
- Dual-key credential rotation with a 24-hour pending-key expiry. The first valid request signed with the new key confirms adoption and revokes the previous key; expiry preserves the previous key.
- One-time provisioning/rotation responses are marked no-store/no-cache.
- Credential revocation and rotation are revalidated under database row locks before a device request is accepted, preventing a stale pre-transaction credential read from authorizing a request after revocation.
- Persistent device-security audit events record provisioning-code issuance, initial provisioning, rotation initiation/confirmation and emergency revocation without recording secret material. Household administrators can inspect the latest 20 events in the device console.
- Integration coverage includes invitation acceptance, authorization boundaries, device provisioning, single-use codes, valid/invalid HMAC requests, replay rejection, rate limiting, credential rotation, expiry fallback, credential lifecycle audit persistence, and administrator-only audit display.

Still pending:
- ESP32 firmware implementation for serial provisioning, temporary local Wi-Fi portal, NTP/SNTP gating, NVS credential storage, and HMAC request signing.
- Device telemetry ingestion, diagnostics, command polling, acknowledgements, and execution-result reporting.
- Production HTTPS/proxy validation and end-to-end tests with a physical ESP32.


## 9. Production and staging release gates

- No unauthenticated household/device administration endpoints may be exposed.
- Initial bootstrap secrets must be supplied securely and removed after the first administrator is provisioned.
- Health liveness/readiness endpoints remain anonymous and disclose no sensitive configuration.
- Swagger remains Development-only.
- Secrets are supplied through environment variables/Render secrets, never source control.
- Staging must use synthetic data until human authorization, device authentication, provisioning, migrations, and security tests have been verified.
- This feature does not certify electrical safety. Firmware local protection remains authoritative.
