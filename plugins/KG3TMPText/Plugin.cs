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
using System.Security.Cryptography;
using System.Text;
using System.Net.Http;
using System.Threading.Tasks;

namespace KG3TMPText
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony harmony;
		
        private static string translationFilePath = Path.Combine(Paths.GameRootPath, "assets", "TMPText", "translated.json");

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
			Task.Run(async () =>
			{
				await CheckAndUpdateTranslationFile();
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
				catch (Exception ex)
				{
					Log.LogError($"Failed to load translations: {ex}");
					translatedTexts = new Dictionary<string, string>();
				}
			});
		}
		
		private async Task CheckAndUpdateTranslationFile()
		{
			string remoteUrl = "https://raw.githubusercontent.com/SkipperSkipTR/Kindergarten3-TR/refs/heads/main/MasterDatabase/TMPText/translated.json";

			try
			{
				using HttpClient client = new HttpClient();
				var response = await client.GetAsync(remoteUrl);
				if (!response.IsSuccessStatusCode)
				{
					Log.LogWarning($"Failed to fetch remote translation file: {response.StatusCode}");
					return;
				}

				string remoteJson = await response.Content.ReadAsStringAsync();
				string remoteHash = ComputeSha256Hash(remoteJson);

				string localJson = "";
				string localHash = "";

				if (File.Exists(translationFilePath))
				{
					localJson = File.ReadAllText(translationFilePath);
					localHash = ComputeSha256Hash(localJson);
				}

				if (remoteHash != localHash)
				{
					File.WriteAllText(translationFilePath, remoteJson);
					Log.LogInfo("Updated local translation file (SHA256 hash changed).");
				}
				else
				{
					Log.LogInfo("Local translation file is up-to-date (SHA256 match).");
				}
			}
			catch (Exception ex)
			{
				Log.LogError($"Error checking/updating translation file: {ex}");
			}
		}
		
		private string ComputeSha256Hash(string rawData)
		{
			using (SHA256 sha256Hash = SHA256.Create())
			{
				byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
				StringBuilder builder = new StringBuilder();
				foreach (byte b in bytes)
				{
					builder.Append(b.ToString("x2"));
				}
				return builder.ToString();
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