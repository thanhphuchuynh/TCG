using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace TCG.SwitchStreaming
{
    public sealed class CardArtOwner : MonoBehaviour
    {
        internal int Id;
        void OnDisable() { if (Id != 0) CardArtSession.DisableById(Id); }
        void OnEnable() { if (Id != 0) CardArtSession.ReactivateById(Id); }
        void OnDestroy() { if (Id != 0) CardArtSession.Forget(Id); }
    }

    public static class CardArtSession
    {
        sealed class State
        {
            public Component Owner;
            public CardArtOwner Marker;
            public CanvasGroup Group;
            public float Alpha;
            public bool Hidden, Interactable, BlocksRaycasts, Active = true, Visible = true, Queued;
            public int Generation;
            public Action Refresh;
            public Frame Frame;
            public readonly Dictionary<string, ArtCache<LoadedArtBundle>.Lease> Warm = new Dictionary<string, ArtCache<LoadedArtBundle>.Lease>();
            public readonly Dictionary<string, ArtCache<LoadedArtBundle>.Lease> Primed = new Dictionary<string, ArtCache<LoadedArtBundle>.Lease>();
        }
        sealed class Frame
        {
            public State State;
            public int Generation, Pending;
            public bool Building = true, Suppressed;
            public string Error;
            public readonly Dictionary<string, ArtCache<LoadedArtBundle>.Lease> Leases = new Dictionary<string, ArtCache<LoadedArtBundle>.Lease>();
            public readonly HashSet<string> Keys = new HashSet<string>(StringComparer.Ordinal);
        }
        sealed class Scope : IDisposable
        {
            readonly Frame frame, previous;
            readonly Dictionary<string, ArtCache<LoadedArtBundle>.Lease> old;
            bool ended;
            public Scope(Frame frame, Frame previous, Dictionary<string, ArtCache<LoadedArtBundle>.Lease> old)
            { this.frame = frame; this.previous = previous; this.old = old; }
            public void Dispose()
            {
                if (ended) return;
                ended = true; current = previous; frame.Building = false;
                ClearImages(frame.State, old, frame.Leases);
                Release(old);
                PrunePreloads(frame);
                if (frame.Suppressed && frame.Keys.Count > 0 || frame.Pending > 0 || frame.Error != null) Hide(frame.State);
                else Show(frame.State);
                if (!frame.Suppressed && frame.Pending == 0 && frame.Error == null) Release(frame.State.Primed);
                Finish(frame);
            }
        }
        sealed class EmptyScope : IDisposable { public void Dispose() { } }
        static readonly Dictionary<int, State> owners = new Dictionary<int, State>();
        static readonly List<int> ownerIds = new List<int>();
        static Frame current;

        public static IDisposable Begin(Component owner, Action refresh, bool initiallyVisible = true)
        {
            if (!UnityArtService.Available) return new EmptyScope();
            if (!owner) throw new ArgumentNullException("owner");
            int id = owner.GetInstanceID(); State state;
            if (!owners.TryGetValue(id, out state))
            {
                state = new State { Owner = owner, Visible = initiallyVisible };
                owners.Add(id, state);
                state.Marker = owner.gameObject.AddComponent<CardArtOwner>(); state.Marker.Id = id;
                state.Group = owner.GetComponent<CanvasGroup>();
                if (!state.Group) state.Group = owner.gameObject.AddComponent<CanvasGroup>();
            }
            state.Refresh = refresh;
            var old = state.Frame == null ? new Dictionary<string, ArtCache<LoadedArtBundle>.Lease>() : state.Frame.Leases;
            var frame = new Frame { State = state, Generation = ++state.Generation, Suppressed = !state.Active || !state.Visible || !owner.gameObject.activeInHierarchy };
            state.Frame = frame;
            UnityArtService.Instance.Trace("begin " + id + " generation=" + frame.Generation + " suppressed=" + frame.Suppressed);
            var scope = new Scope(frame, current, old); current = frame; return scope;
        }

        public static bool TryResolve(int monster, string role, int index, out Sprite sprite)
        { return TryResolveKey("monster:" + monster + ":" + role + ":" + index, out sprite); }

        public static bool TryResolveKey(string key, out Sprite sprite)
        {
            sprite = null;
            if (!UnityArtService.Available) return false;
            var service = UnityArtService.Instance; ArtEntry entry;
            if (!service.Find(key, out entry)) return false;
            if (current == null) throw new InvalidOperationException("Migrated artwork requested outside an owner session: " + key);
            var frame = current;
            frame.Keys.Add(key);
            sprite = service.Placeholder;
            if (frame.Suppressed) return true;
            ArtCache<LoadedArtBundle>.Lease lease;
            if (!frame.Leases.TryGetValue(entry.bundle, out lease))
            {
                frame.Pending++;
                bool completed = false;
                lease = service.Acquire(entry, ready =>
                {
                    completed = true; frame.Pending--;
                    service.Trace("ready generation=" + frame.Generation + " current=" + frame.State.Generation + " pending=" + frame.Pending);
                    if (ready.Error != null) frame.Error = ready.Error;
                    Finish(frame);
                });
                frame.Leases.Add(entry.bundle, lease);
                if (lease.Ready && !completed) frame.Pending--;
            }
            if (!lease.Ready) return true;
            if (lease.Error != null) { frame.Error = lease.Error; return true; }
            try { sprite = lease.Value.Sprite(entry.asset); }
            catch (Exception error) { frame.Error = error.Message; }
            return true;
        }

        static void Finish(Frame frame)
        {
            var state = frame.State;
            if (frame.Building || state.Frame != frame || state.Generation != frame.Generation) return;
            if (frame.Error != null)
            {
                UnityArtService.Instance.ReportError(frame.Error);
                ClearImages(state, frame.Leases, null); Release(frame.Leases); Hide(state); return;
            }
            if (frame.Pending == 0 && state.Hidden && state.Active && state.Visible) state.Queued = true;
        }
        static void Hide(State state)
        {
            if (state.Hidden || !state.Group) return;
            state.Alpha = state.Group.alpha;
            state.Interactable = state.Group.interactable; state.BlocksRaycasts = state.Group.blocksRaycasts;
            state.Group.alpha = 0; state.Group.interactable = false; state.Group.blocksRaycasts = false; state.Hidden = true;
        }
        static void Show(State state)
        {
            if (!state.Hidden || !state.Group) return;
            state.Group.alpha = state.Alpha; state.Group.interactable = state.Interactable; state.Group.blocksRaycasts = state.BlocksRaycasts; state.Hidden = false;
        }
        static void ClearImages(State state, Dictionary<string, ArtCache<LoadedArtBundle>.Lease> old, Dictionary<string, ArtCache<LoadedArtBundle>.Lease> keep)
        {
            if (!state.Owner || old.Count == 0) return;
            foreach (var image in state.Owner.GetComponentsInChildren<Image>(true))
            {
                if (image.GetComponentInParent<CardArtOwner>(true) != state.Marker) continue;
                foreach (var pair in old)
                    if ((keep == null || !keep.ContainsKey(pair.Key)) && pair.Value.Value != null && pair.Value.Value.Owns(image.sprite))
                    { image.sprite = null; break; }
            }
        }
        static void Release(Dictionary<string, ArtCache<LoadedArtBundle>.Lease> leases)
        {
            foreach (var lease in leases.Values) lease.Dispose();
            leases.Clear();
        }
        static void PrunePreloads(Frame frame)
        {
            var needed = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in frame.Keys)
            {
                ArtEntry entry;
                if (UnityArtService.Instance.Find(key, out entry)) needed.Add(entry.bundle);
            }
            foreach (var leases in new[] { frame.State.Primed, frame.State.Warm })
            {
                var removed = new List<string>();
                foreach (var pair in leases)
                    if (!needed.Contains(pair.Key)) { pair.Value.Dispose(); removed.Add(pair.Key); }
                foreach (string key in removed) leases.Remove(key);
            }
        }

        public static void Disable(Component owner) { if (owner) DisableById(owner.GetInstanceID()); }
        internal static void DisableById(int id)
        {
            State state; if (!owners.TryGetValue(id, out state)) return;
            UnityArtService.Instance.Trace("disable " + id);
            state.Active = false; state.Generation++; state.Queued = false;
            Hide(state);
            if (state.Frame != null) { ClearImages(state, state.Frame.Leases, null); Release(state.Frame.Leases); }
            Release(state.Warm);
            Release(state.Primed);
        }
        public static void Reactivate(Component owner) { if (owner) ReactivateById(owner.GetInstanceID()); }
        internal static void ReactivateById(int id)
        {
            State state; if (!owners.TryGetValue(id, out state) || !state.Owner || !state.Owner.gameObject.activeInHierarchy) return;
            UnityArtService.Instance.Trace("reactivate " + id + " keys=" + (state.Frame == null ? -1 : state.Frame.Keys.Count));
            state.Active = true;
            if (state.Visible && state.Frame != null && state.Frame.Keys.Count > 0) state.Queued = true;
        }
        public static void SetVisible(Component owner, bool visible)
        {
            if (!owner) return; State state;
            if (!owners.TryGetValue(owner.GetInstanceID(), out state) || state.Visible == visible) return;
            state.Visible = visible;
            if (visible) Reactivate(owner); else Disable(owner);
        }
        public static void Prefetch(Component owner)
        {
            if (!owner || !UnityArtService.Available) return; State state;
            if (!owners.TryGetValue(owner.GetInstanceID(), out state) || state.Frame == null) return;
            var service = UnityArtService.Instance;
            foreach (string key in state.Frame.Keys)
            {
                ArtEntry entry;
                if (!service.Find(key, out entry) || state.Warm.ContainsKey(entry.bundle)) continue;
                bool completed = false;
                var lease = service.Acquire(entry, ready => { completed = true; ready.Dispose(); state.Warm.Remove(entry.bundle); });
                if (lease.Ready) lease.Dispose();
                else if (!completed) state.Warm[entry.bundle] = lease;
            }
        }
        public static void Prime(Component owner)
        {
            if (!owner || !UnityArtService.Available) return; State state;
            if (!owners.TryGetValue(owner.GetInstanceID(), out state) || state.Frame == null) return;
            var service = UnityArtService.Instance;
            foreach (string key in state.Frame.Keys)
            {
                ArtEntry entry;
                if (!service.Find(key, out entry) || state.Primed.ContainsKey(entry.bundle)) continue;
                var lease = service.Acquire(entry, ready => { if (ready.Error != null) service.ReportError(ready.Error); });
                state.Primed.Add(entry.bundle, lease);
                if (lease.Ready && lease.Error != null) service.ReportError(lease.Error);
            }
        }
        public static bool Ready(Component owner)
        {
            if (!owner || !UnityArtService.Available) return true; State state;
            if (!owners.TryGetValue(owner.GetInstanceID(), out state) || state.Frame == null) return true;
            var service = UnityArtService.Instance;
            foreach (string key in state.Frame.Keys)
            {
                ArtEntry entry;
                if (!service.Find(key, out entry)) continue;
                ArtCache<LoadedArtBundle>.Lease lease;
                if (!state.Frame.Leases.TryGetValue(entry.bundle, out lease) && !state.Primed.TryGetValue(entry.bundle, out lease)) return false;
                if (!lease.Ready || lease.Error != null || lease.Value == null) return false;
            }
            return true;
        }
        public static void CancelPrime(Component owner)
        {
            if (!owner) return; State state;
            if (owners.TryGetValue(owner.GetInstanceID(), out state)) Release(state.Primed);
        }
        public static bool Displayed(Component owner)
        {
            if (!owner || !UnityArtService.Available) return true; State state;
            return !owners.TryGetValue(owner.GetInstanceID(), out state) || !state.Hidden;
        }
        internal static void Forget(int id)
        {
            State state; if (!owners.TryGetValue(id, out state)) return;
            DisableById(id); owners.Remove(id);
        }
        public static void Tick()
        {
            ownerIds.Clear();
            ownerIds.AddRange(owners.Keys);
            foreach (int id in ownerIds)
            {
                State state;
                if (!owners.TryGetValue(id, out state)) continue;
                if (!state.Owner) { Forget(id); continue; }
                if (!state.Queued || !state.Active || !state.Visible || !state.Owner.gameObject.activeInHierarchy) continue;
                state.Queued = false;
                UnityArtService.Instance.Trace("refresh " + id);
                try { state.Refresh(); }
                catch (Exception error) { Hide(state); UnityArtService.Instance.ReportError(error.ToString()); }
            }
        }
    }
}
