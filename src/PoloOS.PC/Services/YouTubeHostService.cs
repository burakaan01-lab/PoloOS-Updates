using System.Diagnostics;

namespace PoloOS.PC.Services;

public sealed class YouTubeHostService : IAsyncDisposable
{
    private Process? _process;

    public string? EnginePath { get; private set; }
    public bool IsRunning => _process is { HasExited: false };
    public event Action<string>? Trace;
    public event Action<bool>? RunningChanged;

    public string? FindEngine()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "YouTube", "youtfast.exe"),
            Path.Combine(AppContext.BaseDirectory, "YouTubeLite", "youtfast.exe"),
            Path.Combine(AppContext.BaseDirectory, "youtfast.exe")
        };
        EnginePath = candidates.FirstOrDefault(File.Exists);
        return EnginePath;
    }

    public Task<bool> StartAsync()
    {
        if (IsRunning)
        {
            Trace?.Invoke("[YOUTUBE] YouTFast zaten çalışıyor.");
            return Task.FromResult(true);
        }

        var executable = FindEngine();
        if (executable is null)
        {
            Trace?.Invoke("[YOUTUBE][ERR] YouTube\\youtfast.exe bulunamadı.");
            RunningChanged?.Invoke(false);
            return Task.FromResult(false);
        }

        try
        {
            var directory = Path.GetDirectoryName(executable)!;
            var existing = FindRunningProcess(executable);
            if (existing is not null)
            {
                existing.EnableRaisingEvents = true;
                existing.Exited += OnProcessExited;
                _process = existing;
                Trace?.Invoke("[YOUTUBE] YouTFast zaten açık; mevcut pencereye geçildi.");
                RunningChanged?.Invoke(true);
                return Task.FromResult(true);
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    CreateNoWindow = false
                },
                EnableRaisingEvents = true
            };
            process.StartInfo.Environment["PATH"] = directory + ";" + Environment.GetEnvironmentVariable("PATH");
            process.Exited += OnProcessExited;

            if (!process.Start())
            {
                process.Dispose();
                Trace?.Invoke("[YOUTUBE][ERR] YouTFast başlatılamadı.");
                RunningChanged?.Invoke(false);
                return Task.FromResult(false);
            }

            _process = process;
            Trace?.Invoke("[YOUTUBE] YouTFast başlatıldı.");
            RunningChanged?.Invoke(true);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Trace?.Invoke("[YOUTUBE][ERR] Başlatma: " + ex.Message);
            RunningChanged?.Invoke(false);
            return Task.FromResult(false);
        }
    }

    public async Task StopAsync()
    {
        var process = _process;
        _process = null;
        if (process is null)
        {
            RunningChanged?.Invoke(false);
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                Trace?.Invoke("[YOUTUBE] YouTFast kapatılıyor.");
                if (process.CloseMainWindow())
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
        }
        catch (TimeoutException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
        }
        catch (Exception ex)
        {
            Trace?.Invoke("[YOUTUBE][ERR] Kapatma: " + ex.Message);
        }
        finally
        {
            process.Dispose();
            RunningChanged?.Invoke(false);
            Trace?.Invoke("[YOUTUBE] YouTFast durdu.");
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (!ReferenceEquals(_process, sender)) return;
        _process = null;
        RunningChanged?.Invoke(false);
        Trace?.Invoke("[YOUTUBE] YouTFast kapandı.");
    }

    private static Process? FindRunningProcess(string executable)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    return process;
            }
            catch { process.Dispose(); }
        }
        return null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
