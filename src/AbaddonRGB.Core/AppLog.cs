namespace AbaddonRGB.Core;

/// <summary>Registro do que o programa faz, em arquivo e na tela de diagnóstico.</summary>
public sealed class AppLog
{
    private const int MaxRecent = 400;
    private const int DaysToKeep = 7;

    private readonly object _lock = new();
    private readonly List<string> _recent = new();
    private readonly string? _folder;

    public event Action<string>? LineAdded;

    public AppLog(string? folder)
    {
        _folder = folder;
        if (folder is null) return;
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var file in Directory.GetFiles(folder, "abaddon-*.log"))
                if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-DaysToKeep))
                    File.Delete(file);
        }
        catch { }
    }

    public string? Folder => _folder;

    public IReadOnlyList<string> Recent
    {
        get { lock (_lock) return _recent.ToArray(); }
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("AVISO", message);
    public void Error(string message) => Write("ERRO", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}";
        lock (_lock)
        {
            _recent.Add(line);
            if (_recent.Count > MaxRecent) _recent.RemoveRange(0, _recent.Count - MaxRecent);

            if (_folder is not null)
            {
                try
                {
                    File.AppendAllText(Path.Combine(_folder, $"abaddon-{DateTime.Now:yyyyMMdd}.log"),
                        line + Environment.NewLine);
                }
                catch { }
            }
        }

        try { LineAdded?.Invoke(line); } catch { }
    }
}
