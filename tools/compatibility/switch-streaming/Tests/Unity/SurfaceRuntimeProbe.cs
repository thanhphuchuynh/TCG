using System;
using System.Collections;
using System.IO;
using TCG.SwitchStreaming;
using UnityEngine;

public sealed class SurfaceRuntimeProbe : MonoBehaviour
{
    public string BundleRoot;
    public uint[] Crc;
    const string A = "surface:floor:0:mainTexture", B = "surface:wall:1:normalMap", Alias = "surface:ceiling:2:mainTexture";
    int passed;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    void Pass(string name) { passed++; Debug.Log("SURFACE PASS " + name); }
    static SurfaceEntry Entry(int i, uint crc, string key)
    { string guid = i.ToString().PadLeft(32, '0'); return new SurfaceEntry { key = key, bundle = "surface_" + guid, asset = "assets/tcgstreamedsurface/" + guid + ".asset", bytes = 340, crc = crc }; }
    static bool Throws(Action action) { try { action(); return false; } catch (Exception) { return true; } }
    IEnumerator Start()
    {
        var tests = Tests(); Exception failure = null;
        while (true) { bool more; try { more = tests.MoveNext(); } catch (Exception error) { failure = error; break; } if (!more) break; yield return tests.Current; }
        string result = failure == null ? "PASS actual Unity surface runtime cases: " + passed : "FAIL " + failure;
        File.WriteAllText(Path.GetFullPath("surface-runtime-result.txt"), result + "\n"); Debug.Log(result);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(failure == null ? 0 : 1);
#endif
    }
    IEnumerator Tests()
    {
        var manifest = new SurfaceManifest(); manifest.entries.Add(Entry(1, Crc[0], A)); manifest.entries.Add(Entry(2, Crc[1], B)); manifest.entries.Add(Entry(1, Crc[0], Alias));
        var service = new GameObject("Surface service").AddComponent<SurfaceTextureService>(); service.Initialize(BundleRoot, manifest, 0);
        var material = new Material(Shader.Find("Standard")); var a = new GameObject("manager a"); var b = new GameObject("manager b");
        SurfaceTextures.SetTexture(material, a, "_MainTex", A, null); var native = material.GetTexture("_MainTex") as Texture2D;
        Check(native && service.Cache.PinnedBytes == 340 && service.Cache.PendingCount == 0, "Required texture did not load synchronously");
        Check(native.width == 8 && native.mipmapCount == 4 && native.wrapMode == TextureWrapMode.Clamp && native.filterMode == FilterMode.Trilinear && native.anisoLevel == 3 && native.GetPixels32()[17].g == 17, "Authored texture changed"); Pass("synchronous original pixels/mips/sampler");
        SurfaceTextures.SetTexture(material, b, "_MainTex", A, null);
        Check(material.GetTexture("_MainTex") == native && service.Cache.PinnedBytes == 340, "Same-key owners failed to share native texture");
        SurfaceTextures.SetTexture(material, b, "_MainTex", Alias, null);
        Check(material.GetTexture("_MainTex") == native && service.Cache.PinnedBytes == 340 && service.Cache.LoadedBytes == 340, "Alias did not share native texture");
        a.SetActive(false); Check(native && material.GetTexture("_MainTex") == native && service.Cache.PinnedBytes == 340, "Disable released visible world texture");
        Destroy(a); yield return null; Check(native && material.GetTexture("_MainTex") == native && service.Cache.PinnedBytes == 340, "First shared owner cleared other owner");
        Destroy(b); yield return null; Check(!native && !material.GetTexture("_MainTex") && service.Cache.LoadedBytes == 0, "Final owner did not clear before native unload"); Pass("alias dedup shared owners disable destroy native unload");
        var neverActive = new GameObject("never active manager"); neverActive.SetActive(false);
        SurfaceTextures.SetTexture(material, neverActive, "_MainTex", A, null); native = (Texture2D)material.GetTexture("_MainTex");
        Destroy(neverActive); yield return null; yield return null;
        Check(!native && !material.GetTexture("_MainTex") && service.Cache.PinnedBytes == 0, "Never active manager leaked native texture or reference"); Pass("never active owner destruction");
        a = new GameObject("surviving material replacement manager");
        for (int replacementIndex = 0; replacementIndex < 3; replacementIndex++)
        {
            var disposedMaterial = new Material(Shader.Find("Standard"));
            SurfaceTextures.SetTexture(disposedMaterial, a, "_MainTex", A, null); native = (Texture2D)disposedMaterial.GetTexture("_MainTex");
            Check(native && service.Cache.PinnedBytes == 340, "Replacement material did not acquire original texture");
            Destroy(disposedMaterial); yield return null; yield return null;
            Check(a && !native && service.Cache.PinnedBytes == 0 && service.Cache.LoadedBytes == 0, "Destroyed material retained native texture pins while owner survived");
        }
        Destroy(a); yield return null; Pass("destroyed materials release while manager survives repeated replacement");
        a = new GameObject("replacement a"); b = new GameObject("replacement b");
        SurfaceTextures.SetTexture(material, a, "_MainTex", A, null); native = (Texture2D)material.GetTexture("_MainTex");
        Check(native && native.GetPixels32()[17].g == 17, "Reload lost original texture");
        SurfaceTextures.SetTexture(material, b, "_MainTex", B, null); var replacement = material.GetTexture("_MainTex");
        Check(replacement && replacement != native && service.Cache.PinnedBytes == 340, "Replacement retained superseded pins");
        Destroy(a); yield return null; Check(material.GetTexture("_MainTex") == replacement, "Stale owner clobbered replacement");
        SurfaceTextures.SetTexture(material, b, "_MainTex", B, null); Check(service.Cache.PinnedBytes == 340, "Same owner repeated bind leaked pins");
        var fallback = new Texture2D(2, 2); material.SetTexture("_MainTex", fallback); Destroy(b); yield return null;
        Check(material.GetTexture("_MainTex") == fallback && fallback && material && service.Cache.LoadedBytes == 0, "External override clobbered or borrowed material destroyed"); Pass("reload replace stale owner external override repeated bind");
        a = new GameObject("fallback owner"); SurfaceTextures.SetTexture(material, a, "_MainTex", A, fallback);
        Check(material.GetTexture("_MainTex") == fallback && service.Cache.PinnedBytes == 0, "Non-null runtime override ignored");
        SurfaceTextures.SetTexture(material, a, "_MainTex", "unknown", null);
        Check(!material.GetTexture("_MainTex") && fallback && service.Cache.PinnedBytes == 0, "Absent key/null fallback changed original null semantics");
        SurfaceTextures.SetTexture(material, a, "_MainTex", "unknown", fallback); Destroy(a); yield return null;
        Check(fallback && material && material.GetTexture("_MainTex") == fallback, "Owner teardown cleared borrowed fallback slot or destroyed borrowed object");
        a = new GameObject("resident fallback service teardown"); SurfaceTextures.SetTexture(material, a, "_MainTex", A, fallback);
        Destroy(service.gameObject); yield return null;
        Check(fallback && material.GetTexture("_MainTex") == fallback, "Service teardown cleared borrowed resident fallback slot");
        Destroy(a); yield return null;
        service = new GameObject("Surface service after borrowed teardown").AddComponent<SurfaceTextureService>(); service.Initialize(BundleRoot, manifest, 0);
        Pass("resident runtime mod fallback and absent null survive owner and service teardown");
        a = new GameObject("atomic error owner"); SurfaceTextures.SetTexture(material, a, "_MainTex", A, null); native = (Texture2D)material.GetTexture("_MainTex");
        // Reinitialization is prohibited, and bad manifest validation is side effect free.
        Check(Throws(() => service.Initialize(BundleRoot, manifest, 0)), "Service accepted reinitialize");
        var invalid = new SurfaceManifest(); invalid.entries.Add(Entry(1, Crc[0], A)); invalid.entries[0].bundle = "../escape";
        Check(Throws(() => invalid.Validate()), "Traversal bundle accepted");
        invalid = new SurfaceManifest(); invalid.entries.Add(Entry(1, Crc[0], A)); invalid.entries.Add(Entry(1, Crc[0] ^ 1, Alias)); Check(Throws(() => invalid.Validate()), "Conflicting alias metadata accepted");
        invalid = new SurfaceManifest(); invalid.version = 2; Check(Throws(() => invalid.Validate()), "Unknown version accepted");
        invalid = new SurfaceManifest(); invalid.entries.Add(Entry(1, Crc[0], A)); invalid.entries.Add(Entry(1, Crc[0], A)); Check(Throws(() => invalid.Validate()), "Duplicate key accepted");
        invalid = new SurfaceManifest(); invalid.entries.Add(Entry(1, Crc[0], A)); invalid.entries[0].asset = "assets/tcgstreamedsurface/../../evil.asset"; Check(Throws(() => invalid.Validate()), "Noncanonical address accepted"); Pass("manifest validation");
        Destroy(service.gameObject); yield return null; Check(!native && !material.GetTexture("_MainTex") && fallback && material, "Service teardown left owned references or destroyed borrowed objects"); Destroy(a); yield return null; Pass("service teardown");
        manifest = new SurfaceManifest(); manifest.entries.Add(Entry(1, Crc[0], A));
        manifest.entries.Add(Entry(2, Crc[1] ^ 1, B)); manifest.entries.Add(Entry(3, Crc[2], "surface:wall:3:mainTexture"));
        manifest.entries.Add(Entry(4, Crc[3], "surface:wall:4:mainTexture")); manifest.entries[3].bytes = 1;
        manifest.entries.Add(Entry(5, Crc[0], "surface:wall:5:mainTexture"));
        manifest.entries.Add(Entry(6, Crc[5], "surface:wall:6:mainTexture"));
        service = new GameObject("Error service").AddComponent<SurfaceTextureService>(); service.Initialize(BundleRoot, manifest, 0); a = new GameObject("error owner");
        SurfaceTextures.SetTexture(material, a, "_MainTex", A, null); native = (Texture2D)material.GetTexture("_MainTex");
        foreach (string key in new[] { B, "surface:wall:3:mainTexture", "surface:wall:4:mainTexture", "surface:wall:5:mainTexture", "surface:wall:6:mainTexture" })
        {
            Check(Throws(() => SurfaceTextures.SetTexture(material, a, "_MainTex", key, null)), "Invalid required texture accepted: " + key);
            Check(material.GetTexture("_MainTex") == native && native && service.Cache.PinnedBytes == 340 && service.Cache.LoadedBytes == 340 && service.LastError != null, "Failed load changed material or pins: " + key);
            Pass("atomic required failure " + key);
        }
        // A mod may reuse a texture already obtained from a material. Its borrowed
        // non-null override must keep that very native object alive.
        SurfaceTextures.SetTexture(material, a, "_MainTex", B, native);
        yield return null;
        Check(native && material.GetTexture("_MainTex") == native && service.Cache.PinnedBytes == 340, "Borrowed cached fallback was unloaded or replaced"); Pass("borrowed cached fallback retains native texture");
        service.SendMessage("LowMemory"); Check(native && service.Cache.PinnedBytes == 340, "Low memory unloaded active texture");
        Destroy(a); yield return null; Destroy(service.gameObject); yield return null;
        Check(Throws(() => { var required = SurfaceTextureService.Instance; }), "Missing canonical file accepted");
        string local = Path.Combine(Application.streamingAssetsPath, "TCGStreamedSurfaces"); Directory.CreateDirectory(local);
        string canonical = Resources.Load<TextAsset>("TCGStreaming/SurfaceCatalog").text; File.WriteAllText(Path.Combine(local, "catalog.json"), canonical + " ");
        Check(Throws(() => { var required = SurfaceTextureService.Instance; }), "Canonical byte drift accepted");
        File.WriteAllText(Path.Combine(local, "catalog.json"), canonical); string bundle = Entry(1, Crc[0], A).bundle; File.Copy(Path.Combine(BundleRoot, bundle), Path.Combine(local, bundle), true);
        service = SurfaceTextureService.Instance; a = new GameObject("canonical owner"); SurfaceTextures.SetTexture(material, a, "_MainTex", A, null);
        Check(material.GetTexture("_MainTex") && service.Cache.PinnedBytes == 340, "Canonical exact catalogue did not load");
        Destroy(a); yield return null; Check(service.Cache.LoadedBytes == 340, "Default 64 MiB soft budget did not retain unpinned texture");
        service.SendMessage("LowMemory"); yield return null; Check(service.Cache.LoadedBytes == 0, "Low memory did not trim unpinned cache"); Pass("canonical exact mismatch missing low memory soft budget");
        Destroy(service.gameObject); Destroy(material); Destroy(fallback); yield return null;
    }
}
