using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Builds the HUD's SDF font assets from the TTFs in Assets/S3/UI/Fonts (Hud.uss points at them).</summary>
public static class S3UISetup
{
    const string FontDir = "Assets/S3/UI/Fonts";
    static readonly string[] Fonts = { "LilitaOne-Regular", "Rubik-Medium", "Rubik-Bold", "Rubik-ExtraBold" };

    [MenuItem("S3/Create UI Font Assets")]
    public static void CreateFontAssets()
    {
        foreach (var name in Fonts)
        {
            if (File.Exists($"{FontDir}/{name}-SDF.asset")) continue;
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<Font>($"{FontDir}/{name}.ttf");
            EditorApplication.ExecuteMenuItem("Assets/Create/UI Toolkit/Text/Font Asset/SDF");
            AssetDatabase.RenameAsset($"{FontDir}/{name} SDF.asset", $"{name}-SDF");
        }
        AssetDatabase.SaveAssets();
    }
}
