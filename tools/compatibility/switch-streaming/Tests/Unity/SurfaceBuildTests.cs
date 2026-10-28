using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using TCG.SwitchStreaming.Editor;

[Serializable] public sealed class SurfaceBuildData
{
    public Texture mainTexture,roughnessMap,normalMap; public string name="keep-name"; public float price=13.5f,smoothness=0.7f; public Color color=Color.magenta;public bool showBar=true;
}
public sealed class SurfaceBuildTable : ScriptableObject
{
    public List<SurfaceBuildData> m_FloorDecoDataList=new List<SurfaceBuildData>(),m_WallDecoDataList=new List<SurfaceBuildData>(),m_WallBarDecoDataList=new List<SurfaceBuildData>(),m_CeilingDecoDataList=new List<SurfaceBuildData>();
}
public static class SurfaceBuildTests
{
    static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();}
    static void Refuses(Action action){bool refused=false;try{action();}catch(InvalidOperationException){refused=true;}if(!refused)throw new Exception("Unexpected asset restoration accepted");}
    public static void Run()
    {
        const string folder="Assets/SurfaceBuildFixtures",path=folder+"/catalogue.asset";
        AssetDatabase.DeleteAsset(folder);Directory.CreateDirectory(folder);AssetDatabase.Refresh();
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="authored-surface"};texture.SetPixels(new[]{Color.red,Color.blue,Color.green,Color.white});texture.Apply();AssetDatabase.CreateAsset(texture,folder+"/texture.asset");
        var table=ScriptableObject.CreateInstance<SurfaceBuildTable>();table.m_FloorDecoDataList.Add(new SurfaceBuildData{mainTexture=texture,roughnessMap=texture});table.m_WallDecoDataList.Add(new SurfaceBuildData{normalMap=texture});AssetDatabase.CreateAsset(table,path);AssetDatabase.SaveAssets();
        var records=SurfaceBuild.CollectRecords(table);
        if(records.Count!=3 || records[0].texture!=texture || records[0].bytes!=16)throw new Exception("Original shared texture/bytes/nullable fields changed");
        if(records.Find(r=>r.key=="surface:floor:0:roughnessMap").property!="m_FloorDecoDataList.Array.data[0].roughnessMap")throw new Exception("Semantic property mapping changed");
        string backup=Path.GetFullPath("surface-catalogue-original.asset");File.Copy(path,backup,true);string original=Hash(path),guid=AssetDatabase.AssetPathToGUID(path);
        SurfaceBuild.StripCatalogue(table,records);AssetDatabase.SaveAssets();string prepared=Hash(path);
        if(table.m_FloorDecoDataList[0].mainTexture||table.m_FloorDecoDataList[0].roughnessMap||table.m_WallDecoDataList[0].normalMap)throw new Exception("Required catalogue refs remain");
        var data=table.m_FloorDecoDataList[0];if(data.name!="keep-name"||data.price!=13.5f||data.smoothness!=0.7f||data.color!=Color.magenta||!data.showBar||table.m_FloorDecoDataList.Count!=1)throw new Exception("Unrelated metadata changed");
        File.AppendAllText(path,"\n# authored edit\n");string drift=Hash(path);
        Refuses(()=>SurfaceBuild.RestoreAsset(path,backup,guid,original,prepared));if(Hash(path)!=drift)throw new Exception("Authored edit clobbered");
        File.Copy(backup,path,true);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        SurfaceBuild.RestoreAsset(path,backup,guid,original,prepared); // known original/idempotent
        table=AssetDatabase.LoadAssetAtPath<SurfaceBuildTable>(path);records=SurfaceBuild.CollectRecords(table);SurfaceBuild.StripCatalogue(table,records);AssetDatabase.SaveAssets();prepared=Hash(path);
        Refuses(()=>SurfaceBuild.RestoreAsset(path,backup,guid,original,null)); // interrupted after write before receipt
        SurfaceBuild.RestoreAsset(path,backup,guid,original,prepared);if(Hash(path)!=original)throw new Exception("Known prepared did not restore original bytes");
        table=AssetDatabase.LoadAssetAtPath<SurfaceBuildTable>(path);records=SurfaceBuild.CollectRecords(table);SurfaceBuild.StripCatalogue(table,records);AssetDatabase.SaveAssets();prepared=Hash(path);
        string meta=path+".meta",originalMeta=File.ReadAllText(meta);File.WriteAllText(meta,originalMeta.Replace(guid,"11111111111111111111111111111111"));AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        Refuses(()=>SurfaceBuild.RestoreAsset(path,backup,guid,original,prepared));File.WriteAllText(meta,originalMeta);AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        File.AppendAllText(backup,"\n# backup drift\n");Refuses(()=>SurfaceBuild.RestoreAsset(path,backup,guid,original,prepared));
        Debug.Log("SURFACE_BUILD_PASS exact catalogue, shared raw pixels, metadata, guarded edit/backup/interruption/idempotent restoration");EditorApplication.Exit(0);
    }
}
