namespace Melodee.Common.Services.Security;

public interface IOpenSubsonicSecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedData);
}
