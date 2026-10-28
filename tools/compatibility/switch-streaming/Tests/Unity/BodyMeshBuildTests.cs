using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TCG.SwitchStreaming;
using TCG.SwitchStreaming.Editor;

public static class BodyMeshBuildTests
{
    const string Folder = "Assets/BodyFixtures", ScenePath = Folder + "/Start.unity", Prefab = Folder + "/Actor.prefab";
    [Serializable] sealed class ReceiptFixture { public int version; public string source, backup, preparedSha256, baselineSha256, sha256; }
    static string FixtureHash(string path) { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Refuses(Action action) { bool refused = false; try { action(); } catch (InvalidOperationException) { refused = true; } Check(refused, "Unexpected body preparation accepted"); }
    static GameObject Actor(Mesh a, Mesh b)
    {
        var root = new GameObject("actor"); root.SetActive(false);
        var unrelated = GameObject.CreatePrimitive(PrimitiveType.Cube); unrelated.name = "unrelated built-in geometry";
        unrelated.transform.SetParent(root.transform, false);
        var owner = root.AddComponent<CC.CharacterCustomization>();
        var bone = new GameObject("bone"); bone.transform.SetParent(root.transform, false);
        var skins = new SkinnedMeshRenderer[2];
        for (int i = 0; i < skins.Length; i++)
        {
            var child = new GameObject("body " + i); child.transform.SetParent(root.transform, false);
            var skin = child.AddComponent<SkinnedMeshRenderer>(); skins[i] = skin;
            skin.sharedMesh = i == 0 ? a : b; skin.bones = new[] { bone.transform }; skin.rootBone = bone.transform;
            skin.SetBlendShapeWeight(0, 42 + i); skin.localBounds = new Bounds(Vector3.one, new Vector3(3, 4, 5));
        }
        owner.MainMesh = skins[0];
        var group = root.AddComponent<LODGroup>(); group.SetLODs(new[] { new LOD(.5f, new Renderer[] { skins[0] }), new LOD(.1f, new Renderer[] { skins[1] }) });
        return root;
    }
    static BodySource payloadSource; static System.Collections.Generic.List<MeshRecord> payloadRecords;
    static bool convertPayload; static int callbacks;
    public static void ProcessPayload(UnityEngine.SceneManagement.Scene scene)
    {
        if (!convertPayload || scene.path != ScenePath) return;
        foreach (var record in payloadRecords) record.mesh = AssetDatabase.LoadAssetAtPath<Mesh>(record.path);
        BodyMeshBuild.RewriteScene(scene, payloadSource, payloadRecords); callbacks++;
    }
    static void BuildPayload(string folder)
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = Path.GetFullPath(folder + "/BodyTest.exe"), target = BuildTarget.StandaloneWindows });
        Check(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded, "Synthetic body payload build failed");
    }
    [MenuItem("TCG/Run body build tests")]
    public static void Run()
    {
        try
        {
            string layout = Path.GetFullPath("body-source-guard/" + Guid.NewGuid().ToString("N") + "/switch-on-demand/macos-port-work");
            string sourceFile = Path.Combine(layout, "Assets/Scripts/Assembly-CSharp/CC/CharacterCustomization.cs"), backupFile = Path.Combine(Path.GetDirectoryName(layout), "character-mesh-source-backup/CharacterCustomization.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)); Directory.CreateDirectory(Path.GetDirectoryName(backupFile));
            File.WriteAllText(sourceFile, "prepared"); File.WriteAllText(backupFile, "original");
            var receipt = new ReceiptFixture { version = 2, source = sourceFile, backup = backupFile,
                preparedSha256 = FixtureHash(sourceFile), baselineSha256 = FixtureHash(backupFile), sha256 = FixtureHash(sourceFile) };
            string receiptPath = Path.Combine(layout, "character-mesh-preparation.json");
            File.WriteAllText(receiptPath, JsonUtility.ToJson(receipt)); BodyMeshBuild.ValidateCharacterReceipt(layout);
            receipt.version = 1; File.WriteAllText(receiptPath, JsonUtility.ToJson(receipt)); Refuses(() => BodyMeshBuild.ValidateCharacterReceipt(layout));
            receipt.version = 2; File.WriteAllText(receiptPath, JsonUtility.ToJson(receipt));
            File.AppendAllText(sourceFile, "edit"); Refuses(() => BodyMeshBuild.ValidateCharacterReceipt(layout)); File.WriteAllText(sourceFile, "prepared");
            File.AppendAllText(backupFile, "edit"); Refuses(() => BodyMeshBuild.ValidateCharacterReceipt(layout)); File.WriteAllText(backupFile, "original");
            receipt.backup = "/tmp/foreign.cs"; File.WriteAllText(receiptPath, JsonUtility.ToJson(receipt)); Refuses(() => BodyMeshBuild.ValidateCharacterReceipt(layout));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder); Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            foreach (string name in new[] { "a", "b" })
            {
                var mesh = new Mesh { name = "BodyTest-" + name, vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
                mesh.AddBlendShapeFrame("smile", 100, new[] { Vector3.forward, Vector3.forward, Vector3.forward }, new Vector3[3], new Vector3[3]);
                AssetDatabase.CreateAsset(mesh, Folder + "/" + name + ".asset");
            }
            var a = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/a.asset"); var b = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/b.asset");
            var root = Actor(a, b); PrefabUtility.SaveAsPrefabAsset(root, Prefab);
            EditorSceneManager.SaveScene(root.scene, ScenePath);
            var records = BodyMeshBuild.CollectBodies(new[] { Folder + "/a.asset", Folder + "/b.asset" });
            var audit = BodyMeshReferenceAudit.Scan(records, new[] { Prefab }, ScenePath, 2, new[] { Prefab, ScenePath });
            Check(audit.sources.Count == 2 && audit.sources.Sum(s => s.slots.Count) == 4, "Body audit lost authored slots");
            string sceneHash = BodyMeshBuild.Hash(ScenePath);
            var scene = root.scene; var sceneSource = audit.sources.Single(s => s.path == ScenePath);
            byte[] sourceBytes = File.ReadAllBytes(ScenePath); File.AppendAllText(ScenePath, "\n# authored edit\n");
            Refuses(() => BodyMeshBuild.RewriteScene(scene, sceneSource, records));
            Check(root.GetComponent<CC.CharacterCustomization>().MainMesh.sharedMesh == a, "Source drift refusal partly converted scene");
            File.WriteAllBytes(ScenePath, sourceBytes);
            BodyMeshBuild.RewriteScene(scene, sceneSource, records);
            var owner = root.GetComponent<MeshBundleOwner>();
            Check(owner && owner.Bindings.Length == 2 && !root.GetComponent<CC.CharacterCustomization>().MainMesh.sharedMesh, "Scene body ownership missing");
            Check(owner.Bindings[0].defaultWeights[0] == 42 && owner.Bindings[1].defaultWeights[0] == 43 && owner.Bindings[0].defaultBounds.size == new Vector3(3, 4, 5), "Body authored state changed");
            Check(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(s => !s.sharedMesh && s.bones.Length == 1 && s.rootBone.name == "bone") && root.GetComponent<LODGroup>().GetLODs().Length == 2, "Body rig/LOD/reference changed");
            Check(root.GetComponentInChildren<MeshFilter>(true).sharedMesh, "Unrelated built-in mesh was removed");
            Check(BodyMeshBuild.Hash(ScenePath) == sceneHash, "Source scene written by in-memory conversion");
            Refuses(() => BodyMeshBuild.RewriteScene(scene, sceneSource, records));
            EditorSceneManager.CloseScene(scene, true); scene = EditorSceneManager.OpenScene(ScenePath);
            var foreign = new GameObject("unapproved consumer").AddComponent<MeshFilter>(); foreign.sharedMesh = a; EditorSceneManager.SaveScene(scene);
            Refuses(() => BodyMeshReferenceAudit.Scan(records, new[] { Prefab }, ScenePath, 2, new[] { Prefab, ScenePath }));
            UnityEngine.Object.DestroyImmediate(foreign.gameObject); EditorSceneManager.SaveScene(scene);
            var customizer = scene.GetRootGameObjects()[0].GetComponent<CC.CharacterCustomization>(); customizer.MainMesh = null; EditorSceneManager.SaveScene(scene);
            Refuses(() => BodyMeshReferenceAudit.Scan(records, new[] { Prefab }, ScenePath, 2, new[] { Prefab, ScenePath }));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string manifest = Folder + "/catalog.json"; File.WriteAllText(manifest, "original");
            string backups = Path.GetFullPath("body-journal-fixture/" + Guid.NewGuid().ToString("N"));
            var snapshots = BodyMeshBuild.CaptureFiles(backups, new[] { Prefab, manifest });
            CharacterMeshBuild.RewritePrefab(Prefab, records); snapshots[0].preparedSha256 = BodyMeshBuild.Hash(Prefab);
            File.WriteAllText(manifest, "combined"); snapshots[1].preparedSha256 = BodyMeshBuild.Hash(manifest);
            File.AppendAllText(manifest, "unrecorded"); string prepared = BodyMeshBuild.Hash(Prefab);
            Refuses(() => BodyMeshBuild.RestoreFiles(backups, snapshots)); Check(BodyMeshBuild.Hash(Prefab) == prepared, "Late manifest drift partially restored prefab");
            File.WriteAllText(manifest, "combined");
            snapshots[1].preparedSha256 = null;
            Refuses(() => BodyMeshBuild.RestoreFiles(backups, snapshots));
            Check(BodyMeshBuild.Hash(Prefab) == prepared, "Unreceipted write partially restored prefab");
            File.WriteAllText(manifest, "original"); // known interruption before writing second file
            BodyMeshBuild.RestoreFiles(backups, snapshots);
            Check(snapshots.All(s => BodyMeshBuild.Hash(s.path) == s.originalSha256), "Known body state did not restore");
            BodyMeshBuild.RestoreFiles(backups, snapshots);
            var duplicate = new System.Collections.Generic.List<BodyMeshBuild.FileSnapshot>(snapshots); duplicate.Add(snapshots[0]);
            Refuses(() => BodyMeshBuild.RestoreFiles(backups, duplicate));
            string pathBefore = snapshots[1].path; snapshots[1].path = "Assets/../foreign.json";
            Refuses(() => BodyMeshBuild.RestoreFiles(backups, snapshots)); snapshots[1].path = pathBefore;
            string guidBefore = snapshots[0].guid; snapshots[0].guid = "11111111111111111111111111111111";
            Refuses(() => BodyMeshBuild.RestoreFiles(backups, snapshots)); snapshots[0].guid = guidBefore;
            File.AppendAllText(BodyMeshBuild.BackupPath(backups, snapshots[1]), "drift");
            Refuses(() => BodyMeshBuild.RestoreFiles(backups, snapshots));
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = Actor(a, b);
            var control = new Mesh { name = "BodyTest-Control", vertices = new[] { Vector3.zero, Vector3.right * 2, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            AssetDatabase.CreateAsset(control, Folder + "/control.asset"); new GameObject("control").AddComponent<MeshFilter>().sharedMesh = control;
            EditorSceneManager.SaveScene(root.scene, ScenePath);
            var payloadAudit = BodyMeshReferenceAudit.Scan(records, new[] { Prefab }, ScenePath, 2, new[] { Prefab, ScenePath });
            payloadSource = payloadAudit.sources.Single(s => s.path == ScenePath); payloadRecords = records;
            BuildPayload("body-baseline");
            BodyMeshBuild.BeginPlayerBuild(); Refuses(() => BodyMeshBuild.VerifyPlayerBuild(ScenePath, 2, 1));
            convertPayload = true;
            try { BuildPayload("body-converted"); } finally { convertPayload = false; }
            Check(callbacks == 1 && BodyMeshBuild.Hash(ScenePath) == payloadSource.sha256, "Build callback/source fidelity proof missing");
            BodyMeshBuild.VerifyPlayerBuild(ScenePath, 2, 1);
            BodyMeshBuild.BeginPlayerBuild(); Refuses(() => BodyMeshBuild.VerifyPlayerBuild(ScenePath, 2, 1));
            Debug.Log("BODY_BUILD_PASS audit, unknown consumer/owner refusal, scene-only conversion, rig/LOD/weights, batch recovery");
            File.WriteAllText("body-build-result.txt", "PASS body build native tests\n"); EditorApplication.Exit(0);
        }
        catch (Exception error) { File.WriteAllText("body-build-result.txt", "FAIL " + error); Debug.LogException(error); EditorApplication.Exit(1); }
    }
}

public sealed class BodyBuildFixtureProcessor : UnityEditor.Build.IProcessSceneWithReport
{
    public int callbackOrder { get { return 10; } }
    public void OnProcessScene(UnityEngine.SceneManagement.Scene scene, UnityEditor.Build.Reporting.BuildReport report) { BodyMeshBuildTests.ProcessPayload(scene); }
}
