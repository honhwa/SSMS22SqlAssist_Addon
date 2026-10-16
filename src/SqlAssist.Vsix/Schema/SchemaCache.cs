using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Connection;
using SqlAssist.Core.Schema;

namespace SqlAssist.Schema
{
    /// <summary>One schema per (server, database, login), loaded once in the background and shared by every window.
    /// Completion asks on every keystroke, so an in-flight load and a failed load are both remembered:
    /// without that a slow or unreachable server would get one query per character typed.</summary>
    internal sealed class SchemaCache
    {
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(30);

        public static SchemaCache Instance { get; } = new SchemaCache();

        private readonly ConcurrentDictionary<string, Entry> _entries = new ConcurrentDictionary<string, Entry>();

        private sealed class Entry
        {
            public Task Load = Task.CompletedTask;
            public DateTime StartedUtc;
            /// <summary>The latest successful load; kept while a reload runs so completion never goes blank.</summary>
            public volatile SchemaModel Model;
            public volatile bool LastLoadFailed;
            public bool Loaded;
        }

        /// <summary>The schema if it can be had within the wait; otherwise the last good copy (or empty) while loading continues.</summary>
        public async Task<SchemaModel> GetAsync(ConnectionTarget target, TimeSpan wait, CancellationToken token)
        {
            if (target == null) return SchemaModel.Empty;
            var entry = GetOrStart(target);
            var load = entry.Load;
            if (!load.IsCompleted)
                await Task.WhenAny(load, Task.Delay(wait, token)).ConfigureAwait(false);
            return entry.Model ?? SchemaModel.Empty;
        }

        /// <summary>The schema if it is already loaded (starting a load if needed); never waits.</summary>
        public SchemaModel TryGetLoaded(ConnectionTarget target) => target == null ? null : GetOrStart(target).Model;

        /// <summary>The query names a table the cache does not know — it may have just been created. Reload, at most every 30s.</summary>
        public void RefreshIfOlderThanThrottle(ConnectionTarget target)
        {
            if (target == null || !_entries.TryGetValue(target.Key, out var entry)) return;
            if (DateTime.UtcNow - entry.StartedUtc > MinRefreshInterval) Start(target, entry);
        }

        public void Clear()
        {
            _entries.Clear();
            Log.Info("Schema cache cleared");
        }

        private Entry GetOrStart(ConnectionTarget target)
        {
            var entry = _entries.GetOrAdd(target.Key, _ => new Entry());
            var age = DateTime.UtcNow - entry.StartedUtc;
            if (!entry.Loaded || age > StaleAfter || (entry.LastLoadFailed && age > RetryFailedAfter)) Start(target, entry);
            return entry;
        }

        private static void Start(ConnectionTarget target, Entry entry)
        {
            lock (entry)
            {
                if (!entry.Load.IsCompleted) return;
                entry.Loaded = true;
                entry.StartedUtc = DateTime.UtcNow;
                entry.Load = Task.Run(async () =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var model = await SchemaLoader.LoadAsync(target.ConnectionString, CancellationToken.None).ConfigureAwait(false);
                        entry.Model = model;
                        entry.LastLoadFailed = false;
                        Log.Info($"Loaded schema of {target}: {model.Objects.Count} tables/views, {model.ForeignKeys.Count} foreign keys in {sw.ElapsedMilliseconds} ms");
                    }
                    catch (Exception ex)
                    {
                        // Swallowed, not rethrown: nothing awaits this task's outcome, only its completion.
                        entry.LastLoadFailed = true;
                        Log.Error("Loading schema of " + target, ex);
                    }
                });
            }
        }
    }
}
