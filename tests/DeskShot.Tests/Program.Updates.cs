using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using DeskShot;
using DeskShot.Application;
using DeskShot.Infrastructure;
using DeskShot.Presentation;

internal static partial class Program
{
    private sealed class UpdateFixture
    {
        public string Version = "99.0.0";
        public byte[] Payload = Encoding.ASCII.GetBytes("MZ-DeskShot-installer-fixture");
        public string Name => $"DeskShot-{Version}-win-x64-Setup.exe";
        public string Hash => Convert.ToHexString(SHA256.HashData(Payload)).ToLowerInvariant();
        public byte[] Checksum => Encoding.ASCII.GetBytes(Hash + "  " + Name + "\r\n");
        public string Json(bool draft = false, bool prerelease = false, string? url = null, string? name = null, long? size = null) => JsonSerializer.Serialize(new
        {
            tag_name = "v" + Version, draft, prerelease, body = "Novedades de prueba: actualización local del programa.",
            assets = new[] {
                new { name = name ?? Name, browser_download_url = url ?? $"https://github.com/JoseMoscol/deskshot/releases/download/v{Version}/{Name}", state = "uploaded", size = size ?? Payload.Length, digest = "sha256:" + Hash },
                new { name = Name + ".sha256", browser_download_url = $"https://github.com/JoseMoscol/deskshot/releases/download/v{Version}/{Name}.sha256", state = "uploaded", size = (long)Checksum.Length, digest = "sha256:" + Convert.ToHexString(SHA256.HashData(Checksum)).ToLowerInvariant() }
            }
        });
        public AvailableUpdate Release => Parse(Json());
    }
    private static AvailableUpdate Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return GitHubUpdateService.ParseRelease(document.RootElement, new Version(0, 0, 0))!;
    }
    private sealed class UpdateHttp(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Check(request.Headers.Authorization is null && request.Headers.UserAgent.Count > 0, "La consulta usa credenciales o no identifica el cliente.");
            Requests.Add(request.RequestUri!); return handler(request, token);
        }
    }
    private static HttpResponseMessage Bytes(byte[] bytes, long? length = null)
    { var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }; if (length is not null) response.Content.Headers.ContentLength = length; return response; }
    private static UpdateHttp FixtureHandler(UpdateFixture fixture) => new((request, _) => Task.FromResult(request.RequestUri!.Host == "api.github.com"
        ? Bytes(Encoding.UTF8.GetBytes(fixture.Json())) : Bytes(request.RequestUri.AbsolutePath.EndsWith(".sha256") ? fixture.Checksum : fixture.Payload)));
    private static string UpdateTestRoot() => Path.Combine(_output, "updates-" + Guid.NewGuid().ToString("N"));

    private static void TestUpdateMetadata()
    {
        var fixture = new UpdateFixture();
        using var document = JsonDocument.Parse(fixture.Json());
        Check(GitHubUpdateService.ParseRelease(document.RootElement, new Version(99, 0, 0, 0)) is null, "Notifica una versión igual con distinta revisión.");
        Check(GitHubUpdateService.ParseRelease(document.RootElement, new Version(100, 0, 0)) is null, "Permite bajar de versión.");
        foreach (string json in new[] { fixture.Json(draft: true), fixture.Json(prerelease: true) })
        { using var unstable = JsonDocument.Parse(json); Check(GitHubUpdateService.ParseRelease(unstable.RootElement, new Version(0, 0, 0)) is null, "Ofrece un borrador o versión preliminar."); }
        Check(fixture.Release.Installer.Name == fixture.Name, "No elige el instalador de Windows x64.");
        Throws<InvalidDataException>(() => Parse(fixture.Json(url: "https://github.com/otra/cuenta/setup.exe")));
        Throws<InvalidDataException>(() => Parse(fixture.Json(name: "../setup.exe")));
        Throws<InvalidDataException>(() => Parse(fixture.Json(size: GitHubUpdateService.MaxInstallerSize + 1)));
        using var handler = FixtureHandler(fixture); using var client = new HttpClient(handler); using var service = new GitHubUpdateService(client, UpdateTestRoot());
        Check(service.CheckAsync(App.CurrentVersion, CancellationToken.None).GetAwaiter().GetResult()?.Version == new Version(99, 0, 0), "No consulta la API pública.");
    }
    private static void TestUpdateDownload()
    {
        var fixture = new UpdateFixture(); using var handler = FixtureHandler(fixture); using var client = new HttpClient(handler);
        using var service = new GitHubUpdateService(client, UpdateTestRoot());
        var verified = service.DownloadAsync(fixture.Release, null, CancellationToken.None).GetAwaiter().GetResult();
        Check(File.ReadAllBytes(verified.InstallerPath).SequenceEqual(fixture.Payload) && verified.Sha256 == fixture.Hash, "La descarga cambió los bytes o la huella.");
        service.ValidateAsync(verified, CancellationToken.None).GetAwaiter().GetResult();
        int requests = handler.Requests.Count;
        service.DownloadAsync(fixture.Release, null, CancellationToken.None).GetAwaiter().GetResult();
        Check(handler.Requests.Count == requests + 1, "La caché verificada vuelve a descargar el ejecutable.");
        File.WriteAllBytes(verified.InstallerPath, Encoding.ASCII.GetBytes("alterado"));
        Throws<InvalidDataException>(() => service.ValidateAsync(verified, CancellationToken.None).GetAwaiter().GetResult());
        var repaired = service.DownloadAsync(fixture.Release, null, CancellationToken.None).GetAwaiter().GetResult();
        Check(File.ReadAllBytes(repaired.InstallerPath).SequenceEqual(fixture.Payload), "No repara una descarga corrupta.");
        Throws<InvalidDataException>(() => service.ValidateAsync(repaired with { InstallerPath = Path.Combine(_output, "fuera.exe") }, CancellationToken.None).GetAwaiter().GetResult());
    }
    private static void TestUpdateFailures()
    {
        var fixture = new UpdateFixture();
        foreach (bool truncated in new[] { false, true })
        {
            string folder = UpdateTestRoot();
            using var handler = new UpdateHttp((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(".sha256") ? Bytes(fixture.Checksum)
                : Bytes(truncated ? fixture.Payload[..^1] : fixture.Payload.Select(b => (byte)(b ^ 1)).ToArray(), fixture.Payload.Length)));
            using var client = new HttpClient(handler); using var service = new GitHubUpdateService(client, folder);
            Throws<InvalidDataException>(() => service.DownloadAsync(fixture.Release, null, CancellationToken.None).GetAwaiter().GetResult());
            Check(!Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(), "Conserva un ejecutable inválido o un archivo parcial.");
        }
        using var badHandler = new UpdateHttp((_, _) => Task.FromResult(Bytes(Encoding.ASCII.GetBytes("0  otro.exe\n"))));
        using var badClient = new HttpClient(badHandler); using var bad = new GitHubUpdateService(badClient, UpdateTestRoot());
        Throws<InvalidDataException>(() => bad.DownloadAsync(fixture.Release, null, CancellationToken.None).GetAwaiter().GetResult());
    }
    private static void TestUpdateRedirects()
    {
        var fixture = new UpdateFixture();
        foreach (string destination in new[] { "https://release-assets.githubusercontent.com/file", "http://release-assets.githubusercontent.com/file", "https://github.com.ejemplo.invalid/file" })
        {
            using var handler = new UpdateHttp((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(".sha256") ? Bytes(fixture.Checksum)
                : request.RequestUri.Host == "github.com" ? new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri(destination) } } : Bytes(fixture.Payload)));
            using var client = new HttpClient(handler); using var service = new GitHubUpdateService(client, UpdateTestRoot());
            if (destination.StartsWith("https://release-assets.")) Check(service.DownloadAsync(fixture.Release, null, CancellationToken.None).GetAwaiter().GetResult().Sha256 == fixture.Hash, "Bloquea el CDN oficial.");
            else
            {
                Throws<InvalidDataException>(() => service.DownloadAsync(fixture.Release, null, CancellationToken.None).GetAwaiter().GetResult());
                Check(handler.Requests.All(r => r.Scheme == "https" && r.Host != "github.com.ejemplo.invalid"), "Envía una solicitud al destino no permitido.");
            }
        }
    }
    private sealed class SlowUpdateContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => Task.CompletedTask;
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new SlowUpdateStream());
    }
    private sealed class SlowUpdateStream : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) { await Task.Delay(5000, token); return 0; }
    }
    private static void TestUpdateCancellation()
    {
        var fixture = new UpdateFixture(); string folder = UpdateTestRoot();
        using var handler = new UpdateHttp((request, _) => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith(".sha256") ? Bytes(fixture.Checksum) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new SlowUpdateContent() }));
        using var client = new HttpClient(handler); using var service = new GitHubUpdateService(client, folder); using var stop = new CancellationTokenSource(100);
        Throws<OperationCanceledException>(() => service.DownloadAsync(fixture.Release, null, stop.Token).GetAwaiter().GetResult());
        Check(!Directory.Exists(folder) || !Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Any(), "Cancelar deja datos incompletos.");
    }
    private sealed class FixtureUpdateService : IUpdateService
    {
        public bool Fail;
        public readonly AvailableUpdate Release = new UpdateFixture().Release;
        public Task<AvailableUpdate?> CheckAsync(Version version, CancellationToken token) => Fail ? Task.FromException<AvailableUpdate?>(new IOException("Sin conexión de prueba.")) : Task.FromResult<AvailableUpdate?>(Release);
        public Task<VerifiedUpdate> DownloadAsync(AvailableUpdate update, IProgress<UpdateDownloadProgress>? progress, CancellationToken token)
        { progress?.Report(new(100, 100)); return Task.FromResult(new VerifiedUpdate(update, "no-se-ejecuta.exe", new string('0', 64))); }
        public Task ValidateAsync(VerifiedUpdate update, CancellationToken token) => Task.CompletedTask;
    }
    private static void TestUpdateWindow(App app)
    {
        var service = new FixtureUpdateService(); var window = new UpdatesWindow(app, service, checkOnLoad: false);
        window.CheckAsync().GetAwaiter().GetResult();
        Check(((Button)window.FindName("DownloadButton")).Visibility == Visibility.Visible && ((TextBox)window.FindName("NotesBox")).Text.Contains("Novedades"), "No muestra la versión ni sus novedades.");
        window.DownloadAsync().GetAwaiter().GetResult();
        Check(((Button)window.FindName("InstallButton")).Visibility == Visibility.Visible && ((TextBlock)window.FindName("DownloadText")).Text.Contains("SHA-256"), "La descarga no habilita la instalación verificada.");
        RenderContent(window, 644, 611, "updates-ready.png");
        service.Fail = true; window.CheckAsync().GetAwaiter().GetResult();
        Check(((TextBlock)window.FindName("StatusText")).Text.Contains("Sin conexión") && ((Button)window.FindName("CheckButton")).IsEnabled, "La interfaz no permite reintentar sin conexión.");
        Check(!new DeskShot.Domain.AppSettings().CheckUpdatesOnStartup, "La comprobación de red se activa sin elegirla.");
    }
    private static int ShowUpdatesDemo(App app)
    {
        var window = new UpdatesWindow(app, new FixtureUpdateService()); window.Closed += (_, _) => app.Shutdown(); app.Run(window); return 0;
    }
    private static void TestUpdateNetwork()
    {
        using var service = new GitHubUpdateService(downloadRoot: Path.Combine(_output, "update-network"));
        var release = service.CheckAsync(new Version(0, 0, 0), CancellationToken.None).GetAwaiter().GetResult() ?? throw new InvalidOperationException("No se encuentra un Release estable.");
        var downloaded = service.DownloadAsync(release, null, CancellationToken.None).GetAwaiter().GetResult();
        service.ValidateAsync(downloaded, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("  GitHub: " + release.Tag + "; SHA256: " + downloaded.Sha256 + "; no se ejecutó el instalador.");
    }
}
