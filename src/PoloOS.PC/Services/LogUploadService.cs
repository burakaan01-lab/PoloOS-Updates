using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace PoloOS.PC.Services;

public sealed class LogUploadResult
{
    public bool Success { get; init; }
    public string SessionId { get; init; } = "";
    public string TextUrl { get; init; } = "";
    public string Message { get; init; } = "";
}

public sealed class LogUploadService : IDisposable
{
    private readonly RemoteConfig _config;
    private readonly HttpClient _http;

    public LogUploadService(RemoteConfig config)
    {
        _config = config;
        _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        };
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("PoloOS-PC", "1.0"));
    }

    public async Task<LogUploadResult> UploadAsync(
        string logPath,
        string kind,
        string appVersion,
        IReadOnlyCollection<string> uiLines,
        CancellationToken cancellationToken = default)
    {
        if (!_config.IsConfigured)
            return new LogUploadResult
            {
                Success = false,
                Message = "REMOTE sunucu ayarlı değil."
            };

        if (!File.Exists(logPath))
            return new LogUploadResult
            {
                Success = false,
                Message = "Log dosyası bulunamadı."
            };

        var package = CreatePackage(logPath, kind, appVersion, uiLines);

        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(kind, Encoding.UTF8), "kind");
            form.Add(new StringContent(appVersion, Encoding.UTF8), "appVersion");
            form.Add(new StringContent(BuildReadableText(logPath, uiLines), Encoding.UTF8), "text");

            await using var fileStream = new FileStream(
                package,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                useAsync: true);
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(fileContent, "file", Path.GetFileName(package));

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                _config.BuildUri("api/logs"))
            {
                Content = form
            };

            if (!string.IsNullOrWhiteSpace(_config.AuthToken))
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _config.AuthToken);

            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new LogUploadResult
                {
                    Success = false,
                    Message = $"HTTP {(int)response.StatusCode}: {Trim(body, 180)}"
                };
            }

            var sessionId = "";
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("sessionId", out var id))
                    sessionId = id.GetString() ?? "";
            }
            catch
            {
            }

            var textUrl = string.IsNullOrWhiteSpace(sessionId)
                ? ""
                : _config.BuildUri($"api/logs/{Uri.EscapeDataString(sessionId)}/text").ToString();

            if (!string.IsNullOrWhiteSpace(sessionId))
                SaveLastSession(sessionId, textUrl, kind, appVersion);

            TryDelete(package);
            return new LogUploadResult
            {
                Success = true,
                SessionId = sessionId,
                TextUrl = textUrl,
                Message = string.IsNullOrWhiteSpace(sessionId)
                    ? "Log yüklendi."
                    : string.IsNullOrWhiteSpace(textUrl)
                        ? $"Log yüklendi • Session {sessionId}"
                        : $"Log yüklendi • Session {sessionId} • TEXT {textUrl}"
            };
        }
        catch (Exception ex)
        {
            return new LogUploadResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private static string CreatePackage(
        string logPath,
        string kind,
        string appVersion,
        IReadOnlyCollection<string> uiLines)
    {
        var outbox = Path.Combine(AppContext.BaseDirectory, "logs", "outbox");
        Directory.CreateDirectory(outbox);

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipPath = Path.Combine(outbox, $"PoloOS_{kind}_{stamp}_{Guid.NewGuid():N}.zip");

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(logPath, Path.GetFileName(logPath), CompressionLevel.Fastest);

        var metadata = new
        {
            appVersion,
            kind,
            createdLocal = DateTimeOffset.Now,
            sourceLog = Path.GetFileName(logPath)
        };
        AddTextEntry(
            zip,
            "session.json",
            JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        AddTextEntry(zip, "ui.log", string.Join(Environment.NewLine, uiLines));

        return zipPath;
    }

    private static void AddTextEntry(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static string BuildReadableText(
        string logPath,
        IReadOnlyCollection<string> uiLines)
    {
        const int maxSourceChars = 1_500_000;
        string source;
        try
        {
            source = File.ReadAllText(logPath);
            if (source.Length > maxSourceChars)
                source = source[^maxSourceChars..];
        }
        catch (Exception ex)
        {
            source = "[SOURCE LOG READ ERROR] " + ex.Message;
        }

        var ui = string.Join(Environment.NewLine, uiLines);
        return $"POLOOS READABLE SESSION LOG{Environment.NewLine}" +
               $"SOURCE={Path.GetFileName(logPath)}{Environment.NewLine}" +
               $"CREATED={DateTimeOffset.Now:O}{Environment.NewLine}" +
               $"{Environment.NewLine}=== DIAGNOSTIC FILE LOG ==={Environment.NewLine}" +
               source +
               $"{Environment.NewLine}{Environment.NewLine}=== UI LOG ==={Environment.NewLine}" +
               ui;
    }

    private static void SaveLastSession(
        string sessionId,
        string textUrl,
        string kind,
        string appVersion)
    {
        try
        {
            Directory.CreateDirectory(RemoteConfig.ConfigDirectory);
            var path = Path.Combine(RemoteConfig.ConfigDirectory, "last-session.txt");
            var text = $"SESSION={sessionId}{Environment.NewLine}" +
                       $"TEXT_URL={textUrl}{Environment.NewLine}" +
                       $"KIND={kind}{Environment.NewLine}" +
                       $"APP_VERSION={appVersion}{Environment.NewLine}" +
                       $"SAVED_UTC={DateTimeOffset.UtcNow:O}{Environment.NewLine}";
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static string Trim(string text, int length) =>
        text.Length <= length ? text : text[..length] + "…";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    public void Dispose() => _http.Dispose();
}
