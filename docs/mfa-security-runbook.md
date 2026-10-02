# MFA security controls and rollout

The application now encrypts TOTP seeds, consumes accepted time steps atomically,
and maintains revocable pending logins in SQL. These controls need the existing
durable, certificate-encrypted Data Protection key ring on every production node.

## Storage and verification boundary

`MfaSecretProtection` only protects values. Its versioned Data Protection purpose
includes the member ID, so copying one member's ciphertext into another member's
row cannot authenticate the second member. Repositories return ciphertext through
`MemberMfaCredential.ProtectedTotpSecret`; there is no plaintext seed property on
that model. `MfaVerificationService` is the only runtime MFA decryption caller.

The verification service returns enrollment material only for an unconfirmed
credential whose version matches the pending challenge. Once enabled, it returns
verification results, never the seed. Enrollment pages necessarily show the seed
and QR code, require a current pending challenge, and use `Cache-Control: no-store`.
No seed, code, recovery code or cookie value belongs in logs or audit evidence.

The verifier matches a code within the configured 30-second +/-1-step window.
SQL accepts it only when its step is strictly greater than `lastAcceptedTimeStep`,
and the credential version, ciphertext and enabled state still match. A single
update enables enrollment, records its step and installs the recovery-code hashes.
Concurrent requests therefore cannot confirm enrollment twice or reuse a step.
Recovery-code changes update only their own column and cannot rewind the TOTP step.

## Wrong-code lockout

Every TOTP, enrollment or recovery code spends one attempt *before* it is
checked, under a row lock on `StaffOps_MemberMfa`, and only an accepted code
clears the count. Parallel guesses therefore queue behind each other; none can
start once the budget is spent (`MfaStorageIntegrationTests`). The count is of
consecutive failures across every sign-in, not per challenge:

| Consecutive wrong codes | Lock |
|---|---|
| 10 | 15 minutes |
| 20, 30, … 70 | doubles each time (30 min, 1 h … 16 h) |
| 80, 90 | 24 hours |
| 100 | until an Admin resets the member's MFA |

That is 100 guesses in a member's lifetime without a success, roughly a 1 in
3,300 chance against a six-digit code with a one-step window. Waiting out a
fixed lock used to restore a fresh budget: 960 guesses a day, indefinitely.

The hard stop is a denial-of-service lever for someone who already holds the
member's password. That is accepted: the password is then compromised anyway,
and reaching 100 is itself the alert. Reset through **Staff admin → Reset MFA**
by another Admin; for a tenant's only Admin, use the operator recovery process
below. Treat any member reaching the hard stop as a suspected credential
compromise: rotate the password before re-enrolling.

Recovery codes are stored as salted PBKDF2. The unsalted SHA-256 form used
before 27 September 2026 is no longer accepted. A member holding only old codes
signs in with their authenticator, or is reset and re-enrolled for a new batch.

## Pending login lifecycle

`StaffOps_MfaChallenge` stores a random challenge key, member ID, credential
version, identity security stamp, a hash of a separate browser-binding secret,
and a five-minute expiry. The pending cookie contains a protected reference; it
is not a member session. Legacy pending cookies have no reference and fail closed.

Every read and final consumption checks account approval, lockout, active staff
status, security stamp, enrollment version, expiry and browser binding. Final
sign-in requires an atomic SQL delete of the challenge; one concurrent request
can consume it. TOTP acceptance by itself does not create a member session.

- MFA reset deletes all pending challenges and the credential in the same scope.
- Member-save notifications revoke all pending challenges, including password
  reset and approval/security edits. Stamp comparison provides a further check.
- Staff anonymization/deactivation deletes pending challenges in its transaction.
  A subsequently reactivated account cannot resume the old challenge.
- Logout and a replacement password login clear the current browser's challenge.
- Expired records are purged when a new challenge starts.

`hwb-mfa-browser` is host-only, HttpOnly, SameSite=Strict, Secure outside
Development, scoped to `/staffops/account`, and expires after five minutes.
Copying the pending cookie alone, or combining it with another browser's binding,
does not work. Copies of **both** cookies remain bearer credentials until expiry,
revocation or consumption; this is not a hardware/device identity guarantee.
Do not claim resistance to complete browser-cookie theft. If an auditor requires
that stronger property, use device proof of possession such as WebAuthn and test
that flow separately. Antiforgery remains required on verification POSTs.

## Upgrade existing databases

1. Arrange a maintenance window and stop/drain all old application instances.
   Do not let old code run against the upgraded table: it expects plaintext seeds.
2. Back up SQL and the matching key ring/certificates. Keep backups in protected
   storage with separate access controls. Prove key access and restore in staging.
3. Start one new instance. Migration `2026-09-staffops-09` adds ciphertext,
   credential-version and accepted-step columns plus the challenge table. It
   protects each legacy seed, preserves enrollment/recovery metadata, and blanks
   `totpSecret` in the migration transaction. Re-running the data conversion does
   not re-encrypt migrated rows or reset their accepted steps.
4. A failed StaffOps migration now aborts application startup. Repair the key-ring
   or data problem and retry; do not bypass the migration or copy seeds to logs.
5. Inspect **counts only**: no nonblank legacy seed values, and every enrollment
   has ciphertext and a credential version. Verify an existing account can use a
   current code and that replay fails. Verify enrollment and reset on a test account.
6. Start the remaining upgraded instances with the same application name and
   shared key ring. Confirm security events and cross-instance replay tests.

The historical `totpSecret` column remains empty for compatibility with the
append-only migration chain; runtime code never falls back to it. Encryption
does not erase old backups, transaction logs or database-page remnants. Protect
those copies under the retention policy. If old seeds may have been exposed,
reset affected MFA enrollments and issue new recovery codes after identity checks.

Rollback requires stopping the new fleet and restoring a matched pre-upgrade
database/key-ring backup, or a reviewed forward repair. Simply running the old
binary against emptied seed columns is not a supported rollback.

## Key rotation and recovery

Keep `DataProtection:KeyRingDirectory`, application name `ProgrammePulse`, and
certificate/private-key access consistent on all nodes. Use the configured new
certificate plus `PreviousCertificatePath`/password during certificate rollover.
Old key XML and long-lived ciphertext may still need older certificates. Retain
them until all necessary keys, ciphertext and backups have a proven recovery path.
Ordinary rollover does not automatically re-encrypt existing key XML or MFA rows.

Tests verify MFA seed decryption after provider restart and certificate rollover,
and refusal with another member's purpose or a missing key ring. Production must
still exercise a restore drill. If keys cannot be recovered, verification fails
closed. Use an independently verified administrative recovery process to reset
MFA and enroll a new seed; never enable plaintext fallback. Treat theft of both
SQL and the key material as compromise, requiring credential rotation and incident
response rather than routine rollover.

## Evidence

- `MfaStorageIntegrationTests`: real SQL encrypted storage, legacy conversion,
  idempotency, replay, concurrent verification/enrollment and credential versions.
- `MfaChallengeIntegrationTests`: real password flow, multiple pending browsers,
  reset/deactivation revocation, expiry, stale stamps, cookie binding and atomic
  challenge consumption.
- `SecurityBoundaryIntegrationTests`: invalid MFA cannot sign in; accounts
  deactivated after password verification cannot finish signing in.
- `MfaSecretProtectionTests` and `DataProtectionKeyRingConfigurationTests`: key,
  member-purpose, enrollment-disclosure and certificate-rotation boundaries.

Run with `PP_REQUIRE_SQL_TESTS=1`. See [security assurance](security-assurance.md)
for CI evidence and the remaining deployment gates. Algorithm and identity
references: [RFC 6238 replay requirement](https://www.rfc-editor.org/rfc/rfc6238#section-5.2)
and [ASP.NET Core security stamps](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-api-authorization?view=aspnetcore-10.0#signout-everywhere).
