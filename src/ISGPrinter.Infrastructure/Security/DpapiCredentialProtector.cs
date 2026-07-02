using System.Security.Cryptography;
using System.Text;
using ISGPrinter.Application.Abstractions;

namespace ISGPrinter.Infrastructure.Security;

public sealed class DpapiCredentialProtector : ICredentialProtector
{
    public string Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
        return Convert.ToBase64String(protectedBytes);
    }

    public string Unprotect(string protectedText)
    {
        if (string.IsNullOrEmpty(protectedText))
        {
            return string.Empty;
        }

        var bytes = Convert.FromBase64String(protectedText);
        var plainBytes = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.LocalMachine);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
