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
- Additional users join only through Administrator-issued email invitations. Invitation creation, email delivery, acceptance, revocation, and household role management are implemented.
- Invitations use cryptographically random single-use tokens; only SHA-256 hashes are stored. They expire after 24 hours, are revocable, and are bound to one household and one assigned role. New accounts are created only through a valid invitation; existing users must sign in with the invited email address to accept.
- Password recovery uses 30-minute, single-use Identity reset tokens delivered by email. Responses do not disclose whether an address exists. This flow is implemented.
- SMTP delivery is provider-independent and configured through deployment secrets/environment variables. Credentials must never be committed. Password reset is implemented. Configure `EMS_SMTP_HOST`, `EMS_SMTP_PORT` (default 587), `EMS_SMTP_USERNAME`, `EMS_SMTP_PASSWORD`, `EMS_SMTP_FROM`, `EMS_SMTP_ENABLE_SSL` (default true), and HTTPS `EMS_PUBLIC_BASE_URL`.
- MFA uses authenticator-app TOTP. Administrators must enroll before accessing protected operations; MFA is optional for Members and Viewers. Enrollment, authenticator challenge, and ten one-time recovery codes are implemented. Recovery codes are cryptographically random, stored only as SHA-256 hashes, and redeemed atomically once.
- Generate ten one-time recovery codes at MFA enrollment. Show them once and persist only hashes. There is no email-only MFA bypass.
- Login sessions are non-persistent, limited to eight hours maximum, and expire after fifteen minutes of inactivity. Cookies are HTTP-only, Secure, and SameSite protected. Sensitive actions require recent authentication.
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
- Backend stores only a cryptographic hash of device secrets; firmware stores the secret in standard ESP32 NVS for this initial release. Flash extraction remains a documented physical-access risk.
- Device credentials are never shared between devices or with human accounts.

## 5. Device request authentication

- Sign every device request with HMAC-SHA-256 using the device-specific secret.
- Canonical request input includes HTTP method, normalized path, timestamp, unique nonce, and SHA-256 body digest.
- Verify signature using constant-time comparison.
- Enforce a bounded timestamp skew and atomically reject reused nonces. Nonce retention must cover the accepted timestamp window.
- Require HTTPS. Reject unauthenticated, stale, malformed, replayed, revoked, inactive, or cross-device requests.
- Synchronize device time using NTP/SNTP. A device must have valid synchronized time before signing requests; do not fall back to an untrusted local clock or backend-time shortcut.
- Authentication failures are rate-limited per device and source IP. They do not automatically revoke or permanently lock the device. Security events are recorded for Administrator review.

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

- Administrator can initiate rotation.
- Backend creates a replacement credential while the current credential remains valid.
- Both credentials are temporarily accepted during rotation.
- Device must authenticate with the replacement and explicitly confirm successful adoption.
- Only after confirmation does the backend revoke the previous credential.
- If confirmation does not arrive before rotation expiry, discard the pending replacement and retain the existing credential.
- Emergency revocation remains available to an Administrator.

## 8. Current implementation status

Implemented on `feat/household-device-security`:
- Separate ASP.NET Core Identity persistence and migration history.
- Email/password sign-in and sign-out pages; public self-registration is not mapped.
- Secure application cookies with a 15-minute inactivity timeout and an eight-hour absolute session limit.
- Administrator bootstrap through deployment environment secrets.
- Household membership persistence, administrator-issued invitations, single-use invitation acceptance, membership role changes, and last-administrator protection.
- Account security profile, household user management, role-aware device registration UI, and clean anonymous account pages.
- Integration coverage for invitation acceptance and authorization, anonymous API rejection, administrator MFA enforcement, household role restrictions, and cross-household isolation.

Still pending:
- Lost-authenticator and lost-recovery-code administrative recovery workflow.
- Recent-authentication checks for sensitive operations.
- Password-reset delivery and token redemption.
- Lost-authenticator and lost-recovery-code administrative recovery workflow.
- Recent-authentication checks for sensitive operations.
- Full ESP32 provisioning, HMAC authentication, replay protection, and credential rotation.

## 9. Production and staging release gates

- No unauthenticated household/device administration endpoints may be exposed.
- Initial bootstrap secrets must be supplied securely and removed after the first administrator is provisioned.
- Health liveness/readiness endpoints remain anonymous and disclose no sensitive configuration.
- Swagger remains Development-only.
- Secrets are supplied through environment variables/Render secrets, never source control.
- Staging must use synthetic data until human authorization, device authentication, provisioning, migrations, and security tests have been verified.
- This feature does not certify electrical safety. Firmware local protection remains authoritative.
