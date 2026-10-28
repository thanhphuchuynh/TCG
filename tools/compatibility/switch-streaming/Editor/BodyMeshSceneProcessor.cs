using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine.SceneManagement;

namespace TCG.SwitchStreaming.Editor
{
    public sealed class BodyMeshSceneProcessor : IProcessSceneWithReport
    {
        public int callbackOrder { get { return 0; } }
        public void OnProcessScene(Scene scene, BuildReport report) { BodyMeshBuild.ProcessScene(scene, report); }
    }
}
