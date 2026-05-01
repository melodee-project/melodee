---
description: 'Dependency injection patterns and anti-patterns for .NET services'
applyTo: '**/*.cs'
---

# Dependency Injection Guidelines

## Overview

This project uses .NET's built-in dependency injection. All services must be properly registered and injected - never manually instantiated within consuming classes.

## Concrete Classes vs Interfaces for Melodee.Common.Services

### Prefer Concrete Classes Over Interfaces

**IMPORTANT**: For internal application services in `Melodee.Common.Services`, prefer injecting concrete classes directly rather than creating interfaces.

✅ **PREFERRED - Concrete Class:**
```csharp
// Service definition - no interface needed
public class UserService
{
    public UserService(
        ILogger logger,
        AlbumService albumService,      // Concrete class
        SongService songService)        // Concrete class
    {
        _albumService = albumService;
        _songService = songService;
    }
}

// DI Registration
services.AddScoped<UserService>();
services.AddScoped<AlbumService>();
services.AddScoped<SongService>();
```

❌ **AVOID - Unnecessary Interface:**
```csharp
// Don't create interfaces for single-implementation internal services
public interface IUserService { /* mirrors UserService exactly */ }
public class UserService : IUserService { }

services.AddScoped<IUserService, UserService>();  // Unnecessary abstraction
```

### When Interfaces ARE Appropriate

Create interfaces only when there's a genuine reason:

| Use Interface When | Example |
|-------------------|---------|
| Multiple implementations exist | `ISerializer` → `JsonSerializer`, `XmlSerializer` |
| External plugin/extension point | `ILyricPlugin`, `IMetaTagPlugin` |
| .NET framework requirement | `IHostedService`, `IDisposable` |
| Genuinely different behaviors | `IPasswordHashService` (could have Argon2, BCrypt variants) |
| Third-party library contracts | `ILogger<T>`, `IConfiguration` |

### Why Concrete Classes for Internal Services?

1. **KISS Principle**: One class = one file to maintain, not two
2. **IDE Navigation**: Go directly to implementation, no extra hop
3. **Refactoring**: Change one file, not interface + implementation
4. **Honest Design**: Interface with single implementation is speculative abstraction
5. **Testing**: Modern .NET supports mocking concrete classes with virtual methods, or use real services with in-memory databases

### Services Using Concrete Classes (No Interfaces)

These `Melodee.Common.Services` classes are registered directly without interfaces:

- `UserService`
- `UserProfileService`
- `UserAuthenticationService`
- `UserQueueService`
- `AlbumService`
- `ArtistService`
- `ArtistDuplicateFinder`
- `SongService`
- `LibraryService`
- `PlaylistService`
- `PodcastService`
- `StatisticsService`
- `ScrobbleService`
- `PartySessionService`
- `PartyQueueService`
- `PartyPlaybackService`
- `PartySessionEndpointRegistryService`

### Existing Interfaces That Are Appropriate

These interfaces are justified because they represent genuine abstractions:

- `ISerializer` - Multiple formats (JSON, XML)
- `IPasswordHashService` - Security implementations may vary
- `IOpenSubsonicSecretProtector` - Security abstraction
- `IMelodeeConfigurationFactory` - Configuration abstraction
- `IBlacklistService` - Could have different storage backends
- `IPartyNotificationService` - Cross-project abstraction (defined in Common, implemented in Blazor)
- `ICacheManager` - Infrastructure abstraction with factory pattern
- `IFileSystemService` - Infrastructure/testability abstraction
- `IPlaybackBackend` - Plugin/extension interface
- Plugin interfaces (`ILyricPlugin`, `IMetaTagPlugin`, etc.)

### Testing Without Interfaces

**Option 1: Use Real Services with In-Memory Database (Preferred)**
```csharp
[Fact]
public async Task GetUser_WithValidId_ReturnsUser()
{
    // Real service, real behavior, in-memory database
    var userService = new UserService(
        logger,
        inMemoryDbContextFactory,
        configFactory,
        /* real dependencies */);
    
    var result = await userService.GetAsync(1);
    Assert.True(result.IsSuccess);
}
```

**Option 2: Virtual Methods for Partial Mocking**
```csharp
public class UserService
{
    public virtual async Task<User?> GetAsync(int id) { /* ... */ }
}

// In tests - mock specific methods if needed
var mockService = Substitute.ForPartsOf<UserService>(/* args */);
mockService.GetAsync(Arg.Any<int>()).Returns(testUser);
```

## CRITICAL RULES - NEVER VIOLATE

### 1. NEVER Use Nullable Constructor Parameters for Dependencies

**ABSOLUTE RULE**: Constructor parameters for injected services must NEVER be nullable with default values.

❌ **NEVER DO THIS:**
```csharp
public class UserService
{
    public UserService(
        ILogger logger,
        PasswordHashService? passwordHashService = null,  // WRONG!
        EmailService? emailService = null)                // WRONG!
    {
        // Creating fallback instances - NEVER DO THIS
        _passwordHashService = passwordHashService ?? new PasswordHashService();
        _emailService = emailService ?? new EmailService();
    }
}
```

✅ **ALWAYS DO THIS:**
```csharp
public class UserService
{
    public UserService(
        ILogger logger,
        PasswordHashService passwordHashService,  // Required, non-nullable
        EmailService emailService)                // Required, non-nullable
    {
        _passwordHashService = passwordHashService;
        _emailService = emailService;
    }
}
```

**Why?**:
- Nullable dependencies hide the true requirements of a class
- Manual instantiation violates the Dependency Inversion Principle
- Makes unit testing difficult - can't easily substitute dependencies
- Creates hidden coupling between classes
- DI container should fail fast if a service isn't registered

### 2. NEVER Manually Instantiate Services Inside Other Services

**ABSOLUTE RULE**: Services must receive all dependencies through constructor injection, never create them internally.

❌ **NEVER DO THIS:**
```csharp
public class OrderService
{
    private readonly PaymentService _paymentService;
    
    public OrderService(PaymentService? paymentService = null)
    {
        // WRONG: Manual instantiation
        _paymentService = paymentService ?? new PaymentService(
            new Logger(),
            new PaymentGateway(),
            new ConfigurationService());
    }
}
```

✅ **ALWAYS DO THIS:**
```csharp
public class OrderService
{
    private readonly PaymentService _paymentService;
    
    public OrderService(PaymentService paymentService)
    {
        _paymentService = paymentService;
    }
}

// In Program.cs or service registration:
services.AddScoped<PaymentService>();
services.AddScoped<OrderService>();
```

**Why?**:
- The DI container manages lifetimes (Singleton, Scoped, Transient)
- Manual instantiation bypasses lifetime management
- Creates tight coupling and makes refactoring difficult
- Hides the dependency graph from the composition root

### 3. ALL Services Must Be Registered in DI Container

**ABSOLUTE RULE**: Every service must be registered in the DI composition roots.

**Melodee has TWO composition roots that must be kept in sync:**

| Project | Location | Purpose |
|---------|----------|---------|
| `Melodee.Blazor` | `src/Melodee.Blazor/Program.cs` | Web application DI setup |
| `Melodee.Cli` | `src/Melodee.Cli/Command/CommandBase.cs` | CLI application DI setup |

When adding a new service to `Melodee.Common.Services`, you **MUST** register it in **BOTH** locations.

```csharp
// In BOTH Program.cs (Blazor) AND CommandBase.cs (CLI)
services.AddScoped<UserService>();
services.AddScoped<AlbumService>();
services.AddScoped<ArtistService>();
services.AddSingleton<IPasswordHashService, PasswordHashService>();  // Interface justified
```

**Why both?**
- `Melodee.Blazor` is the web server application
- `Melodee.Cli` is the command-line tool for administration and batch operations
- Both consume services from `Melodee.Common.Services`
- Missing registrations in either will cause runtime failures

### 4. Use Guard Clauses for Required Dependencies

**BEST PRACTICE**: Use guard clauses to fail fast if dependencies are null (though with proper DI, they never should be).

```csharp
public class UserService
{
    private readonly PasswordHashService _passwordHashService;
    
    public UserService(PasswordHashService passwordHashService)
    {
        _passwordHashService = Guard.Against.Null(passwordHashService);
    }
}
```

## Dependency Lifetime Guidelines

### Singleton
- Configuration services
- Caching services (ICacheManager)
- Services with no per-request state

### Scoped (Default for most services)
- Database contexts and context factories
- Services that need per-request state
- Services that depend on scoped services

### Transient
- Lightweight, stateless services
- Services that should never be shared

```csharp
// Examples
services.AddSingleton<ICacheManager, CacheManager>();
services.AddScoped<UserService>();
services.AddTransient<IPasswordHashService, PasswordHashService>();
```

## Code Review Checklist

Before approving any PR, verify:

- [ ] No nullable constructor parameters with `= null` defaults for services
- [ ] No manual `new ServiceName()` instantiation inside service constructors
- [ ] All new services are registered in **BOTH** DI composition roots:
  - [ ] `src/Melodee.Blazor/Program.cs`
  - [ ] `src/Melodee.Cli/Command/CommandBase.cs`
- [ ] Concrete classes used for single-implementation internal services
- [ ] Interfaces only created when genuinely needed (multiple implementations, plugins, external contracts)
- [ ] Appropriate lifetime (Singleton/Scoped/Transient) is chosen
- [ ] Guard clauses used for constructor parameters where appropriate

## Common Anti-Patterns to Reject

### Speculative Interface
```csharp
// WRONG: Interface with single implementation "just in case"
public interface IUserService { /* exact copy of UserService */ }
public class UserService : IUserService { }
```

### Service Locator Pattern
```csharp
// WRONG: Don't use service locator
public class BadService
{
    public void DoWork()
    {
        var service = ServiceLocator.Get<UserService>();  // Anti-pattern!
    }
}
```

### Ambient Context / Static Access
```csharp
// WRONG: Don't use static service access
public class BadService
{
    public void DoWork()
    {
        var hash = PasswordHashService.Instance.Hash(password);  // Anti-pattern!
    }
}
```

### Optional Dependencies via Null
```csharp
// WRONG: Don't make dependencies optional
public BadService(ILogger logger, AlbumService? albumService = null)
{
    _albumService = albumService;  // Now you need null checks everywhere
}
```

## Summary

**The Golden Rules**: 
> 1. If a class needs a service to function, that service must be a required constructor parameter and registered in the DI container.
> 2. Use concrete classes for internal single-implementation services. Interfaces are for genuine abstractions.

**Four Cardinal Sins**:
1. ❌ Nullable service parameters with defaults
2. ❌ Manual `new Service()` inside constructors
3. ❌ Service locator or static service access
4. ❌ Creating interfaces for single-implementation internal services

**Always Remember**:
- DI container is the single source of truth for service resolution
- Constructor parameters declare the contract - make dependencies explicit
- Fail fast at startup if dependencies aren't registered
- Interfaces should earn their place - don't create them speculatively
