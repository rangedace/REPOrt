using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Launcher
{
    const string Game = "REPO.exe";


    [STAThread]
    static int Main(string[] args)
    {
        try {
            if (args.Length == 2 && args[0] == "--session") {
                RunSession(Path.GetFullPath(args[1]));
                return 0;
            }
            EnsureSteam();
            string source = Locate();
            if (source == null) return 0;
            // A separate worker owns the temporary directory until Unity exits.
            var worker = new ProcessStartInfo(Application.ExecutablePath,
                "--session \"" + source + "\"");
            worker.UseShellExecute = false;
            Process.Start(worker).Dispose();
            return 0;
        } catch (Exception e) {
            MessageBox.Show(e.Message, "REPOrt", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    static void EnsureSteam()
    {
        if (Process.GetProcessesByName("steam").Length == 0) {
            string exe = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam","SteamExe",null) as string;
            if (!File.Exists(exe)) throw new IOException("Client Steam introuvable.");
            Process.Start(exe,"-silent").Dispose();
        }
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalMinutes < 2) {
            object user = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess","ActiveUser",0);
            if (user is int && (int)user != 0 && Process.GetProcessesByName("steam").Length > 0) return;
            System.Threading.Thread.Sleep(500);
        }
        throw new IOException("Connecte-toi à Steam puis relance REPOrt.");
    }

    static string Locate()
    {
        string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (!String.IsNullOrEmpty(steam)) {
            string libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            var roots = new System.Collections.Generic.List<string> { steam };
            if (File.Exists(libraries))
                foreach (Match m in Regex.Matches(File.ReadAllText(libraries), "\"path\"\\s+\"([^\"]+)\""))
                    roots.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            foreach (string root in roots) {
                string folder = Path.Combine(root, "steamapps", "common", "REPO");
                if (File.Exists(Path.Combine(folder, Game))) return Path.GetFullPath(folder);
            }
        }
        using (var dialog = new OpenFileDialog { Title = "Choisis REPO.exe", Filter = "R.E.P.O.|REPO.exe" })
            return dialog.ShowDialog() == DialogResult.OK ? Path.GetDirectoryName(dialog.FileName) : null;
    }

    static void RunSession(string source)
    {
        if (!File.Exists(Path.Combine(source, Game)) || !Directory.Exists(Path.Combine(source, "REPO_Data")))
            throw new IOException("Installation R.E.P.O. introuvable.");
        if (Process.GetProcessesByName("REPO").Length != 0)
            throw new IOException("Ferme la partie R.E.P.O. en cours puis relance REPOrt.");
        string dir = Path.Combine(source, ".REPOrt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var installed = new System.Collections.Generic.Stack<string>();
        try {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("runtime.zip"))
            using (var zip = new ZipArchive(s, ZipArchiveMode.Read))
                zip.ExtractToDirectory(dir);
            string[] names = {"BepInEx","doorstop_config.ini","winhttp.dll"};
            foreach (string name in names) {
                string dest = Path.Combine(source,name);
                if (File.Exists(dest) || Directory.Exists(dest))
                    throw new IOException("Installation modifiée détectée. Fichier conservé : " + dest);
            }
            foreach (string name in names) {
                string from = Path.Combine(dir,name), dest = Path.Combine(source,name);
                if (Directory.Exists(from)) Directory.Move(from,dest);
                else File.Move(from,dest);
                installed.Push(name);
            }
            var start = new ProcessStartInfo(Path.Combine(source,Game)) { WorkingDirectory=source, UseShellExecute=false };
            start.EnvironmentVariables["SteamAppId"] = "3241660";
            start.EnvironmentVariables["SteamGameId"] = "3241660";
            using (var game = Process.Start(start)) game.WaitForExit();
        } finally {
            foreach (string name in installed) {
                string from = Path.Combine(source,name);
                for (int attempt=0;;attempt++) {
                    try {
                        if (Directory.Exists(from)) Directory.Delete(from,true);
                        else File.Delete(from);
                        break;
                    } catch (IOException) { if (attempt>=9) throw; System.Threading.Thread.Sleep(500); }
                }
            }
            Directory.Delete(dir,true);
        }
    }
}
