# Review Remediation — Phased Implementation Plan (2026-01-13)

This document is an implementation-ready plan to remediate the consolidated findings in `design/reviews/20260113.combined.md`. It is intentionally prescriptive to prevent drift and prevent “design decisions” from being pushed down to coding agents.

## Non-Negotiable Constraints (Applies to Every Phase)

- **Do not modify any existing unit/integration test files** (no edits to existing files under `tests/**`). If a test fails after a change, fix production code/configuration, not tests.
- **All existing tests must pass at the end of every phase** (`dotnet test`), and remain passing after subsequent phases.
- **Add new tests only as new files** (new `*.cs` test files are allowed). Do not rename existing tests, do not delete tests, and do not “weaken” assertions by changing test expectations.
- **No “temporary” security bypasses** (no toggles that default to insecure behavior in production).
- **No time estimates or time frames** (priority only).

## Priority Model Used by This Plan

- **P0**: Critical security/availability issues that must be addressed first.
- **P1**: High-impact reliability/security fixes next.
- **P2**: Hardening, maintainability, and scalability improvements after P0/P1.
- **P3**: Opportunistic cleanups and best practices.

This plan is ordered by priority. If a later phase depends on an earlier phase, the dependency is explicitly stated.

--

## Coding Agent Template
```aiignore
You are implementing ONLY: Phase {{PHASE}} from `design/requirements/20260113-review-remdiation-implemention.md`.

  Context docs (read first):
  - `design/reviews/20260113.combined.md`
  - `design/requirements/20260113-review-remdiation-implemention.md` (read Phase {{PHASE}} in full, plus any explicitly listed dependencies/prerequisites for Phase {{PHASE}})

  Non-negotiable constraints:
  - Do NOT modify any existing file under `tests/**` (no edits, renames, or deletions). If tests fail, fix production code/config—not tests.
  - New tests are allowed ONLY as new test files (new `*.cs` files) and must be placed exactly where Phase {{PHASE}} specifies.
  - All existing tests must pass at the end of this phase: run `dotnet test` (full suite) and ensure green.
  - Do NOT implement any other phase(s). Do NOT introduce alternate designs. Follow Phase {{PHASE}} “Implementation Decisions (Locked)” exactly.
  - No insecure temporary bypasses; for security-sensitive config, fail closed in non-Development environments.

  Before coding:
  1) Read applicable instruction files under `.github/instructions/` based on the files Phase {{PHASE}} touches:
     - Security-sensitive changes (auth/tokens/cookies/file paths/external URLs/user input): `security-and-owasp.instructions.md`
     - Performance-sensitive changes (hot paths/DB queries/streaming/large collections): `performance-optimization.instructions.md`
     - C#: `csharp.instructions.md`
     - Tests: `testing.instructions.md`
  2) Confirm Phase {{PHASE}} prerequisites are satisfied (only as described in the Phase Map/phase text).

  Scope:
  - Change ONLY what Phase {{PHASE}} requires. If something is not required by Phase {{PHASE}}, do not change it “because it’s nearby.”

  Implementation requirements:
  - Treat Phase {{PHASE}} section as a checklist. Implement every “Implementation Steps (Explicit)” item.
  - Adhere to every “Implementation Decisions (Locked)” item; do not deviate.
  - If Phase {{PHASE}} specifies exact files/paths/values/config keys/test locations, use exactly those.
  - If Phase {{PHASE}} is ambiguous or conflicts with current code/tests, STOP and ask for clarification (do not guess).

  Execution order:
  1) Run `dotnet test` before making changes and record whether baseline is green.
  2) Implement Phase {{PHASE}} changes.
  3) Add any required tests as NEW files only (no edits to existing tests).
  4) Run `dotnet test` again and ensure all tests pass.
  5) Update the Phase Map in `design/requirements/20260113-review-remdiation-implemention.md`:
     - Mark ONLY Phase {{PHASE}} as complete (`[x]`).
     - Do not check any other phases.
  6) Final summary must include:
     - list of files changed/added
     - mapping of each Phase {{PHASE}} deliverable to the specific file(s) that satisfy it
     - confirmation that `dotnet test` passed

  Stop conditions:
  - Tests fail and cannot be fixed without modifying existing tests => STOP and report failures.
  - Phase {{PHASE}} requirements are unclear => STOP and ask questions.
```

---

## Phase Map (Progress Tracking)

- [x] Phase 0 (Gate) — Baseline, safety rails, and no-regression checkpoint
- [x] Phase 1 (P0) — Remove password secrets from claims + introduce password hashing (stop reversible login passwords)
- [x] Phase 2 (P0) — Secrets hygiene: confirm non-commit, rotate, and add automated secret scanning gates
- [x] Phase 3 (P0) — Fix Blazor `AuthService` JWT validation and stop storing auth tokens in `localStorage` for the UI path
- [x] Phase 4 (P0) — Replace permissive CORS with strict allowlist policies (dev vs. prod)
- [x] Phase 5 (P0) — Prevent path traversal in email template loading and add root containment checks
- [x] Phase 6 (P0) — Centralize file path guarding for all destructive file operations (delete/move)
- [ ] Phase 7 (P0) — Harden external fetches against SSRF + resource exhaustion using existing `SsrfValidator`
- [ ] Phase 8 (P0) — Fix MQL regex evaluation to be timeout-safe (ReDoS) without `Task.Run(...).Result`
- [ ] Phase 9 (P1) — Fix base URL generation (Host header trust) and eliminate sync-over-async in request paths
- [ ] Phase 10 (P1) — Remove `async void` handlers and eliminate sync-over-async patterns flagged by review
- [ ] Phase 11 (P1) — Fix high-risk performance issues (unbounded parallel file reads, missing pagination, N+1 hot paths)
- [ ] Phase 12 (P1) — Standardize error handling (no secret leakage; consistent error envelopes)
- [ ] Phase 13 (P2) — Observability hardening (correlation IDs, metrics, tracing baseline)
- [ ] Phase 14 (P2) — CI/CD hardening gates (SCA, container scanning, formatting/analyzers as checks)
- [ ] Phase 15 (P2) — Policy hardening (rate limiting configuration + security headers/CSP are centralized and validated)
- [ ] Phase 16 (P2) — Data-access hardening (indexes + production-like DB integration tests)
- [ ] Phase 17 (P2) — Cache hardening (invalidation strategy + concurrency safety)
- [ ] Phase 18 (P2/P3) — Structured refactors and hygiene (Program.cs modularization, DbContext configuration split, dependency hygiene, MD5 scoping, SDK pinning)

---

## Phase 0 (Gate) — Baseline, Safety Rails, No-Regression Checkpoint

### Goal
Establish a “no regression” baseline and create guardrails so subsequent phases are applied safely, consistently, and without test patching.

### Implementation Steps (Must be followed exactly)

1. **Capture baseline test status**
   - Run `dotnet test` for the entire solution.
   - Record the pass/fail result in the implementation PR description (not in tests).
2. **Identify test execution expectations**
   - Confirm which test projects are unit vs. integration and whether any require external services (DB containers, etc.).
   - If tests require external dependencies, document the required local setup in the PR description (not in `tests/**`).
3. **Freeze existing tests**
   - Add a team rule (PR checklist) that blocks modifications under `tests/**` for this remediation work.
4. **Confirm the scope of “auth” surfaces**
   - Enumerate (as a checklist in the PR description) the auth entry points:
     - Web UI login flow (Blazor)
     - `api/v*/auth/*` endpoints (JWT access + refresh token)
     - OpenSubsonic endpoints (username + token/salt or password)
     - Any CLI auth paths

### Definition of Done

- `dotnet test` passes with no changes to existing tests.
- A PR checklist exists stating “no `tests/**` edits” for subsequent phases.

---

## Phase 1 (P0) — Remove Password Secrets from Claims + Introduce Password Hashing (Stop Reversible Login Passwords)

### Findings Addressed

- `P0-01: Passwords appear to be stored with reversible encryption`
- Additional critical risk discovered while validating combined findings:
  - `src/Melodee.Common/Models/UserInfo.cs` includes `PasswordEncrypted` as a claim and derives auth tokens using decrypted password material.

### Non-Negotiable Security Outcomes (These are requirements)

- No authentication/authorization mechanism may depend on storing or transmitting a reversible “login password” after this phase.
- No password (or encrypted password) may ever be placed in a `ClaimsPrincipal`.
- OpenSubsonic compatibility must remain functional, but **it must not require storing a reversible “login password.”**

### Implementation Decisions (Locked)

1. **Password hashing algorithm**
   - Use **BCrypt** via the already present dependency (`BCrypt.Net-Next` is referenced in reviews).
   - Cost factor: **12** (explicitly fixed for initial remediation; can be made configurable later, but default remains 12).
2. **Separate “login password” from “OpenSubsonic secret”**
   - Introduce a dedicated **OpenSubsonic secret** (an “app password” concept) that is stored reversibly but is **not the user’s login password** and is **never transmitted to clients**.
   - The OpenSubsonic secret can remain encrypted-at-rest (reversible), but must be:
     - protected with authenticated encryption
     - protected by root containment rules for any file persistence (if ever written)
     - never included in claims, logs, or API responses

### Data Model Changes (Explicit)

1. **Add new database columns to `User`**
   - `PasswordHash` (nullable initially)
   - `PasswordHashAlgorithm` (nullable initially; fixed string value `"bcrypt"`)
   - `OpenSubsonicSecretProtected` (nullable initially; authenticated-encrypted string)
   - Keep existing `PasswordEncrypted` column temporarily as a **legacy field** for migration only.
   - Locked column constraints:
     - `PasswordHash`: `varchar(255)` (BCrypt hashes fit; do not exceed this without updating the plan)
     - `PasswordHashAlgorithm`: `varchar(32)` (values like `bcrypt`)
     - `OpenSubsonicSecretProtected`: `varchar(2048)` (must accommodate `v1:gcm:` prefix + base64 payload)

2. **Migration strategy**
   - Create a new EF Core migration that:
     - adds the three new columns
     - keeps `PasswordEncrypted` as-is (no schema removal in this phase)

### Code Changes (Explicit Files and Required Changes)

1. **Eliminate password material from claims**
   - File: `src/Melodee.Common/Models/UserInfo.cs`
   - Required changes:
     - Remove `PasswordEncrypted` from the `UserInfo` record.
     - Remove `ClaimTypeRegistry.PasswordEncrypted` claim emission.
     - Remove `UserToken` derivation that uses decrypted password.
     - `FromClaimsPrincipal` must no longer read password-related claims.
   - File: `src/Melodee.Common/Constants/ClaimTypeRegistry.cs`
     - Remove (or deprecate and stop using) `PasswordEncrypted` claim type.

2. **Introduce a dedicated password hashing service**
   - Add a service interface + implementation in `src/Melodee.Common/Services/Security/`:
     - `IPasswordHashService`
     - `PasswordHashService`
   - Required behaviors:
     - `Hash(string password) => string`
     - `Verify(string password, string hash) => bool`
     - Must use BCrypt cost factor 12.
     - Must handle null/empty defensively (return false on verify).

3. **Introduce an authenticated-protection mechanism for OpenSubsonic secret**
   - Add a service in `src/Melodee.Common/Services/Security/`:
     - `IOpenSubsonicSecretProtector`
     - `OpenSubsonicSecretProtector`
   - Required behaviors (locked decisions):
     - Use **AES-GCM** authenticated encryption.
     - Ciphertext encoding format stored in DB:
       - `v1:gcm:<base64(nonce||tag||ciphertext)>`
     - Nonce length: 12 bytes (random per encryption).
     - Tag length: 16 bytes.
     - Key derivation: `SHA256(Encoding.UTF8.GetBytes(configKey))` to obtain 32 bytes.
     - Config key name (locked): `Security:OpenSubsonicSecretKey` (must be required in production).
     - Config key minimum length (locked): 32 characters; recommended operational length: 64+ characters.
     - Do not reuse `User.PublicKey` as IV/nonce (nonce is per-encryption).

4. **Update login/auth flows to use hashed passwords**
   - File: `src/Melodee.Common/Services/UserService.cs`
   - Required changes:
     - The “web/API login” methods (`LoginUserAsync`, `LoginUserByUsernameAsync`, and any equivalents) must:
       - verify using `PasswordHash` (BCrypt) only
       - not decrypt anything
       - not compare against `PasswordEncrypted`
     - If `PasswordHash` is null:
       - perform a one-time migration by validating against the legacy method *once*, then set `PasswordHash` and clear the legacy path for future logins.
       - “Legacy method” is strictly limited to:
         - decrypt legacy password
         - compare plaintext equality (only inside the migration step)
   - File: `src/Melodee.Blazor/Controllers/Melodee/AuthController.cs`
     - Must continue to authenticate via `UserService`, but now `UserService` verifies against `PasswordHash`.
     - Ensure failures remain generic (“Invalid credentials”) and do not leak whether the email/username exists.

5. **Update OpenSubsonic authentication to use the dedicated secret**
   - Wherever OpenSubsonic token verification currently relies on decrypting `PasswordEncrypted`, it must be changed to:
     - prefer `OpenSubsonicSecretProtected` (decrypt via `IOpenSubsonicSecretProtector`)
     - if null, fall back to legacy `PasswordEncrypted` **only for migration**
     - once used successfully, populate `OpenSubsonicSecretProtected` and stop relying on legacy storage for that user
   - OpenSubsonic secret generation (locked):
     - If no secret exists, generate a random 32-byte value and encode as base64url (no padding), store protected.
     - Do not display it in logs or API responses.

### Tests to Add (New Files Only; No Test Edits)

Add new tests that validate the following without touching existing tests:

1. **Claims do not contain password material**
   - Add a new test file in `tests/Melodee.Tests.Common/` (this is the locked location for these tests).
   - Assert: no claim type `urn:user:password:encrypted` exists; no claim contains `PasswordEncrypted` value.

2. **Password verification uses BCrypt**
   - Add a new test file in `tests/Melodee.Tests.Common/` for `PasswordHashService`:
     - `Hash` produces a BCrypt hash string
     - `Verify` returns true for correct password, false for incorrect

3. **OpenSubsonic secret protector is authenticated**
   - Add a new test file in `tests/Melodee.Tests.Common/` for `OpenSubsonicSecretProtector`:
     - Encrypt + decrypt roundtrip works
     - Tampering with ciphertext/tag causes decrypt to fail

### Definition of Done

- Existing tests (`dotnet test`) pass without modification.
- No password or encrypted password appears in claims.
- Web/API login uses BCrypt hashes (not reversible encryption).
- OpenSubsonic auth uses a dedicated secret (protected) and does not depend on “login password” reversibility.

---

## Phase 2 (P0) — Secrets Hygiene: Confirm Non-Commit, Rotate, and Add Automated Secret Scanning Gates

### Findings Addressed

- `P0-09: Secrets handling is inconsistent; .env contains real secrets (even if ignored)`

### Implementation Decisions (Locked)

- `.env` is for local development only and must never contain production credentials.
- Repository must have automated secret scanning gates; “manual discipline” is not acceptable as the only control.

### Implementation Steps (Explicit)

1. **Confirm `.env` was never committed**
   - Run `git log -- .env` and verify no commits contain it.
   - If any commit contains secrets, treat as a security incident:
     - rotate all affected secrets
     - invalidate compromised tokens

2. **Create `/.env.example`**
   - Include placeholders only (no real values).
   - Include comments that explicitly forbid real secrets.

3. **Rotate exposed secrets**
   - Rotate:
     - DB password used by any shared environment
     - JWT signing keys
     - `Security:OpenSubsonicSecretKey` introduced in Phase 1 (if already deployed anywhere)
   - Record rotation completion in PR description.

4. **Add secret scanning gates**
   - Add a CI job that runs secret scanning on:
     - PRs
     - default branch
   - The job must fail the build on detection of high-confidence secrets.
   - Locked tool: `gitleaks` (use it consistently; do not introduce multiple scanners with conflicting rules).

### Definition of Done

- `.env` is not in git history (or secrets rotated if it was).
- CI fails if secrets are introduced.
- Existing tests pass.

---

## Phase 3 (P0) — Fix Blazor `AuthService` JWT Validation and Stop Storing Auth Tokens in `localStorage` for the UI Path

### Findings Addressed

- `P0-02: JWT validation is configured to skip issuer/audience validation` (Blazor `AuthService` path)
- `P0-03: Browser storage of JWTs in localStorage increases XSS blast radius`

### Implementation Decisions (Locked)

1. **Blazor UI must use cookie authentication**
   - Use the already configured cookie scheme (`melodee_auth`).
   - UI must not persist JWTs in `localStorage`.
2. **JWTs remain valid for API clients**
   - `api/v*/auth/*` may continue to issue JWTs for non-browser clients.
3. **Single source of truth for issuer/audience**
   - Use existing config keys:
     - `Jwt:Key`, `Jwt:Issuer`, `Jwt:Audience` (already required in `Program.cs`)
   - Do not introduce a second parallel token config for the Blazor UI.

### Implementation Steps (Explicit)

1. **Replace Blazor “token persistence” model**
   - File: `src/Melodee.Blazor/Services/AuthService.cs`
   - Required changes:
     - Remove `ILocalStorageService` usage for auth token persistence.
     - Remove the `melodee_auth_token` storage key entirely.
     - `EnsureAuthenticatedAsync` must rely on server-side auth state, not local tokens.

2. **Move UI sign-in/sign-out to server-side cookie auth**
   - File: `src/Melodee.Blazor/Controllers/Melodee/AuthController.cs`
   - Add explicit endpoints for browser-based auth that set/clear cookies:
     - `POST /api/v{version}/auth/cookie/sign-in`:
       - validates credentials (via `UserService`)
       - creates claims identity (without any secret claims; Phase 1 requirement)
       - calls `HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties)`
     - `POST /api/v{version}/auth/cookie/sign-out`:
       - calls `HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme)`
   - Do not change the existing JWT-based endpoints; keep them for API clients.

3. **Make Blazor authorization state come from cookie auth (no local token state)**
   - File: `src/Melodee.Blazor/Services/CustomAuthStateProvider.cs`
     - Remove this provider from DI (it must not drive auth state after this phase).
   - File: `src/Melodee.Blazor/Program.cs`
     - Remove registration of `CustomAuthStateProvider`:
       - remove `builder.Services.AddScoped<AuthenticationStateProvider, CustomAuthStateProvider>();`
     - Remove registration of `IAuthService` with `AuthService` if it is only used for auth state:
       - remove `builder.Services.AddScoped<IAuthService, AuthService>();`
     - Do not register a custom `AuthenticationStateProvider` replacement in this phase unless absolutely required; rely on the framework-provided provider created by server-side components + cookie auth.
   - Blazor UI behavior (locked):
     - after successful sign-in, force a full page reload so the new auth cookie is applied to a fresh circuit:
       - `NavigationManager.NavigateTo("/", forceLoad: true);`
     - after sign-out, force a full page reload:
       - `NavigationManager.NavigateTo("/login", forceLoad: true);`

4. **Remove any JWT validation code in `AuthService`**
   - Once cookie auth is the UI path, `AuthService` must not validate/parse JWTs at all.
   - If JWT parsing is still required for non-browser scenarios, move that code into a dedicated API client library, not Blazor UI runtime.

### Tests to Add (New Files Only)

- Add a new `WebApplicationFactory<Program>` integration test in `tests/Melodee.Tests.Blazor/` that:
  - posts to `auth/cookie/sign-in`
  - verifies the response includes a `Set-Cookie: melodee_auth=...; HttpOnly; Secure; SameSite=Strict`
  - verifies a subsequent request to an `[Authorize]` endpoint succeeds with the cookie
  - Locked location: `tests/Melodee.Tests.Blazor/`

### Definition of Done

- Blazor UI no longer stores JWTs in `localStorage`.
- Cookie auth is the UI mechanism.
- Existing tests pass.

---

## Phase 4 (P0) — Replace Permissive CORS with Strict Allowlist Policies (Dev vs. Prod)

### Findings Addressed

- `P0-04: CORS is configured as “allow everything” in the runtime pipeline`

### Implementation Decisions (Locked)

- CORS must be defined as named policies with explicit allowlists.
- Production must never use `AllowAnyOrigin`.
- Config key for allowed origins (locked): `Cors:AllowedOrigins` (array/list).

### Implementation Steps (Explicit)

1. **Define CORS policies**
   - File: `src/Melodee.Blazor/Program.cs`
   - Add:
     - `builder.Services.AddCors(...)` with a named policy `MelodeeCors`.
   - Policy rules (locked):
     - Allowed origins: `Cors:AllowedOrigins` only (no wildcard).
     - Allowed methods: `GET, POST, PUT, PATCH, DELETE, OPTIONS`.
     - Allowed headers: `Authorization, Content-Type, If-None-Match, If-Match`.
     - Exposed headers: keep existing list (`Accept-Ranges`, `Content-Range`, `Content-Length`, `Content-Type`) plus `ETag`.
     - `AllowCredentials()` only if cookie-based browser auth is used (Phase 3).

2. **Environment behavior**
   - In Development only:
     - If `Cors:AllowedOrigins` is empty/missing, allow `http://localhost:*` and `https://localhost:*` explicitly (no `AllowAnyOrigin`).
   - In non-Development:
     - If `Cors:AllowedOrigins` is empty/missing, throw on startup.

3. **Replace runtime lambda CORS**
   - Replace `app.UseCors(bb => ...)` with `app.UseCors("MelodeeCors")`.

### Tests to Add (New Files Only)

- Add a new integration test verifying:
  - a request with an origin not in allowlist does not receive `Access-Control-Allow-Origin`
  - a request with an allowed origin receives it

### Definition of Done

- No permissive `AllowAnyOrigin/Method/Header` remains in production runtime.
- Existing tests pass.

---

## Phase 5 (P0) — Prevent Path Traversal in Email Template Loading and Add Root Containment Checks

### Findings Addressed

- `P0-05: Path traversal risk when loading email templates by language code`

### Implementation Decisions (Locked)

1. **Allowed culture codes**
   - Only allow these culture codes for templates (exact set matches request localization list in `Program.cs`):
     - `en-US`, `de-DE`, `es-ES`, `fr-FR`, `it-IT`, `ja-JP`, `pt-BR`, `ru-RU`, `zh-CN`, `ar-SA`
2. **Template folder naming**
   - Folder name must be the lowercase invariant form of the culture code (e.g., `en-us`).
3. **Path containment enforcement**
   - After combining root + relative path, the resulting full path must be verified to remain under the templates library root.

### Implementation Steps (Explicit)

1. **Normalize and validate language code**
   - File: `src/Melodee.Blazor/Services/Email/EmailTemplateService.cs`
   - Replace `NormalizeLanguageCode` with a function that:
     - if null/empty, returns `en-us`
     - if not in the allowed set (case-insensitive), returns `en-us`
     - rejects any value containing path separators or `..` (treat as invalid; return `en-us`)

2. **Enforce root containment**
   - In `LoadTemplateFromLibraryAsync`:
     - compute `root = Path.GetFullPath(libraryResult.Data.Path)`
     - compute `candidate = Path.GetFullPath(Path.Combine(root, relativeTemplatePath))`
     - require `candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)`
     - if not, log a warning and return null (do not throw raw path info)

3. **Remove silent exception swallowing**
   - Replace `catch { return null; }` with:
     - `catch (Exception ex) { logger.LogWarning(ex, "..."); return null; }`
   - The log message must not include attacker-controlled raw input without sanitization.

### Tests to Add (New Files Only)

- Add a unit test for language normalization rejecting `../` and returning `en-us`.
- Add a unit test that ensures `LoadTemplateFromLibraryAsync` refuses to read paths outside the root.

### Definition of Done

- Template loading cannot traverse outside the templates library root.
- Existing tests pass.

---

## Phase 6 (P0) — Centralize File Path Guarding for All Destructive File Operations (Delete/Move)

### Findings Addressed

- `P0-06: Unconstrained file system operations can enable destructive path abuse`

### Implementation Decisions (Locked)

1. **All destructive file operations must require an “allowed root”**
   - No public service method may accept an arbitrary path and delete/move it without verifying it is under an allowed root directory.
2. **Containment check implementation**
   - Use `Path.GetFullPath` containment checks (not string replace, not regex).
3. **Symlink behavior**
   - Treat symlink escapes as unsafe:
     - if your platform cannot reliably detect symlink escapes, restrict operations to non-symlink roots and document it.

### Implementation Steps (Explicit)

1. **Introduce a shared path guard helper**
   - Add a utility in `src/Melodee.Common/Utility/`:
     - `PathGuard` with methods:
       - `string EnsureUnderRoot(string root, string candidatePath)`
       - `bool IsUnderRoot(string root, string candidatePath)`
     - Must:
       - normalize both paths to full paths
       - enforce `candidate` starts with `root` + separator
       - reject root equals candidate for recursive deletes unless explicitly allowed

2. **Update file system service APIs**
   - File: `src/Melodee.Common/Services/FileSystemService.cs`
   - Required changes:
     - Replace methods that accept `path` with methods that accept `(root, path)` or accept an enum “root type” and resolve root internally.
     - Every call to `File.Delete`, `Directory.Delete`, `File.Move`, `Directory.Move` must be preceded by a `PathGuard` check.

3. **Update callers**
   - Identify all call sites of `FileSystemService` destructive methods and pass the appropriate allowed root.
   - Allowed roots (locked set; implement exactly):
     - music library root(s)
     - templates library root
     - cache root
     - temp root
   - Do not introduce new roots without updating this plan.

### Tests to Add (New Files Only)

- Path guard tests:
  - rejects `..` escapes
  - rejects absolute path outside root
  - accepts valid path under root

### Definition of Done

- No destructive file operation can run on a path outside an allowed root.
- Existing tests pass.

---

## Phase 7 (P0) — Harden External Fetches Against SSRF + Resource Exhaustion Using Existing `SsrfValidator`

### Findings Addressed

- `P0-07: SSRF and resource exhaustion risk when fetching arbitrary external URLs`

### Implementation Decisions (Locked)

- All outbound URL fetches that accept user-controlled or metadata-controlled URLs must:
  - pass `SsrfValidator.ValidateUrlAsync` before the request
  - enforce max redirects: **3** (each hop revalidated using `ValidateRedirectAsync`)
  - enforce max response size: **10 MiB**
  - enforce request timeout: **10 seconds**
  - enforce scheme allowlist: `https` only unless config explicitly allows `http` (reusing the existing `PodcastHttpAllowHttp` where appropriate)

### Implementation Steps (Explicit)

1. **Replace unsafe image fetching**
   - File: `src/Melodee.Common/Services/Extensions/HttpClientFactoryExtensions.cs`
   - Required changes:
     - Inject/require `ISsrfValidator` (do not leave it unused).
     - Validate URL before issuing request.
     - Use a named `HttpClient` with:
       - `Timeout = TimeSpan.FromSeconds(10)`
       - `AllowAutoRedirect = false` (manual redirect handling with validation)
     - Enforce max response size by streaming content and stopping at 10 MiB:
       - use `HttpCompletionOption.ResponseHeadersRead`
       - read `response.Content.ReadAsStreamAsync(...)` into a buffer loop
       - stop reading and treat as failure once the cap is exceeded (do not attempt to allocate the full payload)
     - Do not use `Trace.WriteLine` for security-relevant logging; use structured logging.

2. **Standardize error behavior**
   - On blocked SSRF validation:
     - return `null` (for image fetch) and log a warning (sanitized URL).
   - On non-success status code:
     - return `null` (no exception throwing of parsed HTML error bodies).

### Tests to Add (New Files Only)

- SSRF validator is already unit-testable; add an integration-like test for image fetch helper:
  - blocks `http://127.0.0.1/...`
  - blocks redirects to private IP
  - enforces size cap

### Definition of Done

- Outbound image fetching cannot SSRF into private/reserved ranges and cannot download unbounded content.
- Existing tests pass.

---

## Phase 8 (P0) — Fix MQL Regex Evaluation to be Timeout-Safe (ReDoS) Without `Task.Run(...).Result`

### Findings Addressed

- `P0-08: Regex evaluation can still hang (ReDoS) despite a guard abstraction`

### Implementation Decisions (Locked)

- Regex evaluation must be performed using `Regex` timeout support, not by attempting cancellation via `Task.Run`.
- `SafeMatch` must not call `.Result` on tasks.
- Default timeout remains **500ms** unless explicitly overridden by the caller.

### Implementation Steps (Explicit)

1. **Fix regex construction**
   - File: `src/Melodee.Mql/Security/MqlRegexGuard.cs`
   - Required changes in `SafeMatch`:
     - Construct regex using the `Regex(string pattern, RegexOptions options, TimeSpan matchTimeout)` constructor.
     - Use `actualTimeout` as `matchTimeout`.
     - Prefer `RegexOptions.NonBacktracking` in addition to current options when compatible with supported runtime behavior.

2. **Remove `Task.Run` and `.Result`**
   - Replace with direct `regex.IsMatch(testString)` (or `regex.Match`), relying on timeout.

3. **Ensure exceptions are mapped to the existing error codes**
   - Timeout => `MQL_REGEX_TIMEOUT`
   - ArgumentException => `MQL_REGEX_INVALID`
   - Other => `MQL_REGEX_ERROR`

### Tests to Add (New Files Only)

- Add a regression test in `tests/Melodee.Mql.Tests/` with a known catastrophic pattern and confirm `SafeMatch` returns timeout within the configured limit.

### Definition of Done

- Regex evaluation cannot hang indefinitely and does not block on `.Result`.
- Existing tests pass.

---

## Phase 9 (P1) — Fix Base URL Generation (Host Header Trust) and Eliminate Sync-over-Async in Request Paths

### Findings Addressed

- `P1-01: Host header trust and sync-over-async in base URL resolution`

### Implementation Decisions (Locked)

1. **Externally visible URLs must be configuration-driven**
   - `SettingRegistry.SystemBaseUrl` must be present for any feature that generates externally consumed links (password reset, invites, etc.).
2. **Host header must not be used as a fallback for external links**
   - If config is missing, return null and fail the operation that needs external URLs with a safe error.
3. **No sync-over-async**
   - No `.GetAwaiter().GetResult()` in request paths.

### Implementation Steps (Explicit)

1. **Make base URL resolution async**
   - File: `src/Melodee.Blazor/Services/BaseUrlService.cs`
   - Change contract:
     - `Task<string?> GetBaseUrlAsync(CancellationToken ct = default)`
   - Cache configuration result in memory for the process lifetime (or for a bounded TTL) to avoid repeated DB/config calls.

2. **Remove Host header fallback for external links**
   - For operations requiring base URL:
     - if `SystemBaseUrl` is missing, return a 500 with a safe message and a correlation ID (do not construct from `Request.Host`).

### Tests to Add (New Files Only)

- Unit test: if `SystemBaseUrl` is missing, `GetBaseUrlAsync` returns null and the caller fails safely.

### Definition of Done

- No Host header fallback for external URLs.
- No sync-over-async in base URL resolution.
- Existing tests pass.

---

## Phase 10 (P1) — Remove `async void` Handlers and Eliminate Sync-over-Async Patterns Flagged by Review

### Findings Addressed

- `P1-02: async void event handlers hide exceptions`
- `P1-03: Blocking async calls can deadlock and degrade throughput`

### Implementation Decisions (Locked)

- No `async void` except for true event handlers that cannot be changed (and then they must offload work safely).
- Any remaining `async void` must follow the pattern:
  - `void Handler(...) => _ = HandlerAsync();`
  - `Task HandlerAsync()` wraps body in try/catch with logging.

### Implementation Steps (Explicit)

1. **Replace `async void` configuration handlers**
   - Files:
     - `src/Melodee.Common/Services/SearchEngines/ArtistSearchEngineService.cs`
     - `src/Melodee.Blazor/Components/Layout/MainLayout.razor`
   - Convert to fire-and-forget wrapper pattern with explicit exception handling.

2. **Scan for sync-over-async**
   - Replace `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` in:
     - filters
     - controllers
     - background jobs
   - If a synchronous boundary is required, it must be confined to:
     - application startup
     - `Main()`
     - and must be documented in code with a single “why this is safe” comment.

### Definition of Done

- No `async void` remains except wrapped pattern.
- No blocking calls remain in request path code.
- Existing tests pass.

---

## Phase 11 (P1) — Fix High-Risk Performance Issues (Unbounded Parallel File Reads, Missing Pagination, N+1 Hot Paths)

### Findings Addressed

- `P1-04: Memory and I/O spikes in image merge/CRC computation`
- `P1-05: N+1 query patterns`
- `P1-06: Missing pagination and request limits`

### Implementation Decisions (Locked)

1. **Bound concurrency for file processing**
   - Max degree of parallelism: **4** (explicit).
   - Use streaming reads (no `ReadAllBytesAsync` for entire-file hashing).
2. **Pagination must be enforced server-side**
   - Default page size: **50**
   - Max page size: **200**
3. **N+1 prevention approach**
   - Batch load and dictionary lookup is preferred over per-item `FirstOrDefault` scans in loops.

### Implementation Steps (Explicit)

1. **Fix image merge CRC**
   - File: `src/Melodee.Common/Services/ServiceBase.cs`
   - Replace `File.ReadAllBytesAsync` + `Task.WhenAll` with:
     - streaming CRC computation
     - bounded concurrency (4)

2. **Enforce pagination**
   - For every list endpoint:
     - add parameters `pageNumber`, `pageSize` (or `offset`, `limit`) using the locked defaults and caps above
     - apply `Skip/Take` at the database query level

3. **Fix known N+1 hot paths**
   - Implement explicit batching in the specific controllers/services called out by reviews.
   - Use `AsNoTracking` for read-only queries consistently.

### Tests to Add (New Files Only)

- Add tests verifying:
  - pagination caps are enforced
  - query counts remain bounded for known endpoints (use existing N+1 detection infrastructure if present, without modifying existing tests)

### Definition of Done

- No unbounded parallel full-file loads in image merge path.
- Pagination enforced with caps.
- Existing tests pass.

---

## Phase 12 (P1) — Standardize Error Handling (No Secret Leakage; Consistent Error Envelopes)

### Findings Addressed

- `P1-07: Inconsistent error handling can leak information and complicate clients`
- `P1-08: File upload and content handling need defense in depth` (error surface component)

### Implementation Decisions (Locked)

- All API endpoints must return a consistent error envelope for failures:
  - error code
  - safe message
  - correlation ID
- Authentication errors must not reveal whether a user exists.
- No exception stack traces are returned to clients in production.

### Implementation Steps (Explicit)

1. **Global exception handling**
   - Add an exception-handling middleware or filter that:
     - maps known exceptions to stable error codes
     - logs full detail server-side (sanitized)
     - returns safe response bodies client-side

2. **Update places that swallow exceptions**
   - Replace silent catches (e.g., in template loading) with warning logs and safe fallbacks.

3. **File upload validation (minimum baseline)**
   - Identify all upload endpoints using this locked discovery approach (do not improvise):
     - search for controller/component actions accepting `IFormFile`, `IFormFileCollection`, `Stream`, or `byte[]`
     - search for `[FromForm]` models containing `IFormFile`
   - Apply the following locked baseline controls to every upload endpoint:
     - **Max request size**:
       - default cap: 100 MiB
       - allow per-endpoint override via config: `Uploads:Overrides:<EndpointName>:MaxBytes`
     - **File type allowlist**:
       - images: `image/jpeg`, `image/png`, `image/webp`
       - audio (if applicable): `audio/mpeg`, `audio/flac`, `audio/mp4`, `audio/ogg`, `audio/wav`
       - if an endpoint needs a type not listed here, it must be explicitly added to this plan before implementation
     - **Magic-byte sniffing** for images (minimum):
       - JPEG/PNG/WebP signature checks (do not trust only extension or content-type)
     - **Storage safety**:
       - write to temp/quarantine first
       - use server-generated filenames
       - apply root containment checks (Phase 6) before final move
     - **Logging**:
       - log only sanitized filenames and correlation IDs
       - never log file contents or secrets

### Definition of Done

- Error envelope is consistent across endpoints.
- No secret leakage in error responses.
- Existing tests pass.

---

## Phase 13 (P2) — Observability Hardening (Correlation IDs, Metrics, Tracing Baseline)

### Findings Addressed

- `P2-05: Observability gaps`

### Implementation Decisions (Locked)

- Correlation ID must be present in every request log line and propagated to outbound HTTP calls and background jobs.
- Metrics must include:
  - request duration
  - error counts
  - job durations
  - cache hit/miss

### Definition of Done

- A correlation ID is visible in logs for representative request types.
- Existing tests pass.

---

## Phase 14 (P2) — CI/CD Hardening Gates (SCA, Container Scanning, Formatting/Analyzers as Checks)

### Findings Addressed

- `P2-06: CI/CD hardening`

### Implementation Decisions (Locked)

- CI must include:
  - secret scanning (Phase 2)
  - dependency vulnerability scanning
  - container image scanning (if Docker images are built in CI)
  - code analyzers/format checks (non-destructive; do not auto-fix in CI)

### Definition of Done

- CI fails on introduced vulnerabilities/secrets as configured.
- Existing tests pass.

---

## Phase 15 (P2) — Policy Hardening (Rate Limiting Configuration + Security Headers/CSP are Centralized and Validated)

### Findings Addressed

- `P2-09: Rate limiting and other policy “magic numbers” should be configuration-driven and validated`
- `P2-10: Security headers/CSP configuration should be centralized, reviewed, and testable`

### Implementation Decisions (Locked)

1. **Rate limiting is configuration-driven**
   - All token bucket values must come from configuration (not hardcoded in `Program.cs`).
   - Locked configuration section name: `RateLimiting`
2. **Security headers are centralized**
   - CSP and related security headers must be defined in one place (one middleware) and be testable via integration tests.
   - Policy must not be weaker in production than in development.

### Implementation Steps (Explicit)

1. **Move rate limiting policies to configuration**
   - File: `src/Melodee.Blazor/Program.cs`
   - Replace inline `TokenBucketRateLimiterOptions` values with options binding:
     - `RateLimiting:MelodeeApi:*`
     - `RateLimiting:MelodeeAuth:*`
   - Add startup validation:
     - values must be positive
     - queue limits must be non-negative
     - ensure auth policy is stricter than general API policy (explicit validation rule)

2. **Centralize CSP and security headers**
   - File: `src/Melodee.Blazor/Program.cs`
   - Replace scattered header setting logic with a single middleware (one file or one extension method).
   - Locked minimum header set:
     - `Content-Security-Policy` (must exist)
     - `Strict-Transport-Security` (production only)
     - `X-Content-Type-Options: nosniff`
     - `Referrer-Policy`
     - `Permissions-Policy`
   - Locked requirement: do not include secrets or configuration values verbatim in headers.

### Tests to Add (New Files Only)

- Add a `WebApplicationFactory<Program>` integration test in `tests/Melodee.Tests.Blazor/` that asserts required headers exist on a representative endpoint.

### Definition of Done

- Rate limiting values are no longer hardcoded in startup.
- Required security headers are consistently present.
- Existing tests pass.

---

## Phase 16 (P2) — Data-Access Hardening (Indexes + Production-like DB Integration Tests)

### Findings Addressed

- `P2-03: Database index strategy likely needs review for large-scale usage`
- `P2-08: Integration testing with production-like database and security scenarios is incomplete`

### Implementation Decisions (Locked)

1. **Indexes are added only when backed by evidence**
   - Evidence sources (locked):
     - query patterns already identified in services/controllers
     - EF Core query logs in development (sanitized)
     - measurable slow queries in benchmarks/integration tests
2. **Integration tests must run against the production database engine**
   - If production is PostgreSQL, integration tests must include PostgreSQL coverage.

### Implementation Steps (Explicit)

1. **Index review and additions**
   - Identify the top query patterns flagged in `design/reviews/20260113.combined.md` (user/song history, scan histories, party queue ordering).
   - Add explicit `HasIndex(...)` definitions in EF Core model configuration for those patterns.
   - Create migrations for index additions only (do not mix schema + behavioral changes in one migration).

2. **Add production-like DB integration tests**
   - Add a dedicated integration test fixture that uses PostgreSQL (Testcontainers preferred; if not possible in CI, run on a separate CI job that is still required for merges).
   - Locked requirement:
     - existing in-memory DB tests remain unchanged and still run
     - the new PostgreSQL tests must validate at least one representative “query-heavy” endpoint/service

### Definition of Done

- Index migrations exist and are narrowly scoped.
- PostgreSQL integration tests exist and pass (without modifying existing tests).

---

## Phase 17 (P2) — Cache Hardening (Invalidation Strategy + Concurrency Safety)

### Findings Addressed

- `P2-04: Cache invalidation is manual and some cache structures may be unsafe under concurrency`

### Implementation Decisions (Locked)

1. **Cache invalidation must be centralized**
   - Cache key generation and invalidation must not be duplicated across services.
2. **Concurrency safety is required**
   - No `HashSet`/non-thread-safe collection may be enumerated while being mutated by other threads in cache code.

### Implementation Steps (Explicit)

1. **Fix concurrency issues**
   - File: `src/Melodee.Mql/MqlExpressionCache.cs` (as referenced in the review)
   - Replace unsafe collection usage with either:
     - a concurrent collection, or
     - lock-protected access with copy-out before enumeration
   - Locked rule: no enumeration over a mutable non-thread-safe collection without a lock that also covers mutation.

2. **Centralize invalidation**
   - Introduce a cache invalidation service that owns:
     - key namespaces
     - “clear by entity type” semantics
   - Update callers to use that single service.

### Tests to Add (New Files Only)

- Add a concurrency stress test (bounded iterations) that:
  - concurrently adds entries and clears by entity type
  - asserts no exceptions and consistent end state

### Definition of Done

- Cache code is concurrency-safe for the identified race conditions.
- Cache invalidation is driven by a single mechanism.
- Existing tests pass.

---

## Phase 18 (P2/P3) — Structured Refactors and Hygiene (Program.cs, DbContext split, dependency + MD5 scoping, SDK pinning)

### Findings Addressed

- `P2-01: Program.cs monolith`
- `P2-02: Large services / monolithic Common`
- `P2-11: DbContext configuration/seed bloat`
- `P3-01: MD5 usage scoping`
- `P2-07: Dependency risk (beta/unmaintained)`
- `P3-03: Cache size estimation via serialization`
- `P3-04: TODO/test sprawl entropy`
- `P3-05: ConfigureAwait policy (intentional)`
- `P3-02: Framework targeting and SDK pinning should be explicit (preview risk)`

### Implementation Decisions (Locked)

- Refactors must be “mechanical” and low-risk:
  - no behavior changes without tests
  - no sweeping renames unless required for clarity/security
- MD5 is allowed only in:
  - explicit OpenSubsonic compatibility code paths (documented)
  - deterministic non-security ID generation (prefer SHA-256 if replacing)
 - SDK version is pinned in `global.json` if the project requires a non-default SDK on common build agents.

### Definition of Done

- Refactors do not change behavior and all tests pass.
