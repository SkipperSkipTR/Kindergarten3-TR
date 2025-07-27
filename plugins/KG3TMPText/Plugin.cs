using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using BepInEx.Unity.Mono;
using System;

namespace KG3TMPText
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony harmony;

        // Change this to your actual game root folder path or detect dynamically
        // For demo, let's assume Environment.CurrentDirectory is root folder
        private static string translationFilePath = Path.Combine(Environment.CurrentDirectory, "assets", "TMPText", "translated.json");

        private static Dictionary<string, string> translatedTexts = new();

        private void Awake()
        {
            Log = Logger;
            harmony = new Harmony("TextMeshProTextImport");
            harmony.PatchAll();

            LoadTranslatedTexts();
            Log.LogInfo($"Loaded {translatedTexts.Count} translations from {translationFilePath}");
        }

        private void LoadTranslatedTexts()
        {
            try
            {
                if (File.Exists(translationFilePath))
                {
                    string json = File.ReadAllText(translationFilePath);
                    translatedTexts = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                }
                else
                {
                    Log.LogWarning($"Translation file not found at {translationFilePath}");
                    translatedTexts = new Dictionary<string, string>();
                }
            }
            catch (System.Exception ex)
            {
                Log.LogError($"Failed to load translations: {ex}");
                translatedTexts = new Dictionary<string, string>();
            }
        }

        // Helper to get full path key like [SceneName] path/to/object
        public static string GetObjectPath(GameObject obj)
        {
            string path = obj.name;
            var current = obj.transform;
            while (current.parent != null)
            {
                current = current.parent;
                path = current.name + "/" + path;
            }
            var sceneName = obj.scene.name;
            return $"[{sceneName}] {path}";
        }

        [HarmonyPatch(typeof(TextMeshProUGUI), "OnEnable")]
        class TextMeshProUGUI_OnEnable_Patch
        {
            static void Postfix(TextMeshProUGUI __instance)
            {
                if (__instance == null) return;
                if (string.IsNullOrEmpty(__instance.text)) return;

                var path = Plugin.GetObjectPath(__instance.gameObject);

                if (Plugin.translatedTexts.TryGetValue(path, out var translated))
                {
                    __instance.text = translated;
                }

                if (__instance.name == "RestartTitle")
                {
                    __instance.enableAutoSizing = true;
                    __instance.ForceMeshUpdate();
                }
            }
        }

        [HarmonyPatch(typeof(Text), "OnEnable")]
        class Text_OnEnable_Patch
        {
            static void Postfix(Text __instance)
            {
                if (__instance == null) return;
                if (string.IsNullOrEmpty(__instance.text)) return;

                var path = Plugin.GetObjectPath(__instance.gameObject);

                if (__instance.name == "DayCompleteText")
                {
                    __instance.resizeTextForBestFit = true;
                }

                if (Plugin.translatedTexts.TryGetValue(path, out var translated))
                {
                    __instance.text = translated;
                }
            }
        }
    }
}