using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

internal static partial class Program
{
    private static void CompileUpdateFixture(string source, string output, bool newer = false)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Microsoft.NET/Framework64/v4.0.30319/csc.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "/nologo", "/target:winexe", "/platform:x64", "/out:" + output }) start.ArgumentList.Add(argument);
        if (newer) start.ArgumentList.Add("/define:NEW");
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Fixtures", source));
        using var process = Process.Start(start)!; process.WaitForExit(); Check(process.ExitCode == 0, "No se pudo compilar el ejecutable sintético de actualización.");
    }
    private static void WaitForUpdate(Func<bool> condition, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(50);
        Check(condition(), message);
    }
    private static void TestUpdateHandoff()
    {
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskShot", "Updates", "1.0.0");
        Check(!Directory.Exists(cache), "La carpeta 1.0.0 ya existe; no se modifica una descarga del usuario.");
        Directory.CreateDirectory(cache);
        string folder = Path.Combine(_output, "Instalación sintética con espacios " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string app = Path.Combine(folder, "DeskShot.exe"), installer = Path.Combine(cache, "DeskShot-1.0.0-win-x64-Setup.exe");
        string helper = Path.Combine(cache, "launcher.exe"), planPath = Path.Combine(cache, "handoff-test.json");
        Process? parent = null, launcher = null;
        try
        {
            CompileUpdateFixture("UpdateApp.cs", app); CompileUpdateFixture("UpdateApp.cs", Path.Combine(cache, "new-app.exe"), newer: true);
            CompileUpdateFixture("UpdateInstaller.cs", installer);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "DeskShot.UpdateLauncher.exe"), helper);
            parent = Process.Start(new ProcessStartInfo(app) { UseShellExecute = false, CreateNoWindow = true })!;
            WaitForUpdate(() => File.Exists(Path.Combine(folder, "parent-started.txt")), "No arrancó la aplicación anterior sintética.");
            string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(installer))).ToLowerInvariant();
            string nonce = Guid.NewGuid().ToString("N");
            void Plan(string digest) => File.WriteAllText(planPath, JsonSerializer.Serialize(new { ParentId = parent.Id, ParentStartTicks = parent.StartTime.ToUniversalTime().Ticks, InstallerPath = installer,
                Sha256 = digest, AppPath = app, TargetDirectory = folder, Version = "1.0.0", Nonce = nonce }));
            Process Launch()
            {
                var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true }; start.ArgumentList.Add(planPath); return Process.Start(start)!;
            }
            // Un archivo alterado nunca llega a estar listo ni se ejecuta.
            Plan(new string('0', 64)); launcher = Launch(); Check(launcher.WaitForExit(15000) && launcher.ExitCode != 0 && !File.Exists(planPath + ".ready"), "El asistente acepta un instalador corrupto."); launcher.Dispose(); launcher = null;
            Check(!File.Exists(Path.Combine(folder, "installer-args.txt")), "Se ejecutó el instalador sin validarlo.");
            if (File.Exists(planPath + ".error")) File.Delete(planPath + ".error");
            // Preparar y cancelar no debe programar una instalación para más tarde.
            Plan(hash); launcher = Launch(); WaitForUpdate(() => File.Exists(planPath + ".ready"), "El asistente no confirmó que está listo.");
            File.WriteAllText(planPath + ".cancel", nonce); Check(launcher.WaitForExit(15000) && launcher.ExitCode == 2, "No cancela el traspaso preparado."); launcher.Dispose(); launcher = null;
            Check(!File.Exists(Path.Combine(folder, "installer-args.txt")) && !parent.HasExited, "Cancelar altera la aplicación anterior.");
            // Solo con señal explícita y tras salir el padre puede instalarse.
            Plan(hash); launcher = Launch(); WaitForUpdate(() => File.Exists(planPath + ".ready"), "El asistente no volvió a prepararse.");
            File.WriteAllText(planPath + ".go", nonce); Thread.Sleep(200);
            Check(!File.Exists(Path.Combine(folder, "installer-args.txt")), "Inició el instalador antes de cerrar la aplicación.");
            File.WriteAllText(Path.Combine(folder, "exit-parent.txt"), "exit"); Check(parent.WaitForExit(15000), "No salió el padre de prueba.");
            Check(launcher.WaitForExit(15000) && launcher.ExitCode == 0, "No terminó la instalación y el reinicio sintéticos.");
            WaitForUpdate(() => File.Exists(Path.Combine(folder, "restarted.txt")), "No volvió a abrir la nueva versión.");
            Check(File.ReadAllText(Path.Combine(folder, "installer-args.txt")).Contains("/DESKSHOTUPDATE=1"), "El asistente no evita un segundo arranque desde Setup.");
            Console.WriteLine("  Traspaso real: SHA-256, cancelación, espera del padre, carpeta con espacios/acentos y reinicio verificados sin tocar la instalación del usuario.");
        }
        finally
        {
            if (parent is not null && !parent.HasExited) { File.WriteAllText(Path.Combine(folder, "exit-parent.txt"), "exit"); parent.WaitForExit(15000); }
            if (launcher is not null)
            {
                if (!launcher.HasExited) { File.WriteAllText(planPath + ".cancel", File.Exists(planPath) ? JsonDocument.Parse(File.ReadAllText(planPath)).RootElement.GetProperty("Nonce").GetString() : ""); launcher.WaitForExit(15000); }
                launcher.Dispose();
            }
            parent?.Dispose();
            // Solo la carpeta creada por esta prueba, con archivos de nombres conocidos.
            foreach (string file in new[] { installer, helper, Path.Combine(cache, "new-app.exe"), planPath, planPath + ".error", planPath + ".ready", planPath + ".go", planPath + ".cancel" })
                if (File.Exists(file)) File.Delete(file);
            if (!Directory.EnumerateFileSystemEntries(cache).Any()) Directory.Delete(cache);
        }
    }
}
