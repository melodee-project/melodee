using System.Security.Cryptography;
using System.Text;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;

namespace Melodee.Common.Services.Security;

/// <summary>
/// Protects secrets (e.g., user tokens/passwords) using AES-GCM.
/// </summary>
/// <remarks>
/// This service requires <c>security.secretKey</c> to be present in the application's settings.
/// The value must be at least 32 characters and should be provided via a secret source (environment variable,
/// database setting, Kubernetes secret, etc.), not hardcoded.
///
/// Examples:
/// <list type="bullet">
/// <item>
/// <description>Database setting: <c>security.secretKey</c></description>
/// </item>
/// <item>
/// <description>Environment variable: <c>security_secretKey</c></description>
/// </item>
/// </list>
/// </remarks>
public sealed class SecretProtector : ISecretProtector
{
    private const string Prefix = "v1:gcm:";
    private const int NonceLength = 12;
    private const int TagLength = 16;
    private readonly byte[] _key;

    /// <summary>
    /// Creates a new <see cref="SecretProtector"/>.
    /// </summary>
    /// <param name="configurationFactory">Melodee configuration factory.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <c>security.secretKey</c> is missing or does not meet the minimum length requirement.
    /// </exception>
    public SecretProtector(IMelodeeConfigurationFactory configurationFactory)
    {
        // Example to generate a random key:
        // openssl rand -base64 48 | tr -d '\n
        var configuration = configurationFactory.GetConfigurationAsync().GetAwaiter().GetResult();
        var configKey = configuration.GetValue<string>(SettingRegistry.SecuritySecretKey)
                        ?? throw new InvalidOperationException($"{SettingRegistry.SecuritySecretKey} configuration is required");

        if (configKey.Length < 32)
        {
            throw new InvalidOperationException($"{SettingRegistry.SecuritySecretKey} must be at least 32 characters");
        }

        _key = SHA256.HashData(Encoding.UTF8.GetBytes(configKey));
    }

    public string Protect(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            throw new ArgumentException("Secret cannot be null or empty", nameof(secret));
        }

        var nonce = new byte[NonceLength];
        RandomNumberGenerator.Fill(nonce);

        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[secretBytes.Length];
        var tag = new byte[TagLength];

        using var aesGcm = new AesGcm(_key, TagLength);
        aesGcm.Encrypt(nonce, secretBytes, ciphertext, tag);

        var combined = new byte[NonceLength + TagLength + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceLength);
        Buffer.BlockCopy(tag, 0, combined, NonceLength, TagLength);
        Buffer.BlockCopy(ciphertext, 0, combined, NonceLength + TagLength, ciphertext.Length);

        return Prefix + Convert.ToBase64String(combined);
    }

    public string Unprotect(string protectedData)
    {
        if (string.IsNullOrEmpty(protectedData))
        {
            throw new ArgumentException("Protected data cannot be null or empty", nameof(protectedData));
        }

        if (!protectedData.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid protected data format", nameof(protectedData));
        }

        var combined = Convert.FromBase64String(protectedData[Prefix.Length..]);

        if (combined.Length < NonceLength + TagLength)
        {
            throw new ArgumentException("Invalid protected data length", nameof(protectedData));
        }

        var nonce = new byte[NonceLength];
        var tag = new byte[TagLength];
        var ciphertext = new byte[combined.Length - NonceLength - TagLength];

        Buffer.BlockCopy(combined, 0, nonce, 0, NonceLength);
        Buffer.BlockCopy(combined, NonceLength, tag, 0, TagLength);
        Buffer.BlockCopy(combined, NonceLength + TagLength, ciphertext, 0, ciphertext.Length);

        var plaintext = new byte[ciphertext.Length];

        using var aesGcm = new AesGcm(_key, TagLength);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return Encoding.UTF8.GetString(plaintext);
    }
}
