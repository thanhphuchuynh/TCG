using System;
using System.Collections.Generic;
using UnityEngine;

namespace TCG.SwitchStreaming
{
    // One token per owner/material/slot. The slot's owners share the current value.
    // A new value invalidates every previous token only after SetTexture succeeds.
    public static class SurfaceTextures
    {
        sealed class Slot
        {
            public Material Material;
            public string Name;
            public Texture Texture;
            public readonly List<Binding> Bindings = new List<Binding>();
        }
        sealed class Binding
        {
            public SurfaceMaterialOwner Owner;
            public Slot Slot;
            public ArtCache<LoadedSurfaceBundle>.Lease Lease;
        }
        static readonly List<Slot> slots = new List<Slot>();
        static readonly Dictionary<SurfaceMaterialOwner, List<Binding>> owners = new Dictionary<SurfaceMaterialOwner, List<Binding>>();

        public static void SetTexture(Material material, GameObject owner, string slot, string key, Texture fallback)
        {
            // Let Unity perform its own material/property validation; do not restrict
            // SetTexture to shader properties visible through HasProperty.
            if (!owner) throw new ArgumentNullException("owner");
            ArtCache<LoadedSurfaceBundle>.Lease lease = null;
            Texture texture = fallback;
            try
            {
                if (!fallback)
                {
                    var service = SurfaceTextureService.Instance;
                    if (service.Contains(key)) { lease = service.Acquire(key); texture = lease.Value.Texture; }
                }
                else lease = SurfaceTextureService.Host.Borrow(fallback);
                // The host also sweeps owners that Unity never activated.
                var host = SurfaceTextureService.Host;
                var holder = owner.GetComponent<SurfaceMaterialOwner>();
                if (!holder) holder = owner.AddComponent<SurfaceMaterialOwner>();
                Slot previous = null;
                foreach (var candidate in slots) if (candidate.Material == material && candidate.Name == slot) { previous = candidate; break; }
                bool shared = previous != null && previous.Texture == texture && material.GetTexture(slot) == previous.Texture;
                material.SetTexture(slot, texture);
                if (previous != null && !shared) RemoveSlot(previous, false);
                var current = shared ? previous : new Slot { Material = material, Name = slot, Texture = texture };
                if (!shared) slots.Add(current);
                List<Binding> owned;
                if (!owners.TryGetValue(holder, out owned)) { owned = new List<Binding>(); owners.Add(holder, owned); }
                var prior = owned.Find(binding => binding.Slot == current);
                var bindingNew = new Binding { Owner = holder, Slot = current, Lease = lease };
                current.Bindings.Add(bindingNew); owned.Add(bindingNew); lease = null;
                if (prior != null) RemoveBinding(prior, false);
            }
            finally { if (lease != null) lease.Dispose(); }
        }
        static void RemoveBinding(Binding binding, bool clear)
        {
            var slot = binding.Slot;
            slot.Bindings.Remove(binding);
            List<Binding> owned;
            if (owners.TryGetValue(binding.Owner, out owned)) { owned.Remove(binding); if (owned.Count == 0) owners.Remove(binding.Owner); }
            if (slot.Bindings.Count == 0)
            {
                // Only cached values belong to this service. Resident and mod
                // fallbacks remain borrowed material state after owner teardown.
                // Clear cached references before the final pin can unload them.
                if (clear && binding.Lease != null && slot.Material && slot.Material.GetTexture(slot.Name) == slot.Texture) slot.Material.SetTexture(slot.Name, null);
                slots.Remove(slot);
            }
            if (binding.Lease != null) binding.Lease.Dispose();
        }
        static void RemoveSlot(Slot slot, bool clear)
        { foreach (var binding in slot.Bindings.ToArray()) RemoveBinding(binding, clear); }
        internal static void Release(SurfaceMaterialOwner owner)
        {
            List<Binding> bindings;
            if (owners.TryGetValue(owner, out bindings)) foreach (var binding in bindings.ToArray()) RemoveBinding(binding, true);
        }
        internal static void ReleaseDestroyedOwners()
        {
            // A live manager can replace/destroy materials repeatedly. Dead native
            // targets cannot render and must not keep their textures pinned.
            foreach (var slot in slots.ToArray()) if (!slot.Material) RemoveSlot(slot, false);
            // Unity does not send OnDestroy to a GameObject that was never active.
            foreach (var owner in new List<SurfaceMaterialOwner>(owners.Keys)) if (!owner) Release(owner);
        }
        internal static void ReleaseAll() { foreach (var slot in slots.ToArray()) RemoveSlot(slot, true); }
    }

    public sealed class SurfaceMaterialOwner : MonoBehaviour
    {
        // Disabling the manager does not hide the separate world renderer.
        void OnDestroy() { SurfaceTextures.Release(this); }
    }
}
