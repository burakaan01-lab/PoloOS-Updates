namespace PoloOS.Diagnostics.Core;

public sealed class DiagLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();
    public string Path { get; }
    public event Action<string>? LineWritten;
    public DiagLog(string path) { Path = path; Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); _writer = new StreamWriter(path, append: false) { AutoFlush = true }; }
    public void Write(string text) { var line = $"{DateTime.Now:HH:mm:ss.fff}  {text}"; lock (_gate) _writer.WriteLine(line); LineWritten?.Invoke(line); }
    public void Dispose() => _writer.Dispose();
}
