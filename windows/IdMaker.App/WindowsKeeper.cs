// Keeps this computer's sign-in to the shared register with Windows' own
// data protection (DPAPI, CurrentUser): the file it ends up in can only be
// opened by the same Windows account on the same computer. Copied anywhere
// else, or read by another user, it is noise.
//
// ProtectedData is part of the Windows desktop runtime the app already runs
// on; it is not an added package.

using System.Security.Cryptography;
using System.Text;
using IdMaker.Core;

namespace IdMaker.App;

internal sealed class WindowsKeeper : ISecretKeeper
{
    // Not a secret: it only stops a blob another program protected for this user being taken for ours.
    private static readonly byte[] Purpose = Encoding.UTF8.GetBytes("Alpha Premier ID Maker: register sign-in, v1");

    public string? Protect(string secret)
    {
        try { return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Purpose, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; }     // no user profile loaded: the sign-in then lasts for this run only
    }

    public string? Unprotect(string kept)
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(kept), Purpose, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; }     // another account's, another computer's, or damaged
        catch (FormatException) { return null; }
    }
}
