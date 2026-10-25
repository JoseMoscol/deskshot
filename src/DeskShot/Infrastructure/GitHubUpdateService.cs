using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeskShot.Application;

namespace DeskShot.Infrastructure;

public sealed class GitHubUpdateService : IUpdateService, IDisposable
{
    public const string Repository = "JoseMoscol/deskshot";
    public const long MaxInstallerSize = 500_000_000;
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _downloadRoot;
    public GitHubUpdateService(HttpClient? client = null, string? downloadRoot = null)
    {
        _ownsClient = client is null;
        _client = client ?? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15) }) { Timeout = Timeout.InfiniteTimeSpan };
        _downloadRoot = Path.GetFullPath(downloadRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskShot", "Updates"));
    }
    public static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));
    public async Task<AvailableUpdate?> CheckAsync(Version current, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var response = await GetAsync(new Uri($"https://api.github.com/repos/{Repository}/releases/latest"), true, timeout.Token).ConfigureAwait(false);
        byte[] data = await ReadLimitedAsync(response, 1_000_000, timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(data);
        return ParseRelease(document.RootElement, current);
    }
    internal static AvailableUpdate? ParseRelease(JsonElement release, Version current)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v\d+\.\d+\.\d+$") || !Version.TryParse(tag[1..], out var version) || tag != "v" + version.ToString(3))
            throw new InvalidDataException("La versión publicada no tiene un formato compatible.");
        if (Normalize(version) <= Normalize(current)) return null;
        string name = $"DeskShot-{version.ToString(3)}-win-x64-Setup.exe";
        var assets = release.GetProperty("assets").EnumerateArray().ToArray();
        UpdateAsset ReadAsset(string expectedName, long maxSize)
        {
            var matching = assets.Where(a => a.GetProperty("name").GetString() == expectedName).ToArray();
            if (matching.Length != 1) throw new InvalidDataException("Esta versión aún no tiene todos los archivos de actualización.");
            var asset = matching[0]; long size = asset.GetProperty("size").GetInt64();
            if (size <= 0 || size > maxSize || asset.GetProperty("state").GetString() != "uploaded")
                throw new InvalidDataException("El archivo de actualización está incompleto o excede el tamaño permitido.");
            string expectedUrl = $"https://github.com/{Repository}/releases/download/{tag}/{expectedName}";
            if (asset.GetProperty("browser_download_url").GetString() != expectedUrl)
                throw new InvalidDataException("El archivo no pertenece al repositorio de DeskShot.");
            string? digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
            if (digest is not null && !Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$")) throw new InvalidDataException("Huella del archivo no válida.");
            return new(expectedName, new Uri(expectedUrl), size, digest?.ToLowerInvariant());
        }
        return new(version, tag, (release.TryGetProperty("body", out var body) ? body.GetString() : null) ?? "Sin notas de versión.",
            new Uri($"https://github.com/{Repository}/releases/tag/{tag}"), ReadAsset(name, MaxInstallerSize), ReadAsset(name + ".sha256", 1024));
    }
    public async Task<VerifiedUpdate> DownloadAsync(AvailableUpdate update, IProgress<UpdateDownloadProgress>? progress, CancellationToken token)
    {
        ValidateRelease(update);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(20));
        token = timeout.Token;
        using var checksumResponse = await GetAsync(update.Checksum.Url, false, token).ConfigureAwait(false);
        byte[] checksumBytes = await ReadLimitedAsync(checksumResponse, 1024, token).ConfigureAwait(false);
        if (checksumBytes.Length != update.Checksum.Size) throw new InvalidDataException("El archivo SHA-256 llegó incompleto.");
        CheckDigest(checksumBytes, update.Checksum.Digest);
        var match = Regex.Match(Encoding.UTF8.GetString(checksumBytes), @"\A([0-9a-fA-F]{64})[ \t]+\*?([^\r\n]+)\r?\n?\z");
        if (!match.Success || match.Groups[2].Value != update.Installer.Name) throw new InvalidDataException("La huella no corresponde al instalador esperado.");
        string expectedHash = match.Groups[1].Value.ToLowerInvariant();
        if (update.Installer.Digest is not null && update.Installer.Digest != "sha256:" + expectedHash)
            throw new InvalidDataException("Las huellas publicadas no coinciden.");
        string folder = Path.Combine(_downloadRoot, update.Version.ToString(3)); Directory.CreateDirectory(folder);
        foreach (string partial in Directory.EnumerateFiles(folder, update.Installer.Name + ".*.part"))
            if (Regex.IsMatch(Path.GetFileName(partial), "^" + Regex.Escape(update.Installer.Name) + "\\.[0-9a-f]{32}\\.part$"))
                try { File.Delete(partial); } catch (IOException) { }
        string destination = Path.Combine(folder, update.Installer.Name);
        var verified = new VerifiedUpdate(update, destination, expectedHash);
        if (File.Exists(destination))
        {
            try { await ValidateAsync(verified, token).ConfigureAwait(false); progress?.Report(new(update.Installer.Size, update.Installer.Size)); return verified; }
            catch (InvalidDataException) { /* Volver a descargar una copia incompleta o alterada. */ }
        }
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var response = await GetAsync(update.Installer.Url, false, token).ConfigureAwait(false);
            if (response.Content.Headers.ContentLength is long contentLength && contentLength != update.Installer.Size)
                throw new InvalidDataException("El tamaño de descarga no coincide con la versión publicada.");
            await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0; byte[] buffer = new byte[65536]; var clock = Stopwatch.StartNew();
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                while (true)
                {
                    int count = await input.ReadAsync(buffer, token).ConfigureAwait(false); if (count == 0) break;
                    received += count;
                    if (received > update.Installer.Size) throw new InvalidDataException("La descarga supera el tamaño esperado.");
                    hash.AppendData(buffer, 0, count); await output.WriteAsync(buffer.AsMemory(0, count), token).ConfigureAwait(false);
                    if (clock.ElapsedMilliseconds >= 150) { progress?.Report(new(received, update.Installer.Size)); clock.Restart(); }
                }
            }
            string actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            if (received != update.Installer.Size || actualHash != expectedHash) throw new InvalidDataException("La descarga no superó la verificación SHA-256. No se ejecutará.");
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
            progress?.Report(new(received, received)); return verified;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task ValidateAsync(VerifiedUpdate update, CancellationToken token)
    {
        ValidateRelease(update.Release);
        string expected = Path.Combine(_downloadRoot, update.Release.Version.ToString(3), update.Release.Installer.Name);
        if (!string.Equals(Path.GetFullPath(update.InstallerPath), expected, StringComparison.OrdinalIgnoreCase) || !Regex.IsMatch(update.Sha256, "^[0-9a-f]{64}$"))
            throw new InvalidDataException("La actualización no pertenece a la caché de DeskShot.");
        if (update.Release.Installer.Digest is not null && update.Release.Installer.Digest != "sha256:" + update.Sha256)
            throw new InvalidDataException("La huella del instalador no corresponde a la versión publicada.");
        await using var file = new FileStream(expected, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (file.Length != update.Release.Installer.Size || Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false)).ToLowerInvariant() != update.Sha256)
            throw new InvalidDataException("El instalador cambió después de descargarlo. Vuelve a descargar la actualización.");
    }
    private static void ValidateRelease(AvailableUpdate update)
    {
        string name = $"DeskShot-{Normalize(update.Version).ToString(3)}-win-x64-Setup.exe";
        string prefix = $"https://github.com/{Repository}/releases/download/v{Normalize(update.Version).ToString(3)}/";
        if (update.Tag != "v" + Normalize(update.Version).ToString(3) || update.Installer.Name != name || update.Checksum.Name != name + ".sha256" ||
            update.Installer.Url.AbsoluteUri != prefix + name || update.Checksum.Url.AbsoluteUri != prefix + name + ".sha256" ||
            update.Installer.Size <= 0 || update.Installer.Size > MaxInstallerSize || update.Checksum.Size <= 0 || update.Checksum.Size > 1024)
            throw new InvalidDataException("Los archivos de la actualización no son válidos.");
    }
    private async Task<HttpResponseMessage> GetAsync(Uri uri, bool api, CancellationToken token)
    {
        for (int redirects = 0; redirects <= 5; redirects++)
        {
            if (uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length != 0 || !AllowedHost(uri.Host, api))
                throw new InvalidDataException("La descarga intentó salir de los servidores permitidos de GitHub.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("DeskShot-Updater/1.0");
            if (api) { request.Headers.Accept.ParseAdd("application/vnd.github+json"); request.Headers.Add("X-GitHub-Api-Version", "2022-11-28"); }
            var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var location = response.Headers.Location; response.Dispose();
                if (location is null) throw new InvalidDataException("GitHub devolvió una redirección sin destino.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location); continue;
            }
            if (!response.IsSuccessStatusCode)
            {
                var code = response.StatusCode; response.Dispose();
                if (code is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) throw new HttpRequestException("GitHub limitó las consultas. Inténtalo de nuevo más tarde.");
                if (code == HttpStatusCode.NotFound) throw new HttpRequestException("No se encontró la versión o el archivo publicado.");
                throw new HttpRequestException("GitHub no pudo completar la solicitud. Código " + (int)code + ".");
            }
            return response;
        }
        throw new InvalidDataException("Demasiadas redirecciones al descargar la actualización.");
    }
    private static bool AllowedHost(string host, bool api) => api ? host == "api.github.com" : host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com" or "github-releases.githubusercontent.com";
    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream(); byte[] buffer = new byte[4096];
        while (true) { int count = await stream.ReadAsync(buffer, token).ConfigureAwait(false); if (count == 0) break; if (memory.Length + count > limit) throw new InvalidDataException("La respuesta de actualización excede el tamaño permitido."); memory.Write(buffer, 0, count); }
        return memory.ToArray();
    }
    private static void CheckDigest(byte[] bytes, string? expected)
    {
        if (expected is not null && "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() != expected)
            throw new InvalidDataException("El archivo de huella llegó alterado.");
    }
    public void Dispose() { if (_ownsClient) _client.Dispose(); }
}
