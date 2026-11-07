# Security model — human authentication and device identity

## Status

Human account authentication, household membership/roles, invitation workflows, device provisioning and device HMAC authentication are implemented in the current codebase. MFA is currently disabled for all accounts by the database migration that disabled MFA. Do not claim MFA is an active control. Production security review and deployment validation remain outstanding.

## 1. Separate principals

Human users and ESP32 devices are independent security principals:
- Humans use ASP.NET Core Identity and browser authentication.
- Devices use a per-device secret and HMAC-SHA-256 request signatures.
- A device credential never grants access to human or household administration APIs.
- A human browser session never substitutes for device request authentication.

## 2. Human account lifecycle

- Public self-registration is disabled.
- The first administrator and initial household are created through operator-controlled deployment bootstrap.
- Additional users join through administrator-issued invitations.
- Invitations use random single-use tokens; only SHA-256 token hashes are stored. They expire after 24 hours, can be revoked, and are bound to a household and assigned role.
- Existing users must sign in with the invited email address to accept an invitation. New users create an account through the valid invitation flow.
- Password reset uses single-use time-limited Identity reset tokens delivered by email.
- Sensitive operations use recent-authentication checks where configured.
- Household membership changes and security-sensitive events are recorded in the security audit history.
- MFA is disabled for all accounts by the current migration; MFA-related UI or recovery code classes do not mean MFA is currently enforced.

## 3. Household authorization

Household roles are Administrator, Household Member and Read-only Viewer. Household membership is distinct from global Identity roles. Server-side access services derive household membership from the authenticated principal. Client-provided household IDs and device codes are selectors, not proof of authorization. Cross-household reads and writes must fail closed.

Administrator-only operations include household member/invitation management and privileged device lifecycle actions. Role changes/removals protect the last remaining household administrator and invalidate relevant sessions/security stamps.

## 4. Device provisioning and credentials

1. An authorized administrator requests a provisioning code for a registered active device.
2. The backend creates a short-lived, single-use code; only its hash is stored.
3. The ESP32 submits device code and provisioning code over HTTPS with trusted CA validation.
4. The backend returns the device credential. Firmware validates the response and persists the secret in Preferences/NVS before using it.
5. Provisioning includes bounded recovery behavior for the case where the server issued a credential but the device lost power before persisting the response.
6. Credential rotation is staged so firmware stores the replacement secret before the backend adopts it on a signed heartbeat. Old credentials are revoked after successful adoption.
7. Administrators can revoke credentials.

The device credential encryption key is supplied by deployment configuration. Preserve it across deployments and backups; changing it without a migration/rotation plan can make stored secrets unreadable.

## 5. Signed device requests and replay protection

The signature is HMAC-SHA-256 over a canonical request containing HTTP method, exact request path and query, timestamp, nonce and SHA-256 body digest.

Headers: X-EMS-Device, X-EMS-Timestamp, X-EMS-Nonce and X-EMS-Signature. Server checks include device state, active credential, signature, timestamp freshness and nonce uniqueness. Signed requests are rate-limited. Firmware refuses signed traffic when its clock has not completed fresh NTP synchronization.

Telemetry sampleId de-duplication is separate from request nonce replay protection: a retried payload can retain its sample ID while using fresh request authentication metadata.

## 6. Deployment secrets

- EMS_BOOTSTRAP_ADMIN_EMAIL
- EMS_BOOTSTRAP_ADMIN_PASSWORD
- EMS_BOOTSTRAP_HOUSEHOLD_NAME
- EMS_DEVICE_CREDENTIAL_ENCRYPTION_KEY (base64-encoded 32-byte key)
- SMTP credentials and EMS_PUBLIC_BASE_URL where email flows are enabled
- Firmware Wi-Fi, device code, one-time provisioning code and trusted CA in local-only Config.h

Do not commit or log secrets. Remove bootstrap credentials after initial provisioning. Use a unique encryption key and retain a secure backup.

## 7. Security limitations and release gates

- MFA is currently disabled.
- Production TLS/proxy forwarding, secure-cookie behavior, CSRF, rate limits, email delivery and secret lifecycle need deployment-specific review.
- Security audit logs and application logs must be reviewed for sensitive data leakage.
- Device credential encryption key backup/rotation and incident recovery must be documented.
- The current firmware does not implement relay control, remote commands or protective trips.
- Independent security review, penetration testing and physical device commissioning are required before production use.

Security controls reduce software risk; they do not replace certified electrical protection or qualified electrical work.
