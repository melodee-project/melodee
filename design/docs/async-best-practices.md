# Async Best Practices

This is internal engineering guidance for Melodee contributors.

## Guidelines

- Avoid `.Result`, `.Wait()`, and `GetAwaiter().GetResult()` in request threads and service code.
- Prefer end-to-end asynchronous flows with `await`.
- Use `ConfigureAwait(false)` in reusable library code where the surrounding project conventions call for it.
- Do not block inside dependency-injection registrations; resolve asynchronous dependencies at runtime.
- Use `Task.Run` sparingly. Prefer genuinely asynchronous I/O APIs over moving blocking I/O to the thread pool.
- Accept and propagate `CancellationToken` for operations that may block or perform I/O.

## Exceptions

- Generated code or APIs that require synchronous contracts may be excluded with a clear justification.
- UI components with purely synchronous work should not add asynchronous plumbing without a concrete need.
- A deliberate synchronous boundary must not block an ASP.NET request thread on asynchronous work.

