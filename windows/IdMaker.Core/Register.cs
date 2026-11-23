// Register of issued IDs.
//
// Two layers:
//   local   this computer's own list. Always on. Instant.
//   shared  one Firestore collection in the company's Firebase project (see
//           register/ at the top of the repository), so every HR computer
//           checks duplicates against the same list. It is reached over
//           Firestore's and Firebase Auth's REST interfaces with HttpClient:
//           no Firebase package.
//
// The shared register is read whole, once, and every number typed after
// that is answered from memory. Asking the register about one number at a
// time meant a round trip on every keystroke, and until the answer came
// back the number looked free. Issuing a card that duplicates one somebody
// else issued is the single worst thing this app can do, so the check
// cannot be the slowest part of the form.
//
// The read is the fast check; the write is the one that cannot be wrong.
// Each number is one document, named by the number, and it is only ever
// created: the register refuses to create a document that is already
// there, and its rules refuse every change or removal. Two computers
// issuing the same number at the same moment cannot both succeed.

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IdMaker.Core;

public sealed record RegisterEntry
{
    public string Id { get; init; } = "";
    public int Year { get; init; }
    public int Seq { get; init; }
    public string Name { get; init; } = "";
    public string Title { get; init; } = "";
    /// <summary>ISO 8601, UTC.</summary>
    public string IssuedAt { get; init; } = "";
    public bool Replacement { get; init; }
    public string Last { get; init; } = "";
    public string First { get; init; } = "";
    public string Middle { get; init; } = "";
    public string Suffix { get; init; } = "";
    public string Role1 { get; init; } = "";
    public string Role2 { get; init; } = "";
    /// <summary>Who the card is for: PersonKind.Employee or PersonKind.Intern. Empty on cards issued before the form asked.</summary>
    public string Kind { get; init; } = "";

    /// <summary>"12 Nov 2026", or empty when the date cannot be read.</summary>
    public string IssuedOn =>
        DateTimeOffset.TryParse(IssuedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d)
            ? d.ToLocalTime().ToString("d MMM yyyy", CultureInfo.GetCultureInfo("en-PH"))
            : "";

    /// <summary>
    /// A card as it is recorded at the moment it is issued. The form and the
    /// batch both make their entries here, so the number's spelling and the
    /// moment's format are decided once.
    /// </summary>
    public static RegisterEntry Issue(int year, int seq, string name, string title, NameParts parts,
        string role1, string role2, string kind, bool replacement, DateTime now) => new()
    {
        Id = Naming.EmployeeId(year, seq), Year = year, Seq = seq, Name = name, Title = title,
        Last = parts.Last, First = parts.First, Middle = parts.Middle, Suffix = parts.Suffix,
        Role1 = role1, Role2 = role2, Kind = kind, Replacement = replacement, IssuedAt = Stamp(now),
    };

    /// <summary>The moment of issue as the register writes it: UTC, to the millisecond.</summary>
    public static string Stamp(DateTime now) => now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}

public enum RegisterFieldKind { Text, Whole, YesNo }

/// <summary>
/// One field of an issued ID: its name (in Firestore and as a CSV column),
/// what kind of value it is, and how to read it from and put it into an entry.
/// </summary>
public sealed class RegisterField
{
    private readonly Func<RegisterEntry, string>? _text;
    private readonly Func<RegisterEntry, string, RegisterEntry>? _withText;
    private readonly Func<RegisterEntry, int>? _whole;
    private readonly Func<RegisterEntry, int, RegisterEntry>? _withWhole;
    private readonly Func<RegisterEntry, bool>? _yes;
    private readonly Func<RegisterEntry, bool, RegisterEntry>? _withYes;

    public string Name { get; }
    public RegisterFieldKind Kind { get; }
    /// <summary>Left out of the document sent to Firestore when it is empty, rather than sent as "".</summary>
    public bool OnlyWhenKnown { get; }

    private RegisterField(string name, RegisterFieldKind kind, bool onlyWhenKnown,
        Func<RegisterEntry, string>? text = null, Func<RegisterEntry, string, RegisterEntry>? withText = null,
        Func<RegisterEntry, int>? whole = null, Func<RegisterEntry, int, RegisterEntry>? withWhole = null,
        Func<RegisterEntry, bool>? yes = null, Func<RegisterEntry, bool, RegisterEntry>? withYes = null)
    {
        Name = name; Kind = kind; OnlyWhenKnown = onlyWhenKnown;
        _text = text; _withText = withText; _whole = whole; _withWhole = withWhole; _yes = yes; _withYes = withYes;
    }

    public static RegisterField Text(string name, Func<RegisterEntry, string> get, Func<RegisterEntry, string, RegisterEntry> with, bool onlyWhenKnown = false) =>
        new(name, RegisterFieldKind.Text, onlyWhenKnown, text: get, withText: with);

    public static RegisterField Whole(string name, Func<RegisterEntry, int> get, Func<RegisterEntry, int, RegisterEntry> with) =>
        new(name, RegisterFieldKind.Whole, false, whole: get, withWhole: with);

    public static RegisterField YesNo(string name, Func<RegisterEntry, bool> get, Func<RegisterEntry, bool, RegisterEntry> with) =>
        new(name, RegisterFieldKind.YesNo, false, yes: get, withYes: with);

    private InvalidOperationException NotA(string kind) => new($"The register field “{Name}” is not {kind}.");

    public string TextOf(RegisterEntry e) => _text is null ? throw NotA("text") : _text(e);
    public int WholeOf(RegisterEntry e) => _whole is null ? throw NotA("a whole number") : _whole(e);
    public bool YesOf(RegisterEntry e) => _yes is null ? throw NotA("a yes or no") : _yes(e);
    public RegisterEntry With(RegisterEntry e, string value) => _withText is null ? throw NotA("text") : _withText(e, value);
    public RegisterEntry With(RegisterEntry e, int value) => _withWhole is null ? throw NotA("a whole number") : _withWhole(e, value);
    public RegisterEntry With(RegisterEntry e, bool value) => _withYes is null ? throw NotA("a yes or no") : _withYes(e, value);

    /// <summary>
    /// What is sent to Firestore for a text field: the entry's value as it
    /// would be read back, so nothing is written that reading would not
    /// return. For most fields that is the value itself; a kind goes through
    /// PersonKind.Parse on the way in, so "Intern" is sent as "intern" and a
    /// word that is no kind as "" (and, being OnlyWhenKnown, not at all).
    /// </summary>
    public string Sent(RegisterEntry e) => TextOf(With(new RegisterEntry(), TextOf(e)));
}

/// <summary>
/// The fields of an issued ID, written once. The document sent to
/// Firestore, the document read back, the CSV saved and the CSV brought in
/// are all made from this list, so a field cannot be added to one and
/// forgotten in another.
///
/// The same list is `issuedId` in firestore.rules (Deign86/apg-keystone),
/// the table in register/README.md and the columns in register/migrate.mjs:
/// a field added here is added there in the same piece of work.
/// WireFormatTests holds what is sent to the letter.
/// </summary>
public static class RegisterFields
{
    /// <summary>In the order they are sent to Firestore.</summary>
    public static readonly IReadOnlyList<RegisterField> All = new[]
    {
        RegisterField.Text("id", e => e.Id, (e, v) => e with { Id = v }),
        RegisterField.Whole("year", e => e.Year, (e, v) => e with { Year = v }),
        RegisterField.Whole("seq", e => e.Seq, (e, v) => e with { Seq = v }),
        RegisterField.Text("name", e => e.Name, (e, v) => e with { Name = v }),
        RegisterField.Text("title", e => e.Title, (e, v) => e with { Title = v }),
        RegisterField.Text("issuedAt", e => e.IssuedAt, (e, v) => e with { IssuedAt = v }),
        RegisterField.YesNo("replacement", e => e.Replacement, (e, v) => e with { Replacement = v }),
        RegisterField.Text("last", e => e.Last, (e, v) => e with { Last = v }),
        RegisterField.Text("first", e => e.First, (e, v) => e with { First = v }),
        RegisterField.Text("middle", e => e.Middle, (e, v) => e with { Middle = v }),
        RegisterField.Text("suffix", e => e.Suffix, (e, v) => e with { Suffix = v }),
        RegisterField.Text("role1", e => e.Role1, (e, v) => e with { Role1 = v }),
        RegisterField.Text("role2", e => e.Role2, (e, v) => e with { Role2 = v }),
        // Read through PersonKind.Parse wherever it comes from. Sent only when it is known: a card
        // waiting in the outbox since before the form asked has none, and the rules take that.
        RegisterField.Text("kind", e => e.Kind, (e, v) => e with { Kind = PersonKind.Parse(v) }, onlyWhenKnown: true),
    };

    /// <summary>The two fields the register adds to every document it sends: who issued it. They are not part of an entry.</summary>
    public const string By = "by", ByEmail = "byEmail";

    public static RegisterField Named(string name) => All.First(f => f.Name == name);

    /// <summary>The columns of the saved list, in the order it has always had: the person, then the card.</summary>
    public static readonly IReadOnlyList<RegisterField> CsvColumns =
        new[] { "id", "year", "seq", "last", "first", "middle", "suffix", "name", "title", "role1", "role2", "issuedAt", "replacement", "kind" }.Select(Named).ToList();
}

/// <summary>
/// Who an ID is for. The attendance system keeps interns only, and is to
/// take new interns from the register, so the register has to say which a
/// card is.
/// </summary>
public static class PersonKind
{
    public const string Employee = "employee";
    public const string Intern = "intern";

    /// <summary>The kind as the register writes it, or "" when the text is neither.</summary>
    public static string Parse(string? text) => (text ?? "").Trim().ToLowerInvariant() switch
    {
        Employee => Employee,
        Intern => Intern,
        _ => "",
    };
}

/// <summary>A failure the operator can act on: its message is shown as it is.</summary>
public sealed class RegisterException : Exception
{
    public RegisterException(string message) : base(message) { }
}

public sealed record PingResult(bool Ok, int Rows, string Error);

/// <summary>
/// The Firebase project the shared register is kept in. These two values
/// name the project; they are not secrets. Who may read or write is decided
/// by signing in and by the project's security rules.
/// </summary>
public sealed record RegisterProject(string ProjectId, string ApiKey, string Company = "apg")
{
    public string SignInUrl => "https://identitytoolkit.googleapis.com/v1/accounts:signInWithPassword?key=" + Uri.EscapeDataString(ApiKey);
    public string RefreshUrl => "https://securetoken.googleapis.com/v1/token?key=" + Uri.EscapeDataString(ApiKey);
    /// <summary>Where the company's documents are; the register is the list "idreg" under it.</summary>
    public string Documents =>
        $"https://firestore.googleapis.com/v1/projects/{Uri.EscapeDataString(ProjectId)}/databases/(default)/documents/keystone/{Uri.EscapeDataString(Company)}";
}

/// <summary>
/// Keeps this computer's sign-in so that only this Windows account can read
/// it back. The app gives Windows' own data protection (DPAPI); Core has no
/// Windows in it, so it only knows the shape.
/// </summary>
public interface ISecretKeeper
{
    /// <summary>The secret, wrapped so it is safe in a file. Null when it cannot be wrapped.</summary>
    string? Protect(string secret);
    /// <summary>The secret back, or null when this account cannot open it (another user, another computer, a damaged file).</summary>
    string? Unprotect(string kept);
}

public sealed record SignInResult(bool Ok, string Error);

/// <summary>What became of one card sent to the shared register.</summary>
public enum SharedOutcome
{
    /// <summary>It is in the register.</summary>
    Sent,
    /// <summary>The register could not be reached (or nobody is signed in): kept, to be sent again.</summary>
    Waiting,
    /// <summary>The register already holds this number for somebody else: this card is a duplicate.</summary>
    Refused,
}

public sealed record SharedWrite(SharedOutcome Outcome, RegisterEntry? Holder = null);

/// <summary>A card this computer issued on a number the shared register had already given to somebody else.</summary>
public sealed record RefusedCard(RegisterEntry Card, RegisterEntry Holder);

public sealed partial class Register
{
    private const string Key = "register";
    private const string OutboxKey = "register-outbox";
    private const string RefusedKey = "register-refused";
    private const string SignInKey = "register-signin";
    private const string ListName = "idreg";

    public const string SignInFirst = "Sign in under Issued IDs to use the shared register.";
    private const string NotAMember = "This account may not use the ID register. Ask the CEO to add it in Keystone: Settings › User Accounts.";
    private const string SignInAgain = "This computer’s sign-in is no longer accepted: the password was changed or the account was turned off. Sign in again under Issued IDs.";

    private readonly IKeyValueStore _store;
    private readonly HttpClient _http;
    private readonly ISecretKeeper? _keeper;
    private readonly object _gate = new();
    private Dictionary<string, RegisterEntry>? _index;      // the shared register, once it has been read
    private Task<bool>? _loading;                           // the read in flight, so ten callers make one request
    private int _version;                                   // goes up when the sign-in changes, so an answer for the old one is not kept
    private bool _signInRead;
    private SavedSignIn? _signIn;                           // who is signed in on this computer
    private string? _idToken;                               // the short-lived pass Firebase gives for a sign-in
    private DateTimeOffset _idTokenUntil;
    private Task<string?>? _refreshing;                     // the renewal in flight, so ten callers make one request

    /// <summary>The project the app ships with. Null is no shared register; tests point it somewhere that does not exist.</summary>
    public RegisterProject? Project { get; init; } = CardConfig.Register;

    /// <summary>The time, for deciding when a pass has run out. Tests set it.</summary>
    public Func<DateTimeOffset> Clock { get; init; } = () => DateTimeOffset.UtcNow;

    /// <param name="keeper">Wraps the sign-in for the disk. Without one the sign-in lasts for this run only and is never written.</param>
    public Register(IKeyValueStore store, HttpMessageHandler? handler = null, ISecretKeeper? keeper = null)
    {
        _store = store;
        _keeper = keeper;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        // the per-request timeout below is the one that speaks; this only stops it being cut short
        _http.Timeout = Timeout.InfiniteTimeSpan;
    }

    /* ---- local --------------------------------------------------- */

    private List<RegisterEntry> ReadLocal() => _store.Read(Key, new List<RegisterEntry>());

    /// <summary>Newest first.</summary>
    public IReadOnlyList<RegisterEntry> All() =>
        ReadLocal().OrderByDescending(e => e.IssuedAt, StringComparer.Ordinal).ToList();

    /// <summary>The local entry for this number, or null.</summary>
    public RegisterEntry? Find(int year, int? seq)
    {
        if (seq is null) return null;
        var id = Naming.EmployeeId(year, seq.Value);
        return ReadLocal().FirstOrDefault(e => e.Id == id);
    }

    /// <summary>Insert or replace by id.</summary>
    public void Record(RegisterEntry entry)
    {
        lock (_gate)
        {
            var list = ReadLocal().Where(e => e.Id != entry.Id).ToList();
            list.Add(entry);
            _store.Write(Key, list);
        }
    }

    /// <summary>
    /// Take in a register CSV downloaded from this app or the earlier web
    /// app. Entries already here are kept as they are. Returns how many were added.
    /// </summary>
    public int ImportCsv(string csvText)
    {
        var rows = Csv.Parse(csvText);
        if (rows.Count < 2) return 0;
        var head = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        string Cell(IReadOnlyList<string> r, string name)
        {
            var i = head.IndexOf(name);
            return i >= 0 && i < r.Count ? r[i].Trim() : "";
        }
        var added = 0;
        lock (_gate)
        {
            var list = ReadLocal();
            var have = list.Select(e => e.Id).ToHashSet();
            foreach (var r in rows.Skip(1))
            {
                var id = Cell(r, "id");
                if (id.Length == 0 || have.Contains(id)) continue;
                var entry = new RegisterEntry();
                var readable = true;
                foreach (var field in RegisterFields.All)
                {
                    // the header row was read in lower case
                    var cell = Cell(r, field.Name.ToLowerInvariant());
                    switch (field.Kind)
                    {
                        case RegisterFieldKind.Whole:
                            // a row whose year or number is not a number is not a card
                            if (int.TryParse(cell, NumberStyles.None, CultureInfo.InvariantCulture, out var whole)) entry = field.With(entry, whole);
                            else readable = false;
                            break;
                        case RegisterFieldKind.YesNo:
                            entry = field.With(entry, cell.Equals("yes", StringComparison.OrdinalIgnoreCase));
                            break;
                        default:
                            entry = field.With(entry, cell);
                            break;
                    }
                }
                if (!readable) continue;
                list.Add(entry);
                have.Add(id);
                added++;
            }
            if (added > 0) _store.Write(Key, list);
        }
        return added;
    }

    /* ---- shared: who is signed in ---------------------------------
       Signing in is asked once per computer. What is kept is not the
       password but the renewal token Firebase hands back, wrapped by the
       keeper (Windows' DPAPI in the app) so only this Windows account can
       open it. The pass used on each request lasts an hour and lives in
       memory only.
       ---------------------------------------------------------------- */

    private sealed record SavedSignIn(string Email, string Uid, string RefreshToken);
    private sealed record KeptSignIn(string Kept);

    /// <summary>Is there a shared register at all in this build?</summary>
    public bool Shared => Project is not null;

    public bool SignedIn => SignInState() is not null;

    /// <summary>The email signed in on this computer, or "".</summary>
    public string SignedInAs => SignInState()?.Email ?? "";

    /// <summary>Why the last sign-in on this computer stopped being accepted, or "".</summary>
    public string SignInProblem { get; private set; } = "";

    /// <summary>Why the register was last unreachable, in words for the operator, or "".</summary>
    public string LastProblem { get; private set; } = "";

    private SavedSignIn? SignInState()
    {
        lock (_gate)
        {
            if (_signInRead) return _signIn;
            _signInRead = true;
            var kept = _store.Read<KeptSignIn?>(SignInKey, null)?.Kept;
            if (string.IsNullOrEmpty(kept) || _keeper is null) return _signIn;
            var plain = _keeper.Unprotect(kept);
            if (plain is null) return _signIn;          // another account's, or damaged: ask again
            try
            {
                var s = JsonSerializer.Deserialize<SavedSignIn>(plain, StoreJson.Options);
                if (s is not null && !string.IsNullOrEmpty(s.RefreshToken)) _signIn = s;
            }
            catch (JsonException) { /* not ours: ask again */ }
            return _signIn;
        }
    }

    /// <summary>Call with the gate held.</summary>
    private void KeepSignIn(SavedSignIn? s)
    {
        _signInRead = true;
        _signIn = s;
        var kept = s is null ? null : _keeper?.Protect(JsonSerializer.Serialize(s, StoreJson.Options));
        // never in plain text: with no keeper, or one that cannot wrap it, the sign-in lasts for this run only
        if (kept is null) _store.Set(SignInKey, null); else _store.Write(SignInKey, new KeptSignIn(kept));
    }

    /// <summary>Call with the gate held. Whatever was read or is being read belonged to the sign-in before.</summary>
    private void SignInChanged()
    {
        _version++;
        _index = null;
        _loading = null;
        _refreshing = null;
        _idToken = null;
    }

    /// <summary>
    /// Sign this computer in. The account must be one the CEO made in
    /// Keystone: a sign-in that is not a member is not kept.
    /// </summary>
    public async Task<SignInResult> SignIn(string email, string password)
    {
        if (Project is not { } project) return new SignInResult(false, "This copy of the app has no shared register.");
        var mail = (email ?? "").Trim();
        if (mail.Length == 0) return new SignInResult(false, "Enter the email you sign in to Keystone with.");
        if (string.IsNullOrEmpty(password)) return new SignInResult(false, "Enter the password.");
        try
        {
            var body = new JsonObject { ["email"] = mail, ["password"] = password, ["returnSecureToken"] = true };
            var answer = await Send(() => new HttpRequestMessage(HttpMethod.Post, project.SignInUrl)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            }).ConfigureAwait(false);
            if (answer.Status != HttpStatusCode.OK) return new SignInResult(false, SignInRefusal(answer));
            var token = Text(answer.Body?["idToken"]);
            var refresh = Text(answer.Body?["refreshToken"]);
            var uid = Text(answer.Body?["localId"]);
            if (token.Length == 0 || refresh.Length == 0 || uid.Length == 0)
                return new SignInResult(false, "Signing in answered, but not with a sign-in. Try once more.");

            // signed in is not enough: only a member of the company's records may open the register
            var look = await Send(() => Authorized(HttpMethod.Get, $"{project.Documents}/{ListName}?pageSize=1", token)).ConfigureAwait(false);
            if (look.Status is HttpStatusCode.Forbidden) return new SignInResult(false, NotAMember);
            if (look.Status != HttpStatusCode.OK) return new SignInResult(false, Refusal(look));

            lock (_gate)
            {
                SignInChanged();
                KeepSignIn(new SavedSignIn(Text(answer.Body?["email"]) is { Length: > 0 } e ? e : mail, uid, refresh));
                _idToken = token;
                _idTokenUntil = Clock() + Lifetime(answer.Body?["expiresIn"]);
                SignInProblem = "";
                LastProblem = "";
            }
            return new SignInResult(true, "");
        }
        catch (RegisterException e) { return new SignInResult(false, e.Message); }
    }

    /// <summary>Forget this computer's sign-in. Cards still waiting stay in the outbox for whoever signs in next.</summary>
    public void SignOut()
    {
        lock (_gate)
        {
            SignInChanged();
            KeepSignIn(null);
            SignInProblem = "";
        }
    }

    private static string SignInRefusal(Answer a)
    {
        var why = Text(a.Body?["error"]?["message"]);
        if (why.StartsWith("TOO_MANY_ATTEMPTS", StringComparison.Ordinal)) return "Too many tries. Wait a few minutes, then sign in again.";
        if (why.StartsWith("USER_DISABLED", StringComparison.Ordinal)) return "This account has been turned off. Ask the CEO to turn it back on.";
        if (why is "EMAIL_NOT_FOUND" or "INVALID_PASSWORD" or "INVALID_LOGIN_CREDENTIALS" or "INVALID_EMAIL" || why.StartsWith("MISSING_", StringComparison.Ordinal))
            return "That email or password is not right. Check both and try again. A forgotten password is reset from Keystone’s sign-in screen (Forgot password?).";
        if (why.Contains("API key", StringComparison.OrdinalIgnoreCase)) return "Signing in is not set up correctly in this copy of the app. Tell IT: the Firebase API key was not accepted.";
        return $"Signing in was refused (HTTP {(int)a.Status}{(why.Length > 0 ? ", " + why : "")}). Try again in a minute.";
    }

    // a minute short, so a pass is never sent in its last seconds
    private static TimeSpan Lifetime(JsonNode? expiresIn) => TimeSpan.FromSeconds(Math.Max(0, Int(expiresIn) is var s and > 0 ? s - 60 : 0));

    /// <summary>
    /// The pass for a request, renewed when it has run out. Null when nobody
    /// is signed in, or the sign-in is no longer accepted.
    /// </summary>
    private Task<string?> Token(bool renew = false)
    {
        lock (_gate)
        {
            var s = SignInState();
            if (s is null || Project is null) return Task.FromResult<string?>(null);
            if (!renew && _idToken is not null && Clock() < _idTokenUntil) return Task.FromResult<string?>(_idToken);
            if (_refreshing is not null) return _refreshing;
            var renewal = Renew(Project, s, _version);
            // one that has already finished is not "in flight": keeping it would hand its answer out for ever
            if (!renewal.IsCompleted) _refreshing = renewal;
            return renewal;
        }
    }

    private async Task<string?> Renew(RegisterProject project, SavedSignIn s, int version)
    {
        try
        {
            var answer = await Send(() => new HttpRequestMessage(HttpMethod.Post, project.RefreshUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["grant_type"] = "refresh_token", ["refresh_token"] = s.RefreshToken }),
            }).ConfigureAwait(false);

            if (answer.Status == HttpStatusCode.OK && Text(answer.Body?["id_token"]) is { Length: > 0 } token)
            {
                lock (_gate)
                {
                    if (version != _version) return null;       // signed out, or somebody else signed in, meanwhile
                    _idToken = token;
                    _idTokenUntil = Clock() + Lifetime(answer.Body?["expires_in"]);
                    if (Text(answer.Body?["refresh_token"]) is { Length: > 0 } next && next != s.RefreshToken) KeepSignIn(s with { RefreshToken = next });
                    return token;
                }
            }

            var why = Text(answer.Body?["error"]?["message"]);
            if (answer.Status == HttpStatusCode.BadRequest && (why.StartsWith("TOKEN_EXPIRED", StringComparison.Ordinal) || why.StartsWith("USER_DISABLED", StringComparison.Ordinal)
                || why.StartsWith("USER_NOT_FOUND", StringComparison.Ordinal) || why.StartsWith("INVALID_REFRESH_TOKEN", StringComparison.Ordinal)
                || why.StartsWith("INVALID_GRANT_TYPE", StringComparison.Ordinal) || why.StartsWith("MISSING_REFRESH_TOKEN", StringComparison.Ordinal)))
            {
                lock (_gate)
                {
                    if (version != _version) return null;
                    SignInChanged();
                    KeepSignIn(null);
                    SignInProblem = SignInAgain;
                }
                return null;
            }
            throw new RegisterException($"The sign-in could not be renewed (HTTP {(int)answer.Status}). Try again in a minute.");
        }
        finally { lock (_gate) { if (version == _version) _refreshing = null; } }
    }

    /* ---- shared: talking to it ------------------------------------ */

    private sealed record Answer(HttpStatusCode Status, JsonNode? Body);

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, JsonNode? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return request;
    }

    /// <summary>
    /// One request. Not reaching the service at all is said plainly, so the
    /// operator is not left guessing; what the service answered, good or
    /// bad, is handed back for the caller to read.
    /// </summary>
    private async Task<Answer> Send(Func<HttpRequestMessage> make)
    {
        var seconds = (int)CardConfig.RegisterTimeout.TotalSeconds;
        using var cts = new CancellationTokenSource(CardConfig.RegisterTimeout);
        HttpResponseMessage r;
        try
        {
            using var request = make();
            r = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new RegisterException($"No answer from the shared register within {seconds} seconds. Check this computer is online and try once more.");
        }
        catch (HttpRequestException)
        {
            throw new RegisterException("This computer could not reach the shared register. Check it is online.");
        }
        catch (Exception e) when (e is UriFormatException or InvalidOperationException)
        {
            throw new RegisterException("The shared register’s address is not one this computer can open. Tell IT.");
        }

        using (r)
        {
            string text;
            try { text = await r.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                throw new RegisterException($"No answer from the shared register within {seconds} seconds. Try once more.");
            }
            catch (HttpRequestException)
            {
                throw new RegisterException("The connection to the shared register dropped. Check this computer is online and try once more.");
            }
            if (string.IsNullOrWhiteSpace(text)) return new Answer(r.StatusCode, null);
            try { return new Answer(r.StatusCode, JsonNode.Parse(text)); }
            catch (JsonException)
            {
                // a captive Wi-Fi page or a proxy's error page, not the register
                if (r.IsSuccessStatusCode) throw new RegisterException("Something between this computer and the shared register answered instead of it (a Wi-Fi sign-in page, usually). Open a web page to check the connection, then try again.");
                return new Answer(r.StatusCode, null);
            }
        }
    }

    /// <summary>What to tell the operator about an answer that was not a yes.</summary>
    private static string Refusal(Answer a) => a.Status switch
    {
        HttpStatusCode.Forbidden => NotAMember,
        HttpStatusCode.Unauthorized => SignInAgain,
        HttpStatusCode.TooManyRequests => "The shared register is busy. Try again in a minute.",
        _ => $"The shared register answered with an error (HTTP {(int)a.Status}). Try again in a minute.",
    };

    /// <summary>
    /// A request to the register as whoever is signed in. A pass the
    /// register no longer takes is renewed once and the request sent again.
    /// Throws when nobody is signed in or the service cannot be reached.
    /// </summary>
    private async Task<Answer> Ask(HttpMethod method, string url, JsonNode? body = null)
    {
        var token = await Token().ConfigureAwait(false) ?? throw new RegisterException(SignInProblem.Length > 0 ? SignInProblem : SignInFirst);
        var answer = await Send(() => Authorized(method, url, token, body)).ConfigureAwait(false);
        if (answer.Status != HttpStatusCode.Unauthorized) return answer;
        token = await Token(renew: true).ConfigureAwait(false) ?? throw new RegisterException(SignInProblem.Length > 0 ? SignInProblem : SignInFirst);
        return await Send(() => Authorized(method, url, token, body)).ConfigureAwait(false);
    }

    private static string Text(JsonNode? n) => n is JsonValue v ? (v.TryGetValue<string>(out var s) ? s : v.ToJsonString().Trim('"')) : "";
    private static int Int(JsonNode? n) =>
        n is JsonValue v && (v.TryGetValue<int>(out var i) || (v.TryGetValue<double>(out var d) && (i = (int)d) == d)
            || int.TryParse(Text(n), NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) ? i : 0;

    /* ---- shared: documents ----------------------------------------
       Firestore's REST interface writes each value inside a wrapper that
       names its type: {"stringValue":"Ana"}, {"integerValue":"2026"} (a
       whole number travels as text), {"booleanValue":true}.
       ---------------------------------------------------------------- */

    private static JsonObject Str(string v) => new() { ["stringValue"] = v };
    private static JsonObject Whole(int v) => new() { ["integerValue"] = v.ToString(CultureInfo.InvariantCulture) };

    private static JsonObject Document(RegisterEntry e, SavedSignIn by)
    {
        var fields = new JsonObject();
        foreach (var field in RegisterFields.All.Where(f => !f.OnlyWhenKnown)) fields[field.Name] = Wrapped(field, e);
        // who issued it: the rules check this is the account making the request
        fields[RegisterFields.By] = Str(by.Uid);
        fields[RegisterFields.ByEmail] = Str(by.Email);
        foreach (var field in RegisterFields.All.Where(f => f.OnlyWhenKnown))
        {
            // a kind is "intern", "employee" or not there
            if (field.Sent(e) is { Length: > 0 } known) fields[field.Name] = Str(known);
        }
        return new JsonObject { ["fields"] = fields };
    }

    // one value in Firestore's wrapper, by the field's kind
    private static JsonObject Wrapped(RegisterField field, RegisterEntry e) => field.Kind switch
    {
        RegisterFieldKind.Whole => Whole(field.WholeOf(e)),
        RegisterFieldKind.YesNo => new JsonObject { ["booleanValue"] = field.YesOf(e) },
        _ => Str(field.TextOf(e)),
    };

    // one value read back out of a document (already unwrapped to text by Value), into the entry
    private static RegisterEntry Unwrapped(RegisterField field, RegisterEntry e, string value) => field.Kind switch
    {
        RegisterFieldKind.Whole => field.With(e, Int(JsonValue.Create(value))),
        RegisterFieldKind.YesNo => field.With(e, value is "true" or "yes"),
        _ => field.With(e, value),
    };

    private static string Value(JsonNode? wrapped)
    {
        if (wrapped is not JsonObject o) return "";
        foreach (var kind in new[] { "stringValue", "integerValue", "doubleValue", "booleanValue" })
            if (o[kind] is { } v) return Text(v);
        return "";
    }

    // Documents are only ever written by this app under the register's
    // rules, but nothing about their shape is taken on trust: one that will
    // not read is skipped, not allowed to stop the rest being read.
    private static RegisterEntry? ParseEntry(JsonNode? node)
    {
        if (node is not JsonObject doc) return null;
        var fields = doc["fields"] as JsonObject;
        var entry = new RegisterEntry();
        foreach (var field in RegisterFields.All) entry = Unwrapped(field, entry, Value(fields?[field.Name]));
        // the document's own name is the number, whatever its fields say
        var path = Text(doc["name"]);
        if (path.Length > 0) entry = entry with { Id = path[(path.LastIndexOf('/') + 1)..] };
        return entry.Id.Length == 0 ? null : entry;
    }

    /// <summary>Check the register is answering this computer's sign-in, and count what is in it.</summary>
    public async Task<PingResult> Ping()
    {
        if (Project is null) return new PingResult(false, 0, "This copy of the app has no shared register.");
        if (!SignedIn) return new PingResult(false, 0, SignInProblem.Length > 0 ? SignInProblem : SignInFirst);
        if (!await LoadIndex(force: true).ConfigureAwait(false))
            return new PingResult(false, 0, LastProblem.Length > 0 ? LastProblem : "The shared register did not answer. Try once more.");
        lock (_gate) return new PingResult(true, _index?.Count ?? 0, "");
    }

    /// <summary>
    /// Read the whole shared register. True when it is in memory afterwards,
    /// false when it could not be read: no shared register, nobody signed
    /// in, or out of reach (LastProblem says which).
    /// </summary>
    public Task<bool> LoadIndex(bool force = false)
    {
        lock (_gate)
        {
            if (Project is not { } project) return Task.FromResult(false);
            if (SignInState() is null)
            {
                LastProblem = SignInProblem.Length > 0 ? SignInProblem : SignInFirst;
                return Task.FromResult(false);
            }
            if (_index is not null && !force) return Task.FromResult(true);
            if (_loading is not null && !force) return _loading;
            var mine = LoadIndexCore(project, _version);
            // one that has already finished is not "in flight": keeping it would hand a failed read out for ever
            _loading = mine.IsCompleted ? null : mine;
            return mine;
        }
    }

    private const int PageSize = 300;
    private const int MostPages = 400;       // 120,000 cards; a register that long is a fault, not a list

    private async Task<bool> LoadIndexCore(RegisterProject project, int version)
    {
        try
        {
            var index = new Dictionary<string, RegisterEntry>();
            var page = "";
            for (var n = 0; n < MostPages; n++)
            {
                var url = $"{project.Documents}/{ListName}?pageSize={PageSize}" + (page.Length > 0 ? "&pageToken=" + Uri.EscapeDataString(page) : "");
                var answer = await Ask(HttpMethod.Get, url).ConfigureAwait(false);
                if (answer.Status != HttpStatusCode.OK) throw new RegisterException(Refusal(answer));
                // an empty register answers {} with no "documents" at all
                if (answer.Body is not JsonObject o) throw new RegisterException("The shared register answered, but not with a list. Try once more.");
                foreach (var doc in o["documents"] as JsonArray ?? new JsonArray())
                {
                    if (ParseEntry(doc) is { } e) index[e.Id] = e;
                }
                page = Text(o["nextPageToken"]);
                if (page.Length == 0) break;
            }
            lock (_gate)
            {
                // the sign-in changed while this was in flight: these rows were read as somebody else
                if (version != _version) return false;
                _index = index;
                LastProblem = "";
            }
            return true;
        }
        catch (RegisterException e)
        {
            lock (_gate) { if (version == _version) LastProblem = e.Message; }
            return false;
        }
        finally { lock (_gate) { if (version == _version) _loading = null; } }
    }

    /// <summary>Has the shared register been read?</summary>
    public bool IndexLoaded { get { lock (_gate) return _index is not null; } }

    /// <summary>
    /// The shared entry for this number, without asking anybody. Known is
    /// false when the register has not been read yet.
    /// </summary>
    public (bool Known, RegisterEntry? Entry) FindSharedNow(int year, int? seq)
    {
        if (!Shared || seq is null) return (true, null);
        lock (_gate)
        {
            if (_index is null) return (false, null);
            return (true, _index.TryGetValue(Naming.EmployeeId(year, seq.Value), out var e) ? e : null);
        }
    }

    /// <summary>Keep the index true after issuing a card, without re-reading the register.</summary>
    public void NoteShared(RegisterEntry entry)
    {
        lock (_gate) { if (_index is not null && entry.Id.Length > 0) _index[entry.Id] = entry; }
    }

    /* ---- cards the shared register has not been told about yet ------
       A card saved while the register could not be reached is already
       issued: it is in the local list and in somebody's hand. If the shared
       write were simply dropped, no other computer would ever learn the
       number was taken. So it is kept here and sent again later.

       A card the register refuses is a different thing: the number already
       belongs to somebody else. It is taken out of the outbox (sending it
       again would be refused again) and kept in its own list until the
       operator has dealt with the card.
       ---------------------------------------------------------------- */

    private List<RegisterEntry> ReadOutbox() => _store.Read(OutboxKey, new List<RegisterEntry>());

    /// <summary>How many issued cards are still waiting to be written to the shared register.</summary>
    public int WaitingForShared { get { lock (_gate) return ReadOutbox().Count; } }

    private void SetWaiting(RegisterEntry entry, bool waiting)
    {
        lock (_gate)
        {
            var list = ReadOutbox().Where(e => e.Id != entry.Id || e.IssuedAt != entry.IssuedAt).ToList();
            if (waiting) list.Add(entry);
            _store.Set(OutboxKey, list.Count == 0 ? null : JsonSerializer.Serialize(list, StoreJson.Options));
        }
    }

    /// <summary>Cards issued here on a number the shared register had already given to somebody else. Oldest first.</summary>
    public IReadOnlyList<RefusedCard> Refused { get { lock (_gate) return _store.Read(RefusedKey, new List<RefusedCard>()); } }

    private void NoteRefused(RegisterEntry card, RegisterEntry holder)
    {
        lock (_gate)
        {
            var list = _store.Read(RefusedKey, new List<RefusedCard>()).Where(r => r.Card.Id != card.Id || r.Card.IssuedAt != card.IssuedAt).ToList();
            list.Add(new RefusedCard(card, holder));
            _store.Write(RefusedKey, list);
            // the number is the other person's: the duplicate check must say so from now on
            if (_index is not null) _index[holder.Id] = holder;
        }
    }

    /// <summary>The operator has dealt with the refused cards (taken them back, issued new numbers).</summary>
    public void ClearRefused() => _store.Set(RefusedKey, null);

    /// <summary>Try again to write every waiting card. Returns how many are still waiting afterwards.</summary>
    public async Task<int> SendWaiting()
    {
        if (!Shared) return 0;
        List<RegisterEntry> waiting;
        lock (_gate) waiting = ReadOutbox();
        foreach (var entry in waiting)
        {
            // still out of reach: no point trying the rest
            if ((await RecordShared(entry).ConfigureAwait(false)).Outcome == SharedOutcome.Waiting) break;
        }
        return WaitingForShared;
    }

    /// <summary>
    /// Record in the shared register. Sent: it is there. Waiting: it could
    /// not be reached, and the card is kept to be sent again by SendWaiting.
    /// Refused: the number already belongs to Holder; the card is a duplicate.
    /// </summary>
    public async Task<SharedWrite> RecordShared(RegisterEntry entry)
    {
        if (!Shared) return new SharedWrite(SharedOutcome.Waiting);
        var result = await Create(entry).ConfigureAwait(false);
        SetWaiting(entry, result.Outcome == SharedOutcome.Waiting);
        if (result is { Outcome: SharedOutcome.Refused, Holder: { } holder }) NoteRefused(entry, holder);
        return result;
    }

    // The same moment on two computers must not make the same replacement
    // document, and the same card sent twice must: its issue time, to the
    // millisecond, names it.
    private static string ReplacementName(RegisterEntry e)
    {
        var name = new string(e.IssuedAt.Where(char.IsAsciiLetterOrDigit).ToArray());
        return name.Length > 0 ? name : "undated";
    }

    /// <summary>The card did not get there: say why, and leave it waiting.</summary>
    private SharedWrite Unsent(Answer answer)
    {
        lock (_gate) LastProblem = Refusal(answer);
        return new SharedWrite(SharedOutcome.Waiting);
    }

    private async Task<SharedWrite> Create(RegisterEntry entry)
    {
        try
        {
            SavedSignIn? who;
            RegisterProject? project;
            lock (_gate) { who = SignInState(); project = Project; }
            if (who is null || project is null) return new SharedWrite(SharedOutcome.Waiting);

            var number = Uri.EscapeDataString(entry.Id);
            var body = Document(entry, who);

            // Create, never write. A number that already has a document is refused: with 409 ("already there") when
            // Firestore looks first, with 403 when the rules, which allow no change to a document, answer first.
            // 403 is also what a sign-in that may not use the register gets, so which it was is settled by looking.
            var made = await Ask(HttpMethod.Post, $"{project.Documents}/{ListName}?documentId={number}", body).ConfigureAwait(false);
            if (made.Status == HttpStatusCode.OK) return new SharedWrite(SharedOutcome.Sent);
            if (made.Status is not (HttpStatusCode.Conflict or HttpStatusCode.Forbidden)) return Unsent(made);

            var there = await Ask(HttpMethod.Get, $"{project.Documents}/{ListName}/{number}").ConfigureAwait(false);
            // nothing there, so the refusal was about who is asking or what was sent, not about the number
            if (there.Status != HttpStatusCode.OK || ParseEntry(there.Body) is not { } holder) return Unsent(there.Status == HttpStatusCode.NotFound ? made : there);

            if (entry.Replacement)
            {
                // the one exception: a replacement is recorded under the card it replaces, which stays as it was
                var name = Uri.EscapeDataString(ReplacementName(entry));
                var under = await Ask(HttpMethod.Post, $"{project.Documents}/{ListName}/{number}/replacements?documentId={name}", body).ConfigureAwait(false);
                if (under.Status == HttpStatusCode.OK) return new SharedWrite(SharedOutcome.Sent);
                if (under.Status is not (HttpStatusCode.Conflict or HttpStatusCode.Forbidden)) return Unsent(under);
                // refused because it is already there: this same card, sent before and the answer lost
                var again = await Ask(HttpMethod.Get, $"{project.Documents}/{ListName}/{number}/replacements/{name}").ConfigureAwait(false);
                return again.Status == HttpStatusCode.OK ? new SharedWrite(SharedOutcome.Sent) : Unsent(again.Status == HttpStatusCode.NotFound ? under : again);
            }

            // this same card, sent before and the answer lost on the way back: same moment, same name, same account
            if (holder.IssuedAt == entry.IssuedAt && holder.Name == entry.Name && Value(there.Body?["fields"]?["by"]) == who.Uid) return new SharedWrite(SharedOutcome.Sent);
            return new SharedWrite(SharedOutcome.Refused, holder);
        }
        catch (RegisterException e)
        {
            lock (_gate) LastProblem = e.Message;
            return new SharedWrite(SharedOutcome.Waiting);
        }
    }

    /* ---- export -------------------------------------------------- */

    public string ToCsv()
    {
        static string Esc(object? v) => "\"" + (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"") + "\"";
        var rows = new List<string> { string.Join(',', RegisterFields.CsvColumns.Select(f => Esc(f.Name))) };
        foreach (var e in All())
        {
            rows.Add(string.Join(',', RegisterFields.CsvColumns.Select(f => Esc(f.Kind switch
            {
                RegisterFieldKind.Whole => f.WholeOf(e),
                RegisterFieldKind.YesNo => f.YesOf(e) ? "yes" : "no",
                _ => f.TextOf(e),
            }))));
        }
        return "﻿" + string.Join("\r\n", rows);
    }
}

/// <summary>Minimal RFC 4180 reader: quoted fields, doubled quotes, CR / LF / CRLF.</summary>
public static class Csv
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var s = text.StartsWith('﻿') ? text[1..] : text;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < s.Length && s[i + 1] == '"') { field.Append('"'); i++; } else quoted = false;
                }
                else field.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': quoted = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n': row.Add(field.ToString()); rows.Add(row); row = new List<string>(); field.Clear(); break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows.Where(r => r.Any(v => v.Trim().Length > 0)).ToList();
    }
}
