# Pull Request Template

## Summary

Please provide a brief description of the changes in this PR.

## Type of Change

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to change)
- [ ] Security fix (addresses findings from security review)
- [ ] Documentation update
- [ ] Refactoring (no functional changes)
- [ ] CI/CD changes

## Testing

Please describe the tests you have added or modified. Ensure that `dotnet test` passes locally before submitting.

## Remediation Guardrails

**For security remediation PRs only:**

- [ ] No edits under `tests/**` (existing test files are not modified)
- [ ] `dotnet test` passes locally
- [ ] No insecure defaults introduced (fail-closed for missing config in non-dev environments)
- [ ] Security-sensitive changes reviewed (CORS/auth/crypto/file paths/outbound URLs)
- [ ] New tests added as separate files (not modifying existing tests)

## Checklist

- [ ] My code follows the project's coding conventions
- [ ] I have performed a self-review of my code
- [ ] I have commented my code, particularly in hard-to-understand areas
- [ ] I have made corresponding changes to the documentation
- [ ] My changes generate no new warnings
- [ ] I have added tests that prove my fix is effective or that my feature works
- [ ] New and existing unit tests pass locally with my changes
- [ ] Any dependent changes have been merged and published in downstream modules

## Related Issues

Link any related issues using GitHub keywords (e.g., `closes #123`, `fixes #456`).

## Additional Notes

Add any additional context or information that reviewers should be aware of.
