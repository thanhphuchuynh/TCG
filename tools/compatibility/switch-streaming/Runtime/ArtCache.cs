using System;
using System.Collections.Generic;

namespace TCG.SwitchStreaming
{
    // Main-thread ownership policy. The Unity backend owns I/O and native objects.
    public sealed class ArtCache<T> where T : class
    {
        internal sealed class Entry
        {
            public string Key;
            public long Bytes, LastUse;
            public int Pins;
            public bool Loading = true;
            public T Value;
            public string Error;
            public readonly List<Lease> Waiters = new List<Lease>();
        }

        public sealed class Lease : IDisposable
        {
            readonly ArtCache<T> cache;
            readonly Entry entry;
            internal readonly Action<Lease> Changed;
            public bool Released { get; private set; }
            public bool Ready { get { return !entry.Loading; } }
            public T Value { get { return Released ? null : entry.Value; } }
            public string Error { get { return entry.Error; } }
            internal Lease(ArtCache<T> cache, Entry entry, Action<Lease> changed)
            { this.cache = cache; this.entry = entry; Changed = changed; }
            public void Dispose()
            {
                if (Released) return;
                Released = true;
                cache.Release(entry);
            }
        }

        readonly long budget;
        readonly Action<string, Action<T, string>> load;
        readonly Action<T> unload;
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        long clock;
        public long LoadedBytes { get; private set; }
        public long PeakBytes { get; private set; }
        public long PinnedBytes
        {
            get { long bytes = 0; foreach (var e in entries.Values) if (e.Pins > 0) bytes += e.Bytes; return bytes; }
        }
        public int PendingCount
        {
            get { int count = 0; foreach (var e in entries.Values) if (e.Loading) count++; return count; }
        }

        public ArtCache(long budget, Action<string, Action<T, string>> load, Action<T> unload)
        {
            if (budget < 0) throw new ArgumentOutOfRangeException("budget");
            this.budget = budget;
            this.load = load ?? throw new ArgumentNullException("load");
            this.unload = unload ?? throw new ArgumentNullException("unload");
        }

        public Lease Acquire(string key, long bytes, Action<Lease> changed = null)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Missing bundle key");
            if (bytes <= 0) throw new ArgumentOutOfRangeException("bytes");
            Entry e;
            bool created = !entries.TryGetValue(key, out e);
            if (created)
            {
                e = new Entry { Key = key, Bytes = bytes };
                entries.Add(key, e);
            }
            else if (e.Bytes != bytes) throw new ArgumentException("Conflicting shared texture size: " + key);
            e.Pins++;
            e.LastUse = ++clock;
            var lease = new Lease(this, e, changed);
            if (e.Loading && changed != null) e.Waiters.Add(lease);
            if (created)
            {
                try { load(key, (value, error) => Complete(e, value, error)); }
                catch (Exception error)
                {
                    if (!e.Loading) throw; // Do not disguise a consumer callback failure as I/O failure.
                    Complete(e, null, error.Message);
                }
            }
            return lease;
        }

        void Complete(Entry e, T value, string error)
        {
            if (!e.Loading) throw new InvalidOperationException("Bundle completed twice: " + e.Key);
            e.Loading = false;
            e.Error = error ?? (value == null ? "Bundle loader returned no asset" : null);
            if (e.Error != null)
            {
                entries.Remove(e.Key); // Failed owners retain their result; a new request may retry.
                if (value != null) unload(value);
            }
            else
            {
                e.Value = value;
                LoadedBytes += e.Bytes;
                PeakBytes = Math.Max(PeakBytes, LoadedBytes);
            }
            var waiters = e.Waiters.ToArray();
            e.Waiters.Clear();
            List<Exception> errors = null;
            foreach (var lease in waiters)
            {
                if (lease.Released) continue;
                try { lease.Changed(lease); }
                catch (Exception failure)
                {
                    if (errors == null) errors = new List<Exception>();
                    errors.Add(failure);
                }
            }
            Trim();
            if (errors != null) throw new AggregateException(errors);
        }

        void Release(Entry e)
        {
            if (e.Pins <= 0) throw new InvalidOperationException("Unbalanced artwork release");
            e.Pins--;
            e.LastUse = ++clock;
            Trim();
        }

        public void Trim() { TrimTo(budget); }
        public void TrimAllUnpinned() { TrimTo(0); }
        void TrimTo(long target)
        {
            while (LoadedBytes > target)
            {
                Entry oldest = null;
                foreach (var e in entries.Values)
                    if (!e.Loading && e.Value != null && e.Pins == 0 && (oldest == null || e.LastUse < oldest.LastUse)) oldest = e;
                if (oldest == null) break;
                entries.Remove(oldest.Key);
                var value = oldest.Value;
                oldest.Value = null;
                LoadedBytes -= oldest.Bytes;
                unload(value);
            }
        }
    }
}
