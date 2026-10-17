using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SqlAssist.Connection;
using SqlAssist.Core.Search;

namespace SqlAssist.Search
{
    /// <summary>One Search Index per (server, login, database). The first search waits for the load; later ones get the
    /// loaded copy at once, and a copy older than 10 minutes is reloaded in the background while the old one serves.</summary>
    internal sealed class SearchIndexCache
    {
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan RetryFailedAfter = TimeSpan.FromMinutes(1);

        public static SearchIndexCache Instance { get; } = new SearchIndexCache();

        private readonly ConcurrentDictionary<string, Entry> _entries = new ConcurrentDictionary<string, Entry>();

        private sealed class Entry
        {
            public Task Load = Task.CompletedTask;
            public DateTime StartedUtc;
            public volatile IReadOnlyList<SearchItem> Items;
            public volatile Exception LastError;
            public bool Started;
        }

        /// <summary>The index of <paramref name="database"/> on the target's server. Throws the load's error when there is
        /// no earlier copy to fall back on (offline, no permission), so the caller can skip that database.</summary>
        public async Task<IReadOnlyList<SearchItem>> GetAsync(ConnectionTarget server, string database, CancellationToken token)
        {
            var entry = GetOrStart(server, database);
            if (entry.Items == null)
            {
                var load = entry.Load;
                var cancelled = new TaskCompletionSource<bool>();
                using (token.Register(() => cancelled.TrySetResult(true)))
                    await Task.WhenAny(load, cancelled.Task).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }
            if (entry.Items != null) return entry.Items;
            throw entry.LastError ?? new InvalidOperationException("Search index of " + database + " did not load");
        }

        public void Clear()
        {
            _entries.Clear();
            Log.Info("Search index cache cleared");
        }

        private Entry GetOrStart(ConnectionTarget server, string database)
        {
            var entry = _entries.GetOrAdd((server.ServerKey + "|" + database).ToLowerInvariant(), _ => new Entry());
            var age = DateTime.UtcNow - entry.StartedUtc;
            if (!entry.Started || age > StaleAfter || (entry.LastError != null && age > RetryFailedAfter)) Start(server, database, entry);
            return entry;
        }

        private static void Start(ConnectionTarget server, string database, Entry entry)
        {
            lock (entry)
            {
                if (!entry.Load.IsCompleted) return;
                entry.Started = true;
                entry.StartedUtc = DateTime.UtcNow;
                entry.Load = Task.Run(async () =>
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        // Not the caller's token: a search cancelled by the next keystroke still wants this index.
                        var items = await SearchIndexLoader.LoadAsync(server.ConnectionString, database, CancellationToken.None).ConfigureAwait(false);
                        entry.Items = items;
                        entry.LastError = null;
                        long chars = 0;
                        foreach (var i in items) chars += i.Definition?.Length ?? 0;
                        Log.Info($"Loaded search index of {server.Server}/{database}: {items.Count} items, {chars / 1024} KB of definitions in {sw.ElapsedMilliseconds} ms");
                    }
                    catch (Exception ex)
                    {
                        entry.LastError = ex;
                        Log.Error("Loading search index of " + server.Server + "/" + database, ex);
                    }
                });
            }
        }
    }
}
