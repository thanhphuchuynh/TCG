using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

// Keep the recovered original game icon in future native Mac builds.
public static class TCGNativeAppIcon
{
    [PostProcessBuild(1000)]
    public static void CopyIcon(BuildTarget target, string outputPath)
    {
        if (target != BuildTarget.StandaloneOSX) return;
        string source = Path.Combine(Application.dataPath, "Editor/NativeAppIcon.icns");
        if (!File.Exists(source)) throw new FileNotFoundException("Original Mac app icon missing", source);
        string resources = Path.Combine(outputPath, "Contents/Resources");
        Directory.CreateDirectory(resources);
        File.Copy(source, Path.Combine(resources, "PlayerIcon.icns"), true);
    }
}
