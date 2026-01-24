using Jint;
using Script = Acornima.Ast.Script;
using Melodee.Common.Services.Caching;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IScriptCacheService
{
    Task<Prepared<Script>> GetOrCreatePreparedScriptAsync(string cacheKey, string scriptBody, CancellationToken cancellationToken = default);
    void Invalidate(string cacheKey);
    void InvalidateAll();
}

public sealed class ScriptCacheService : IScriptCacheService
{
    private const string CacheRegion = "scripts";
    private readonly ICacheManager _cacheManager;
    private readonly ILogger _logger;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    public ScriptCacheService(
        ICacheManager cacheManager,
        ILogger logger)
    {
        _cacheManager = cacheManager;
        _logger = logger;
    }

    public async Task<Prepared<Script>> GetOrCreatePreparedScriptAsync(string cacheKey, string scriptBody, CancellationToken cancellationToken = default)
    {
        return await _cacheManager.GetAsync(
            cacheKey,
            async () => await CreatePreparedScriptAsync(scriptBody, cancellationToken),
            cancellationToken,
            DefaultTtl,
            CacheRegion).ConfigureAwait(false);
    }

    public void Invalidate(string cacheKey)
    {
        _cacheManager.Remove(cacheKey, CacheRegion);
        _logger.Debug("Invalidated cached script with key {CacheKey}", cacheKey);
    }

    public void InvalidateAll()
    {
        _cacheManager.ClearRegion(CacheRegion);
        _logger.Debug("Invalidated all cached scripts");
    }

    private static async Task<Prepared<Script>> CreatePreparedScriptAsync(string scriptBody, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            return Engine.PrepareScript(scriptBody, null, true, null);
        }, cancellationToken).ConfigureAwait(false);
    }
}
