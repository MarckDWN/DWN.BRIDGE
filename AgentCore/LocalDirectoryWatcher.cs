using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace AIBridge.AgentCore
{
    public class LocalDirectoryWatcher : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new();
        private readonly int _debounceMilliseconds = 1500; // Aspetta 1.5s dall'ultimo salvataggio prima di triggherare

        public event EventHandler<string>? OnFileChanged;

        public LocalDirectoryWatcher(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            _watcher = new FileSystemWatcher(directoryPath)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                Filter = "*.*",
                EnableRaisingEvents = true
            };

            _watcher.Changed += HandleFileSystemEvent;
            _watcher.Created += HandleFileSystemEvent;
            _watcher.Renamed += (s, e) => HandleFileSystemEvent(s, new FileSystemEventArgs(WatcherChangeTypes.Renamed, Path.GetDirectoryName(e.FullPath)!, e.Name!));
        }

        private void HandleFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            // Evita di scatenare eventi su file temporanei creati dagli editor (es. file .tmp o terminanti con ~)
            if (e.FullPath.EndsWith("~") || e.FullPath.EndsWith(".tmp")) return;

            // Logica di Debounce (se salvi 5 volte in un secondo, l'evento parte solo una volta alla fine)
            _debounceTimers.AddOrUpdate(
                e.FullPath,
                path => new Timer(TimerCallback, path, _debounceMilliseconds, Timeout.Infinite),
                (path, existingTimer) =>
                {
                    existingTimer.Change(_debounceMilliseconds, Timeout.Infinite);
                    return existingTimer;
                });
        }

        private void TimerCallback(object? state)
        {
            if (state is string path)
            {
                if (_debounceTimers.TryRemove(path, out var timer))
                {
                    timer.Dispose();
                }

                OnFileChanged?.Invoke(this, path);
            }
        }

        public void Dispose()
        {
            _watcher.Dispose();
            foreach (var timer in _debounceTimers.Values)
            {
                timer.Dispose();
            }
        }
    }
}
