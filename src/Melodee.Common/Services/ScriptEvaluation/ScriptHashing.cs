using System.Security.Cryptography;
using System.Text;

namespace Melodee.Common.Services.ScriptEvaluation;

public static class ScriptHashing
{
    public static string Sha256Hex(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
