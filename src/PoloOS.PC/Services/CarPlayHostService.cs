using System.Diagnostics;

namespace PoloOS.PC.Services;

/// <summary>
/// Starts and stops the real ChairPlay receiver next to PoloOS.
/// The CarPlay protocol remains isolated from the PoloOS UI so it can be
/// replaced or upgraded without changing the vehicle/OBD layer.
/// </summary>
public sealed class CarPlayHostService : IAsyncDisposable
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
            Path.Combine(AppContext.BaseDirectory, "CarPlay", "ChairPlay.exe"),
            Path.Combine(AppContext.BaseDirectory, "ChairPlay.exe")
        };

        EnginePath = candidates.FirstOrDefault(File.Exists);
        return EnginePath;
    }

    public Task<bool> StartAsync()
    {
        if (IsRunning)
        {
            Trace?.Invoke("[CARPLAY] ChairPlay motoru zaten çalışıyor.");
            return Task.FromResult(true);
        }

        var executable = FindEngine();
        if (executable is null)
        {
            Trace?.Invoke("[CARPLAY][ERR] CarPlay\\ChairPlay.exe bulunamadı. Motor klasörü PoloOS yanında olmalı.");
            RunningChanged?.Invoke(false);
            return Task.FromResult(false);
        }

        try
        {
            var existing = FindRunningProcess(executable);
            if (existing is not null)
            {
                existing.EnableRaisingEvents = true;
                existing.Exited += OnProcessExited;
                _process = existing;
                Trace?.Invoke("[CARPLAY] ChairPlay zaten çalışıyor; mevcut motora bağlanıldı.");
                RunningChanged?.Invoke(true);
                return Task.FromResult(true);
            }

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    WorkingDirectory = Path.GetDirectoryName(executable)!,
                    UseShellExecute = true
                },
                EnableRaisingEvents = true
            };
            process.StartInfo.ArgumentList.Add("run");
            process.Exited += OnProcessExited;

            if (!process.Start())
            {
                process.Dispose();
                Trace?.Invoke("[CARPLAY][ERR] ChairPlay başlatılamadı.");
                RunningChanged?.Invoke(false);
                return Task.FromResult(false);
            }

            _process = process;
            Trace?.Invoke("[CARPLAY] ChairPlay motoru başlatıldı; iPhone bağlantısı bekleniyor.");
            RunningChanged?.Invoke(true);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Trace?.Invoke("[CARPLAY][ERR] Motor başlatma: " + ex.Message);
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
                Trace?.Invoke("[CARPLAY] ChairPlay motoru kapatılıyor.");
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
            Trace?.Invoke("[CARPLAY][ERR] Motor kapatma: " + ex.Message);
        }
        finally
        {
            process.Dispose();
            RunningChanged?.Invoke(false);
            Trace?.Invoke("[CARPLAY] ChairPlay motoru durdu.");
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (ReferenceEquals(_process, sender))
        {
            _process = null;
            RunningChanged?.Invoke(false);
            Trace?.Invoke("[CARPLAY] ChairPlay motoru kapandı.");
        }
    }

    private static Process? FindRunningProcess(string executable)
    {
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(process.MainModule?.FileName) &&
                    string.Equals(process.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    return process;
            }
            catch
            {
                process.Dispose();
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
