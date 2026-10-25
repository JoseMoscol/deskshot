using System.Diagnostics;
using System.Text.Json;
using DeskShot.Application;
using Microsoft.Win32;

namespace DeskShot.Infrastructure;

public sealed class UpdateHandoff : IDisposable
{
    private readonly string _planPath, _nonce;
    private readonly Process _process;
    private bool _committed;
    private UpdateHandoff(string planPath, string nonce, Process process) { _planPath = planPath; _nonce = nonce; _process = process; }
    public static string? InstalledDirectory(string appPath)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
            using (var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskShot_is1"))
                if (key?.GetValue("InstallLocation") is string directory && !string.IsNullOrWhiteSpace(directory) && Path.IsPathFullyQualified(directory) &&
                    string.Equals(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(Path.GetFullPath(appPath))?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) return directory;
        return null;
    }
    public static async Task<UpdateHandoff> PrepareAsync(VerifiedUpdate update, string appPath, CancellationToken token)
    {
        string source = Path.Combine(AppContext.BaseDirectory, "DeskShot.UpdateLauncher.exe");
        if (!File.Exists(source)) throw new FileNotFoundException("Falta el asistente de actualización. Reinstala DeskShot conservando todos sus archivos.", source);
        string folder = Path.GetDirectoryName(update.InstallerPath)!;
        string nonce = Guid.NewGuid().ToString("N");
        string helper = Path.Combine(folder, "DeskShot.UpdateLauncher-" + nonce + ".exe");
        File.Copy(source, helper);
        string planPath = Path.Combine(folder, "handoff-" + nonce + ".json");
        using var current = Process.GetCurrentProcess();
        var plan = new { ParentId = Environment.ProcessId, ParentStartTicks = current.StartTime.ToUniversalTime().Ticks,
            update.InstallerPath, update.Sha256, AppPath = appPath, TargetDirectory = InstalledDirectory(appPath) ?? "",
            Version = update.Release.Version.ToString(3), Nonce = nonce };
        await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan), token);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
        start.ArgumentList.Add(planPath);
        var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar el asistente de actualización.");
        var handoff = new UpdateHandoff(planPath, nonce, process);
        try
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(15))
            {
                token.ThrowIfCancellationRequested();
                if (File.Exists(planPath + ".ready") && await File.ReadAllTextAsync(planPath + ".ready", token) == nonce) return handoff;
                if (process.HasExited) break;
                await Task.Delay(80, token);
            }
            string message = File.Exists(planPath + ".error") ? await File.ReadAllTextAsync(planPath + ".error", token) : "El asistente de actualización no respondió.";
            throw new IOException(message);
        }
        catch { handoff.Dispose(); throw; }
    }
    public void Commit()
    {
        if (_process.HasExited) throw new IOException("El asistente de actualización terminó antes de instalar. Inténtalo de nuevo.");
        File.WriteAllText(_planPath + ".go", _nonce); _committed = true;
    }
    public void Dispose()
    {
        if (!_committed) try { File.WriteAllText(_planPath + ".cancel", _nonce); } catch (IOException) { }
        _process.Dispose();
    }
}
