using System;
using System.Collections;
using System.IO;
using TCG.SwitchStreaming;
using UnityEngine;

public sealed class MeshRuntimeProbe : MonoBehaviour
{
    public string BundleRoot, NestedBodyKey;
    public GameObject NestedBodyPrefab;
    public uint Crc, SecondCrc, BadPayloadCrc;
    const string First = "mesh:11111111111111111111111111111111", Second = "mesh:22222222222222222222222222222222";
    int passed;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    IEnumerator Start()
    {
        var tests = Tests(); Exception failure = null;
        while (true)
        {
            bool more; try { more = tests.MoveNext(); } catch (Exception error) { failure = error; break; }
            if (!more) break;
            yield return tests.Current;
        }
        string result = failure == null ? "PASS actual Unity mesh runtime cases: " + passed : "FAIL " + failure;
        File.WriteAllText(Path.GetFullPath("mesh-runtime-result.txt"), result + "\n"); Debug.Log(result);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(failure == null ? 0 : 1);
#endif
    }
    MeshBundleOwner Owner(string key, bool active = true)
    {
        var go = new GameObject("part"); go.SetActive(false);
        var bone = new GameObject("bone"); bone.transform.SetParent(go.transform, false);
        var skin = go.AddComponent<SkinnedMeshRenderer>(); skin.bones = new[] { bone.transform }; skin.rootBone = bone.transform;
        var filter = go.AddComponent<MeshFilter>();
        var owner = go.AddComponent<MeshBundleOwner>();
        owner.Bindings = new[] {
            new MeshBinding { key = key, skinned = skin, defaultWeights = new[] { 23f }, defaultBounds = new Bounds(Vector3.one, new Vector3(3, 4, 5)) },
            new MeshBinding { key = key, filter = filter }
        };
        if (active) go.SetActive(true);
        return owner;
    }
    IEnumerator Tests()
    {
        var manifest = new MeshManifest();
        manifest.entries.Add(new MeshEntry { key = First, bundle = "mesh_rigged", asset = First, bytes = 1024, crc = Crc });
        manifest.entries.Add(new MeshEntry { key = Second, bundle = "mesh_second", asset = Second, bytes = 1024, crc = SecondCrc });
        manifest.entries.Add(new MeshEntry { key = "mesh:33333333333333333333333333333333", bundle = "mesh_missing", asset = First, bytes = 1024, crc = Crc });
        manifest.entries.Add(new MeshEntry { key = "mesh:44444444444444444444444444444444", bundle = "mesh_wrongcrc", asset = First, bytes = 1024, crc = Crc ^ 1u });
        manifest.entries.Add(new MeshEntry { key = "mesh:55555555555555555555555555555555", bundle = "mesh_badpayload", asset = First, bytes = 1024, crc = BadPayloadCrc });
        var invalid = new MeshManifest(); invalid.entries.Add(new MeshEntry { key = First, bundle = "../escape", asset = First, bytes = 1, crc = 1 });
        bool rejected = false; try { invalid.Validate(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "Traversal bundle accepted"); passed++;
        var service = new GameObject("Mesh service").AddComponent<MeshService>(); service.Initialize(BundleRoot, manifest, 0);
        var a = Owner(First); var b = Owner(First);
        var skin = a.GetComponent<SkinnedMeshRenderer>(); var native = skin.sharedMesh;
        Check(native && native == b.GetComponent<SkinnedMeshRenderer>().sharedMesh && native == a.GetComponent<MeshFilter>().sharedMesh && service.Cache.PinnedBytes == 1024 && service.Cache.PendingCount == 0, "Synchronous shared mesh binding failed");
        Check(native.vertexCount == 3 && native.triangles.Length == 3 && native.bindposes.Length == 1 && native.boneWeights[2].weight0 == 1 && native.blendShapeCount == 1 && native.GetBlendShapeName(0) == "smile" && native.uv[2] == Vector2.up, "Authored rig/geometry/blendshape/UV payload changed");
        var delta = new Vector3[3]; native.GetBlendShapeFrameVertices(0, 0, delta, null, null);
        Check(delta[1] == Vector3.forward && skin.GetBlendShapeWeight(0) == 23 && skin.localBounds.size == new Vector3(3, 4, 5), "Authored delta/default weights/bounds changed"); passed++;
        skin.SetBlendShapeWeight(0, 67); var bounds = new Bounds(new Vector3(7, 8, 9), new Vector3(10, 11, 12)); skin.localBounds = bounds;
        var bone = skin.bones[0]; var rootBone = skin.rootBone;
        a.gameObject.SetActive(false); Check(!skin.sharedMesh && !a.GetComponent<MeshFilter>().sharedMesh && native && service.Cache.PinnedBytes == 1024, "One owner destroyed shared native mesh or retained references");
        b.gameObject.SetActive(false); yield return null;
        Check(!native && service.Cache.PinnedBytes == 0 && service.Cache.LoadedBytes == 0, "Final unpin did not destroy native mesh"); passed++;
        a.gameObject.SetActive(true);
        Check(skin.sharedMesh && skin.GetBlendShapeWeight(0) == 67 && skin.bones[0] == bone && skin.rootBone == rootBone && skin.localBounds == bounds, "Rehydrate lost current weights/bones/root/bounds");
        for (int i = 0; i < 4; i++) { a.gameObject.SetActive(false); a.gameObject.SetActive(true); Check(skin.GetBlendShapeWeight(0) == 67, "Repeated rehydrate lost setup state"); }
        a.gameObject.SetActive(false); passed++;
        var body = Owner(First, false); var unrelated = Owner(Second, false);
        unrelated.transform.SetParent(body.transform, false);
        MeshBundleOwner.PrepareRoot(body.gameObject);
        var bodySkin = body.GetComponent<SkinnedMeshRenderer>();
        Check(bodySkin.sharedMesh && bodySkin.sharedMesh.GetBlendShapeName(0) == "smile" &&
            !unrelated.GetComponent<SkinnedMeshRenderer>().sharedMesh && service.Cache.PinnedBytes == 1024,
            "Inactive root initialization did not prepare exactly its own authored geometry");
        bodySkin.SetBlendShapeWeight(0, 88);
        yield return null; yield return null;
        Check(!bodySkin.sharedMesh && service.Cache.PinnedBytes == 0, "Inactive root initialization retained geometry after setup frame");
        MeshBundleOwner.PrepareRoot(body.gameObject);
        Check(bodySkin.sharedMesh && bodySkin.GetBlendShapeWeight(0) == 88 && !unrelated.GetComponent<SkinnedMeshRenderer>().sharedMesh,
            "Reused inactive initialization lost setup values or prepared unrelated child");
        body.gameObject.SetActive(true);
        Check(bodySkin.sharedMesh && bodySkin.GetBlendShapeWeight(0) == 88, "Body activation reset original setup state");
        body.gameObject.SetActive(false); Destroy(body.gameObject); yield return null;
        var ordinary = new GameObject("unmigrated customizer");
        MeshBundleOwner.PrepareRoot(ordinary); Destroy(ordinary);
        Check(service.Cache.PinnedBytes == 0 && service.InactivePendingCount == 0, "Root setup cleanup or no-owner path retained pins"); passed++;
        var parent = new GameObject("inactive original setup parent"); parent.SetActive(false);
        var source = Owner(First, false);
        var clone = MeshBundleOwner.Instantiate(source.gameObject, parent.transform);
        var cloneSkin = clone.GetComponent<SkinnedMeshRenderer>();
        Check(cloneSkin.sharedMesh && cloneSkin.sharedMesh.blendShapeCount == 1 && cloneSkin.GetBlendShapeWeight(0) == 23, "Inactive Instantiate returned before original setup could inspect mesh");
        cloneSkin.SetBlendShapeWeight(0, 81); cloneSkin.localBounds = bounds;
        yield return null; yield return null;
        Check(!cloneSkin.sharedMesh && service.Cache.PinnedBytes == 0, "Never activated clone leaked pins after setup frame");
        clone.SetActive(true); parent.SetActive(true);
        Check(cloneSkin.sharedMesh && cloneSkin.GetBlendShapeWeight(0) == 81 && cloneSkin.localBounds == bounds && cloneSkin.bones[0].parent == clone.transform, "Late activation lost cloned rig/current setup values");
        Destroy(parent); Destroy(source.gameObject); yield return null;
        Check(service.Cache.PinnedBytes == 0, "Destroy active clone leaked pins"); passed++;
        parent = new GameObject("destroy never active parent"); parent.SetActive(false); source = Owner(First, false);
        clone = MeshBundleOwner.Instantiate(source.gameObject, parent.transform); Destroy(clone); yield return null; yield return null;
        Check(service.Cache.PinnedBytes == 0, "Destroyed never active clone leaked pins"); Destroy(parent); Destroy(source.gameObject); passed++;
        a.gameObject.SetActive(true); var replacement = Owner(Second); Destroy(a.gameObject); yield return null;
        Check(replacement.GetComponent<SkinnedMeshRenderer>().sharedMesh.name == "second" && service.Cache.PinnedBytes == 1024, "Switch/destroy discarded replacement mesh: name=" + replacement.GetComponent<SkinnedMeshRenderer>().sharedMesh.name + " pinned=" + service.Cache.PinnedBytes);
        Destroy(replacement.gameObject); Destroy(b.gameObject); yield return null;
        Check(service.Cache.PinnedBytes == 0 && service.Cache.LoadedBytes == 0, "Destroy final choice leaked cache"); passed++;
        foreach (var key in new[] { "mesh:33333333333333333333333333333333", "mesh:44444444444444444444444444444444", "mesh:55555555555555555555555555555555" })
        {
            using (var lease = service.Acquire(key)) Check(lease.Ready && lease.Value == null && !string.IsNullOrEmpty(lease.Error), "Invalid local bundle accepted: " + key);
            Check(service.Cache.PinnedBytes == 0 && service.Cache.LoadedBytes == 0 && service.LastError != null, "Failed local load retained pins/data or lost diagnostic"); passed++;
        }
        var failedOwner = Owner(First, false);
        failedOwner.Bindings[1].key = "mesh:33333333333333333333333333333333";
        bool partialFailed = false; try { failedOwner.Prepare(); } catch (Exception) { partialFailed = true; }
        Check(partialFailed && !failedOwner.GetComponent<SkinnedMeshRenderer>().sharedMesh && !failedOwner.GetComponent<MeshFilter>().sharedMesh && service.Cache.PinnedBytes == 0, "Partial prepare failure retained a mesh or pin");
        Destroy(failedOwner.gameObject); passed++;
        Destroy(service.gameObject); yield return null;
        bool missingManifest = false; try { var required = MeshService.Instance; } catch (Exception) { missingManifest = true; }
        Check(missingManifest, "Missing required local canonical manifest accepted"); passed++;
        string localRoot = Path.Combine(Application.streamingAssetsPath, "TCGStreamedMeshes"); Directory.CreateDirectory(localRoot);
        string canonical = Resources.Load<TextAsset>("TCGStreaming/MeshCatalog").text;
        File.WriteAllText(Path.Combine(localRoot, "catalog.json"), canonical + " ");
        bool drift = false; try { var required = MeshService.Instance; } catch (Exception error) { drift = error.Message.Contains("does not match"); }
        Check(drift, "Canonical manifest byte drift was accepted"); passed++;
        File.WriteAllText(Path.Combine(localRoot, "catalog.json"), canonical);
        File.Copy(Path.Combine(BundleRoot, "mesh_rigged"), Path.Combine(localRoot, "mesh_rigged"), true);
        var requiredService = MeshService.Instance;
        using (var lease = requiredService.Acquire(First)) Check(lease.Ready && lease.Value != null && lease.Value.Mesh.blendShapeCount == 1, "Exact canonical local manifest failed to initialize authored mesh");
        requiredService.Cache.TrimAllUnpinned(); Destroy(requiredService.gameObject); passed++;
        yield return null;
        var nestedManifest = new MeshManifest();
        nestedManifest.entries.Add(new MeshEntry { key = NestedBodyKey, bundle = "mesh_rigged", asset = First, bytes = 1024, crc = Crc });
        var nestedService = new GameObject("nested body cache").AddComponent<MeshService>(); nestedService.Initialize(BundleRoot, nestedManifest, 0);
        var nestedClone = UnityEngine.Object.Instantiate(NestedBodyPrefab);
        var customizer = nestedClone.GetComponentInChildren<CC.CharacterCustomization>(true);
        Check(!nestedClone.activeSelf && customizer && !customizer.MainMesh.sharedMesh, "Nested inactive fixture started with eager body");
        customizer.Initialize();
        Check(customizer.Parses == 1 && customizer.MainMesh.sharedMesh && customizer.MainMesh.GetBlendShapeWeight(0) == 61 &&
            !nestedClone.GetComponent<MeshBundleOwner>() && customizer.GetComponent<MeshBundleOwner>(), "First nested initialization lost exact owner or setup");
        yield return null; yield return null;
        Check(!customizer.MainMesh.sharedMesh && nestedService.Cache.PinnedBytes == 0, "Nested inactive first initialization retained pins");
        customizer.Initialize();
        Check(customizer.Parses == 1 && customizer.Reloads == 1 && customizer.MainMesh.GetBlendShapeWeight(0) == 81, "Nested inactive reused setup failed");
        yield return null; yield return null;
        Check(!customizer.MainMesh.sharedMesh && nestedService.Cache.PinnedBytes == 0, "Nested inactive reuse retained pins");
        nestedClone.SetActive(true);
        Check(customizer.MainMesh.sharedMesh && customizer.MainMesh.GetBlendShapeWeight(0) == 81 && customizer.MainMesh.bones[0] == customizer.transform,
            "Nested later activation lost initialized appearance or rig");
        nestedClone.SetActive(false); Check(!customizer.MainMesh.sharedMesh && nestedService.Cache.PinnedBytes == 0, "Nested disable retained owned mesh");
        Destroy(nestedClone); Destroy(nestedService.gameObject); yield return null; passed++;

    }
}
