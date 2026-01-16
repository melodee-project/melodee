using System.Globalization;
using System.Security.Claims;
using Bunit;
using FluentAssertions;
using Melodee.Blazor.Components.Onboarding;
using Melodee.Blazor.Components.Pages.Onboarding;
using Melodee.Blazor.Services;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Setup;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NodaTime;
using Serilog;

namespace Melodee.Tests.Blazor.Components.Onboarding;

public class OnboardingWizardTests : BunitContext, IDisposable
{
    private readonly Mock<ISetupCheckService> _setupCheckServiceMock;
    private readonly Mock<IMelodeeConfigurationFactory> _configFactoryMock;
    private readonly Mock<IDbContextFactory<MelodeeDbContext>> _dbContextFactoryMock;
    private readonly Mock<ILogger> _loggerMock;
    private readonly Mock<ICacheManager> _cacheManagerMock;
    private readonly TestLocalizationService _localizationService;
    private readonly DbContextOptions<MelodeeDbContext> _dbOptions;
    private readonly Bunit.TestDoubles.BunitAuthorizationContext _authContext;

    public OnboardingWizardTests()
    {
        // Reset static cache before each test to ensure clean state
        OnboardingStateService.ResetOnboardingCache();
        
        _setupCheckServiceMock = new Mock<ISetupCheckService>();
        _configFactoryMock = new Mock<IMelodeeConfigurationFactory>();
        _dbContextFactoryMock = new Mock<IDbContextFactory<MelodeeDbContext>>();
        _loggerMock = new Mock<ILogger>();
        _cacheManagerMock = new Mock<ICacheManager>();
        _localizationService = new TestLocalizationService();
        _dbOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseInMemoryDatabase($"OnboardingWizardTests_{Guid.NewGuid()}")
            .Options;

        Services.AddSingleton(_setupCheckServiceMock.Object);
        Services.AddSingleton(_configFactoryMock.Object);
        Services.AddSingleton(_dbContextFactoryMock.Object);
        Services.AddSingleton(_loggerMock.Object);
        Services.AddSingleton(_cacheManagerMock.Object);
        Services.AddSingleton<ILocalizationService>(_localizationService);
        Services.AddSingleton<AuthenticationStateProvider>(new TestAuthStateProvider());
        Services.AddSingleton<Radzen.DialogService>();
        Services.AddSingleton<Radzen.NotificationService>();
        Services.AddSingleton<OnboardingStateService>(CreateOnboardingStateService());

        // Add bUnit authorization support for AuthorizeView component
        _authContext = this.AddAuthorization();
        // Set user as authorized for tests that need it
        _authContext.SetAuthorized("TestUser");

        // Add mock SettingService required by OnboardingBranding component
        var mockSettingService = new Mock<SettingService>();
        Services.AddSingleton(mockSettingService.Object);

        Services.AddSingleton<NavigationManager>(new TestNavigationManager());

        JSInterop.Mode = JSRuntimeMode.Loose;

        SetupDbContextFactory();
    }

    public new void Dispose()
    {
        using var context = new MelodeeDbContext(_dbOptions);
        context.Database.EnsureDeleted();
        base.Dispose();
    }

    [Fact]
    public void OnboardingGuard_WhenRequired_RedirectsToWizard()
    {
        var config = new MelodeeConfiguration(new Dictionary<string, object?>
        {
            [SettingRegistry.SystemOnboardingCompletedAt] = ""
        });

        var status = new SetupStatus(
            IsReady: true,
            Items: [],
            BlockingItems: [],
            CheckedAt: DateTimeOffset.UtcNow);

        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var navManager = Services.GetRequiredService<NavigationManager>() as TestNavigationManager;
        var cut = Render<OnboardingGuard>(parameters => parameters
            .AddChildContent("<div id=\"content\"></div>"));

        cut.WaitForAssertion(() => navManager!.Uri.Should().EndWith("/onboarding"));
    }

    [Fact]
    public void OnboardingGuard_WhenCompletedAndReady_DoesNotRedirect()
    {
        var config = new MelodeeConfiguration(new Dictionary<string, object?>
        {
            [SettingRegistry.SystemOnboardingCompletedAt] = "2024-01-01T00:00:00Z"
        });

        var status = new SetupStatus(
            IsReady: true,
            Items: [],
            BlockingItems: [],
            CheckedAt: DateTimeOffset.UtcNow);

        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var navManager = Services.GetRequiredService<NavigationManager>() as TestNavigationManager;
        var cut = Render<OnboardingGuard>(parameters => parameters
            .AddChildContent("<div id=\"content\"></div>"));

        cut.Markup.Should().Contain("content");
        navManager!.Uri.Should().Be("http://localhost/");
    }

    [Fact]
    public void BlockingPage_RendersRetryAndSupportLink()
    {
        var status = new SetupStatus(
            IsReady: false,
            Items: [],
            BlockingItems:
            [
                new SetupItem("blocking", "Blocking item", SetupCheckSeverity.Blocking, false, "Details")
            ],
            CheckedAt: DateTimeOffset.UtcNow);

        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var cut = Render<Blocking>();

        cut.Markup.Should().Contain(_localizationService.Localize("Onboarding.RetryButton"));
        cut.Markup.Should().Contain(_localizationService.Localize("Onboarding.SupportLink"));
    }

    [Fact]
    public void OnboardingWizard_NextAdvancesStep()
    {
        var config = new MelodeeConfiguration(new Dictionary<string, object?>
        {
            [SettingRegistry.SystemOnboardingCompletedAt] = ""
        });

        var status = new SetupStatus(
            IsReady: false,
            Items: [],
            BlockingItems: [],
            CheckedAt: DateTimeOffset.UtcNow);

        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var cut = Render<Melodee.Blazor.Components.Pages.Onboarding.Index>();

        cut.Markup.Should().Contain("Step 1 of 8");

        var nextButton = cut.FindAll("button")
            .First(button => button.TextContent.Contains("Common.Next", StringComparison.OrdinalIgnoreCase));
        nextButton.Click();

        cut.Markup.Should().Contain("Step 2 of 8");
    }

    [Fact]
    public async Task OnboardingVerify_DownloadChecklist_UsesLocalizedFileName()
    {
        await SeedLibrariesAsync();

        var config = new MelodeeConfiguration(new Dictionary<string, object?>
        {
            [SettingRegistry.SystemBaseUrl] = "https://example.com",
            [SettingRegistry.SystemSiteName] = "Melodee",
            [SettingRegistry.SecuritySecretKey] = new string('a', 32),
            [SettingRegistry.SystemOnboardingCompletedAt] = "2024-01-01T00:00:00Z"
        });

        var status = new SetupStatus(
            IsReady: true,
            Items: [],
            BlockingItems: [],
            CheckedAt: DateTimeOffset.UtcNow);

        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);
        _setupCheckServiceMock.Setup(x => x.SetupCheckAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(status);

        var checklistService = new ChecklistService(
            _configFactoryMock.Object,
            _cacheManagerMock.Object,
            _dbContextFactoryMock.Object,
            new Mock<IHostEnvironment>().Object,
            _localizationService);
        Services.AddSingleton(checklistService);

        var expectedPrefix = "melodee-checklist-";
        JSInterop.SetupVoid("downloadFile", args =>
        {
            var fileName = args.Arguments[0] as string;
            fileName.Should().NotBeNull();
            fileName.Should().StartWith(expectedPrefix);
            fileName.Should().EndWith(".md");
            return true;
        });

        var cut = Render<Melodee.Blazor.Components.Onboarding.OnboardingVerify>(parameters => parameters
            .Add(p => p.OnBack, EventCallback.Factory.Create(this, () => { }))
            .Add(p => p.OnComplete, EventCallback.Factory.Create(this, () => { })));

        var downloadButton = cut.FindAll("button")
            .First(button => button.TextContent.Contains("Onboarding.DownloadChecklistButton", StringComparison.OrdinalIgnoreCase));
        await downloadButton.ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
    }

    private OnboardingStateService CreateOnboardingStateService()
    {
        return new OnboardingStateService(
            _setupCheckServiceMock.Object,
            _configFactoryMock.Object,
            _dbContextFactoryMock.Object,
            _loggerMock.Object,
            _cacheManagerMock.Object);
    }

    private void SetupDbContextFactory()
    {
        _dbContextFactoryMock.Setup(x => x.CreateDbContext())
            .Returns(() => new MelodeeDbContext(_dbOptions));
        _dbContextFactoryMock.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MelodeeDbContext(_dbOptions));
    }

    private async Task SeedLibrariesAsync()
    {
        await using var context = new MelodeeDbContext(_dbOptions);
        context.Libraries.AddRange(
            CreateLibrary(LibraryType.Inbound, "/tmp/inbound"),
            CreateLibrary(LibraryType.Staging, "/tmp/staging"),
            CreateLibrary(LibraryType.Storage, "/tmp/storage"));
        await context.SaveChangesAsync();
    }

    private static Library CreateLibrary(LibraryType type, string path)
    {
        return new Library
        {
            Name = type.ToString(),
            Path = path,
            Type = (int)type,
            SortOrder = (int)type,
            ApiKey = Guid.NewGuid(),
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };
    }

    private sealed class TestAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _user = new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "tester"),
            new Claim(ClaimTypes.Role, "Administrator")
        }, "test"));

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(_user));
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        private readonly Dictionary<string, string> _strings = new()
        {
            ["Onboarding.RetryButton"] = "Retry",
            ["Onboarding.SupportLink"] = "View documentation",
            ["Onboarding.ChecklistFileName"] = "melodee-checklist-{0:yyyy-MM-dd}.md",
            ["Onboarding.ChecklistTemplate"] = "Checklist for {1} at {0}"
        };

        public CultureInfo CurrentCulture { get; private set; } = new("en-US");
        public IReadOnlyList<CultureInfo> SupportedCultures { get; } = [new CultureInfo("en-US")];
        public event Action<CultureInfo>? CultureChanged;

        public string Localize(string key) => _strings.TryGetValue(key, out var value) ? value : key;

        public string Localize(string key, string fallback)
            => _strings.TryGetValue(key, out var value) ? value : fallback;

        public string Localize(string key, params object[] args)
            => string.Format(CurrentCulture, Localize(key), args);

        public Task SetCultureAsync(CultureInfo culture)
        {
            CurrentCulture = culture;
            CultureChanged?.Invoke(culture);
            return Task.CompletedTask;
        }

        public Task SetCultureAsync(string cultureCode)
            => SetCultureAsync(new CultureInfo(cultureCode));

        public Task<CultureInfo> GetUserCultureAsync()
            => Task.FromResult(CurrentCulture);

        public string FormatDate(DateTime date, string? format = null)
            => date.ToString(format ?? "d", CurrentCulture);

        public string FormatNumber(decimal number, string? format = null)
            => number.ToString(format ?? "G", CurrentCulture);

        public bool IsRightToLeft() => false;

        public string GetTextDirection() => "ltr";
    }

    private sealed class TestNavigationManager : NavigationManager
    {
        public TestNavigationManager()
        {
            Initialize("http://localhost/", "http://localhost/");
        }

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
