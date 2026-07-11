<!-- markdownlint-disable-file -->

# CodeQL Security Remediation Record

**Updated**: 2026-07-11
**Status**: Source fixes complete; GitHub rescan pending

## Scope

This document records high-confidence CodeQL remediations and justified
compatibility exceptions. It is not a substitute for the live GitHub code
scanning dashboard, and it does not claim that future scans will produce no new
findings.

## July 2026 Remediation

The live baseline contained seven open CodeQL alerts:

| Rule | Count | Severity | Location |
|------|------:|----------|----------|
| `cs/exposure-of-sensitive-information` | 6 | Medium | `ForgotPassword.razor` |
| `py/clear-text-storage-sensitive-data` | 1 | High | `setup_melodee.py` |

### Password Reset Privacy

Six password-reset log events included an email passed through
`LogSanitizer.MaskEmail`. The helper retained part of the local address and the
complete domain, so the logs still persisted private user information.

The reset flow now logs generic operational events without the address, base
URL, template subject, reset token, or exception payload. The configured reset
base URL must be an absolute HTTP(S) URL with a host and no user information,
query, or fragment. Rate limiting, token generation, email delivery, generic
user responses, and account enumeration resistance are unchanged.

The same privacy boundary now covers adjacent SMTP and authentication paths:
SMTP logs omit message/configuration values and raw exceptions; login,
migration, profile-lookup, and blacklist logs use generic outcomes or internal
numeric user IDs rather than email addresses and usernames.

### Startup Configuration Redaction

The startup configuration factory previously wrote every process environment
variable value to `Trace`, including database passwords, authentication keys,
tokens, and complete connection strings. All configuration diagnostics now use
a centralized deny-by-default redactor. Sensitive and unrecognized keys emit
only `[REDACTED]`; an explicit set of operational paths, ports, versions,
environments, and limits remains visible after log-forging characters are
escaped. Credential-bearing or parameterized URLs are also redacted.

Focused tests cover sensitive names, unknown values, safe operational values,
URL credentials, and injected line endings.

### Protected Setup Secrets

Unattended Compose setup requires a stable database password and authentication
key across restarts. Both supported setup utilities now use one shared writer
that:

- generates independent URL-safe values from 32 and 64 random bytes;
- replaces settings by exact key instead of matching example values;
- never prints either generated value;
- creates an unpredictable temporary file with `O_CREAT`, `O_EXCL`, and
  `O_NOFOLLOW` where available;
- applies mode `0600` through the open descriptor before writing on POSIX;
- atomically publishes a new file without replacement, or replaces only an
  explicitly approved and revalidated regular file;
- removes partial and temporary files if writing or publication fails; and
- refuses live/dangling symlinks and other non-regular destinations.

The ignored deployment file is the necessary persistence boundary. It is
owner-only (`0600`) on POSIX; on Windows it inherits the containing directory's
ACL. One narrow `codeql[py/clear-text-storage-sensitive-data]` suppression
documents that unavoidable sink. The clear-text-storage query is not excluded
globally.

### Restored Python Coverage

A local CodeQL 2.26.0 run after restoring Python to the advanced workflow
surfaced 22 pre-existing findings that the stale server-side setup no longer
updated:

- one polynomial ReDoS path in GitHub Link-header parsing;
- two clear-text demo password/API-key log paths; and
- 19 filesystem path-injection paths in the destructive incoming cleanup tool.

The Link header now uses bounded linear parsing, and demo-user setup never
prints credentials, generated key material, encrypted values, or raw failure
payloads. The cleanup tool now validates a canonical cleanup root beneath an
explicit trusted boundary, rejects filesystem-root authority and symlink
escapes, revalidates every read/mutation, preflights all ZIP members before
extracting any content, and rejects absolute or parent-traversing SFV entries.
Its default boundary is the current working directory; administrators targeting
an absolute path outside it must explicitly provide `--trusted-boundary`.

A fresh Python database reports zero findings for all 22 rules/paths.

### CodeQL Workflow Coverage

The version-controlled advanced workflow analyzes GitHub Actions, C#,
JavaScript/TypeScript, and Python. Compiled C# uses autobuild; interpreted
languages use build mode `none`. Python has a dedicated local-threat-model
configuration for command-line and filesystem maintenance utilities; the web
application languages use the default remote boundary. Workflow actions are
pinned to verified full commit SHAs.

GitHub's default setup is currently `not-configured`. The Python alert belongs
to old `dynamic/github-code-scanning/codeql:analyze` and
`dynamic/github-code-scanning/codeql:upload` configurations. After an advanced
Python scan completes on `main`, an administrator must delete those stale
dynamic configurations in **Code scanning > Tool status** while retaining the
checked-in workflow configuration, so obsolete alert state no longer remains
attached to the branch.

### Precise Security Models

The prior configuration excluded entire C# security queries and used a
value-preserving `summaryModel` as though it were a sanitizer. Those broad
exclusions were removed.

The auto-discovered local model pack marks only the return value of
`LogSanitizer.Sanitize` as a `log-injection` barrier. It also marks the
deny-by-default `ConfigurationLogRedactor.RedactValue` return as a
`file-content-store` barrier so CodeQL recognizes that secret configuration
values cannot reach diagnostic logs:

```yaml
extensions:
  - addsTo:
      pack: codeql/csharp-all
      extensible: barrierModel
    data:
      - ["Melodee.Common.Utility", "LogSanitizer", false, "Sanitize", "(System.String)", "", "ReturnValue", "log-injection", "manual"]
      - ["Melodee.Common.Configuration", "ConfigurationLogRedactor", false, "RedactValue", "(System.String,System.Object)", "", "ReturnValue", "file-content-store", "manual"]
```

These models do not treat email or identifier masking as privacy sanitizers.
Future real findings from the previously excluded queries must be fixed at the
source or dismissed individually with evidence.

## Historical Remediation

| Category | Status | Approach |
|----------|--------|----------|
| Regex denial of service | Fixed | Runtime-constructed regular expressions use explicit timeouts. |
| Path traversal | Fixed | Uploaded file paths are resolved beneath an approved root with `SafePath`. |
| Markdown cross-site scripting | Fixed | Rendered Markdown passes through an HTML allowlist sanitizer. |
| Log forging | Mitigated | User-controlled log fields use `LogSanitizer.Sanitize`; CodeQL has a precise barrier model. |
| Unvalidated redirection | Fixed | Jellyfin redirects use parsed GUID values instead of raw input. |
| Weak cryptography | Compatibility exception | MD5 remains only where required by OpenSubsonic/Last.fm protocols or for non-security ETags and deterministic identifiers. |

Weak algorithms must not be used for password storage, authentication designs,
signatures outside required compatibility protocols, or new security-sensitive
features.

## Verification

- Forgot-password Blazor project build: zero warnings and errors.
- Blazor tests: 1,213 passed, 14 skipped, zero failed.
- Setup security tests: five passed.
- Python compilation, Ruff, formatting, and Compose interpolation checks passed.
- `actionlint` and YAML parsing pass for the advanced CodeQL workflow and
  configuration.

Alert closure requires a successful GitHub scan after these changes reach a
branch analyzed by the advanced workflow.

## References

- [Resolving code scanning alerts](https://docs.github.com/en/code-security/how-tos/manage-security-alerts/manage-code-scanning-alerts/resolve-alerts)
- [Customizing CodeQL library models for C#](https://codeql.github.com/docs/codeql-language-guides/customizing-library-models-for-csharp/)
- [CWE-117: Improper Output Neutralization for Logs](https://cwe.mitre.org/data/definitions/117.html)
- [CWE-312: Cleartext Storage of Sensitive Information](https://cwe.mitre.org/data/definitions/312.html)
- [CWE-359: Exposure of Private Personal Information](https://cwe.mitre.org/data/definitions/359.html)
- [OWASP Log Injection](https://owasp.org/www-community/attacks/Log_Injection)
