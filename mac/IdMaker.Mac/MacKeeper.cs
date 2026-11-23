using System.Security.Cryptography;
using System.Text;
using IdMaker.Core;
using Security;

namespace IdMaker.Mac;

/// <summary>
/// Keeps the register sign-in wrapped by the Mac: the wrapping key lives in
/// this user's Keychain and never in a file, so the file that holds the
/// wrapped sign-in is useless on another account or another computer. The
/// Mac counterpart of the Windows app's DPAPI keeper.
/// </summary>
internal sealed class MacKeeper : ISecretKeeper
{
    private const string Service = "Alpha Premier ID Maker";
    private const string Account = "register sign-in key, v1";
    private const int NonceBytes = 12, TagBytes = 16, KeyBytes = 32;

    public string? Protect(string secret)
    {
        var key = Key(create: true);
        if (key is null) return null;     // no Keychain to keep the key in: the sign-in then lasts for this run only
        var nonce = RandomNumberGenerator.GetBytes(NonceBytes);
        var plain = Encoding.UTF8.GetBytes(secret);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagBytes];
        using var aes = new AesGcm(key, TagBytes);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string? Unprotect(string kept)
    {
        var key = Key(create: false);
        if (key is null) return null;     // another account's, another computer's, or the key was removed
        try
        {
            var all = Convert.FromBase64String(kept);
            if (all.Length < NonceBytes + TagBytes) return null;
            var plain = new byte[all.Length - NonceBytes - TagBytes];
            using var aes = new AesGcm(key, TagBytes);
            aes.Decrypt(all.AsSpan(0, NonceBytes), all.AsSpan(NonceBytes + TagBytes), all.AsSpan(NonceBytes, TagBytes), plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (FormatException) { return null; }
        catch (CryptographicException) { return null; }     // damaged, or wrapped with another key
    }

    private static SecRecord Query() => new(SecKind.GenericPassword) { Service = Service, Account = Account };

    private static byte[]? Key(bool create)
    {
        var found = SecKeyChain.QueryAsData(Query(), false, out var status);
        if (status == SecStatusCode.Success && found is not null && found.Length == (nuint)KeyBytes) return found.ToArray();
        if (!create || status != SecStatusCode.ItemNotFound) return null;
        var key = RandomNumberGenerator.GetBytes(KeyBytes);
        var record = Query();
        record.ValueData = NSData.FromArray(key);
        return SecKeyChain.Add(record) == SecStatusCode.Success ? key : null;
    }

    /// <summary>Removes the wrapping key, so nothing wrapped with it opens again. For the self-test.</summary>
    internal static void Forget() => SecKeyChain.Remove(Query());
}
