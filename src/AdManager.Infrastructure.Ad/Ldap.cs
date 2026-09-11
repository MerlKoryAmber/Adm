using System.DirectoryServices;
using AdManager.Application;
using AdManager.Application.Abstractions;

namespace AdManager.Infrastructure.Ad;

/// <summary>Единая точка бинда к AD под operational identity (gMSA/StoredCredential).</summary>
internal static class Ldap
{
    public static DirectoryEntry Bind(AdConnectionOptions opt, IOperationalCredentialProvider cred, string dn)
    {
        var id = cred.GetIdentity(opt.Domain);
        var path = $"LDAP://{opt.Server}/{dn}";
        return id.Credential is null
            ? new DirectoryEntry(path, null, null, AuthenticationTypes.Secure)
            : new DirectoryEntry(path, id.Credential.UserName, id.Credential.Password, AuthenticationTypes.Secure);
    }
}
