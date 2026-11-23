// Stand-ins for the shared register's services, so no test ever calls the
// real project: an HTTP handler that answers from a function, a keeper that
// wraps a secret the way Windows would, and a Firebase project that exists
// only in memory. Shared with IdMaker.App.Tests, which links this file to
// drive the real window against the same pretend project.

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using IdMaker.Core;

namespace IdMaker.Tests;

/// <summary>Answers the register's requests from a function, and counts them.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, Task<HttpResponseMessage>> _answer;
    public List<string> Requests { get; } = new();

    public FakeHttp(Func<HttpRequestMessage, string, Task<HttpResponseMessage>> answer) { _answer = answer; }

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (Requests) Requests.Add($"{request.Method} {request.RequestUri}");
        return await _answer(request, body);
    }
}

/// <summary>Wraps a secret the way Windows would, only reversibly and in plain sight, and counts how often it is asked.</summary>
internal sealed class FakeKeeper : ISecretKeeper
{
    public bool Broken { get; set; }
    public string? Protect(string secret) => Broken ? null : "kept:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(secret));
    public string? Unprotect(string kept)
    {
        if (Broken || !kept.StartsWith("kept:", StringComparison.Ordinal)) return null;
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(kept[5..])); }
        catch (FormatException) { return null; }
    }
}

/// <summary>
/// A Firebase project that exists only in memory: sign-in, renewing a
/// sign-in, and the register's list in Firestore, answering the same
/// requests with the same shapes and refusals as the real services, and
/// holding to the register's rules (members only; create, never change).
/// </summary>
internal sealed class FakeFirebase
{
    public const string Email = "hr@apg.test";
    public const string Password = "correct horse";
    public const string Uid = "uid-hr";

    /// <summary>A project that does not exist ("demo-" projects are never served by Firebase), so a test that slipped past FakeHttp could not reach anybody's data.</summary>
    public static readonly RegisterProject Project = new("demo-idmaker", "test-api-key");

    public sealed record Account(string Password, string Uid, bool Member = true, bool Disabled = false);

    public Dictionary<string, Account> Accounts { get; } = new() { [Email] = new Account(Password, Uid) };
    /// <summary>The list, by path under idreg: "APG-2026-124", or "APG-2026-124/replacements/20261117T013000000Z".</summary>
    public SortedDictionary<string, JsonObject> Docs { get; } = new(StringComparer.Ordinal);
    /// <summary>The computer is offline: every request fails before it is answered.</summary>
    public bool Offline { get; set; }
    /// <summary>The register (not sign-in) answers every request with this.</summary>
    public HttpStatusCode? Failing { get; set; }
    /// <summary>Reads of the list wait for this before they answer.</summary>
    public Task? ListGate { get; set; }
    /// <summary>
    /// How a create of a document that is already there is refused: 409 when Firestore looks before asking the
    /// rules, 403 when the rules (which allow no change) answer first. The app must take either.
    /// </summary>
    public HttpStatusCode AlreadyThere { get; set; } = HttpStatusCode.Conflict;
    /// <summary>A write is made, but its answer is lost on the way back.</summary>
    public bool LoseNextAnswer { get; set; }
    public int SignIns { get; private set; }
    public int Renewals { get; private set; }
    public int Lists { get; private set; }
    public int Creates { get; private set; }
    public FakeHttp Http { get; }
    public List<string> Requests => Http.Requests;

    private readonly Dictionary<string, string> _passes = new();        // id token -> uid
    private readonly Dictionary<string, string> _renewals = new();      // refresh token -> uid
    private int _n;

    public FakeFirebase() { Http = new FakeHttp(Answer); }

    /// <summary>Every pass handed out so far stops working, as it does after an hour.</summary>
    public void ExpirePasses() => _passes.Clear();
    /// <summary>Every sign-in is cancelled, as when a password is changed or an account turned off.</summary>
    public void CancelSignIns() { _passes.Clear(); _renewals.Clear(); }

    public void Put(RegisterEntry e, string by = "uid-someone-else") => Docs[e.Id] = Fields(e, by);

    public RegisterEntry? Get(string path) => Docs.TryGetValue(path, out var f) ? Entry(path, f) : null;

    private static JsonObject Fields(RegisterEntry e, string by) => new()
    {
        ["id"] = S(e.Id), ["year"] = new JsonObject { ["integerValue"] = e.Year.ToString() }, ["seq"] = new JsonObject { ["integerValue"] = e.Seq.ToString() },
        ["name"] = S(e.Name), ["title"] = S(e.Title), ["issuedAt"] = S(e.IssuedAt), ["replacement"] = new JsonObject { ["booleanValue"] = e.Replacement },
        ["by"] = S(by),
    };

    private static JsonObject S(string v) => new() { ["stringValue"] = v };
    private static string V(JsonObject f, string name) =>
        f[name] is JsonObject o ? (o["stringValue"] ?? o["integerValue"] ?? o["booleanValue"])?.ToString() ?? "" : "";

    private static RegisterEntry Entry(string path, JsonObject f) => new()
    {
        Id = path.Split('/')[0], Name = V(f, "name"), Title = V(f, "title"), IssuedAt = V(f, "issuedAt"),
        Year = int.TryParse(V(f, "year"), out var y) ? y : 0, Seq = int.TryParse(V(f, "seq"), out var q) ? q : 0,
        Replacement = V(f, "replacement") == "true",
    };

    private static HttpResponseMessage Error(HttpStatusCode status, string message, string state = "") =>
        FakeHttp.Json(new JsonObject { ["error"] = new JsonObject { ["code"] = (int)status, ["message"] = message, ["status"] = state } }.ToJsonString(), status);

    private static Dictionary<string, string> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2)).ToDictionary(p => Uri.UnescapeDataString(p[0]), p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : "");

    private async Task<HttpResponseMessage> Answer(HttpRequestMessage request, string body)
    {
        if (Offline) throw new HttpRequestException("no route to host");
        // SAFETY: HttpClient never sends a request without an address.
        var uri = request.RequestUri!;
        var query = Query(uri);

        if (uri.Host == "identitytoolkit.googleapis.com")
        {
            SignIns++;
            if (query.GetValueOrDefault("key") != Project.ApiKey) return Error(HttpStatusCode.BadRequest, "API key not valid. Please pass a valid API key.", "INVALID_ARGUMENT");
            var asked = JsonNode.Parse(body)!;
            var email = asked["email"]?.ToString() ?? "";
            if (!Accounts.TryGetValue(email, out var account) || account.Password != asked["password"]?.ToString()) return Error(HttpStatusCode.BadRequest, "INVALID_LOGIN_CREDENTIALS");
            if (account.Disabled) return Error(HttpStatusCode.BadRequest, "USER_DISABLED");
            var (pass, renewal) = ($"pass-{++_n}", $"renewal-{_n}");
            _passes[pass] = account.Uid;
            _renewals[renewal] = account.Uid;
            return FakeHttp.Json(new JsonObject { ["idToken"] = pass, ["refreshToken"] = renewal, ["localId"] = account.Uid, ["email"] = email, ["expiresIn"] = "3600" }.ToJsonString());
        }

        if (uri.Host == "securetoken.googleapis.com")
        {
            Renewals++;
            var form = body.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
            if (form.GetValueOrDefault("grant_type") != "refresh_token" || !_renewals.TryGetValue(form.GetValueOrDefault("refresh_token") ?? "", out var uid))
                return Error(HttpStatusCode.BadRequest, "TOKEN_EXPIRED");
            var pass = $"pass-{++_n}";
            _passes[pass] = uid;
            return FakeHttp.Json(new JsonObject { ["id_token"] = pass, ["refresh_token"] = form["refresh_token"], ["expires_in"] = "3600", ["user_id"] = uid }.ToJsonString());
        }

        if (uri.Host != "firestore.googleapis.com") return new HttpResponseMessage(HttpStatusCode.NotFound);
        if (Failing is { } failing) return new HttpResponseMessage(failing);
        var root = $"/v1/projects/{Project.ProjectId}/databases/(default)/documents/keystone/{Project.Company}/idreg";
        var path = Uri.UnescapeDataString(uri.AbsolutePath);
        if (!path.StartsWith(root, StringComparison.Ordinal)) return Error(HttpStatusCode.Forbidden, "Missing or insufficient permissions.", "PERMISSION_DENIED");
        var bearer = request.Headers.Authorization is { Scheme: "Bearer", Parameter: { } p } ? p : "";
        if (!_passes.TryGetValue(bearer, out var who)) return Error(HttpStatusCode.Unauthorized, "Request had invalid authentication credentials.", "UNAUTHENTICATED");
        if (!Accounts.Values.Any(a => a.Uid == who && a.Member)) return Error(HttpStatusCode.Forbidden, "Missing or insufficient permissions.", "PERMISSION_DENIED");
        var rest = path[root.Length..].Trim('/');
        string Name(string doc) => $"projects/{Project.ProjectId}/databases/(default)/documents/keystone/{Project.Company}/idreg/{doc}";

        if (request.Method == HttpMethod.Get && rest.Length == 0)
        {
            Lists++;
            if (ListGate is { } gate) await gate;
            var size = int.TryParse(query.GetValueOrDefault("pageSize"), out var n) ? n : 300;
            var from = int.TryParse(query.GetValueOrDefault("pageToken"), out var t) ? t : 0;
            var top = Docs.Where(d => !d.Key.Contains('/')).ToList();
            var answer = new JsonObject();
            var page = top.Skip(from).Take(size).ToList();
            if (page.Count > 0) answer["documents"] = new JsonArray(page.Select(d => (JsonNode)new JsonObject { ["name"] = Name(d.Key), ["fields"] = d.Value.DeepClone() }).ToArray());
            if (from + size < top.Count) answer["nextPageToken"] = (from + size).ToString();
            return FakeHttp.Json(answer.ToJsonString());
        }

        if (request.Method == HttpMethod.Get)
        {
            return Docs.TryGetValue(rest, out var found)
                ? FakeHttp.Json(new JsonObject { ["name"] = Name(rest), ["fields"] = found.DeepClone() }.ToJsonString())
                : Error(HttpStatusCode.NotFound, "Document not found.", "NOT_FOUND");
        }

        // the rules: a card is created, as the account asking, and never changed or removed
        if (request.Method != HttpMethod.Post) return Error(HttpStatusCode.Forbidden, "Missing or insufficient permissions.", "PERMISSION_DENIED");
        Creates++;
        var id = query.GetValueOrDefault("documentId") ?? "";
        var key = rest.Length == 0 ? id : $"{rest}/{id}";
        // SAFETY: the client always sends a document with its fields; a test that does not is wrong and should fail here.
        var fields = (JsonObject)JsonNode.Parse(body)!["fields"]!;
        if (id.Length == 0 || V(fields, "by") != who) return Error(HttpStatusCode.Forbidden, "Missing or insufficient permissions.", "PERMISSION_DENIED");
        if (Docs.ContainsKey(key))
        {
            return AlreadyThere == HttpStatusCode.Conflict
                ? Error(HttpStatusCode.Conflict, "Document already exists: " + Name(key), "ALREADY_EXISTS")
                : Error(HttpStatusCode.Forbidden, "Missing or insufficient permissions.", "PERMISSION_DENIED");
        }
        // SAFETY: DeepClone of a JsonObject is a JsonObject.
        Docs[key] = (JsonObject)fields.DeepClone();
        if (LoseNextAnswer) { LoseNextAnswer = false; throw new HttpRequestException("connection reset"); }
        return FakeHttp.Json(new JsonObject { ["name"] = Name(key), ["fields"] = fields.DeepClone() }.ToJsonString());
    }
}
