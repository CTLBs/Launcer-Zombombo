using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace ZombomboUpdater.Core;

public sealed record InstallState(string Version, string[] Files);
public sealed record UpdateProgress(string Status, int? Percent = null);

public sealed class GameInstaller(HttpClient http)
{
    public const string StateFileName = ".zombombo-update.json";

    public static InstallState? ReadState(string installDirectory)
    {
        var path = Path.Combine(installDirectory, StateFileName);
        return File.Exists(path) ? JsonSerializer.Deserialize<InstallState>(File.ReadAllText(path)) : null;
    }

    public async Task InstallAsync(ReleaseInfo release, string installDirectory,
        IProgress<UpdateProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var destination = Path.GetFullPath(installDirectory);
        if (destination == Path.GetPathRoot(destination))
            throw new InvalidOperationException("Disk kökünü yükleme klasörü olarak seçmeyin.");
        if (destination.TrimEnd(Path.DirectorySeparatorChar).Equals(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Güncelleyicinin bulunduğu klasör oyun klasörü olamaz.");
        Directory.CreateDirectory(destination);
        var workspace = Path.Combine(destination, ".zombombo-work-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        var archivePath = Path.Combine(workspace, "game.zip");
        var stage = Path.Combine(workspace, "stage");
        var backup = Path.Combine(workspace, "backup");
        try
        {
            progress?.Report(new("İndiriliyor", 0));
            await DownloadAsync(release, archivePath, progress, cancellationToken);
            progress?.Report(new("Dosyalar hazırlanıyor"));
            var files = await ExtractSafeAsync(archivePath, stage, cancellationToken);
            progress?.Report(new("Güncelleme uygulanıyor"));
            Apply(stage, backup, destination, release.Version, files);
            progress?.Report(new("Güncel", 100));
        }
        finally
        {
            try { Directory.Delete(workspace, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task DownloadAsync(ReleaseInfo release, string archivePath,
        IProgress<UpdateProgress>? progress, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(archivePath);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long received = 0;
        var total = response.Content.Headers.ContentLength ?? release.Size;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hasher.AppendData(buffer, 0, read);
            received += read;
            if (total > 0) progress?.Report(new("İndiriliyor", (int)Math.Min(99, received * 100 / total)));
        }
        var actual = Convert.ToHexString(hasher.GetHashAndReset());
        if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("İndirilen dosyanın SHA-256 doğrulaması başarısız.");
    }

    public static async Task<string[]> ExtractSafeAsync(string archivePath, string stage, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(stage);
        var stageRoot = Path.GetFullPath(stage) + Path.DirectorySeparatorChar;
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count == 0) throw new InvalidOperationException("ZIP arşivi boş.");
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = entry.FullName.Replace('\\', '/');
            if (raw.StartsWith('/') || raw.Contains(':') || raw.Split('/').Any(p => p is ".." or "."))
                throw new InvalidOperationException("ZIP içinde güvensiz dosya yolu var.");
            var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            if (parts.Any(p => p.EndsWith(' ') || p.EndsWith('.') || IsWindowsDeviceName(p)))
                throw new InvalidOperationException("ZIP içinde Windows için geçersiz dosya adı var.");
            if (parts[0].Equals(StateFileName, StringComparison.OrdinalIgnoreCase) ||
                parts[0].StartsWith(".zombombo-work-", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ZIP, güncelleyiciye ayrılmış dosyaları içeremez.");
            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000) throw new InvalidOperationException("ZIP içinde sembolik bağlantı olamaz.");
            var relative = Path.Combine(parts);
            var target = Path.GetFullPath(Path.Combine(stage, relative));
            if (!target.StartsWith(stageRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ZIP dosyası hedef klasörün dışına çıkıyor.");
            if (raw.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            if (!files.Add(relative)) throw new InvalidOperationException("ZIP içinde yinelenen dosya adı var.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = File.Create(target);
            await input.CopyToAsync(output, cancellationToken);
        }
        if (files.Count == 0) throw new InvalidOperationException("ZIP içinde oyun dosyası yok.");
        return files.Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsWindowsDeviceName(string name)
    {
        var stem = name.Split('.')[0];
        return new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }
            .Contains(stem, StringComparer.OrdinalIgnoreCase);
    }

    private static void Apply(string stage, string backup, string destination, string version, string[] files)
    {
        var previous = ReadState(destination);
        var oldFiles = previous?.Files ?? [];
        if (oldFiles.Any(relative => !IsSafeRelativePath(relative)))
            throw new InvalidOperationException("Kurulu sürümün dosya listesi geçersiz.");
        foreach (var relative in oldFiles.Concat(files).Distinct(StringComparer.OrdinalIgnoreCase))
            EnsureNoReparsePoints(destination, relative);

        var toBackup = oldFiles.Concat(files).Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(relative => File.Exists(Path.Combine(destination, relative))).ToArray();
        var installed = new List<string>();
        var backedUp = new List<string>();
        var statePath = Path.Combine(destination, StateFileName);
        var stateBackup = Path.Combine(backup, StateFileName);
        Directory.CreateDirectory(backup);
        try
        {
            foreach (var relative in toBackup)
            {
                var from = Path.Combine(destination, relative);
                var to = Path.Combine(backup, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
                backedUp.Add(relative);
            }
            foreach (var relative in files)
            {
                var from = Path.Combine(stage, relative);
                var to = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
                installed.Add(relative);
            }
            var stateTemp = statePath + ".tmp";
            File.WriteAllText(stateTemp, JsonSerializer.Serialize(new InstallState(version, files)));
            if (File.Exists(statePath)) File.Move(statePath, stateBackup);
            File.Move(stateTemp, statePath);
        }
        catch
        {
            foreach (var relative in installed) File.Delete(Path.Combine(destination, relative));
            foreach (var relative in backedUp)
            {
                var from = Path.Combine(backup, relative);
                var to = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Move(from, to);
            }
            if (File.Exists(stateBackup)) File.Move(stateBackup, statePath, true);
            throw;
        }
    }

    private static void EnsureNoReparsePoints(string destination, string relative)
    {
        var path = destination;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            path = Path.Combine(path, part);
            if ((File.Exists(path) || Directory.Exists(path)) &&
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Yükleme klasöründe bağlantı içeren bir yol var.");
        }
    }

    private static bool IsSafeRelativePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':')) return false;
        var parts = relative.Replace('\\', '/').Split('/');
        return parts.All(p => p.Length > 0 && p is not ("." or ".."));
    }
}
