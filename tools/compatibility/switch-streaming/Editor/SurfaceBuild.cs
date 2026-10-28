using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace TCG.SwitchStreaming.Editor
{
    public static class SurfaceBuild
    {
        const string Catalogue="Assets/MonoBehaviour/SO_ObjectDataList.asset";
        static readonly string[][] Groups={new[]{"m_FloorDecoDataList","floor"},new[]{"m_WallDecoDataList","wall"},new[]{"m_WallBarDecoDataList","wallbar"},new[]{"m_CeilingDecoDataList","ceiling"}};
        [Serializable] sealed class Journal { public int version;public string path,guid,originalSha256,preparedSha256; }
        [Serializable] sealed class Retained {public string guid,reason;}
        [Serializable] sealed class Report
        {
            public string status="started",error;public int recordCount,bundleCount;public long imageBytes;
            public List<SurfaceRecord> eligibleRecords=new List<SurfaceRecord>();public List<Retained> retained=new List<Retained>();
        }
        static string Project {get{return Path.GetFullPath(Path.Combine(Application.dataPath,".."));}}
        static string Root {get{return Path.GetDirectoryName(Project);}}
        static string JournalPath {get{return Path.Combine(Root,"surface-texture-journal.json");}}
        static string Backup {get{return Path.Combine(Root,"surface-asset-backup",Catalogue);}}
        static string Hash(string path){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();}
        static void Guard()
        {
            if(Path.GetFileName(Root)!="switch-on-demand"||Path.GetFileName(Project)!="macos-port-work"||!File.Exists(Path.Combine(Project,"surface-preparation.json")))
                throw new InvalidOperationException("Surface streaming requires the prepared private on-demand clone");
        }
        public static List<SurfaceRecord> CollectRecords(UnityEngine.Object catalogue)
        {
            var source=new SerializedObject(catalogue);var records=new List<SurfaceRecord>();var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var group in Groups)
            {
                var list=source.FindProperty(group[0]);if(list==null||!list.isArray)throw new InvalidOperationException("Missing original surface catalogue list: "+group[0]);
                for(int index=0;index<list.arraySize;index++)foreach(string field in new[]{"mainTexture","roughnessMap","normalMap"})
                {
                    var property=list.GetArrayElementAtIndex(index).FindPropertyRelative(field);
                    if(property==null||property.propertyType!=SerializedPropertyType.ObjectReference)throw new InvalidOperationException("Unknown surface catalogue schema");
                    if(!property.objectReferenceValue)continue;
                    var texture=property.objectReferenceValue as Texture2D;string guid;long local;
                    if(!texture||!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(texture,out guid,out local))throw new InvalidOperationException("Unsupported original finish texture");
                    string key="surface:"+group[1]+":"+index.ToString(CultureInfo.InvariantCulture)+":"+field;
                    if(!keys.Add(key))throw new InvalidOperationException("Duplicate surface semantic identity");
                    records.Add(new SurfaceRecord{key=key,property=property.propertyPath,textureGuid=guid,texturePath=AssetDatabase.GetAssetPath(texture),textureName=texture.name,bytes=ArtImageSize.Of(texture),texture=texture});
                }
            }
            return records.OrderBy(record=>record.key,StringComparer.Ordinal).ToList();
        }
        public static void StripCatalogue(UnityEngine.Object catalogue,IList<SurfaceRecord> records)
        {
            var source=new SerializedObject(catalogue);
            // Validate the complete batch before modifying any serialized field.
            foreach(var record in records)
            {
                var slot=source.FindProperty(record.property);string guid;long id;
                if(slot==null||slot.propertyType!=SerializedPropertyType.ObjectReference||!slot.objectReferenceValue||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(slot.objectReferenceValue,out guid,out id)||guid!=record.textureGuid||AssetDatabase.GetAssetPath(slot.objectReferenceValue)!=record.texturePath)
                    throw new InvalidOperationException("Surface source changed during build: "+record.property);
            }
            foreach(var record in records)source.FindProperty(record.property).objectReferenceValue=null;
            source.ApplyModifiedPropertiesWithoutUndo();EditorUtility.SetDirty(catalogue);
        }
        public static void RestoreAsset(string path,string backup,string guid,string originalSha256,string preparedSha256)
        {
            if(String.IsNullOrEmpty(path)||!path.StartsWith("Assets/",StringComparison.Ordinal)||path.Contains("\\")||path.Split('/').Any(part=>part==".."||part=="."||part.Length==0))throw new InvalidOperationException("Invalid surface recovery path");
            if(!File.Exists(path)||!File.Exists(backup)||AssetDatabase.AssetPathToGUID(path)!=guid||String.IsNullOrEmpty(originalSha256)||Hash(backup)!=originalSha256)
                throw new InvalidOperationException("Surface catalogue identity/backup changed");
            string actual=Hash(path);
            if(actual!=originalSha256&&(String.IsNullOrEmpty(preparedSha256)||actual!=preparedSha256))throw new InvalidOperationException("Surface catalogue edited or preparation interrupted before receipt; preserve and inspect before recovery");
            if(actual!=originalSha256)File.Copy(backup,path,true);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        static void SaveJournal(Journal journal)
        {
            string temporary=JournalPath+".new";File.WriteAllText(temporary,JsonUtility.ToJson(journal,true));
            if(File.Exists(JournalPath))File.Replace(temporary,JournalPath,null);else File.Move(temporary,JournalPath);
        }
        public static void Restore()
        {
            Guard();if(!File.Exists(JournalPath))return;
            var journal=JsonUtility.FromJson<Journal>(File.ReadAllText(JournalPath));
            if(journal==null||journal.version!=1||journal.path!=Catalogue)throw new InvalidOperationException("Invalid surface identity journal");
            RestoreAsset(journal.path,Backup,journal.guid,journal.originalSha256,journal.preparedSha256);
        }
        public static void Prepare()
        {
            Guard();var report=new Report();
            try
            {
                var catalogue=AssetDatabase.LoadMainAssetAtPath(Catalogue);if(!catalogue)throw new InvalidOperationException("Original surface catalogue missing");
                var records=CollectRecords(catalogue);var audit=SurfaceReferenceAudit.Scan(Catalogue,records);
                report.eligibleRecords=audit.EligibleRecords;report.recordCount=audit.EligibleRecords.Count;
                foreach(var row in audit.RetainedTextures.OrderBy(row=>row.Key,StringComparer.Ordinal))report.retained.Add(new Retained{guid=row.Key,reason=row.Value});
                var groups=audit.EligibleRecords.GroupBy(record=>record.textureGuid).OrderBy(group=>group.Key,StringComparer.Ordinal).ToList();
                report.bundleCount=groups.Count;report.imageBytes=groups.Sum(group=>group.First().bytes);
                File.WriteAllText(Path.Combine(Root,"surface-reference-audit.json"),JsonUtility.ToJson(report,true));
                if(groups.Count==0)throw new InvalidOperationException("No exclusively relocatable finish textures");
                Debug.Log("[SURFACE-BUILD] Audited "+records.Count+" references; eligible textures="+groups.Count+" image bytes="+report.imageBytes);
                var builds=groups.Select(group=>new AssetBundleBuild{assetBundleName="surface_"+group.Key,assetNames=new[]{group.First().texturePath},addressableNames=new[]{"assets/tcgstreamedsurface/"+group.Key+".asset"}}).ToArray();
                string output=Path.Combine(Application.streamingAssetsPath,"TCGStreamedSurfaces");Directory.CreateDirectory(output);
                var built=BuildPipeline.BuildAssetBundles(output,builds,BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.StrictMode|BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows);
                if(!built)throw new InvalidOperationException("Surface texture bundles failed");
                var manifest=new SurfaceManifest();
                foreach(var group in groups)
                {
                    string name="surface_"+group.Key;uint crc;
                    if(built.GetAllDependencies(name).Length!=0||!BuildPipeline.GetCRCForAssetBundle(Path.Combine(output,name),out crc)||crc==0)throw new InvalidOperationException("Surface bundle has dependencies or no native build CRC: "+name);
                    foreach(var record in group)manifest.entries.Add(new SurfaceEntry{key=record.key,bundle=name,asset="assets/tcgstreamedsurface/"+group.Key+".asset",bytes=record.bytes,crc=crc});
                }
                manifest.Validate();string json=JsonUtility.ToJson(manifest,true);File.WriteAllText(Path.Combine(output,"catalog.json"),json);
                Directory.CreateDirectory("Assets/Resources/TCGStreaming");File.WriteAllText("Assets/Resources/TCGStreaming/SurfaceCatalog.txt",json);AssetDatabase.ImportAsset("Assets/Resources/TCGStreaming/SurfaceCatalog.txt",ImportAssetOptions.ForceSynchronousImport);
                catalogue=AssetDatabase.LoadMainAssetAtPath(Catalogue);string original=Hash(Catalogue);
                Directory.CreateDirectory(Path.GetDirectoryName(Backup));if(!File.Exists(Backup))File.Copy(Catalogue,Backup);
                if(Hash(Backup)!=original)throw new InvalidOperationException("Surface backup differs from restored original catalogue");
                var journal=new Journal{version=1,path=Catalogue,guid=AssetDatabase.AssetPathToGUID(Catalogue),originalSha256=original};SaveJournal(journal);
                StripCatalogue(catalogue,audit.EligibleRecords);AssetDatabase.SaveAssets();journal.preparedSha256=Hash(Catalogue);SaveJournal(journal);
                var current=audit.EligibleRecords.Select(record=>new SurfaceRecord{key=record.key,property=record.property,textureGuid=record.textureGuid,texturePath=record.texturePath,textureName=record.textureName,bytes=record.bytes,texture=AssetDatabase.LoadAssetAtPath<Texture2D>(record.texturePath)}).ToList();
                var remaining=SurfaceReferenceAudit.FindEagerReferences(current);
                if(remaining.Count!=0)throw new InvalidOperationException("Unexplained eager finish references remain: "+String.Join("; ",remaining.ToArray()));
                report.status="prepared";Debug.Log("[SURFACE-BUILD] Prepared "+report.bundleCount+" exact original textures");
            }
            catch(Exception error){report.status="failed";report.error=error.ToString();throw;}
            finally{File.WriteAllText(Path.Combine(Root,"surface-build.json"),JsonUtility.ToJson(report,true));}
        }
    }
}
