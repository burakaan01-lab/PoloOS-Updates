using System.Diagnostics;
using System.IO.Compression;

namespace PoloOS.Updater;

internal static class Program
{
    private static int Main(string[] args)
    {
        string? selfDir = null;
        try
        {
            var a = Parse(args);
            var pid = int.Parse(Need(a, "--pid"));
            var installDir = Path.GetFullPath(Need(a, "--install-dir"));
            var package = Path.GetFullPath(Need(a, "--package"));
            var version = Need(a, "--target-version");
            var launch = Need(a, "--launch");
            selfDir = a.TryGetValue("--self-dir", out var s) ? s : AppContext.BaseDirectory;
            var logs = Path.Combine(installDir, "logs"); Directory.CreateDirectory(logs);
            var logPath = Path.Combine(logs, $"updater_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            void Log(string text) { var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {text}"; try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { } Console.WriteLine(line); }

            Log($"START target={version} pid={pid}");
            Wait(pid, Log);
            if (!File.Exists(package)) throw new FileNotFoundException("Update package bulunamadı.", package);

            var staging = Path.Combine(Path.GetTempPath(), "PoloOS_Stage_" + Guid.NewGuid().ToString("N"));
            var backup = Path.Combine(Path.GetTempPath(), "PoloOS_Backup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging); Directory.CreateDirectory(backup);
            try
            {
                ZipFile.ExtractToDirectory(package, staging, true);
                var root = FindRoot(staging);
                var copied = new List<string>();
                foreach (var src in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(root, src);
                    if (Preserve(rel)) continue;
                    var dst = Path.Combine(installDir, rel); Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    if (File.Exists(dst)) { var bak = Path.Combine(backup, rel); Directory.CreateDirectory(Path.GetDirectoryName(bak)!); File.Copy(dst, bak, true); }
                    CopyRetry(src, dst, Log); copied.Add(rel);
                }
                Log($"COPY_OK files={copied.Count}");
                TryDelete(package);
                var exe = Path.Combine(installDir, launch);
                if (!File.Exists(exe)) throw new FileNotFoundException("PoloOS EXE bulunamadı.", exe);
                Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = installDir, UseShellExecute = true });
                Log("SUCCESS target=" + version);
                CleanupLater(selfDir);
                return 0;
            }
            catch (Exception ex)
            {
                Log("UPDATE_FAIL " + ex);
                Restore(backup, installDir, Log);
                var exe = Path.Combine(installDir, launch);
                if (File.Exists(exe)) try { Process.Start(new ProcessStartInfo { FileName = exe, WorkingDirectory = installDir, UseShellExecute = true }); } catch { }
                return 2;
            }
            finally { TryDeleteDir(staging); TryDeleteDir(backup); }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static Dictionary<string,string> Parse(string[] args)
    {
        var d = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++) if (args[i].StartsWith("--") && i + 1 < args.Length) d[args[i]] = args[++i];
        return d;
    }
    private static string Need(Dictionary<string,string> d, string key) => d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException("Eksik parametre: " + key);
    private static void Wait(int pid, Action<string> log)
    {
        try { using var p = Process.GetProcessById(pid); log("WAIT_PROCESS"); if (!p.WaitForExit(30000)) { log("WAIT_TIMEOUT_KILL"); p.Kill(true); p.WaitForExit(5000); } } catch (ArgumentException) { }
    }
    private static string FindRoot(string staging)
    {
        if (File.Exists(Path.Combine(staging, "PoloOS.PC.exe"))) return staging;
        var exe = Directory.EnumerateFiles(staging, "PoloOS.PC.exe", SearchOption.AllDirectories).FirstOrDefault();
        return exe is null ? staging : Path.GetDirectoryName(exe)!;
    }
    private static bool Preserve(string rel)
    {
        var p = rel.Replace('\\', '/');
        return p.StartsWith("logs/", StringComparison.OrdinalIgnoreCase) || p.StartsWith("updates/", StringComparison.OrdinalIgnoreCase) || p.Equals("poloos.remote.json", StringComparison.OrdinalIgnoreCase);
    }
    private static void CopyRetry(string src, string dst, Action<string> log)
    {
        Exception? last = null;
        for (var i = 1; i <= 20; i++) try { File.Copy(src, dst, true); return; } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { last = ex; Thread.Sleep(250); }
        throw new IOException("Dosya kopyalanamadı: " + dst, last);
    }
    private static void Restore(string backup, string install, Action<string> log)
    {
        if (!Directory.Exists(backup)) return;
        foreach (var src in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories)) try { var rel = Path.GetRelativePath(backup, src); var dst = Path.Combine(install, rel); Directory.CreateDirectory(Path.GetDirectoryName(dst)!); File.Copy(src, dst, true); } catch (Exception ex) { log("ROLLBACK_FAIL " + ex.Message); }
        log("ROLLBACK_DONE");
    }
    private static void CleanupLater(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return;
        try { Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = $"/c ping 127.0.0.1 -n 4 >nul & rmdir /s /q \"{dir}\"", CreateNoWindow = true, UseShellExecute = false }); } catch { }
    }
    private static void TryDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }
    private static void TryDeleteDir(string p) { try { if (Directory.Exists(p)) Directory.Delete(p, true); } catch { } }
}
