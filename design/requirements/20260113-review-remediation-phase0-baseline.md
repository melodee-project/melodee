# Review Remediation Phase 0 Baseline Report

**Date:** 2026-01-13
**Phase:** Phase 0 (Gate) — Baseline, safety rails, and no-regression checkpoint

---

## Environment

- **OS:** Linux (confirmed via test execution)
- **.NET SDK:** .NET 10.0 (net10.0)
- **Working Directory:** /home/steven/source/melodee
- **Required Environment Variables:**
  - `ASPNETCORE_ENVIRONMENT` (for environment-specific configuration)
  - `MELODEE_APPSETTINGS_PATH` (optional, for custom appsettings.json)

---

## Baseline Test Run

### Exact Command Executed

```bash
dotnet test --no-restore
```

### Pass/Fail Summary

**Status:** PASSED (all tests passing)

### Test Projects Run

| Test Project | Result | Passed | Failed | Skipped | Duration |
|--------------|--------|--------|--------|---------|----------|
| Melodee.Mql.Tests.dll | Passed | 579 | 0 | 0 | 256 ms |
| Melodee.Tests.Blazor.dll | Passed | 1142 | 0 | 14 | 5 s |
| Melodee.Tests.Common.dll | Passed | 3372 | 0 | 2 | 1 m 6 s |

**Totals:**
- **Total Tests:** 5093
- **Passed:** 5093
- **Failed:** 0
- **Skipped:** 16

### Failing Test Names

**None** — All existing tests pass on the baseline.

---

## Auth Surface Inventory

### Blazor UI Auth State Mechanism

- **File:** `src/Melodee.Blazor/Services/CustomAuthStateProvider.cs`
  - Implements `AuthenticationStateProvider` for Blazor
  - Subscribes to `IAuthService.UserChanged` events
  - Creates `AuthenticationState` from `ClaimsPrincipal`

- **File:** `src/Melodee.Blazor/Services/AuthService.cs`
  - Manages JWT storage in `localStorage` via `ILocalStorageService`
  - Key: `melodee_auth_token`
  - Token validation at lines 67-74 with `ValidateIssuer = false`, `ValidateAudience = false`
  - Login method generates JWT and stores in localStorage (line 121)

### API v*/auth/* Endpoints

**File:** `src/Melodee.Blazor/Controllers/Melodee/AuthController.cs`

| Endpoint | Method | AllowAnonymous | Description |
|----------|--------|----------------|-------------|
| `/api/v{version}/auth/authenticate` | POST | Yes | Username/email + password authentication, returns JWT |
| `/api/v{version}/auth/google` | POST | Yes | Google ID token authentication |
| `/api/v{version}/auth/refresh-token` | POST | Yes | JWT refresh with token rotation |
| `/api/v{version}/auth/refresh` | POST | No (JWT required) | Legacy JWT refresh endpoint |
| `/api/v{version}/auth/logout` | POST | No (JWT required) | Logout and revoke refresh tokens |
| `/api/v{version}/auth/revoke` | POST | No (JWT required) | Revoke specific refresh token |
| `/api/v{version}/auth/password-reset/request` | POST | Yes | Request password reset |
| `/api/v{version}/auth/password-reset/validate/{token}` | GET | Yes | Validate password reset token |
| `/api/v{version}/auth/password-reset/confirm` | POST | Yes | Reset password with token |

### OpenSubsonic Auth Endpoints

**File:** `src/Melodee.Blazor/Controllers/OpenSubsonic/ControllerBase.cs`

OpenSubsonic uses a custom authentication mechanism:

- **Cookie-based authentication:** `melodee_blazor_token` cookie (SHA256 hash verification)
- **Query parameters:** `u` (username), `p` (password), `t` (token), `s` (salt), `apiKey`
- **JWT authentication:** `jwt` query parameter
- **Authentication bypass:** Localhost requests, image requests, and baseUrl requests skip auth

**Token verification location:** `ControllerBase.OnActionExecutionAsync` (lines 79-173)

### CLI Auth Entry Points

**File:** `src/Melodee.Cli/Program.cs`

**No direct CLI authentication entry points.** The CLI:
- Uses configuration-based authentication via `appsettings.json`
- Has user management commands (`mcli user create`, `mcli user delete`, `mcli user list`)
- Requires database connection and configuration, not interactive auth

---

## Guardrails for Remediation Work

### Explicit Checklist

- [x] **No `tests/**` modifications** — Existing test files must not be edited, renamed, or deleted
- [x] **No insecure temporary bypass toggles** — No configuration flags that default to insecure behavior in production
- [x] **Each remediation phase ends with `dotnet test` passing** — All existing tests must pass before moving to the next phase

### Additional Guardrails

- [x] New tests may be added as new files only (no modifications to existing tests)
- [x] All remediation phases must follow the priority order (P0 before P1, etc.)
- [x] Password/secrets remediation must not place reversible credentials in claims
- [x] JWT issuer/audience validation must be enabled in production
- [x] CORS must use explicit allowlists, never `AllowAnyOrigin` in production
- [x] File operations must enforce root containment checks
- [x] External fetches must use SSRF validation
- [x] Regex evaluation must use timeout-safe patterns
