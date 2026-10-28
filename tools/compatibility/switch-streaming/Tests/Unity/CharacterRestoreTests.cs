using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using TCG.SwitchStreaming.Editor;

public static class CharacterRestoreTests
{
    const string Folder = "Assets/CharacterRestoreFixtures";
    static string Project { get { return Path.GetFullPath("."); } }
    static string Backups { get { return Path.GetFullPath("character-restore-backups"); } }
    static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static List<CharacterMeshBuild.PrefabSnapshot> Setup()
    {
        AssetDatabase.DeleteAsset(Folder); Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        AssetDatabase.CreateAsset(mesh, Folder + "/source.asset");
        var snapshots = new List<CharacterMeshBuild.PrefabSnapshot>();
        foreach (string name in new[] { "a", "b" })
        {
            string path = Folder + "/" + name + ".prefab";
            var go = new GameObject(name); go.AddComponent<MeshFilter>().sharedMesh = mesh;
            PrefabUtility.SaveAsPrefabAsset(go, path); UnityEngine.Object.DestroyImmediate(go);
            string backup = Path.Combine(Backups, path); Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(path, backup, true);
            var snapshot = new CharacterMeshBuild.PrefabSnapshot { path = path, guid = AssetDatabase.AssetPathToGUID(path), originalSha256 = Hash(path) };
            CharacterMeshBuild.RewritePrefab(path, CharacterMeshBuild.CollectMeshes(new[] { path }));
            snapshot.preparedSha256 = Hash(path); snapshots.Add(snapshot);
        }
        return snapshots;
    }
    static void Refuses(Action action)
    {
        bool refused = false; try { action(); } catch (InvalidOperationException) { refused = true; }
        if (!refused) throw new Exception("Unexpected restoration was accepted");
    }
    public static void Run()
    {
        var rows = Setup();
        CharacterMeshBuild.RestorePrefabSnapshots(Project, Backups, rows);
        foreach (var row in rows) if (Hash(row.path) != row.originalSha256 || AssetDatabase.AssetPathToGUID(row.path) != row.guid) throw new Exception("Known preparation did not restore original bytes/identity");
        CharacterMeshBuild.RestorePrefabSnapshots(Project, Backups, rows); // idempotent original state

        rows = Setup(); string preparedA = Hash(rows[0].path);
        File.AppendAllText(rows[1].path, "\n# unrelated authored edit\n"); string changedB = Hash(rows[1].path);
        Refuses(() => CharacterMeshBuild.RestorePrefabSnapshots(Project, Backups, rows));
        if (Hash(rows[0].path) != preparedA || Hash(rows[1].path) != changedB) throw new Exception("Drift refusal modified a prefab before whole-batch validation");

        rows = Setup(); preparedA = Hash(rows[0].path);
        File.AppendAllText(Path.Combine(Backups, rows[1].path), "\n# changed backup\n");
        Refuses(() => CharacterMeshBuild.RestorePrefabSnapshots(Project, Backups, rows));
        if (Hash(rows[0].path) != preparedA) throw new Exception("Backup drift caused partial restoration");

        rows = Setup(); preparedA = Hash(rows[0].path);
        string meta = rows[1].path + ".meta", originalMeta = File.ReadAllText(meta);
        File.WriteAllText(meta, originalMeta.Replace(rows[1].guid, "11111111111111111111111111111111")); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Refuses(() => CharacterMeshBuild.RestorePrefabSnapshots(Project, Backups, rows));
        if (Hash(rows[0].path) != preparedA) throw new Exception("Replacement GUID caused partial restoration");
        File.WriteAllText(meta, originalMeta); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        rows = Setup();
        File.Copy(Path.Combine(Backups, rows[1].path), rows[1].path, true); rows[1].preparedSha256 = null;
        CharacterMeshBuild.RestorePrefabSnapshots(Project, Backups, rows); // interrupted before writing next prefab
        foreach (var row in rows) if (Hash(row.path) != row.originalSha256) throw new Exception("Known mixed interrupted state did not restore");

        rows = Setup(); preparedA = Hash(rows[0].path); rows[1].preparedSha256 = null;
        Refuses(() => CharacterMeshBuild.RestorePrefabSnapshots(Project, Backups, rows)); // interrupted after write, before receipt
        if (Hash(rows[0].path) != preparedA) throw new Exception("Unrecorded interrupted write caused partial restoration");

        rows = Setup(); rows.Add(rows[0]);
        Refuses(() => CharacterMeshBuild.RestorePrefabSnapshots(Project, Backups, rows));
        Debug.Log("CHARACTER_RESTORE_PASS original/prepared/idempotent, whole-batch edit/backup/GUID drift, known/unknown interruption, duplicate identity");
        EditorApplication.Exit(0);
    }
}
