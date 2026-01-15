---
description: 'Dependency injection patterns and anti-patterns for .NET services'
applyTo: '**/*.cs'
---

# Dependency Injection Guidelines

## Overview

This project uses .NET's built-in dependency injection. All services must be properly registered and injected - never manually instantiated within consuming classes.

## CRITICAL RULES - NEVER VIOLATE

### 1. NEVER Use Nullable Constructor Parameters for Dependencies

**ABSOLUTE RULE**: Constructor parameters for injected services must NEVER be nullable with default values.

❌ **NEVER DO THIS:**
```csharp
public class UserService
{
    public UserService(
        ILogger logger,
        IPasswordHashService? passwordHashService = null,  // WRONG!
        IEmailService? emailService = null)                // WRONG!
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
        IPasswordHashService passwordHashService,  // Required, non-nullable
        IEmailService emailService)                // Required, non-nullable
    {
        _passwordHashService = passwordHashService;
        _emailService = emailService;
    }
}
```

**Why?**:
- Nullable dependencies hide the true requirements of a class
- Manual instantiation violates the Dependency Inversion Principle
- Makes unit testing difficult - can't easily mock dependencies
- Creates hidden coupling between classes
- DI container should fail fast if a service isn't registered

### 2. NEVER Manually Instantiate Services Inside Other Services

**ABSOLUTE RULE**: Services must receive all dependencies through constructor injection, never create them internally.

❌ **NEVER DO THIS:**
```csharp
public class OrderService
{
    private readonly IPaymentService _paymentService;
    
    public OrderService(IPaymentService? paymentService = null)
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
    private readonly IPaymentService _paymentService;
    
    public OrderService(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }
}

// In Program.cs or service registration:
services.AddScoped<IPaymentService, PaymentService>();
services.AddScoped<OrderService>();
```

**Why?**:
- The DI container manages lifetimes (Singleton, Scoped, Transient)
- Manual instantiation bypasses lifetime management
- Creates tight coupling and makes refactoring difficult
- Hides the dependency graph from the composition root

### 3. ALL Services Must Be Registered in DI Container

**ABSOLUTE RULE**: Every service interface and implementation must be registered in the DI composition roots.

**Melodee has TWO composition roots that must be kept in sync:**

| Project | Location | Purpose |
|---------|----------|---------|
| `Melodee.Blazor` | `src/Melodee.Blazor/Program.cs` | Web application DI setup |
| `Melodee.Cli` | `src/Melodee.Cli/Command/CommandBase.cs` | CLI application DI setup |

When adding a new service to `Melodee.Common.Services`, you **MUST** register it in **BOTH** locations.

```csharp
// In BOTH Program.cs (Blazor) AND CommandBase.cs (CLI)
services.AddScoped<IPasswordHashService, PasswordHashService>();
services.AddScoped<IOpenSubsonicSecretProtector, OpenSubsonicSecretProtector>();
services.AddScoped<IUserAuthenticationService, UserAuthenticationService>();
services.AddScoped<IUserProfileService, UserProfileService>();
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
    private readonly IPasswordHashService _passwordHashService;
    
    public UserService(IPasswordHashService passwordHashService)
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
services.AddScoped<IUserService, UserService>();
services.AddTransient<IPasswordHashService, PasswordHashService>();
```

## Interface Segregation

### Define Interfaces for All Services

Every service should have an interface:

```csharp
// Interface in Melodee.Common/Services/Interfaces or alongside the service
public interface IPasswordHashService
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

// Implementation
public sealed class PasswordHashService : IPasswordHashService
{
    public string Hash(string password) { /* ... */ }
    public bool Verify(string password, string hash) { /* ... */ }
}
```

### Inject Interfaces, Not Implementations

❌ **AVOID:**
```csharp
public UserService(PasswordHashService passwordHashService)  // Concrete type
```

✅ **PREFER:**
```csharp
public UserService(IPasswordHashService passwordHashService)  // Interface
```

## Testing Implications

Proper DI enables easy mocking:

```csharp
[Fact]
public async Task LoginUser_WithValidPassword_ReturnsUser()
{
    // Arrange
    var mockPasswordHash = Substitute.For<IPasswordHashService>();
    mockPasswordHash.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
    
    var service = new UserAuthenticationService(
        logger,
        mockPasswordHash,  // Easy to mock when not nullable
        mockSecretProtector,
        bus,
        userProfileService,
        configFactory);
    
    // Act & Assert
    var result = await service.LoginUserAsync("test@test.com", "password");
    Assert.True(result.IsSuccess);
}
```

## Code Review Checklist

Before approving any PR, verify:

- [ ] No nullable constructor parameters with `= null` defaults for services
- [ ] No manual `new ServiceName()` instantiation inside service constructors
- [ ] All new services are registered in **BOTH** DI composition roots:
  - [ ] `src/Melodee.Blazor/Program.cs`
  - [ ] `src/Melodee.Cli/Command/CommandBase.cs`
- [ ] Services depend on interfaces, not concrete implementations
- [ ] Appropriate lifetime (Singleton/Scoped/Transient) is chosen
- [ ] Guard clauses used for constructor parameters where appropriate

## Common Anti-Patterns to Reject

### Service Locator Pattern
```csharp
// WRONG: Don't use service locator
public class BadService
{
    public void DoWork()
    {
        var service = ServiceLocator.Get<IPasswordHashService>();  // Anti-pattern!
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
public BadService(ILogger logger, ICache? cache = null)
{
    _cache = cache;  // Now you need null checks everywhere
}
```

## Migration Strategy for Existing Code

When fixing existing nullable DI patterns:

1. Register the service in **BOTH** DI composition roots:
   - `src/Melodee.Blazor/Program.cs`
   - `src/Melodee.Cli/Command/CommandBase.cs`
2. Remove the `?` and `= null` from constructor parameter
3. Remove any fallback instantiation code (`?? new ServiceName()`)
4. Remove null checks when using the service
5. Update all callers if manually constructing the service
6. Add/update unit tests to verify proper injection

## Summary

**The Golden Rule**: 
> If a class needs a service to function, that service must be a required constructor parameter and registered in the DI container. No exceptions.

**Three Cardinal Sins**:
1. ❌ Nullable service parameters with defaults
2. ❌ Manual `new Service()` inside constructors
3. ❌ Service locator or static service access

**Always Remember**:
- DI container is the single source of truth for service resolution
- Constructor parameters declare the contract - make dependencies explicit
- Fail fast at startup if dependencies aren't registered, not at runtime with NullReferenceException
