using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using ZombomboUpdater.Core;

var root = Path.Combine(Path.GetTempPath(), "ZombomboUpdaterTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var installed = Path.Combine(root, "game");
    var first = MakeZip(("game.exe", "v1"), ("old.txt", "old"));
    await Install(first, "v1", installed);
    Check(File.ReadAllText(Path.Combine(installed, "game.exe")) == "v1", "First install");
    File.WriteAllText(Path.Combine(installed, "save.dat"), "player save");

    var second = MakeZip(("game.exe", "v2"), ("data/new.txt", "new"));
    await Install(second, "v2", installed);
    Check(File.ReadAllText(Path.Combine(installed, "game.exe")) == "v2", "Update replaces managed files");
    Check(!File.Exists(Path.Combine(installed, "old.txt")), "Obsolete managed file removed");
    Check(File.ReadAllText(Path.Combine(installed, "save.dat")) == "player save", "Unmanaged save preserved");
    Check(GameInstaller.ReadState(installed)?.Version == "v2", "Version recorded");

    using (var locked = new FileStream(Path.Combine(installed, "game.exe"), FileMode.Open, FileAccess.Read, FileShare.None))
    {
        try
        {
            await Install(first, "v3", installed);
            throw new Exception("Locked file test did not fail");
        }
        catch (IOException) { }
    }
    Check(File.ReadAllText(Path.Combine(installed, "data/new.txt")) == "new", "Failed update restores moved files");
    Check(GameInstaller.ReadState(installed)?.Version == "v2", "Failed update keeps version");

    try
    {
        var wrongHash = new ReleaseInfo("v3", new Uri("https://example.test/game.zip"), new string('0', 64), second.Length);
        await new GameInstaller(new HttpClient(new BytesHandler(second))).InstallAsync(wrongHash, installed);
        throw new Exception("Checksum test did not fail");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("SHA-256")) { }
    Check(GameInstaller.ReadState(installed)?.Version == "v2", "Checksum failure preserves install");

    var malicious = MakeZip(("../outside.txt", "unsafe"));
    var maliciousPath = Path.Combine(root, "malicious.zip");
    File.WriteAllBytes(maliciousPath, malicious);
    try
    {
        await GameInstaller.ExtractSafeAsync(maliciousPath, Path.Combine(root, "stage"));
        throw new Exception("Traversal test did not fail");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("güvensiz")) { }
    Check(!File.Exists(Path.Combine(root, "outside.txt")), "ZIP traversal blocked");
    Console.WriteLine("All integration checks passed.");
}
finally
{
    Directory.Delete(root, recursive: true);
}

static async Task Install(byte[] zip, string version, string destination)
{
    using var http = new HttpClient(new BytesHandler(zip));
    var release = new ReleaseInfo(version, new Uri("https://example.test/game.zip"),
        Convert.ToHexString(SHA256.HashData(zip)), zip.Length);
    await new GameInstaller(http).InstallAsync(release, destination);
}

static byte[] MakeZip(params (string Name, string Content)[] entries)
{
    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
    return memory.ToArray();
}

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("Failed: " + name);
    Console.WriteLine("PASS " + name);
}

sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });
}
