using System;
using UnityEngine;
using TCG.SwitchStreaming;

// Minimal native lifecycle/serialization fixture, never copied into the game.
// The source transform's placement before both branches is tested separately.
namespace CC
{
    public sealed class CharacterCustomization : MonoBehaviour
    {
        public SkinnedMeshRenderer MainMesh;
        public int Parses, Reloads;
        bool initialized;
        public void Initialize()
        {
            MeshBundleOwner.PrepareRoot(gameObject);
            if (!MainMesh.sharedMesh) throw new InvalidOperationException("Inactive Initialize has no authored body mesh");
            if (initialized) { Reloads++; MainMesh.SetBlendShapeWeight(0, 81); return; }
            if (MainMesh.sharedMesh.GetBlendShapeName(0) != "smile") throw new InvalidOperationException("Original blendshape parse changed");
            initialized = true; Parses++; MainMesh.SetBlendShapeWeight(0, 61);
        }
    }
}
