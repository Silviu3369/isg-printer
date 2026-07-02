namespace ISGPrinter.Application.Abstractions;

public interface ICredentialProtector
{
    string Protect(string plainText);

    string Unprotect(string protectedText);
}
