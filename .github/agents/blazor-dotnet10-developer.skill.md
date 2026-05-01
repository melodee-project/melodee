---
description: "Skill: Generate high-quality .NET 10 (C# 13) Blazor Server code for the Melodee solution."
name: "Blazor Server (.NET 10) developer skill"
applyTo: "**/*.razor, **/*.razor.cs, **/*.cs"
---

# Blazor Server (.NET 10) developer skill

Use this skill when generating or modifying UI and UI-adjacent code for Melodee.

## Mission

Create maintainable, secure, and performant Blazor Server features that follow Melodee’s architecture and conventions.

## Non-negotiables (Melodee-specific)

1. **Blazor components MUST NOT call internal backend HTTP APIs.**
   - Inject and use service classes directly.
   - `HttpClient` is only for third-party services (MusicBrainz, Spotify, etc.).

2. **Prefer existing services and patterns.**
   - If functionality doesn’t exist, add a service in the appropriate layer following existing conventions.

3. **Security-first by default.**
   - Validate all user input.
   - Don’t log secrets or sensitive user data.
   - Don’t introduce SSRF by blindly fetching user-provided URLs.

4. **Keep components thin.**
   - UI state + rendering in the component.
   - Business logic in services.

## Contract (what “good output” looks like)

- **Components**: readable markup, minimal logic, clear state transitions, async-safe.
- **Services**: DI-friendly, cancellation-token aware, testable, side-effect boundaries clear.
- **Data access**: no N+1 patterns; use efficient EF Core queries; paginate large results.
- **Error handling**: predictable, user-friendly messages; logs contain actionable context.
- **Tests**: at least one happy-path + one edge-case test for non-trivial behavior changes.

## Implementation guidelines

### 1) Component structure

- Prefer `.razor` + `.razor.cs` code-behind when logic grows.
- Use lifecycle methods intentionally:
  - `OnInitializedAsync` for initial load.
  - `OnParametersSetAsync` when parameters drive loading.
- Use `CancellationToken` to cancel in-flight work on dispose.

### 2) Dependency injection

- Prefer constructor injection for `.razor.cs` code-behind.
- Prefer `@inject` only when a component is markup-only or very small.

### 3) Async and rendering

- Use `async/await` end-to-end.
- Avoid `Task.Run` in Blazor UI code.
- Avoid excessive `StateHasChanged()`; rely on event-driven renders.

### 4) Forms and validation

- Use `EditForm` + `DataAnnotationsValidator` or FluentValidation (if already used in the solution).
- Validate at boundaries (input model validation before calling services).
- Prefer explicit user feedback over silent failures.

### 5) Authorization

- Use `AuthorizeView` for conditional UI.
- On operations, enforce authorization in the service/controller layer as well.
  - UI checks are not security.

### 6) Logging and errors

- Log with structured logging and include:
  - operation name
  - relevant IDs (artistId, albumId, etc.)
  - elapsed time when helpful
- Don’t log credentials/tokens/PII.

### 7) Performance

- Avoid repeated service calls during renders.
- Cache or memoize expensive calls when the data is stable.
- Use virtualization for long lists when applicable.

## “Do / Don’t” examples

### Don’t: call internal HTTP APIs from Blazor Server components

```razor
@* WRONG *@
@inject IHttpClientFactory HttpClientFactory

@code {
    private async Task LoadAsync()
    {
        var client = HttpClientFactory.CreateClient("MelodeeApi");
        var artist = await client.GetFromJsonAsync<ArtistDto>("/api/v1/artists/1");
    }
}
```

### Do: inject and use services directly

```razor
@* CORRECT *@
@inject ArtistService ArtistService

@code {
    private Artist? artist;

    protected override async Task OnInitializedAsync()
    {
        artist = await ArtistService.GetByIdAsync(1, CancellationToken.None);
    }
}
```

## When to escalate to another persona

- **Security-sensitive changes** (auth, tokens, external URLs, file paths): use the security reviewer.
- **Performance regressions suspected** (slow queries, rendering): use a performance mindset and profiling.
- **Ambiguous requirements**: write tests first (TDD red/green/refactor).
