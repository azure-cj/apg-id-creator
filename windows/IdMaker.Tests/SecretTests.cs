// What may and may not be written in this repository.
//
// The shared register lives in the company's Firebase project. Nothing about
// it is written here: which project it is (its name and web API key) comes
// from repository secrets when a release is built, and what opens it is a
// person's sign-in, which is never anywhere but on their own computer,
// wrapped by Windows. The old register's address was a credential (an Apps
// Script deployment open to anyone who held it), so that is still looked for.

using System.Text.RegularExpressions;
using IdMaker.Core;

namespace IdMaker.Tests;

public partial class SecretTests
{
    // A real Apps Script deployment id is long; a made-up example in the documents is not.
    [GeneratedRegex(@"script\.google\.com/macros/s/[A-Za-z0-9_-]{20,}")] private static partial Regex Deployment();
    // A service-account key file, or any private key pasted in.
    [GeneratedRegex(@"-{5}BEGIN [A-Z ]*PRIVATE KEY-{5}|""private_key""\s*:\s*""")] private static partial Regex PrivateKey();
    // A Google API key, wherever it is written.
    [GeneratedRegex(@"AIza[0-9A-Za-z_-]{35}")] private static partial Regex ApiKey();
    // A Firebase renewal token or pass saved from a real sign-in.
    [GeneratedRegex(@"""(refresh_?[tT]oken|id_?[tT]oken)""\s*:\s*""[A-Za-z0-9._-]{40,}""")] private static partial Regex Token();

    private static readonly string[] Skip = { ".git", "bin", "obj", "node_modules", "dist" };

    private static IEnumerable<(string Path, string Text)> Files()
    {
        var stack = new Stack<DirectoryInfo>(new[] { new DirectoryInfo(Repo.Root) });
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in dir.EnumerateDirectories())
                if (!Skip.Contains(sub.Name, StringComparer.OrdinalIgnoreCase)) stack.Push(sub);
            foreach (var file in dir.EnumerateFiles())
            {
                if (file.Extension is not (".cs" or ".xaml" or ".md" or ".json" or ".yml" or ".ps1" or ".psd1" or ".iss" or ".gs" or ".mjs" or ".rules" or ".csproj" or ".props" or ".txt" or ".env")) continue;
                yield return (Path.GetRelativePath(Repo.Root, file.FullName).Replace('\\', '/'), File.ReadAllText(file.FullName));
            }
        }
    }

    [Fact]
    public void Nothing_that_opens_the_register_is_written_in_the_repository()
    {
        var hits = new List<string>();
        var looked = 0;
        foreach (var (path, text) in Files())
        {
            looked++;
            if (Deployment().IsMatch(text)) hits.Add($"{path}: an Apps Script address");
            if (PrivateKey().IsMatch(text)) hits.Add($"{path}: a private key");
            if (Token().IsMatch(text)) hits.Add($"{path}: a saved sign-in");
            if (ApiKey().IsMatch(text)) hits.Add($"{path}: a Google API key");
        }
        Assert.True(looked > 50, $"only {looked} files were read; the scan is not looking at the repository");
        Assert.True(hits.Count == 0, "written in the repository: " + string.Join("; ", hits));
    }

    [Fact]
    public void A_build_without_the_secrets_has_no_shared_register()
    {
        // CI for pull requests and developers' builds never get the secrets
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IDMAKER_FIREBASE_API_KEY"))) return;
        Assert.Null(CardConfig.Register);
        Assert.False(new Register(new MemoryStore()).Shared);
    }

    [Fact]
    public void Tests_use_a_project_that_does_not_exist()
    {
        // Firebase never serves a project whose name starts "demo-", so a test that slipped past FakeHttp reaches nobody
        Assert.StartsWith("demo-", Make.Project.ProjectId);
        Assert.DoesNotMatch(ApiKey(), Make.Project.ApiKey);
        Assert.False(Make.LocalRegister().Shared);
    }

    [Fact]
    public async Task Nothing_is_sent_to_the_register_until_somebody_signs_in()
    {
        var http = new FakeHttp((_, _) => throw new InvalidOperationException("a request was sent with nobody signed in"));
        var r = new Register(new MemoryStore(), http) { Project = Make.Project };
        Assert.True(r.Shared);
        Assert.False(r.SignedIn);
        Assert.False(await r.LoadIndex());
        Assert.False((await r.Ping()).Ok);
        Assert.Equal(SharedOutcome.Waiting, (await r.RecordShared(new RegisterEntry { Id = "APG-2026-001", Year = 2026, Seq = 1, Name = "A" })).Outcome);
        Assert.Empty(http.Requests);
    }
}
