using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TCG.SwitchStreaming
{
    [Serializable]
    public sealed class MeshBinding
    {
        public SkinnedMeshRenderer skinned;
        public MeshFilter filter;
        public string key;
        public float[] defaultWeights;
        public Bounds defaultBounds;
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-32000)]
    public sealed class MeshBundleOwner : MonoBehaviour
    {
        public MeshBinding[] Bindings;
        sealed class State
        {
            public MeshBinding Binding;
            public float[] Weights;
            public Transform[] Bones;
            public Transform RootBone;
            public Bounds Bounds;
            public bool Captured;
        }
        State[] states;
        readonly Dictionary<string, ArtCache<LoadedMeshBundle>.Lease> leases = new Dictionary<string, ArtCache<LoadedMeshBundle>.Lease>(StringComparer.Ordinal);
        MeshService service;
        bool prepared;

        public static GameObject Instantiate(GameObject prefab, Transform parent)
        {
            var clone = UnityEngine.Object.Instantiate(prefab, parent);
            try
            {
                foreach (var owner in clone.GetComponentsInChildren<MeshBundleOwner>(true)) owner.Prepare();
                return clone;
            }
            catch { UnityEngine.Object.Destroy(clone); throw; }
        }
        void Awake() { Prepare(); }
        void OnEnable() { Prepare(); }
        public void Prepare()
        {
            if (prepared || Bindings == null || Bindings.Length == 0) return;
            if (states == null)
            {
                states = new State[Bindings.Length];
                for (int i = 0; i < Bindings.Length; i++) states[i] = new State { Binding = Bindings[i] };
            }
            service = MeshService.Instance;
            try
            {
                foreach (var state in states)
                {
                    var binding = state.Binding;
                    if (binding == null || (binding.skinned ? 1 : 0) + (binding.filter ? 1 : 0) != 1)
                        throw new InvalidDataException("Mesh binding requires exactly one live renderer/filter");
                    if (!leases.ContainsKey(binding.key))
                    {
                        var lease = service.Acquire(binding.key); leases.Add(binding.key, lease);
                        if (!lease.Ready || lease.Value == null) throw new InvalidDataException(lease.Error ?? "Mesh load did not complete synchronously");
                    }
                }
                foreach (var state in states)
                {
                    var binding = state.Binding; var mesh = leases[binding.key].Value.Mesh;
                    if (binding.skinned)
                    {
                        var skin = binding.skinned;
                        var bones = state.Captured ? state.Bones : skin.bones;
                        var rootBone = state.Captured ? state.RootBone : skin.rootBone;
                        skin.sharedMesh = mesh; skin.bones = bones; skin.rootBone = rootBone;
                        skin.localBounds = state.Captured ? state.Bounds : binding.defaultBounds;
                        var weights = state.Captured ? state.Weights : binding.defaultWeights;
                        for (int i = 0; i < mesh.blendShapeCount; i++) skin.SetBlendShapeWeight(i, weights != null && i < weights.Length ? weights[i] : 0);
                    }
                    else binding.filter.sharedMesh = mesh;
                }
                prepared = true; service.Track(this);
            }
            catch (Exception error)
            {
                ReleaseMeshes(); service.ReportError(error.Message); throw;
            }
        }
        internal void ReleaseMeshes()
        {
            if (states != null)
                foreach (var state in states)
                {
                    var binding = state.Binding; if (binding == null) continue;
                    if (binding.skinned)
                    {
                        var skin = binding.skinned;
                        if (prepared && skin.sharedMesh)
                        {
                            state.Weights = new float[skin.sharedMesh.blendShapeCount];
                            for (int i = 0; i < state.Weights.Length; i++) state.Weights[i] = skin.GetBlendShapeWeight(i);
                            state.Bones = skin.bones; state.RootBone = skin.rootBone; state.Bounds = skin.localBounds; state.Captured = true;
                        }
                        skin.sharedMesh = null;
                    }
                    if (binding.filter) binding.filter.sharedMesh = null;
                }
            // All Unity references are cleared before any final pin can unload native objects.
            foreach (var lease in leases.Values) lease.Dispose();
            leases.Clear(); prepared = false;
            if (service) service.Forget(this);
        }
        void OnDisable() { ReleaseMeshes(); }
        void OnDestroy() { ReleaseMeshes(); }
    }
}
