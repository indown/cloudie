using System.Diagnostics;

namespace Cloudie.App.Services;

/// <summary>Appends one line per event to the log file and the debugger.</summary>
public sealed class LogService(string path)
{
    private readonly string _path = path;
    private readonly object _gate = new();

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] {level}: {message}";
        Debug.WriteLine(line);
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path) && new FileInfo(_path).Length > 2_000_000) File.Delete(_path);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch
            {
                // Logging must never take the app down.
            }
        }
    }
}
