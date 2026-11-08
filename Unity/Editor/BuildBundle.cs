#if UNITY_EDITOR
using System.IO;
using UnityEditor;

public static class BuildBundle
{
    [MenuItem("Tools/HectorSalamanca/Build AssetBundle")]
    public static void Build()
    {
        const string outDir = "AssetBundles";
        Directory.CreateDirectory(outDir);
        BuildPipeline.BuildAssetBundles(outDir, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        AssetDatabase.Refresh();
        EditorUtility.RevealInFinder(outDir);
    }
}
#endif
