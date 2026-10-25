using System;
using System.IO;
using System.Reflection;
using System.Threading;
[assembly: AssemblyProduct("DeskShot")]
#if NEW
[assembly: AssemblyFileVersion("1.0.0.0")]
#else
[assembly: AssemblyFileVersion("0.9.0.0")]
#endif
internal static class UpdateApp
{
    private static void Main()
    {
        string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
#if NEW
        File.WriteAllText(Path.Combine(folder, "restarted.txt"), "new");
#else
        File.WriteAllText(Path.Combine(folder, "parent-started.txt"), "old");
        for (int i = 0; i < 300 && !File.Exists(Path.Combine(folder, "exit-parent.txt")); i++) Thread.Sleep(100);
#endif
    }
}
