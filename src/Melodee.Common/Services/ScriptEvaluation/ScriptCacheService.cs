using Jint;
using Melodee.Common.Services.Caching;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IScriptCacheService
{
    Task<Engine> GetOrCreateEngineAsync(string scriptBodyHash, string scriptBody, CancellationToken cancellationToken = default);
    void Invalidate(string scriptBodyHash);
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

    public async Task<Engine> GetOrCreateEngineAsync(string scriptBodyHash, string scriptBody, CancellationToken cancellationToken = default)
    {
        return await _cacheManager.GetAsync(
            scriptBodyHash,
            async () => await CreateEngineAsync(scriptBody, cancellationToken),
            cancellationToken,
            DefaultTtl,
            CacheRegion).ConfigureAwait(false);
    }

    public void Invalidate(string scriptBodyHash)
    {
        _cacheManager.Remove(scriptBodyHash, CacheRegion);
        _logger.Debug("Invalidated cached script with hash {ScriptHash}", scriptBodyHash);
    }

    public void InvalidateAll()
    {
        _cacheManager.ClearRegion(CacheRegion);
        _logger.Debug("Invalidated all cached scripts");
    }

    private static async Task<Engine> CreateEngineAsync(string scriptBody, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var engine = new Engine(options =>
            {
                options.Strict = true;
                options.MaxStatements(10000);
            });

            engine.Execute(scriptBody);
            return engine;
        }, cancellationToken).ConfigureAwait(false);
    }
}
