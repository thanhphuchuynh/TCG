using System;
using System.Collections.Generic;
using TCG.SwitchStreaming;

static class HostTests
{
    sealed class Payload
    {
        public bool Destroyed;
        public readonly string Name;
        public Payload(string name) { Name = name; }
    }
    // Only the external Unity load completion is controlled here. The real
    // cache, reservations, callbacks, eviction and disposal are exercised.
    sealed class Loads
    {
        readonly Dictionary<string, Action<Payload, string>> pending = new Dictionary<string, Action<Payload, string>>();
        public void Start(string key, Action<Payload, string> done) { pending.Add(key, done); }
        public Payload Complete(string key)
        {
            var value = new Payload(key); var done = pending[key]; pending.Remove(key); done(value, null); return value;
        }
        public void Fail(string key) { var done = pending[key]; pending.Remove(key); done(null, "missing SD bundle"); }
        public ArtCache<Payload> Cache(long budget) { return new ArtCache<Payload>(budget, Start, p => p.Destroyed = true); }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected rejected invalid input"); }
    static void SharedReservationsProtectOnePayload()
    {
        var loads = new Loads(); var cache = loads.Cache(0);
        var a = cache.Acquire("card", 8); var b = cache.Acquire("card", 8);
        Check(cache.PendingCount == 1 && cache.PinnedBytes == 8, "Shared pending reservation was duplicated");
        var payload = loads.Complete("card");
        Check(a.Ready && b.Ready && ReferenceEquals(a.Value, b.Value), "Shared art was not returned to both owners");
        a.Dispose(); cache.Trim(); Check(!payload.Destroyed && cache.LoadedBytes == 8, "Visible shared art was evicted");
        b.Dispose(); cache.Trim(); Check(payload.Destroyed && cache.LoadedBytes == 0, "Last release did not reclaim over-budget art");
    }
    static void EvictsLeastRecentlyUsedUnpinnedArt()
    {
        var loads = new Loads(); var cache = loads.Cache(16);
        var a = cache.Acquire("a", 8); var pa = loads.Complete("a"); a.Dispose();
        var b = cache.Acquire("b", 8); var pb = loads.Complete("b"); b.Dispose();
        a = cache.Acquire("a", 8); a.Dispose();
        var c = cache.Acquire("c", 8); loads.Complete("c");
        Check(!pa.Destroyed && pb.Destroyed && cache.LoadedBytes == 16, "Eviction did not preserve recently used and pinned art");
        c.Dispose(); cache.TrimAllUnpinned(); Check(cache.LoadedBytes == 0, "Explicit pressure trim retained unused art");
    }
    static void CancelledPendingOwnerCannotReceiveOrLeakArt()
    {
        var loads = new Loads(); var cache = loads.Cache(0); int notified = 0;
        var a = cache.Acquire("cancel", 9, lease => notified++); a.Dispose(); a.Dispose();
        var payload = loads.Complete("cancel");
        Check(notified == 0 && payload.Destroyed && cache.PendingCount == 0 && cache.PinnedBytes == 0, "Cancelled owner received or leaked art");
    }
    static void FailureFinishesAllOwnersAndCanRetry()
    {
        var loads = new Loads(); var cache = loads.Cache(8); int notified = 0;
        var a = cache.Acquire("fail", 8, lease => { Check(lease.Error != null, "Failure missing error"); notified++; });
        var b = cache.Acquire("fail", 8); loads.Fail("fail");
        Check(a.Ready && b.Ready && a.Value == null && b.Error != null && notified == 1 && cache.LoadedBytes == 0, "Failure left inconsistent state");
        a.Dispose(); b.Dispose(); var retry = cache.Acquire("fail", 8); var value = loads.Complete("fail");
        Check(retry.Ready && ReferenceEquals(retry.Value, value), "Failed load could not retry"); retry.Dispose();
    }
    static void PinnedContentMayExceedBudget()
    {
        var loads = new Loads(); var cache = loads.Cache(128L * 1024 * 1024);
        var a = cache.Acquire("large", 160L * 1024 * 1024); var payload = loads.Complete("large"); cache.Trim();
        Check(!payload.Destroyed && cache.PinnedBytes == 160L * 1024 * 1024 && cache.PeakBytes == cache.LoadedBytes, "Budget destroyed visible art");
        a.Dispose(); cache.Trim(); Check(payload.Destroyed, "Over-budget unpinned art survived");
    }
    static void CompletionCallbacksMayReleaseAndAcquire()
    {
        var loads = new Loads(); var cache = loads.Cache(0); ArtCache<Payload>.Lease next = null;
        var first = cache.Acquire("first", 8, lease => { lease.Dispose(); next = cache.Acquire("next", 8); });
        var p = loads.Complete("first"); var q = loads.Complete("next");
        Check(p.Destroyed && !q.Destroyed && next.Ready && cache.PendingCount == 0, "Reentrant completion corrupted ownership");
        first.Dispose(); next.Dispose();
    }
    static void InvalidSizesAndConflictingSharedMetadataAreRejected()
    {
        var loads = new Loads(); var cache = loads.Cache(8);
        Throws(() => cache.Acquire("", 8)); Throws(() => cache.Acquire("x", -1));
        var first = cache.Acquire("same", 8); Throws(() => cache.Acquire("same", 9)); first.Dispose(); loads.Complete("same");
    }
    static void ManifestRejectsTraversalAndAmbiguousKeys()
    {
        foreach (string file in new[] { "../art", "/art", "C:art", "a\\b", "a/b", ".", ".." })
            Throws(() => new ArtManifest { entries = new List<ArtEntry> { new ArtEntry { key = "k", bundle = file, asset = "assets/sprite/a.asset", bytes = 8 } } }.Validate());
        var good = new ArtEntry { key = "monster:1:icon:0", bundle = "art_0123", asset = "assets/sprite/a.asset", bytes = 8, crc = 42 };
        var m = new ArtManifest { entries = new List<ArtEntry> { good } };
        Check(m.Validate().Count == 1, "Valid local manifest rejected"); m.entries.Add(good); Throws(() => m.Validate());
    }
    static int Main()
    {
        Action[] tests = { SharedReservationsProtectOnePayload, EvictsLeastRecentlyUsedUnpinnedArt, CancelledPendingOwnerCannotReceiveOrLeakArt,
            FailureFinishesAllOwnersAndCanRetry, PinnedContentMayExceedBudget, CompletionCallbacksMayReleaseAndAcquire,
            InvalidSizesAndConflictingSharedMetadataAreRejected, ManifestRejectsTraversalAndAmbiguousKeys };
        foreach (var test in tests) { test(); Console.WriteLine("PASS " + test.Method.Name); }
        Console.WriteLine(tests.Length + " cache/manifest tests passed"); return 0;
    }
}
