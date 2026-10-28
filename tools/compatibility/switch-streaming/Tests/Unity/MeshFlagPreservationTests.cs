using System;
using System.IO;
using UnityEditor;
using UnityEngine;

// Consumes only explicitly supplied synthetic bundles; does not build/open a game.
public static class MeshFlagPreservationTests
{
    [Serializable] public sealed class Entry { public string bundle; public int sourceFlag; public uint crc; }
    [Serializable] public sealed class Request { public Entry[] entries; }
    [Serializable] public sealed class Result { public string bundle; public int flag, vertices, blendshapes; public uint independentCrc, unityCrc; public bool unityCrcAvailable; }
    [Serializable] public sealed class Report { public string status; public Result[] entries; }
    public static void Run()
    {
        string requestPath = Environment.GetEnvironmentVariable("TCG_MESH_FLAG_NATIVE_REQUEST");
        if (string.IsNullOrEmpty(requestPath)) throw new Exception("Set TCG_MESH_FLAG_NATIVE_REQUEST to synthetic tool result JSON");
        var request = JsonUtility.FromJson<Request>(File.ReadAllText(requestPath));
        if (request == null || request.entries == null || request.entries.Length == 0) throw new Exception("Missing synthetic bundle evidence");
        var report = new Report { status = "pass", entries = new Result[request.entries.Length] };
        for (int i = 0; i < request.entries.Length; i++)
        {
            var entry = request.entries[i]; uint crc;
            bool available = BuildPipeline.GetCRCForAssetBundle(entry.bundle, out crc);
            if (available && crc != entry.crc) throw new Exception("Unity CRC helper disagrees with independent payload CRC");
            if (!available) Debug.LogWarning("GetCRCForAssetBundle unavailable without adjacent build manifest; native load still validates independent CRC=" + entry.crc);
            var bundle = AssetBundle.LoadFromFile(entry.bundle, entry.crc);
            if (!bundle) throw new Exception("Native load rejected preserved synthetic bundle");
            try
            {
                var payload = bundle.LoadAllAssets<UnityEngine.Object>();
                if (payload.Length != 1 || !(payload[0] is Mesh)) throw new Exception("Unexpected native payload");
                var mesh = bundle.LoadAsset<Mesh>("usage-mesh");
                if (!mesh || mesh.vertexCount != 3 || mesh.vertices[1] != Vector3.right || mesh.triangles[2] != 2 || mesh.uv[2] != Vector2.up || mesh.bindposes.Length != 1 || mesh.boneWeights[2].weight0 != 1 || mesh.blendShapeCount != 1 || mesh.GetBlendShapeName(0) != "usage-shape")
                    throw new Exception("Native geometry/rig/blendshape payload changed");
                var property = new SerializedObject(mesh).FindProperty("m_MeshUsageFlags");
                if (property == null || property.intValue != entry.sourceFlag) throw new Exception("Native authored flag differs from recorded source");
                report.entries[i] = new Result { bundle = entry.bundle, flag = property.intValue, vertices = mesh.vertexCount, blendshapes = mesh.blendShapeCount, independentCrc = entry.crc, unityCrc = crc, unityCrcAvailable = available };
            }
            finally { bundle.Unload(true); }
        }
        string output = Path.Combine(Path.GetDirectoryName(requestPath), "native-evidence.json");
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        Debug.Log("PASS preserved flag native Unity load/CRC/rig tests: " + report.entries.Length);
        EditorApplication.Exit(0);
    }
}
