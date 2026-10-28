using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TCG.SwitchStreaming.Editor
{
    [Serializable]
    public sealed class ArtRecord
    {
        public string key, property, spritePath, spriteGuid, texturePath, textureGuid, spriteName, textureName;
        public long spriteId, bytes;
        [NonSerialized] public Sprite sprite;
    }

    public static partial class StreamingBuild
    {
        public static List<ArtRecord> CollectRecords(UnityEngine.Object catalogue)
        {
            var serialized = new SerializedObject(catalogue);
            var result = new List<ArtRecord>();
            foreach (string field in new[] { "m_DataList", "m_MegabotDataList", "m_FantasyRPGDataList", "m_CatJobDataList", "m_SpecialCardImageList" })
            {
                var list = serialized.FindProperty(field);
                if (list == null) continue;
                for (int i = 0; i < list.arraySize; i++)
                {
                    var item = list.GetArrayElementAtIndex(i);
                    int monster = item.FindPropertyRelative("MonsterType").intValue;
                    Add(result, item.FindPropertyRelative("Icon"), "monster:" + monster + ":icon:0");
                    Add(result, item.FindPropertyRelative("GhostIcon"), "monster:" + monster + ":ghost:0");
                    foreach (var pair in new[] { new[] { "IconList", "variant-icon" }, new[] { "BGList", "variant-bg" } })
                    {
                        var variants = item.FindPropertyRelative(pair[0]);
                        if (variants == null) continue;
                        for (int variant = 0; variant < variants.arraySize; variant++)
                            Add(result, variants.GetArrayElementAtIndex(variant), "monster:" + monster + ":" + pair[1] + ":" + variant);
                    }
                }
            }
            var icons = serialized.FindProperty("m_TetramonImageList");
            if (icons != null) for (int i = 0; i < icons.arraySize; i++) Add(result, icons.GetArrayElementAtIndex(i), "tetramon:" + i);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in result) if (!keys.Add(record.key)) throw new InvalidOperationException("Ambiguous artwork semantic key: " + record.key);
            return result;
        }
        static void Add(List<ArtRecord> records, SerializedProperty property, string key)
        {
            if (property == null || !property.objectReferenceValue) return;
            var sprite = property.objectReferenceValue as Sprite;
            if (!sprite || !sprite.texture) throw new InvalidOperationException("Non-sprite catalogue artwork: " + property.propertyPath);
            string guid; long id;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out guid, out id)) throw new InvalidOperationException("Artwork has no stable source identity");
            string texture = AssetDatabase.GetAssetPath(sprite.texture);
            records.Add(new ArtRecord { key = key, property = property.propertyPath, sprite = sprite,
                spritePath = AssetDatabase.GetAssetPath(sprite), spriteGuid = guid, spriteId = id,
                texturePath = texture, textureGuid = AssetDatabase.AssetPathToGUID(texture), bytes = ImageBytes(sprite.texture),
                spriteName = sprite.name, textureName = sprite.texture.name });
        }
        public static void StripCatalogue(UnityEngine.Object catalogue, List<ArtRecord> records)
        {
            var serialized = new SerializedObject(catalogue);
            foreach (var record in records)
            {
                var property = serialized.FindProperty(record.property);
                string guid; long id;
                if (property == null || !property.objectReferenceValue ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(property.objectReferenceValue, out guid, out id) ||
                    guid != record.spriteGuid || id != record.spriteId)
                    throw new InvalidOperationException("Catalogue changed during preparation: " + record.property);
                property.objectReferenceValue = null;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalogue);
        }
        public static long ImageBytes(Texture2D texture) { return ArtImageSize.Of(texture); }

    }
}
