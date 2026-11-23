// Where the app keeps what it remembers between runs: small named values,
// one file each, in the user's own application-data folder.

using System.Text;
using System.Text.Json;

namespace IdMaker.Core;

public interface IKeyValueStore
{
    string? Get(string key);
    /// <summary>Save a value, or remove it with null. Never throws: a store that cannot be written is a store that forgets.</summary>
    void Set(string key, string? value);
}

public sealed class MemoryStore : IKeyValueStore
{
    private readonly Dictionary<string, string> _values = new();
    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public void Set(string key, string? value)
    {
        if (value is null) _values.Remove(key); else _values[key] = value;
    }
}

/// <summary>One file per key. Written to a temporary file and moved, so a crash cannot leave half a register.</summary>
public sealed class FileStore : IKeyValueStore
{
    private readonly string _dir;
    private readonly object _gate = new();

    public FileStore(string directory) { _dir = directory; }

    public string Directory => _dir;

    // The app's folder inside the user's application data: the same two names on Windows and on the Mac.
    public const string CompanyFolder = "Alpha Premier";
    public const string AppFolder = "ID Maker";

    /// <summary>The per-user folder the installed app uses.</summary>
    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CompanyFolder, AppFolder);

    private string PathOf(string key)
    {
        foreach (var ch in key)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_')) throw new ArgumentException("Store keys are letters, digits, - and _ only.", nameof(key));
        }
        return Path.Combine(_dir, key + ".json");
    }

    public string? Get(string key)
    {
        lock (_gate)
        {
            // Still needed: the file is the user's own and another program (a backup, a virus scanner)
            // can be holding it at this moment. It is then read as "nothing kept": the caller cannot tell
            // that from a first run, which for register.json means the local duplicate check sees no
            // cards until the next read. The store has no way to say "could not" (docs/untangle, X1).
            try { return File.Exists(PathOf(key)) ? File.ReadAllText(PathOf(key), Encoding.UTF8) : null; }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }

    public void Set(string key, string? value)
    {
        lock (_gate)
        {
            try
            {
                var path = PathOf(key);
                if (value is null) { File.Delete(path); return; }
                System.IO.Directory.CreateDirectory(_dir);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, value, new UTF8Encoding(false));
                File.Move(tmp, path, overwrite: true);
            }
            // Still needed: a full disk or a folder that has been made read-only must not stop a card
            // being printed. What could not be written lasts for this run only, and nothing tells the
            // operator; that includes an issued card (docs/untangle, X2).
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

public static class StoreJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    /// <summary>Read a stored value, or the fallback when it is missing or no longer parses.</summary>
    public static T Read<T>(this IKeyValueStore store, string key, T fallback)
    {
        var raw = store.Get(key);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        // Still needed: the file may have been written by an older version of the app, or damaged on
        // disk. It reads as the fallback, and the next write replaces it (docs/untangle, X3).
        try { return JsonSerializer.Deserialize<T>(raw, Options) ?? fallback; }
        catch (JsonException) { return fallback; }
    }

    public static void Write<T>(this IKeyValueStore store, string key, T value) =>
        store.Set(key, JsonSerializer.Serialize(value, Options));
}

/// <summary>
/// Job titles. CardConfig.Titles is the company list; anything typed under
/// "Other…" is remembered on that computer and offered next time, so the
/// list grows into whatever the company actually uses.
/// </summary>
public sealed class TitleList
{
    private const string Key = "titles";
    private readonly IKeyValueStore _store;
    private List<string>? _extra;

    public TitleList(IKeyValueStore store) { _store = store; }

    private List<string> Extra => _extra ??= _store.Read(Key, new List<string>());

    /// <summary>Is this one of the titles that ship with the app?</summary>
    public static bool IsStandard(string title)
    {
        var t = Naming.CollapseSpaces(title);
        return CardConfig.Titles.Any(x => Naming.SameText(x, t));
    }

    /// <summary>The company list first, then titles this computer has used before.</summary>
    public IReadOnlyList<string> All() => CardConfig.Titles.Concat(Extra).ToList();

    /// <summary>Remember a title typed under "Other…". Standard ones are ignored.</summary>
    public void Remember(string title)
    {
        var t = Naming.CollapseSpaces(title);
        if (t.Length == 0 || IsStandard(t)) return;
        var list = Extra.Where(x => !Naming.SameText(x, t)).ToList();
        list.Insert(0, t);
        _extra = list.Take(CardConfig.MaxRememberedTitles).ToList();
        _store.Write(Key, _extra);
    }

    /// <summary>Drop a remembered title (not the company ones).</summary>
    public void Forget(string title)
    {
        var t = Naming.CollapseSpaces(title);
        _extra = Extra.Where(x => !Naming.SameText(x, t)).ToList();
        _store.Write(Key, _extra);
    }
}
