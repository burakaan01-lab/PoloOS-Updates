using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using PoloOS.Diagnostics.Core;
using PoloOS.PC.Models;
using PoloOS.PC.Services;

namespace PoloOS.PC.Views;

public partial class MainWindow : Window
{
    private const string AppVersion = "0.5.10.9.24";
    private const int MaxUiLogLines = 400;
    private readonly RealLiveDataService _live = new();
    private readonly RemoteConfig _remote;
    private readonly UpdateService _updates;
    private readonly LogUploadService _uploads;
    private readonly CarPlayHostService _carPlayHost = new();
    private readonly YouTubeHostService _youtubeHost = new();
    private readonly ObservableCollection<string> _dtc = new();
    private readonly ObservableCollection<string> _tests = new();
    private readonly ObservableCollection<string> _logs = new();
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _diagBusy, _updateBusy, _uploadBusy;
    private UpdateManifest? _availableUpdate;
    private string? _lastLog;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        _remote = RemoteConfig.LoadOrCreate();
        _updates = new UpdateService(_remote);
        _uploads = new LogUploadService(_remote);
        FindControl<ListBox>("DtcList")!.ItemsSource = _dtc;
        FindControl<ListBox>("TestList")!.ItemsSource = _tests;
        FindControl<ListBox>("LogList")!.ItemsSource = _logs;
        FindControl<TextBox>("RemoteBaseUrlBox")!.Text = _remote.BaseUrl;
        WireLive(); WireDiagnostics(); WireLog(); WireMedia(); WireCarPlay(); WireYouTube(); WireRemote();
        SetText("VersionText", $"v{AppVersion} • READ ONLY • FTDI 0");
        UpdateClock();
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        SelectPage("home");
        RefreshRemoteStatus();
        AddLog("[SYSTEM] PoloOS Minimal Test Client hazır.");
        AddLog("[SAFE] Diagnostik işlemler READ ONLY.");
        Opened += async (_, _) => { if (_remote.AutoCheckUpdates) await CheckForUpdatesAsync(true); };
        Closing += async (_, _) => { _clockTimer.Stop(); await _youtubeHost.DisposeAsync(); await _carPlayHost.DisposeAsync(); await _live.DisposeAsync(); _updates.Dispose(); _uploads.Dispose(); };
    }

    private void NavButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string page)
            SelectPage(page);
    }

    private void UpdateClock()
    {
        var now = DateTime.Now;
        SetText("ClockText", now.ToString("HH:mm"));
        SetText("DateText", now.ToString("dd.MM.yyyy"));
    }

    private void SelectPage(string page)
    {
        var pages = new Dictionary<string, (string Control, string Title, string Subtitle)>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = ("HomePage", "HOME", "Polo 9N3 Multimedia Center"),
            ["music"] = ("MusicPage", "MEDIA", "Yerel müzik ve video"),
            ["youtube"] = ("YouTubePage", "YOUTUBE LITE", "Hafif dokunmatik YouTube oynatıcı"),
            ["carplay"] = ("CarPlayPage", "CARPLAY", "iPhone bağlantısı ve araç ekranı"),
            ["navigation"] = ("NavigationPage", "NAVIGATION", "Hafif navigasyon altyapısı"),
            ["vehicle"] = ("VehiclePage", "VEHICLE", "Canlı araç verileri"),
            ["diag"] = ("DiagPage", "VAG DIAG", "Salt-okunur tanılama"),
            ["logs"] = ("LogsPage", "LOGS", "Yerel ve remote kayıtlar"),
            ["bluetooth"] = ("BluetoothPage", "BLUETOOTH", "Telefon bağlantısı"),
            ["settings"] = ("SettingsPage", "SETTINGS", "PoloOS ayarları"),
            ["about"] = ("AboutPage", "ABOUT POLOOS", "Polo 9N3 multimedia platform")
        };
        if (!pages.TryGetValue(page, out var selected))
            return;

        foreach (var item in pages.Values)
        {
            var control = FindControl<Control>(item.Control);
            if (control is not null)
                control.IsVisible = item.Control == selected.Control;
        }

        var navButtons = new[] { "HomeButton", "MusicButton", "CarPlayButton", "NavigationButton", "VehicleButton", "DiagButton", "LogsButton", "BluetoothButton", "SettingsButton" };
        foreach (var name in navButtons)
        {
            var button = FindControl<Button>(name);
            if (button is null)
                continue;
            button.Classes.Remove("active");
            if ((button.Tag as string)?.Equals(page, StringComparison.OrdinalIgnoreCase) == true)
                button.Classes.Add("active");
        }

        SetText("PageTitle", selected.Title);
        SetText("PageSubtitle", selected.Subtitle);
    }

    private void WireLive()
    {
        FindControl<Button>("LiveToggleButton")!.Click += async (_, _) =>
        {
            if (_diagBusy) { AddLog("[LIVE] Diagnostik işlem devam ediyor."); return; }
            if (_live.IsRunning) { await StopLiveAsync("Kullanıcı durdurdu"); return; }
            SetText("LiveStatusText", "BAĞLANIYOR"); SetGlobal("LIVE BAĞLANIYOR");
            SetButton("LiveToggleButton", "CANLI VERİYİ DURDUR");
            await _live.StartAsync();
        };
        FindControl<Button>("AdapterDisconnectButton")!.Click += async (_, _) => await DisconnectAdapterAsync();
        _live.StateReceived += s => Dispatcher.UIThread.Post(() => ApplyState(s));
        _live.Trace += s => Dispatcher.UIThread.Post(() => { AddLog(s); if (s.Contains("BAĞLANDI", StringComparison.OrdinalIgnoreCase)) { SetText("LiveStatusText", "OKUNUYOR"); SetGlobal("LIVE OKUNUYOR"); } else if (s.Contains("RECONNECT", StringComparison.OrdinalIgnoreCase)) SetText("LiveStatusText", "YENİDEN BAĞLANIYOR"); });
        _live.RunningChanged += running => Dispatcher.UIThread.Post(() => { SetButton("LiveToggleButton", running ? "CANLI VERİYİ DURDUR" : "CANLI VERİYİ BAŞLAT"); if (!running) { SetText("LiveStatusText", "DURDU"); if (!_diagBusy) SetGlobal("HAZIR"); } });
    }

    private void WireCarPlay()
    {
        _carPlayHost.Trace += message => Dispatcher.UIThread.Post(() => AddLog(message));
        _carPlayHost.RunningChanged += running => Dispatcher.UIThread.Post(() =>
        {
            SetText("CarPlayStatusText", running ? "MOTOR AÇIK" : "BAĞLI DEĞİL");
            SetText("CarPlayProjectionText", running ? "ChairPlay açık; iPhone bağlantısı bekleniyor" : "iPhone bağlantısı bekleniyor");
            SetEnabled("CarPlayConnectButton", !running);
            SetEnabled("CarPlayDisconnectButton", running);
            SetEnabled("CarPlayAdapterButton", running);
        });

        FindControl<Button>("CarPlayConnectButton")!.Click += async (_, _) =>
        {
            SetText("CarPlayStatusText", "BAŞLATILIYOR");
            SetText("CarPlayProjectionText", "ChairPlay motoru başlatılıyor...");
            SetEnabled("CarPlayConnectButton", false);
            await _carPlayHost.StartAsync();
            if (!_carPlayHost.IsRunning)
            {
                SetText("CarPlayStatusText", "MOTOR YOK");
                SetText("CarPlayProjectionText", "CarPlay\\ChairPlay.exe bulunamadı");
                SetEnabled("CarPlayConnectButton", true);
            }
        };

        FindControl<Button>("CarPlayDisconnectButton")!.Click += async (_, _) => await DisconnectCarPlayAsync("Kullanıcı bağlantıyı kesti");
        FindControl<Button>("CarPlayAdapterButton")!.Click += async (_, _) => await DisconnectCarPlayAsync("CarPlay/OBD paneli serbest bırakıldı");
    }

    private async Task DisconnectCarPlayAsync(string reason)
    {
        await _carPlayHost.StopAsync();
        SetText("CarPlayStatusText", "BAĞLI DEĞİL");
        SetText("CarPlayProjectionText", "iPhone bağlantısı bekleniyor");
        AddLog("[CARPLAY] " + reason);
    }

    private void WireYouTube()
    {
        _youtubeHost.Trace += message => Dispatcher.UIThread.Post(() => AddLog(message));
        _youtubeHost.RunningChanged += running => Dispatcher.UIThread.Post(() =>
        {
            SetText("YouTubeStatusText", running ? "AÇIK" : "HAZIR");
            SetText("YouTubePathText", running ? "YouTFast çalışıyor" : "YouTube motoru bekliyor");
            SetEnabled("YouTubeOpenButton", !running);
            SetEnabled("YouTubeCloseButton", running);
        });

        FindControl<Button>("YouTubeOpenButton")!.Click += async (_, _) =>
        {
            SetText("YouTubeStatusText", "BAŞLATILIYOR");
            SetText("YouTubePathText", "YouTFast açılıyor...");
            SetEnabled("YouTubeOpenButton", false);
            if (!await _youtubeHost.StartAsync())
            {
                SetText("YouTubeStatusText", "MOTOR YOK");
                SetText("YouTubePathText", "YouTube\\youtfast.exe bulunamadı");
                SetEnabled("YouTubeOpenButton", true);
            }
        };

        FindControl<Button>("YouTubeCloseButton")!.Click += async (_, _) => await _youtubeHost.StopAsync();
    }

    private async Task DisconnectAdapterAsync()
    {
        if (_diagBusy)
        {
            AddLog("[ADAPTER] Diagnostik işlemi devam ediyor; işlem bitince adaptör serbest bırakılacak.");
            return;
        }

        if (_live.IsRunning)
            await StopLiveAsync("Adaptör kullanıcı tarafından ayrıldı");

        SetText("LiveStatusText", "ADAPTÖR SERBEST");
        SetGlobal("ADAPTÖR SERBEST");
        AddLog("[ADAPTER] Canlı bağlantı kapatıldı; FTDI/K-Line oturumu serbest bırakıldı.");
    }

    private void WireDiagnostics()
    {
        FindControl<Button>("DtcScanButton")!.Click += async (_, _) => await RunFullScanAsync();
        FindControl<Button>("TestEngineButton")!.Click += async (_, _) => await RunModuleAsync(0x01);
        FindControl<Button>("TestAbsButton")!.Click += async (_, _) => await RunModuleAsync(0x03);
        FindControl<Button>("TestCentralButton")!.Click += async (_, _) => await RunModuleAsync(0x09);
        FindControl<Button>("TestClusterButton")!.Click += async (_, _) => await RunModuleAsync(0x17);
        FindControl<Button>("TestSteeringButton")!.Click += async (_, _) => await RunModuleAsync(0x44);
        FindControl<Button>("TestAllButton")!.Click += async (_, _) => await RunTestAllAsync();
    }

    private void WireLog()
    {
        FindControl<Button>("ClearLogButton")!.Click += (_, _) => { _logs.Clear(); UpdateLogCount(); };
        FindControl<Button>("OpenLogsButton")!.Click += (_, _) => { try { Process.Start(new ProcessStartInfo { FileName = EnsureLogDir(), UseShellExecute = true }); } catch (Exception ex) { AddLog("[LOG][ERR] " + ex.Message); } };
        FindControl<Button>("CopyLogButton")!.Click += (_, _) => CopyUiLog();
    }

    private void WireMedia()
    {
        var path = EnsureMediaDir();
        SetText("MediaPathText", "MEDYA KLASÖRÜ: " + path);
        SetText("HomeMediaPathText", "MEDYA KLASÖRÜ: " + path);
        WireOpenMediaButton("OpenMediaButton", path);
    }

    private void WireOpenMediaButton(string name, string path)
    {
        FindControl<Button>(name)!.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                AddLog("[MEDIA] Medya klasörü açıldı.");
            }
            catch (Exception ex)
            {
                AddLog("[MEDIA][ERR] " + ex.Message);
            }
        };
    }

    private void WireRemote()
    {
        FindControl<Button>("SaveRemoteButton")!.Click += (_, _) =>
        {
            var value = (FindControl<TextBox>("RemoteBaseUrlBox")!.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) value = "https://" + value;
            _remote.BaseUrl = value;
            if (!string.IsNullOrWhiteSpace(value) && !_remote.IsConfigured) { SetText("RemoteStatusText", "REMOTE: ADRES GEÇERSİZ"); return; }
            _remote.Save(); FindControl<TextBox>("RemoteBaseUrlBox")!.Text = _remote.BaseUrl; RefreshRemoteStatus(); AddLog("[REMOTE] Sunucu kaydedildi: " + _remote.BaseUrl);
        };
        FindControl<Button>("UploadLatestLogButton")!.Click += async (_, _) => await UploadLatestAsync();
        FindControl<Button>("UpdateButton")!.Click += async (_, _) => { if (_availableUpdate is null) await CheckForUpdatesAsync(false); else await ApplyUpdateAsync(); };
    }

    private async Task<bool> BeginDiagAsync(string status)
    {
        if (_diagBusy || _updateBusy) return false;
        if (_live.IsRunning) await StopLiveAsync("Diagnostik için durduruldu");
        _diagBusy = true; SetGlobal(status); return true;
    }
    private void EndDiag() { _diagBusy = false; SetGlobal("HAZIR"); }

    private async Task RunFullScanAsync()
    {
        if (!await BeginDiagAsync("DTC TARAMA")) return;
        _dtc.Clear(); SetText("DtcStatusText", "TARANIYOR..."); SetEnabled("DtcScanButton", false);
        string? path = null;
        try
        {
            path = NewLogPath("poloos_ecu"); var p = path;
            var results = await Task.Run(() => { using var log = new DiagLog(p); return new PoloDiagnosticsScanner(0, log).ScanAll(); });
            foreach (var r in results) RenderDtc(r);
            var connected = results.Count(x => x.Connected); var count = results.Sum(x => x.Dtcs.Count);
            SetText("DtcStatusText", $"{connected}/8 BAĞLANDI • {count} DTC"); AddLog($"[DTC] Tamamlandı • {connected}/8 • {count} DTC");
        }
        catch (Exception ex) { _dtc.Add("HATA • " + ex.Message); SetText("DtcStatusText", "HATA"); AddLog("[DTC][ERR] " + ex.Message); }
        finally { SetEnabled("DtcScanButton", true); EndDiag(); }
        if (path is not null && File.Exists(path)) await RegisterLogAsync(path, "dtc_scan");
    }

    private async Task RunModuleAsync(byte address)
    {
        var module = PoloDiagnosticsScanner.DefaultModules.First(x => x.Address == address);
        if (!await BeginDiagAsync($"TEST {address:X2}")) return;
        _tests.Clear(); SetTestButtons(false); SetText("TestStatusText", $"{address:X2} {module.Name} test ediliyor..."); string? path = null;
        try
        {
            path = NewLogPath($"poloos_test_{address:X2}"); var p = path;
            var result = await Task.Run(() => { using var log = new DiagLog(p); return new PoloDiagnosticsScanner(0, log).Scan(module); });
            RenderTest(result); SetText("TestStatusText", result.Connected ? "TEST TAMAM • ECU YANIT VERDİ" : "TEST TAMAM • CEVAP YOK");
        }
        catch (Exception ex) { _tests.Add("HATA • " + ex.Message); AddLog($"[TEST {address:X2}][ERR] {ex.Message}"); }
        finally { SetTestButtons(true); EndDiag(); }
        if (path is not null && File.Exists(path)) await RegisterLogAsync(path, $"test_{address:X2}");
    }

    private async Task RunTestAllAsync()
    {
        if (!await BeginDiagAsync("TEST ALL")) return;
        _tests.Clear(); SetTestButtons(false); string? path = null;
        try
        {
            path = NewLogPath("poloos_test_all"); var p = path;
            var results = await Task.Run(() => { using var log = new DiagLog(p); return new PoloDiagnosticsScanner(0, log).ScanAll(); });
            foreach (var r in results) RenderTest(r); SetText("TestStatusText", $"TAMAMLANDI • {results.Count(x => x.Connected)}/8 MODÜL");
        }
        catch (Exception ex) { _tests.Add("HATA • " + ex.Message); AddLog("[TEST][ERR] " + ex.Message); }
        finally { SetTestButtons(true); EndDiag(); }
        if (path is not null && File.Exists(path)) await RegisterLogAsync(path, "test_all");
    }

    private void RenderDtc(ModuleScanResult r)
    {
        _dtc.Add($"{r.Module.Address:X2} {r.Module.Name} • {(r.Connected ? "OK" : "CEVAP YOK")} • {r.Protocol} • {r.Status}");
        if (!string.IsNullOrWhiteSpace(r.Identification)) _dtc.Add("  " + r.Identification);
        foreach (var d in r.Dtcs) _dtc.Add("  DTC " + d);
    }
    private void RenderTest(ModuleScanResult r) { _tests.Add($"{r.Module.Address:X2} {r.Module.Name} • {(r.Connected ? "OK" : "CEVAP YOK")} • {r.Protocol} • {r.Status}"); if (!string.IsNullOrWhiteSpace(r.Identification)) _tests.Add("  " + r.Identification); foreach (var d in r.Dtcs) _tests.Add("  DTC " + d); }

    private async Task RegisterLogAsync(string path, string kind)
    {
        _lastLog = path; SetText("LastLogText", "SON LOG: " + Path.GetFileName(path));
        if (_remote.AutoUploadLogs && _remote.IsConfigured) await UploadAsync(path, kind);
    }
    private async Task UploadLatestAsync()
    {
        var path = _lastLog;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) path = Directory.EnumerateFiles(EnsureLogDir(), "*.log").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (path is null) { AddLog("[UPLOAD] Log bulunamadı."); return; }
        await UploadAsync(path, "manual");
    }
    private async Task UploadAsync(string path, string kind)
    {
        if (_uploadBusy) return; _uploadBusy = true; SetEnabled("UploadLatestLogButton", false);
        try { SetText("RemoteStatusText", "REMOTE: LOG YÜKLENİYOR"); var r = await _uploads.UploadAsync(path, kind, AppVersion, _logs.ToArray()); AddLog(r.Success ? "[UPLOAD] " + r.Message : "[UPLOAD][ERR] " + r.Message); SetText("RemoteStatusText", r.Success ? (string.IsNullOrWhiteSpace(r.SessionId) ? "REMOTE: LOG YÜKLENDİ" : "SESSION: " + r.SessionId) : "REMOTE: UPLOAD HATA"); }
        finally { _uploadBusy = false; SetEnabled("UploadLatestLogButton", true); }
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_updateBusy) return; _updateBusy = true; SetEnabled("UpdateButton", false);
        if (!silent) AddLog("[UPDATE] Güncelleme kontrol ediliyor...");
        try
        {
            _availableUpdate = await _updates.CheckAsync(AppVersion);
            if (_availableUpdate is null) { SetButton("UpdateButton", "GÜNCELLEME KONTROL"); SetText("RemoteStatusText", "REMOTE: GÜNCEL"); if (!silent) AddLog($"[UPDATE] v{AppVersion} güncel."); }
            else { SetButton("UpdateButton", $"v{_availableUpdate.Version} GÜNCELLE"); SetText("RemoteStatusText", $"YENİ SÜRÜM: v{_availableUpdate.Version}"); AddLog($"[UPDATE] Yeni sürüm: v{_availableUpdate.Version} • {_availableUpdate.Notes}"); }
        }
        catch (Exception ex) { SetText("RemoteStatusText", "REMOTE: KONTROL HATA"); if (!silent) AddLog("[UPDATE][ERR] " + ex.Message); }
        finally { _updateBusy = false; SetEnabled("UpdateButton", true); }
    }

    private async Task ApplyUpdateAsync()
    {
        var m = _availableUpdate; if (m is null || _updateBusy || _diagBusy) return; _updateBusy = true; SetEnabled("UpdateButton", false);
        try
        {
            if (_live.IsRunning) await StopLiveAsync("Güncelleme hazırlanıyor");
            var progress = new Progress<string>(s => { SetText("RemoteStatusText", "UPDATE: " + s); SetButton("UpdateButton", s); });
            AddLog($"[UPDATE] v{m.Version} indiriliyor..."); var package = await _updates.DownloadAndVerifyAsync(m, progress);
            AddLog("[UPDATE] Paket doğrulandı. Updater devralıyor."); _updates.LaunchUpdater(package, m.Version, Environment.ProcessId);
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        }
        catch (Exception ex) { AddLog("[UPDATE][ERR] " + ex.Message); SetText("RemoteStatusText", "UPDATE: HATA"); SetButton("UpdateButton", $"v{m.Version} GÜNCELLE"); _updateBusy = false; SetEnabled("UpdateButton", true); }
    }

    private async Task StopLiveAsync(string reason) { if (_live.IsRunning) { AddLog("[LIVE] " + reason); await _live.StopAsync(); } }
    private void ApplyState(VehicleState s)
    {
        SetText("RpmValue", F(s.Rpm, s.RpmValid, "0", " rpm")); SetText("SpeedValue", F(s.SpeedKmh, s.SpeedValid, "0.0", " km/h")); SetText("BoostValue", F(s.BoostBar, s.BoostValid, "0.000", " bar")); SetText("PedalValue", F(s.PedalPercent, s.PedalValid, "0.0", " %")); SetText("CoolantValue", F(s.CoolantC, s.CoolantValid, "0.0", " °C")); SetText("VoltageValue", F(s.Voltage, s.VoltageValid, "0.00", " V")); SetText("FuelTempValue", F(s.FuelTempC, s.FuelTempValid, "0.0", " °C")); SetText("IntakeTempValue", F(s.IntakeTempC, s.IntakeTempValid, "0.0", " °C")); SetText("MafValue", F(s.MafGps, s.MafValid, "0.00", " g/s")); SetText("InjectionValue", F(s.InjectionMgStroke, s.InjectionValid, "0.00", " mg/st")); SetText("BrakeValue", s.BrakeValid ? (s.BrakePressed ? "BASILI" : "SERBEST") : "—"); SetText("ClutchValue", s.ClutchValid ? (s.ClutchPressed ? "BASILI" : "SERBEST") : "—");
        SetText("CarPlayRpmValue", F(s.Rpm, s.RpmValid, "0", " rpm")); SetText("CarPlaySpeedValue", F(s.SpeedKmh, s.SpeedValid, "0.0", " km/h")); SetText("CarPlayVoltageValue", F(s.Voltage, s.VoltageValid, "0.00", " V"));
    }

    private void RefreshRemoteStatus() => SetText("RemoteStatusText", _remote.IsConfigured ? "REMOTE: HAZIR" : "REMOTE: AYAR BEKLİYOR");
    private void CopyUiLog() { try { using var p = new Process { StartInfo = new ProcessStartInfo { FileName = "clip.exe", RedirectStandardInput = true, UseShellExecute = false, CreateNoWindow = true } }; p.Start(); p.StandardInput.Write(string.Join(Environment.NewLine, _logs)); p.StandardInput.Close(); p.WaitForExit(1500); AddLog("[LOG] Panoya kopyalandı."); } catch (Exception ex) { AddLog("[LOG][ERR] " + ex.Message); } }
    private void AddLog(string text) { _logs.Add($"{DateTime.Now:HH:mm:ss.fff}  {text}"); while (_logs.Count > MaxUiLogLines) _logs.RemoveAt(0); UpdateLogCount(); }
    private void UpdateLogCount() => SetText("LogCountText", $"{_logs.Count} satır");
    private static string EnsureLogDir() { var p = Path.Combine(AppContext.BaseDirectory, "logs"); Directory.CreateDirectory(p); return p; }
    private static string EnsureMediaDir() { var p = Path.Combine(AppContext.BaseDirectory, "Media"); Directory.CreateDirectory(p); return p; }
    private static string NewLogPath(string prefix) => Path.Combine(EnsureLogDir(), $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
    private static string F(double v, bool valid, string fmt, string suffix) => valid ? v.ToString(fmt) + suffix : "—";
    private void SetGlobal(string text) => SetText("GlobalStatusText", text);
    private void SetText(string name, string text) { var c = FindControl<TextBlock>(name); if (c is not null) c.Text = text; }
    private void SetButton(string name, string text) { var c = FindControl<Button>(name); if (c is not null) c.Content = text; }
    private void SetEnabled(string name, bool value) { var c = FindControl<Button>(name); if (c is not null) c.IsEnabled = value; }
    private void SetTestButtons(bool value) { foreach (var n in new[] { "TestEngineButton", "TestAbsButton", "TestCentralButton", "TestClusterButton", "TestSteeringButton", "TestAllButton" }) SetEnabled(n, value); }
}
