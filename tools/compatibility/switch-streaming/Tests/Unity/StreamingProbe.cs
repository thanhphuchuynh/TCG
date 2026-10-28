using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using TCG.SwitchStreaming;

public sealed class StreamingTestView : MonoBehaviour
{
    public Image Target;
    public int Monster, Refreshes;
    public void Render(int monster)
    {
        Monster = monster; Refreshes++;
        using (CardArtSession.Begin(this, () => Render(Monster)))
        {
            Sprite sprite;
            if (!CardArtSession.TryResolve(monster, "icon", 0, out sprite)) throw new Exception("Missing synthetic manifest key");
            Target.sprite = sprite;
        }
    }
}

public sealed class StreamingProbe : MonoBehaviour
{
    public string BundleRoot;
    public uint RedCrc, BlueCrc;
    public Sprite LoadingSprite;
    int passed;
    IEnumerator Start()
    {
        var routine = Tests(); Exception failure = null;
        while (true)
        {
            bool more;
            try { more = routine.MoveNext(); }
            catch (Exception e) { failure = e; break; }
            if (!more) break;
            yield return routine.Current;
        }
        string report = failure == null ? "PASS actual Unity bundle/session tests: " + passed : "FAIL " + failure;
        File.WriteAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "../unity-test-result.txt")), report + "\n");
        Debug.Log(report);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(failure == null ? 0 : 1);
#endif
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    bool ColorIs(Sprite sprite, Color color) { return sprite && sprite != LoadingSprite && sprite.texture.GetPixel(0, 0) == color; }
    StreamingTestView View(string name)
    {
        var root = new GameObject(name); root.AddComponent<RectTransform>();
        var view = root.AddComponent<StreamingTestView>();
        var image = new GameObject("art", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)); image.transform.SetParent(root.transform, false);
        view.Target = image.GetComponent<Image>(); return view;
    }
    IEnumerator Tests()
    {
        var manifest = new ArtManifest();
        manifest.entries.Add(new ArtEntry { key = "monster:1:icon:0", bundle = "art_red", asset = "assets/streamingfixtures/red-sprite.asset", bytes = 256, crc = RedCrc });
        manifest.entries.Add(new ArtEntry { key = "monster:2:icon:0", bundle = "art_red", asset = "assets/streamingfixtures/red-sprite.asset", bytes = 256, crc = RedCrc });
        manifest.entries.Add(new ArtEntry { key = "monster:3:icon:0", bundle = "art_blue", asset = "assets/streamingfixtures/blue-sprite.asset", bytes = 256, crc = BlueCrc });
        manifest.entries.Add(new ArtEntry { key = "monster:4:icon:0", bundle = "art_missing", asset = "assets/streamingfixtures/missing.asset", bytes = 256, crc = 0 });
        var service = new GameObject("Local bundle service").AddComponent<UnityArtService>();
        service.Initialize(BundleRoot, manifest, LoadingSprite, 0);
        service.TraceOwnership = true;
        var distant = View("initially culled world card");
        distant.Monster = 1;
        using (CardArtSession.Begin(distant, () => distant.Render(1), false))
        { Sprite pending; CardArtSession.TryResolve(1, "icon", 0, out pending); distant.Target.sprite = pending; }
        Check(service.Cache.PendingCount == 0 && service.Cache.PinnedBytes == 0, "Initial hidden world cards eagerly loaded artwork");
        CardArtSession.SetVisible(distant, true);
        float firstDeadline = Time.realtimeSinceStartup + 20;
        while (!ColorIs(distant.Target.sprite, Color.red) && Time.realtimeSinceStartup < firstDeadline) yield return null;
        Check(ColorIs(distant.Target.sprite, Color.red), "First visibility did not load deferred artwork");
        distant.gameObject.SetActive(false); Destroy(distant.gameObject); yield return null; passed++;
        var a = View("owner a"); var b = View("owner b");
        a.Render(1); b.Render(2);
        Check(service.Cache.PendingCount == 1 && service.Cache.PinnedBytes == 256, "Actual shared texture load was duplicated");
        float deadline = Time.realtimeSinceStartup + 20;
        while ((a.Target.sprite == LoadingSprite || b.Target.sprite == LoadingSprite) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(a.Target.sprite != null && a.Target.sprite != LoadingSprite && a.Target.sprite == b.Target.sprite, "Actual shared authored sprite did not arrive");
        var red = a.Target.sprite; var texture = red.texture;
        Check(red.rect == new Rect(2, 2, 4, 4) && red.pivot == new Vector2(1, 3) && red.pixelsPerUnit == 32 && red.border == new Vector4(1, 1, 1, 1), "Authored geometry changed");
        a.gameObject.SetActive(false); service.Cache.Trim();
        Check(red != null && b.Target.sprite == red, "Disabling one owner destroyed another's art");
        b.gameObject.SetActive(false); service.Cache.Trim(); yield return null;
        Check(red == null && texture == null && a.Target.sprite == null && b.Target.sprite == null && service.Cache.LoadedBytes == 0, "Native bundle assets were retained after final owner disabled");
        passed++;

        a.gameObject.SetActive(true);
        Check(a.GetComponent<CanvasGroup>().alpha == 0, "Reactivated card exposed cleared artwork before reload");
        deadline = Time.realtimeSinceStartup + 20;
        while ((a.Target.sprite == null || a.Target.sprite == LoadingSprite) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(a.Target.sprite != null && ColorIs(a.Target.sprite, Color.red), "Pooled reactivation did not reload correct art; target=" + (a.Target.sprite ? a.Target.sprite.name : "null") + " frames=" + Time.frameCount + " loaded=" + service.Cache.LoadedBytes + " pending=" + service.Cache.PendingCount);
        a.gameObject.SetActive(false); yield return null; passed++;

        a.gameObject.SetActive(true); a.Render(1); a.Render(3);
        deadline = Time.realtimeSinceStartup + 20;
        while ((a.Target.sprite == null || !ColorIs(a.Target.sprite, Color.blue)) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(a.Target.sprite != null && ColorIs(a.Target.sprite, Color.blue), "Late completion overwrote a reused card");
        for (int i = 0; i < 5; i++) yield return null;
        Check(ColorIs(a.Target.sprite, Color.blue) && service.Cache.PinnedBytes == 256, "Old generation retained or replaced artwork");
        CardArtSession.SetVisible(a, false); yield return null;
        Check(a.Target.sprite == null && service.Cache.PinnedBytes == 0 && a.GetComponent<CanvasGroup>().alpha == 0, "Culled world card retained visible art");
        CardArtSession.Prefetch(a); deadline = Time.realtimeSinceStartup + 20;
        while (service.Cache.PendingCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Check(service.Cache.PinnedBytes == 0, "Completed prefetch retained a permanent pin");
        CardArtSession.SetVisible(a, true); deadline = Time.realtimeSinceStartup + 20;
        while ((a.Target.sprite == null || !ColorIs(a.Target.sprite, Color.blue)) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(a.Target.sprite != null && ColorIs(a.Target.sprite, Color.blue) && a.GetComponent<CanvasGroup>().alpha == 1, "Unculled world card did not restore complete art"); passed++;

        b.gameObject.SetActive(true); b.transform.SetParent(a.transform, false);
        a.Monster = 1; b.Monster = 3;
        using (CardArtSession.Begin(a, () => a.Render(1)))
        {
            Sprite redSprite; CardArtSession.TryResolve(1, "icon", 0, out redSprite); a.Target.sprite = redSprite;
            using (CardArtSession.Begin(b, () => b.Render(3)))
            { Sprite blueSprite; CardArtSession.TryResolve(3, "icon", 0, out blueSprite); b.Target.sprite = blueSprite; }
            CardArtSession.TryResolve(1, "icon", 0, out redSprite); a.Target.sprite = redSprite;
        }
        deadline = Time.realtimeSinceStartup + 20;
        while ((a.Target.sprite == null || !ColorIs(a.Target.sprite, Color.red) || b.Target.sprite == null || !ColorIs(b.Target.sprite, Color.blue)) && Time.realtimeSinceStartup < deadline) yield return null;
        Check(a.Target.sprite != null && ColorIs(a.Target.sprite, Color.red) && b.Target.sprite != null && ColorIs(b.Target.sprite, Color.blue), "Nested ownership changed another card's artwork");
        b.transform.SetParent(null, false); b.gameObject.SetActive(false); passed++;
        a.gameObject.SetActive(false); yield return null; passed++;

        a.gameObject.SetActive(true); a.Render(1); Destroy(a.gameObject); yield return null;
        deadline = Time.realtimeSinceStartup + 20;
        while (service.Cache.PendingCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
        service.Cache.TrimAllUnpinned(); Check(service.Cache.PinnedBytes == 0 && service.Cache.LoadedBytes == 0, "Destroyed pending owner leaked native art"); passed++;

        b.gameObject.SetActive(true); b.Render(4); deadline = Time.realtimeSinceStartup + 20;
        while (service.Cache.PendingCount > 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Check(!string.IsNullOrEmpty(service.LastError), "Missing local file did not report error");
        b.gameObject.SetActive(false); Check(service.Cache.PinnedBytes == 0, "Failed artwork retained pins"); passed++;
        Destroy(b.gameObject);
    }
}
