using BepInEx;
using BepInEx.Unity.Mono;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using System.Collections.Generic;
using System.IO;

namespace KG3Font;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private const string CrayawnSDF = "Crayawn SDF";
    private const string ChunkfiveExSDF = "Chunkfive Ex SDF";
    private static ManualLogSource Logger;
    private static readonly Dictionary<string, TMP_FontAsset> _fontAsset = new();

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        Harmony.CreateAndPatchAll(typeof(Plugin));
    }

    [HarmonyPatch(typeof(TMP_Text), "text", MethodType.Setter), HarmonyPostfix]
    public static void PostSetText(TMP_Text __instance, string value)
    {
        if (__instance.font != null)
        {
            if (__instance.font.name == CrayawnSDF)
                __instance.font = GetFont(CrayawnSDF);
            else if (__instance.font.name == ChunkfiveExSDF)
            {
                __instance.font = GetFont(ChunkfiveExSDF);
                __instance.outlineWidth = 0.2f;
            } 
        }
    }

    private static TMP_FontAsset GetFont(string name)
    {
        if (_fontAsset.TryGetValue(name, out TMP_FontAsset font)) return font;
        string parentPath = Path.GetDirectoryName(Application.dataPath);
        if (parentPath == null)
        {
            Logger.LogError("Parent path is null");
            return null;
        }
        IEnumerable<AssetBundle> allLoadedAssetBundles = AssetBundle.GetAllLoadedAssetBundles();
        AssetBundle fontBundle = null;
        foreach (AssetBundle assetBundle in allLoadedAssetBundles)
        {
            Logger.LogInfo(assetBundle.name);
            if (assetBundle.name == "font")
            {
                fontBundle = assetBundle;
                break;
            }
        }
        if (fontBundle == null)
            fontBundle = AssetBundle.LoadFromFile(Path.Combine(parentPath, "assets", "font"));
        if (fontBundle == null)
        {
            Logger.LogError("Failed to load AssetBundle!");
            return null;
        }
        TMP_FontAsset loadedFont = fontBundle.LoadAsset<TMP_FontAsset>(name);

        _fontAsset[name] = loadedFont;
        return loadedFont;
    }
}