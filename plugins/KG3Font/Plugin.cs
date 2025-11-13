using BepInEx;
using BepInEx.Unity.Mono;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

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
        
        // Manual patching with better error handling
        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        
        // Patch TMP_Text setter
        try
        {
            var tmpTextSetter = AccessTools.PropertySetter(typeof(TMP_Text), "text");
            harmony.Patch(tmpTextSetter, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostSetText)));
            Logger.LogInfo("Successfully patched TMP_Text.text setter");
        }
        catch (System.Exception e)
        {
            Logger.LogError($"Failed to patch TMP_Text: {e}");
        }
        
        // Try to patch LocalizedFonts
        try
        {
            var localizedFontsType = AccessTools.TypeByName("PixelCrushers.LocalizedFonts");
            if (localizedFontsType != null)
            {
                var getTextMeshProFontMethod = AccessTools.Method(localizedFontsType, "GetTextMeshProFont", new[] { typeof(string) });
                if (getTextMeshProFontMethod != null)
                {
                    harmony.Patch(getTextMeshProFontMethod, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostGetTextMeshProFont)));
                    Logger.LogInfo("Successfully patched LocalizedFonts.GetTextMeshProFont");
                }
                else
                {
                    Logger.LogWarning("GetTextMeshProFont method not found");
                }
            }
            else
            {
                Logger.LogWarning("LocalizedFonts type not found - skipping LocalizedFonts patches");
            }
        }
        catch (System.Exception e)
        {
            Logger.LogError($"Failed to patch LocalizedFonts: {e}");
        }
    }

    [HarmonyPostfix]
    public static void PostSetText(TMP_Text __instance, string value)
    {
        if (__instance.font != null)
        {
            if (__instance.font.name.Contains(CrayawnSDF))
            {
                __instance.font = GetFont(CrayawnSDF);
            }
            else if (__instance.font.name == ChunkfiveExSDF)
            {
                __instance.font = GetFont(ChunkfiveExSDF);
                __instance.outlineWidth = 0.2f;
            } 
        }
    }

    public static void PostGetTextMeshProFont(string language, ref TMP_FontAsset __result)
    {
        if (__result != null)
        {
            string originalName = __result.name;
            
            if (originalName.Contains(CrayawnSDF))
            {
                __result = GetFont(CrayawnSDF);
                Logger.LogInfo($"Replaced {CrayawnSDF} for language: {language}");
            }
            else if (originalName == ChunkfiveExSDF)
            {
                __result = GetFont(ChunkfiveExSDF);
                Logger.LogInfo($"Replaced {ChunkfiveExSDF} for language: {language}");
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

        // Check if font bundle is already loaded
        IEnumerable<AssetBundle> allLoadedAssetBundles = AssetBundle.GetAllLoadedAssetBundles();
        AssetBundle fontBundle = null;
        foreach (AssetBundle assetBundle in allLoadedAssetBundles)
        {
            if (assetBundle.name == "font")
            {
                fontBundle = assetBundle;
                Logger.LogInfo($"Found existing font bundle: {assetBundle.name}");
                break;
            }
        }

        // Load font bundle if not found
        if (fontBundle == null)
        {
            string fontPath = Path.Combine(parentPath, "assets", "font");
            fontBundle = AssetBundle.LoadFromFile(fontPath);
            if (fontBundle == null)
            {
                Logger.LogError($"Failed to load AssetBundle from: {fontPath}");
                return null;
            }
            Logger.LogInfo($"Loaded font bundle from: {fontPath}");
        }

        // Load the font asset
        TMP_FontAsset loadedFont = fontBundle.LoadAsset<TMP_FontAsset>(name);
        if (loadedFont == null)
        {
            Logger.LogError($"Failed to load font asset: {name}");
            return null;
        }

        _fontAsset[name] = loadedFont;
        Logger.LogInfo($"Cached font: {name}");
        return loadedFont;
    }
}