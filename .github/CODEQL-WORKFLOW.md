# CodeQL Workflow Configuration

Melodee uses one version-controlled CodeQL advanced setup. The workflow in
`.github/workflows/codeql.yml` uploads results for every supported application
language to GitHub code scanning.

## Scanning Coverage

The workflow runs for pushes and pull requests targeting `main`, every Sunday
at 00:00 UTC, and on manual dispatch. Its matrix analyzes:

- GitHub Actions workflows using the interpreted `none` build mode.
- C# using `autobuild`, so CodeQL observes the compiled solution.
- JavaScript and TypeScript using the interpreted `none` build mode.
- Python using the interpreted `none` build mode and its local threat model.

The shared configuration in `.github/codeql/codeql-config.yml` excludes
documentation, tests, benchmarks, and vendored minified JavaScript from
interpreted-language analysis. C#, JavaScript/TypeScript, and Actions use the
default remote threat model. Python uses
`.github/codeql/codeql-python-config.yml`, which extends the default model with
local files, command-line arguments, environment variables, and other local
sources used by maintenance scripts. Neither configuration excludes C#
findings by rule.

The local models are intentionally narrow. They identify
`LogSanitizer.Sanitize` as a barrier only for the `log-injection` flow kind and
`ConfigurationLogRedactor.RedactValue` as a barrier only when values would be
persisted to an external location such as a log. They are packaged under
`.github/codeql/extensions/log-sanitizer` so advanced and default setup can
discover them. The `.yml` suffix is intentional because the pack manifest only
loads model files matching `models/**/*.yml`.
Repository-wide query exclusions must not be added to hide a false positive.
Prefer a source fix or a precise CodeQL model; dismiss an individual alert only
when neither option accurately represents the behavior.

All workflow actions are pinned to verified full commit SHAs. Keep the release
comments beside those SHAs when updating the pins so reviews can identify the
corresponding upstream release.

## Required Repository Configuration

GitHub default setup must remain disabled while this advanced workflow is
active. Enabling both configurations can disable advanced uploads, duplicate
work, and leave alert status attached to a configuration that no longer runs.

To verify the setting:

1. Open the repository settings and select **Advanced Security** or
   **Code security and analysis**.
2. Find **CodeQL analysis** under code scanning.
3. Confirm default setup is disabled and the checked-in advanced workflow is
   enabled.

The REST API reports the same state through:

```shell
gh api repos/melodee-project/melodee/code-scanning/default-setup
```

The expected `state` is `not-configured`.

## Removing a Stale Configuration

An alert can remain open after its code is fixed when it belongs to an older
configuration that no longer runs. Adding the language to this workflow does
not update the stale configuration's result.

After the advanced workflow has completed successfully on `main`:

1. Open **Security and quality** > **Code scanning**.
2. Open **Tool status**, or open the alert and inspect **Affected branches** >
   **Configurations analyzing** for `main`.
3. Retain `.github/workflows/codeql.yml:analyze` for each matrix language.
4. Delete the stale `dynamic/github-code-scanning/codeql:analyze` and
   `dynamic/github-code-scanning/codeql:upload` configurations left by the
   former default setup. Retain every checked-in workflow configuration.
5. Re-run the CodeQL workflow if an active configuration was removed by
   mistake.

This administrative cleanup is required for findings that exist only in the
former default-setup configuration; a repository commit cannot delete that
server-side configuration.

## Verification

After changing the workflow or CodeQL configuration:

1. Validate `.github/workflows/codeql.yml` with `actionlint`.
2. Run the workflow manually, or push the change to a branch with a pull
   request targeting `main`.
3. Confirm all four matrix jobs complete and upload results.
4. On **Security and quality** > **Code scanning** > **Tool status**, confirm
   recent GitHub Actions, C#, JavaScript/TypeScript, and Python analyses are
   present.
5. Review new alerts before merging; do not dismiss them solely to make the
   check pass.

Useful command-line checks:

```shell
gh run list --workflow codeql.yml --limit 10
gh api --paginate \
  'repos/melodee-project/melodee/code-scanning/alerts?state=open&tool_name=CodeQL&per_page=100'
```

## References

- [Workflow configuration options for code scanning](https://docs.github.com/en/code-security/reference/code-scanning/workflow-configuration-options)
- [CodeQL for compiled languages](https://docs.github.com/en/code-security/how-tos/find-and-fix-code-vulnerabilities/manage-your-configuration/codeql-for-compiled-languages)
- [Resolving code scanning alerts](https://docs.github.com/en/code-security/how-tos/manage-security-alerts/manage-code-scanning-alerts/resolve-alerts)
- [Secure use of GitHub Actions](https://docs.github.com/en/actions/reference/security/secure-use)
- [Customizing CodeQL library models for C#](https://codeql.github.com/docs/codeql-language-guides/customizing-library-models-for-csharp/)
