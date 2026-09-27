using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ZombomboUpdater.Core;

public sealed record ReleaseInfo(string Version, Uri DownloadUrl, string Sha256, long Size);
public sealed class ReleaseNotFoundException : Exception
{
    public ReleaseNotFoundException() : base("Henüz yayınlanmış bir oyun build'i bulunamadı.") { }
}

public sealed class ReleaseClient(HttpClient http)
{
    private static readonly Regex HashPattern = new(@"\b[a-fA-F0-9]{64}\b", RegexOptions.Compiled);

    public async Task<ReleaseInfo> GetLatestAsync(string repository, string assetName, CancellationToken cancellationToken)
    {
        var parts = repository.Trim().Split('/');
        if (parts.Length != 2 || parts.Any(p => !Regex.IsMatch(p, @"^[A-Za-z0-9_.-]+$")))
            throw new InvalidOperationException("GitHub deposunu sahip/depo biçiminde girin.");
        if (string.IsNullOrWhiteSpace(assetName) || assetName.Contains('/') || assetName.Contains('\\'))
            throw new InvalidOperationException("ZIP dosyası adı geçersiz.");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("ZombomboUpdater", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, cancellationToken);
        if ((int)response.StatusCode == 404)
            throw new ReleaseNotFoundException();
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var version = root.GetProperty("tag_name").GetString();
        if (string.IsNullOrWhiteSpace(version)) throw new InvalidOperationException("Release sürüm etiketi boş.");

        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        var zip = assets.FirstOrDefault(a => string.Equals(a.GetProperty("name").GetString(), assetName, StringComparison.OrdinalIgnoreCase));
        var checksum = assets.FirstOrDefault(a => string.Equals(a.GetProperty("name").GetString(), assetName + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (zip.ValueKind == JsonValueKind.Undefined || checksum.ValueKind == JsonValueKind.Undefined)
            throw new InvalidOperationException($"Release içinde {assetName} ve {assetName}.sha256 bulunmalı.");
        var checksumUrl = RequireHttps(checksum.GetProperty("browser_download_url").GetString());
        using var checksumResponse = await http.GetAsync(checksumUrl, cancellationToken);
        checksumResponse.EnsureSuccessStatusCode();
        var checksumText = await checksumResponse.Content.ReadAsStringAsync(cancellationToken);
        var match = HashPattern.Match(checksumText);
        if (!match.Success) throw new InvalidOperationException("SHA-256 dosyası geçersiz.");

        return new ReleaseInfo(version, RequireHttps(zip.GetProperty("browser_download_url").GetString()),
            match.Value.ToLowerInvariant(), zip.GetProperty("size").GetInt64());
    }

    private static Uri RequireHttps(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Release indirme adresi HTTPS olmalı.");
        return uri;
    }
}
