using System;
using System.IO;
using System.Reflection;
[assembly: AssemblyProduct("DeskShot")]
[assembly: AssemblyFileVersion("1.0.0.0")]
internal static class UpdateInstaller
{
    private static int Main(string[] args)
    {
        string folder = null;
        foreach (string arg in args) if (arg.StartsWith("/DIR=", StringComparison.OrdinalIgnoreCase)) folder = arg.Substring(5);
        if (String.IsNullOrEmpty(folder)) return 10;
        File.WriteAllText(Path.Combine(folder, "installer-args.txt"), String.Join("\n", args));
        File.Copy(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "new-app.exe"), Path.Combine(folder, "DeskShot.exe"), true);
        return 0;
    }
}
