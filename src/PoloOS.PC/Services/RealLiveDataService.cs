using PoloOS.Diagnostics.Core;
using PoloOS.PC.Models;

namespace PoloOS.PC.Services;

/// <summary>
/// Single real-vehicle telemetry producer.
/// The same VehicleState object feeds Home, Drive, Live, Black Box,
/// Drive Recorder and normal logger through MainWindow.OnVehicleState().
/// </summary>
public sealed class RealLiveDataService : IAsyncDisposable
{
    private CancellationTokenSource? _cts;
    private Task? _worker;

    public bool IsRunning =>
        _worker is not null &&
        !_worker.IsCompleted;

    public event Action<VehicleState>? StateReceived;
    public event Action<string>? Trace;
    public event Action<bool>? RunningChanged;

    private bool? _acRequested;
    private int _frameCounter;

    public void SetAcRequested(bool value) =>
        _acRequested = value;

    public Task StartAsync()
    {
        if (IsRunning)
            return Task.CompletedTask;

        _cts =
            new CancellationTokenSource();

        _worker =
            Task.Run(
                () => Worker(
                    _cts.Token));

        RunningChanged?.Invoke(true);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        var cts = _cts;
        var worker = _worker;

        _cts = null;
        _worker = null;

        try
        {
            cts?.Cancel();
        }
        catch
        {
        }

        if (worker is not null)
        {
            try
            {
                await worker.WaitAsync(
                    TimeSpan.FromSeconds(4));
            }
            catch
            {
            }
        }

        cts?.Dispose();
        RunningChanged?.Invoke(false);
    }

    private void Worker(
        CancellationToken token)
    {
        var retry = 0;

        while (!token.IsCancellationRequested)
        {
            var logDir =
                Path.Combine(
                    AppContext.BaseDirectory,
                    "logs");

            Directory.CreateDirectory(
                logDir);

            var logPath =
                Path.Combine(
                    logDir,
                    $"poloos_live_{DateTime.Now:yyyyMMdd_HHmmss}.log");

            try
            {
                using var log =
                    new DiagLog(
                        logPath);

                using var reader =
                    new Kw1281EngineLiveReader(
                        0,
                        log);

                Trace?.Invoke(
                    "[LIVE] Motor ECU bağlantısı kuruluyor...");

                reader.Open();

                retry = 0;

                Trace?.Invoke(
                    "[LIVE] BAĞLANDI • " +
                    reader.Identification +
                    " • groups=" +
                    string.Join(
                        ",",
                        reader.AvailableGroups
                            .Select(
                                x => x.ToString("D3"))));

                while (!token.IsCancellationRequested)
                {
                    var frame =
                        reader.ReadFrame();

                    var state =
                        ToVehicleState(
                            frame);

                    StateReceived?.Invoke(
                        state);

                    _frameCounter++;

                    if (_frameCounter % 20 == 0)
                    {
                        Trace?.Invoke(
                            $"[LIVE] RPM={Fmt(frame.Rpm)} " +
                            $"SPD={Fmt(frame.SpeedKmh)} " +
                            $"BOOST={Fmt(frame.BoostGaugeBar)} " +
                            $"PED={Fmt(frame.PedalPercent)} " +
                            $"CLT={Fmt(frame.CoolantC)} " +
                            $"V={Fmt(frame.Voltage)}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                    break;

                retry++;

                Trace?.Invoke(
                    $"[LIVE][RECONNECT {retry}] {ex.Message}");

                token.WaitHandle.WaitOne(
                    Math.Min(
                        3500,
                        700 + retry * 350));
            }
        }
    }

    private VehicleState ToVehicleState(
        EngineLiveFrame f)
    {
        return new VehicleState(
            f.Timestamp,
            f.Rpm ?? 0,
            f.SpeedKmh ?? 0,
            f.BoostGaugeBar ?? 0,
            f.PedalPercent ?? 0,
            f.CoolantC ?? 0,
            f.Voltage ?? 0,
            f.MafGps ?? 0,
            f.FuelTempC ?? 0,
            f.IntakeTempC ?? 0,
            f.InjectionMgStroke ?? 0)
        {
            RpmValid = f.Rpm.HasValue,
            SpeedValid = f.SpeedKmh.HasValue,
            BoostValid = f.BoostGaugeBar.HasValue,
            PedalValid = f.PedalPercent.HasValue,
            CoolantValid = f.CoolantC.HasValue,
            VoltageValid = f.Voltage.HasValue,
            MafValid = f.MafGps.HasValue,
            FuelTempValid = f.FuelTempC.HasValue,
            IntakeTempValid = f.IntakeTempC.HasValue,
            InjectionValid = f.InjectionMgStroke.HasValue,
            BrakePressed = f.BrakePressed ?? false,
            BrakeValid = f.BrakePressed.HasValue,
            ClutchPressed = f.ClutchPressed ?? false,
            ClutchValid = f.ClutchPressed.HasValue,
            AcRequested = _acRequested ?? false,
            AcRequestedValid = _acRequested.HasValue,
            PedalSwitchMask = f.PedalSwitchMask
        };
    }

    private static string Fmt(
        double? value)
        =>
            value.HasValue
                ? value.Value.ToString("0.##")
                : "—";

    public async ValueTask DisposeAsync() =>
        await StopAsync();
}
