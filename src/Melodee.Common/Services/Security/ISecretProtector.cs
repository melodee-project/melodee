namespace Melodee.Common.Services.Security;

public interface ISecretProtector
{
    string Protect(string secret);
    string Unprotect(string protectedData);
}
