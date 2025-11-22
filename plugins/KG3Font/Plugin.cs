using BepInEx;
using BepInEx.Unity.Mono;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine.UI;
using PixelCrushers.DialogueSystem;   // you already depend on this

namespace KG3Font;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private const string CrayawnSDF = "Crayawn SDF";
    private const string ChunkfiveExSDF = "Chunkfive Ex SDF";

    private static ManualLogSource Logger;

    private static readonly Dictionary<string, TMP_FontAsset> _fontAsset = new();
    private static Font _crayawnTTF;

    private static AssetBundle _fontBundle;

    // Global gate: only true when current language is Turkish
    private static bool _useTurkishFonts;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

        // --- Patch TMP_Text.text setter ---
        try
        {
            var tmpTextSetter = AccessTools.PropertySetter(typeof(TMP_Text), "text");
            harmony.Patch(tmpTextSetter, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostSetText)));
            Logger.LogInfo("Successfully patched TMP_Text.text setter");
        }
        catch (Exception e)
        {
            Logger.LogError($"Failed to patch TMP_Text: {e}");
        }

        // --- Patch PixelCrushers.LocalizedFonts stuff ---
        try
        {
            var localizedFontsType = AccessTools.TypeByName("PixelCrushers.LocalizedFonts");
            if (localizedFontsType != null)
            {
                var getTMPFont = AccessTools.Method(localizedFontsType, "GetTextMeshProFont", new[] { typeof(string) });
                if (getTMPFont != null)
                {
                    harmony.Patch(getTMPFont, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostGetTextMeshProFont)));
                    Logger.LogInfo("Successfully patched LocalizedFonts.GetTextMeshProFont");
                }

                var getFont = AccessTools.Method(localizedFontsType, "GetFont", new[] { typeof(string) });
                if (getFont != null)
                {
                    harmony.Patch(getFont, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostGetFont)));
                    Logger.LogInfo("Successfully patched LocalizedFonts.GetFont");
                }
            }
            else
            {
                Logger.LogWarning("LocalizedFonts type not found - skipping LocalizedFonts patches");
            }
        }
        catch (Exception e)
        {
            Logger.LogError($"Failed to patch LocalizedFonts: {e}");
        }

        // --- Patch TextFxUGUI ---
        try
        {
            var textFxUGUIType = AccessTools.TypeByName("TextFx.TextFxUGUI");
            if (textFxUGUIType != null)
            {
                var fontSetter = AccessTools.PropertySetter(textFxUGUIType, "font");
                if (fontSetter != null)
                {
                    harmony.Patch(fontSetter, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostSetTextFxFont)));
                    Logger.LogInfo("Successfully patched TextFxUGUI.font setter");
                }

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
        catch (Exception e)
        {
            Logger.LogError($"Failed to patch TextFxUGUI: {e}");
        }

        // --- Patch DialogueManager.SetLanguage(string) to update gate ---
        try
        {
            var setLang = AccessTools.Method(typeof(DialogueManager), "SetLanguage", new[] { typeof(string) });
            if (setLang != null)
            {
                harmony.Patch(setLang, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostSetLanguage)));
                Logger.LogInfo("Successfully patched DialogueManager.SetLanguage");
            }
            else
            {
                Logger.LogWarning("DialogueManager.SetLanguage not found.");
            }
        }
        catch (Exception e)
        {
            Logger.LogError($"Failed to patch DialogueManager.SetLanguage: {e}");
        }

        // --- Patch UnityEngine.UI.Text.OnEnable for DayCompleteText best-fit ---
        try
        {
            var onEnable = AccessTools.Method(typeof(Text), "OnEnable");
            if (onEnable != null)
            {
                harmony.Patch(onEnable, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostTextOnEnable)));
                Logger.LogInfo("Successfully patched UnityEngine.UI.Text.OnEnable");
            }
            else
            {
                Logger.LogWarning("Text.OnEnable not found.");
            }
        }
        catch (Exception e)
        {
            Logger.LogError($"Failed to patch Text.OnEnable: {e}");
        }


        // Initialize gate on load
        _useTurkishFonts = IsTurkishActive();
    }

    // ------------------------------------------------------------------
    // Language gate helpers
    // ------------------------------------------------------------------

    private static bool IsTurkishKey(string lang)
    {
        if (string.IsNullOrEmpty(lang)) return false;
        lang = lang.Trim();

        return lang.Equals("tr", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("turkish", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("türkçe", StringComparison.OrdinalIgnoreCase)
            || lang.Equals("turkce", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTurkishActive()
    {
        // Prefer PixelCrushers localization if available
        try
        {
            var pcLang = Localization.language; // PixelCrushers.DialogueSystem.Localization
            if (IsTurkishKey(pcLang)) return true;
        }
        catch { }

        // Fallback to PlayerPrefs used by title screen
        var pref = PlayerPrefs.GetString("Lang", "None");
        return IsTurkishKey(pref);
    }

    // Postfix for DialogueManager.SetLanguage
    public static void PostSetLanguage(string language)
    {
        _useTurkishFonts = IsTurkishKey(language);
        Logger.LogInfo($"Language changed to '{language}' => Turkish fonts: {_useTurkishFonts}");
    }

    // ------------------------------------------------------------------
    // Harmony postfixes (gated)
    // ------------------------------------------------------------------

    [HarmonyPostfix]
    public static void PostSetText(TMP_Text __instance, string value)
    {
        if (!_useTurkishFonts) return;
        if (__instance == null || __instance.font == null) return;

        // If it's Chunkfive Ex, keep it as Chunkfive Ex
        if (__instance.font.name == ChunkfiveExSDF)
        {
            __instance.font = GetFont(ChunkfiveExSDF);
            __instance.outlineWidth = 0.2f;
        }
        else
        {
            __instance.font = GetFont(CrayawnSDF);
        }
    }

    public static void PostGetTextMeshProFont(string language, ref TMP_FontAsset __result)
    {
        // This method already knows which language is requested
        if (!IsTurkishKey(language)) return;
        if (__result == null) return;

        string originalName = __result.name;

        if (originalName == ChunkfiveExSDF)
        {
            __result = GetFont(ChunkfiveExSDF);
        }
        else
        {
            __result = GetFont(CrayawnSDF);
        }
    }

    public static void PostGetFont(string language, ref Font __result)
    {
        if (!IsTurkishKey(language)) return;
        if (__result == null) return;

        var crayawnTTF = GetTTFFont();
        if (crayawnTTF != null)
        {
            __result = crayawnTTF;
        }
    }

    public static void PostSetTextFxFont(object __instance)
    {
        if (!_useTurkishFonts) return;

        try
        {
            var fontProperty = AccessTools.Property(__instance.GetType(), "font");
            if (fontProperty == null) return;

            var currentFont = fontProperty.GetValue(__instance) as Font;
            if (currentFont == null) return;

            var crayawnTTF = GetTTFFont();
            if (crayawnTTF != null && crayawnTTF != currentFont)
            {
                fontProperty.SetValue(__instance, crayawnTTF);
            }
        }
        catch (Exception e)
        {
            Logger.LogError($"Error in PostSetTextFxFont: {e}");
        }
    }

    public static void PostSetTextFxText(object __instance, string value)
    {
        if (!_useTurkishFonts) return;

        try
        {
            var fontProperty = AccessTools.Property(__instance.GetType(), "font");
            if (fontProperty == null) return;

            var currentFont = fontProperty.GetValue(__instance) as Font;
            if (currentFont == null) return;

            var crayawnTTF = GetTTFFont();
            if (crayawnTTF != null && crayawnTTF != currentFont)
            {
                fontProperty.SetValue(__instance, crayawnTTF);
            }
        }
        catch (Exception e)
        {
            Logger.LogError($"Error in PostSetTextFxText: {e}");
        }
    }

    // ------------------------------------------------------------------
    // AssetBundle + font loading
    // ------------------------------------------------------------------

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
            Logger.LogError($"Failed to load Font AssetBundle: {fontPath}");
        else
            Logger.LogInfo("Font AssetBundle loaded successfully.");

        return _fontBundle;
    }

    private static Font GetTTFFont()
    {
        if (_crayawnTTF != null) return _crayawnTTF;

        var fontBundle = GetFontBundle();
        if (fontBundle == null)
        {
            Logger.LogError("Font bundle not loaded. Cannot load Crayawn.ttf");
            return null;
        }

        try
        {
            _crayawnTTF = fontBundle.LoadAsset<Font>("Crayawn");
            if (_crayawnTTF != null)
            {  
                return _crayawnTTF;
            }
            Logger.LogError("Crayawn.ttf not found inside assetbundle!");
        }
        catch (Exception e)
        {
            Logger.LogError($"Error while loading Crayawn.ttf from assetbundle: {e}");
        }

        return Resources.GetBuiltinResource<Font>("Arial.ttf");
    }

    public static TMP_FontAsset GetFont(string name)
    {
        if (_fontAsset.TryGetValue(name, out TMP_FontAsset font)) return font;

        var fontBundle = GetFontBundle();
        if (fontBundle == null)
        {
            Logger.LogError("Font bundle not loaded.");
            return null;
        }

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

    public static void PostTextOnEnable(Text __instance)
    {
        if (__instance == null) return;

        // Optional: only do this for Turkish
        if (!_useTurkishFonts) return;

        if (__instance.name == "DayCompleteText")
        {
            __instance.resizeTextForBestFit = true;
        }
    }
}
