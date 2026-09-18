#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class VFXGraphAutoSwitcher
{
    static readonly string[] VFXFiles = new[]
    {
        "Comets.vfx",
        "Lightning.vfx",
        "Lightning_Radial.vfx",
        "Rain.vfx",
        "Snow.vfx",
        "Shooting Stars.vfx"
    };

    static VFXGraphAutoSwitcher()
    { 
        EditorApplication.delayCall += RunOnce;
    }

    private static void RunOnce() 
    {
        EditorApplication.delayCall -= RunOnce;
        TryUpdateVFXFiles(); 
    } 

    private static void TryUpdateVFXFiles()
    { 
         
        bool isUnity6OrNewer = false;
#if UNITY_2023_2_OR_NEWER
        isUnity6OrNewer = true;
#endif  

        string versionFolder = isUnity6OrNewer ? "Unity 6" : "Unity 2022";

        string scriptAssetPath = GetThisScriptPath();
        string basePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(scriptAssetPath), "..")).Replace("\\", "/");

        string versionPath = Path.Combine(basePath, versionFolder);

        bool needsUpdate = false;

        foreach (string file in VFXFiles)
        {
            string txtPath = Path.Combine(versionPath, file + ".txt");
            string vfxPath = Path.Combine(basePath, file);

            if (!File.Exists(txtPath) || !File.Exists(vfxPath))
            {
                needsUpdate = true;
                break; 
            }

            if (!FilesAreEqual(txtPath, vfxPath))
            {
                needsUpdate = true;
                break;
            }
        }

        if (!needsUpdate)
        {
            // Everything is already correct
            return;
        }

        Debug.Log($"[ALTOS] Updating VFX files for Unity {(isUnity6OrNewer ? "6+" : "2021/2022")}");

        // Verwijder oude versies
        foreach (string file in VFXFiles)
        {
            string targetPath = Path.Combine(basePath, file); 
            if (File.Exists(targetPath))
            {
                AssetDatabase.DeleteAsset(targetPath);
            }
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            // Zet nieuwe versies
            foreach (string file in VFXFiles)
            {
                string txtPath = Path.Combine(versionPath, file + ".txt");
                string targetPath = Path.Combine(basePath, file);

                if (File.Exists(txtPath))
                {
                    File.Copy(txtPath, targetPath, true);
                    AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
                }
                else
                {
                    Debug.LogWarning($"[ALTOS] Missing source file: {txtPath}");
                }
            }
        }
        catch
        {
            Debug.LogError("[ALTOS] Something went wrong, please contact us at support@occasoftware.com.");
        }

        AssetDatabase.StopAssetEditing();
        AssetDatabase.Refresh();
    }

    private static bool FilesAreEqual(string path1, string path2)
    {
        byte[] file1 = File.ReadAllBytes(path1);
        byte[] file2 = File.ReadAllBytes(path2);

        if (file1.Length != file2.Length)
            return false;

        for (int i = 0; i < file1.Length; i++)
        {
            if (file1[i] != file2[i])
                return false;
        }

        return true;
    }

    // Finds the script path based on filename
    private static string GetThisScriptPath()
    {
        string[] scriptGUIDs = AssetDatabase.FindAssets("VFXGraphAutoSwitcher t:Script");
        foreach (string guid in scriptGUIDs)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == "VFXGraphAutoSwitcher")
                return path;
        }
        return null;
    }
}
#endif
