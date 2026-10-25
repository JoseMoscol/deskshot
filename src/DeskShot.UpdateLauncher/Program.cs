using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("DeskShot Update Launcher")]
[assembly: AssemblyProduct("DeskShot")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace DeskShot.UpdateLauncher
{
    [DataContract]
    public sealed class UpdatePlan
    {
        [DataMember] public int ParentId;
        [DataMember] public long ParentStartTicks;
        [DataMember] public string InstallerPath;
        [DataMember] public string Sha256;
        [DataMember] public string AppPath;
        [DataMember] public string TargetDirectory;
        [DataMember] public string Version;
        [DataMember] public string Nonce;
    }

    internal static class Program
    {
        private static string PlanPath;
        [STAThread]
        private static int Main(string[] args)
        {
            UpdatePlan plan = null;
            bool committed = false, parentExited = false;
            try
            {
                if (args.Length != 1) return 1;
                PlanPath = Path.GetFullPath(args[0]);
                using (var file = File.OpenRead(PlanPath))
                    plan = (UpdatePlan)new DataContractJsonSerializer(typeof(UpdatePlan)).ReadObject(file);
                ValidatePlan(plan);
                using (OpenVerifiedInstaller(plan)) { }
                File.WriteAllText(PlanPath + ".ready", plan.Nonce);
                var wait = Stopwatch.StartNew();
                while (!Signal(".go", plan.Nonce))
                {
                    if (Signal(".cancel", plan.Nonce) || wait.Elapsed > TimeSpan.FromSeconds(30) || !ParentAlive(plan)) return 2;
                    Thread.Sleep(50);
                }
                committed = true;
                using (var parent = FindParent(plan))
                    if (parent != null && !parent.WaitForExit(120000)) throw new InvalidOperationException("DeskShot no pudo cerrarse. El instalador no se ejecutó.");
                parentExited = true;
                using (var verifiedFile = OpenVerifiedInstaller(plan))
                {
                    string parameters = "/SP- /NORESTART /DESKSHOTUPDATE=1";
                    if (!String.IsNullOrEmpty(plan.TargetDirectory)) parameters += " /DIR=" + Quote(plan.TargetDirectory);
                    var start = new ProcessStartInfo(plan.InstallerPath, parameters) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(plan.InstallerPath) };
                    using (var installer = Process.Start(start))
                    {
                        if (installer == null) throw new IOException("No se pudo abrir el instalador.");
                        installer.WaitForExit();
                        if (installer.ExitCode != 0 && installer.ExitCode != 3010)
                        {
                            if (installer.ExitCode != 2) MessageBox.Show("El instalador no terminó correctamente (código " + installer.ExitCode + "). Se volverá a abrir DeskShot.", "DeskShot", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            Restart(plan.AppPath); return 3;
                        }
                    }
                }
                string installed = String.IsNullOrEmpty(plan.TargetDirectory) ? RegisteredApplication() : Path.Combine(plan.TargetDirectory, "DeskShot.exe");
                if (!IsRequestedVersion(installed, plan.Version)) throw new IOException("No se encontró la nueva versión instalada. Vuelve a ejecutar el instalador descargado.");
                Restart(installed); return 0;
            }
            catch (Exception ex)
            {
                try { if (PlanPath != null) File.WriteAllText(PlanPath + ".error", ex.Message); } catch { }
                if (committed)
                {
                    MessageBox.Show("No se pudo completar la actualización: " + ex.Message, "DeskShot", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    if (parentExited && plan != null) try { Restart(plan.AppPath); } catch { }
                }
                return 1;
            }
            finally
            {
                if (PlanPath != null) foreach (string suffix in new[] { "", ".ready", ".go", ".cancel" })
                    try { if (File.Exists(PlanPath + suffix)) File.Delete(PlanPath + suffix); } catch { }
            }
        }
        private static void ValidatePlan(UpdatePlan plan)
        {
            if (plan == null || !Regex.IsMatch(plan.Version ?? "", @"^\d+\.\d+\.\d+$") || !Regex.IsMatch(plan.Sha256 ?? "", "^[0-9a-f]{64}$") || !Regex.IsMatch(plan.Nonce ?? "", "^[0-9a-f]{32}$"))
                throw new InvalidDataException("El plan de actualización no es válido.");
            string root = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskShot", "Updates", plan.Version));
            if (!String.Equals(Path.GetDirectoryName(PlanPath), root, StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(Path.GetFullPath(plan.InstallerPath), Path.Combine(root, "DeskShot-" + plan.Version + "-win-x64-Setup.exe"), StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(Path.GetFileName(plan.AppPath), "DeskShot.exe", StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(plan.AppPath) || !File.Exists(plan.AppPath))
                throw new InvalidDataException("Las rutas de actualización no corresponden a DeskShot.");
            if (!String.IsNullOrEmpty(plan.TargetDirectory) && !String.Equals(Path.GetFullPath(plan.TargetDirectory).TrimEnd('\\'), Path.GetDirectoryName(Path.GetFullPath(plan.AppPath)).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("La carpeta de instalación no coincide con la aplicación actual.");
        }
        private static FileStream OpenVerifiedInstaller(UpdatePlan plan)
        {
            var file = new FileStream(plan.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                using (var sha = SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
                    if (actual != plan.Sha256) throw new InvalidDataException("La huella del instalador no coincide. No se ejecutará.");
                }
                var info = FileVersionInfo.GetVersionInfo(plan.InstallerPath);
                var version = new Version(plan.Version);
                if (info.ProductName != "DeskShot" || info.FileMajorPart != version.Major || info.FileMinorPart != version.Minor || info.FileBuildPart != version.Build)
                    throw new InvalidDataException("El ejecutable no corresponde a la versión esperada de DeskShot.");
                return file;
            }
            catch { file.Dispose(); throw; }
        }
        private static Process FindParent(UpdatePlan plan)
        {
            try { var process = Process.GetProcessById(plan.ParentId); if (process.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks) return process; process.Dispose(); }
            catch (ArgumentException) { }
            return null;
        }
        private static bool ParentAlive(UpdatePlan plan) { using (var process = FindParent(plan)) return process != null && !process.HasExited; }
        private static bool Signal(string suffix, string nonce) { return File.Exists(PlanPath + suffix) && File.ReadAllText(PlanPath + suffix) == nonce; }
        private static string Quote(string value)
        {
            if (value.IndexOf('"') >= 0) throw new InvalidDataException("Ruta no válida.");
            // Duplicar barras finales conserva también una carpeta raíz al cerrar las comillas.
            int trailing = value.Length - value.TrimEnd('\\').Length;
            return "\"" + value + new string('\\', trailing) + "\"";
        }
        private static string RegisteredApplication()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                using (var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                using (var key = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskShot_is1"))
                {
                    string folder = key == null ? null : key.GetValue("InstallLocation") as string;
                    if (!String.IsNullOrEmpty(folder)) return Path.Combine(folder, "DeskShot.exe");
                }
            return null;
        }
        private static bool IsRequestedVersion(string path, string expected)
        {
            if (String.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            var info = FileVersionInfo.GetVersionInfo(path);
            return info.ProductName == "DeskShot" && new Version(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart) >= new Version(expected);
        }
        private static void Restart(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("No se encuentra DeskShot.exe.");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) });
        }
    }
}
