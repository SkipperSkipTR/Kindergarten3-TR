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
    private static Font _crayawnTTF;

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
                
                // Also patch the regular Font getter for TextFxUGUI
                var getFontMethod = AccessTools.Method(localizedFontsType, "GetFont", new[] { typeof(string) });
                if (getFontMethod != null)
                {
                    harmony.Patch(getFontMethod, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostGetFont)));
                    Logger.LogInfo("Successfully patched LocalizedFonts.GetFont");
                }
                else
                {
                    Logger.LogWarning("GetFont method not found");
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
        
        // Try to patch TextFxUGUI
        try
        {
            var textFxUGUIType = AccessTools.TypeByName("TextFx.TextFxUGUI");
            if (textFxUGUIType != null)
            {
                // Patch the font property setter
                var fontSetter = AccessTools.PropertySetter(textFxUGUIType, "font");
                if (fontSetter != null)
                {
                    harmony.Patch(fontSetter, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostSetTextFxFont)));
                    Logger.LogInfo("Successfully patched TextFxUGUI.font setter");
                }
                
                // Also patch text setter in case font needs to be updated when text changes
                var textSetter = AccessTools.PropertySetter(textFxUGUIType, "text");
                if (textSetter != null)
                {
                    harmony.Patch(textSetter, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostSetTextFxText)));
                    Logger.LogInfo("Successfully patched TextFxUGUI.text setter");
                }
            }
            else
            {
                Logger.LogWarning("TextFxUGUI type not found - skipping TextFxUGUI patches");
            }
        }
        catch (System.Exception e)
        {
            Logger.LogError($"Failed to patch TextFxUGUI: {e}");
        }
    }

    [HarmonyPostfix]
    public static void PostSetText(TMP_Text __instance, string value)
    {
        if (__instance.font != null)
        {
            // If it's Chunkfive Ex, keep it as Chunkfive Ex
            if (__instance.font.name == ChunkfiveExSDF)
            {
                __instance.font = GetFont(ChunkfiveExSDF);
                __instance.outlineWidth = 0.2f;
            }
            // Everything else becomes Crayawn
            else
            {
                __instance.font = GetFont(CrayawnSDF);
            }
        }
    }

    public static void PostGetTextMeshProFont(string language, ref TMP_FontAsset __result)
    {
        if (__result != null)
        {
            string originalName = __result.name;
            
            // If it's Chunkfive Ex, keep it as Chunkfive Ex
            if (originalName == ChunkfiveExSDF)
            {
                __result = GetFont(ChunkfiveExSDF);
                Logger.LogInfo($"Kept {ChunkfiveExSDF} for language: {language}");
            }
            // Everything else becomes Crayawn
            else
            {
                __result = GetFont(CrayawnSDF);
                Logger.LogInfo($"Replaced {originalName} with {CrayawnSDF} for language: {language}");
            }
        }
    }

    // Patch for LocalizedFonts.GetFont (returns regular Font for TextFxUGUI)
    public static void PostGetFont(string language, ref Font __result)
    {
        if (__result != null)
        {
            string originalName = __result.name;
            
            // Replace with Crayawn TTF (unless we want to keep certain fonts)
            // For now, replace everything with Crayawn
            var crayawnTTF = GetTTFFont();
            if (crayawnTTF != null)
            {
                __result = crayawnTTF;
                Logger.LogInfo($"Replaced TTF font {originalName} with Crayawn.ttf for language: {language}");
            }
        }
    }

    // Patch for TextFxUGUI font setter
    public static void PostSetTextFxFont(object __instance)
    {
        try
        {
            var fontProperty = AccessTools.Property(__instance.GetType(), "font");
            if (fontProperty != null)
            {
                var currentFont = fontProperty.GetValue(__instance) as Font;
                if (currentFont != null)
                {
                    var crayawnTTF = GetTTFFont();
                    if (crayawnTTF != null && crayawnTTF != currentFont)
                    {
                        fontProperty.SetValue(__instance, crayawnTTF);
                        Logger.LogInfo($"TextFxUGUI font changed from {currentFont.name} to Crayawn.ttf");
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Logger.LogError($"Error in PostSetTextFxFont: {e}");
        }
    }

    // Patch for TextFxUGUI text setter
    public static void PostSetTextFxText(object __instance, string value)
    {
        try
        {
            var fontProperty = AccessTools.Property(__instance.GetType(), "font");
            if (fontProperty != null)
            {
                var currentFont = fontProperty.GetValue(__instance) as Font;
                if (currentFont != null)
                {
                    var crayawnTTF = GetTTFFont();
                    if (crayawnTTF != null && crayawnTTF != currentFont)
                    {
                        fontProperty.SetValue(__instance, crayawnTTF);
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Logger.LogError($"Error in PostSetTextFxText: {e}");
        }
    }

    private static Font GetTTFFont()
	{
		if (_crayawnTTF != null)
			return _crayawnTTF;

		var fontBundle = GetFontBundle();
		if (fontBundle == null)
		{
			Logger.LogError("Font bundle not loaded. Cannot load Crayawn.ttf");
			return null;
		}

		try
		{
			// Load the TTF directly from the AssetBundle
			_crayawnTTF = fontBundle.LoadAsset<Font>("Crayawn");

			if (_crayawnTTF != null)
			{
				Logger.LogInfo("Loaded Crayawn.ttf from assetbundle.");
				return _crayawnTTF;
			}
			else
			{
				Logger.LogError("Crayawn.ttf not found inside assetbundle!");
			}
		}
		catch (System.Exception e)
		{
			Logger.LogError($"Error while loading Crayawn.ttf from assetbundle: {e}");
		}

		// Safety fallback
		Logger.LogWarning("Falling back to built-in Arial for Crayawn.ttf");
		return Resources.GetBuiltinResource<Font>("Arial.ttf");
	}
	
	private static AssetBundle _fontBundle;

	private static AssetBundle GetFontBundle()
	{
		if (_fontBundle != null) return _fontBundle;

		string parentPath = Path.GetDirectoryName(Application.dataPath);
		if (parentPath == null)
		{
			Logger.LogError("Parent path is null");
			return null;
		}

		string fontPath = Path.Combine(parentPath, "assets", "font");

		_fontBundle = AssetBundle.LoadFromFile(fontPath);
		if (_fontBundle == null)
		{
			Logger.LogError($"Failed to load Font AssetBundle: {fontPath}");
		}
		else
		{
			Logger.LogInfo("Font AssetBundle loaded successfully.");
		}

		return _fontBundle;
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

        AssetBundle fontBundle = GetFontBundle();
		if (fontBundle == null)
		{
			Logger.LogError("Font bundle not loaded.");
			return null;
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