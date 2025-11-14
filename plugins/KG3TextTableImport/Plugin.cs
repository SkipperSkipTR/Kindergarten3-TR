using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using System;
using System.IO;
using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;
using PixelCrushers;
using HarmonyLib;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Collections;

namespace KG3TextTableImport
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        // List your table names here (without .json extension)
        private static readonly string[] TableNames = new[]
        {
            "UITable",
            "Deaths",
            "LabCodes",
            "MissionMap",
            "Monstermon",
            "MonstermonLocations",
            "TimePeriods",
            "UnlockedItems",
			"CreditsScene"
        };

        private const string GitHubRawBaseUrl = "https://raw.githubusercontent.com/SkipperSkipTR/Kindergarten3-TR/refs/heads/main/MasterDatabase/TextTables/";

        private void Awake()
        {
            string localAssetsDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "assets", "TextTables");
            Directory.CreateDirectory(localAssetsDir);

            foreach (var tableName in TableNames)
            {
                string jsonPath = Path.Combine(localAssetsDir, $"{tableName}.json");
                string githubUrl = $"{GitHubRawBaseUrl}{tableName}.json";
                bool needDownload = false;

                try
                {
                    byte[] remoteBytes;
                    using (var client = new WebClient())
                    {
                        client.Headers.Add("User-Agent", "KG3TextTableExport");
                        remoteBytes = client.DownloadData(githubUrl);
                    }

                    if (File.Exists(jsonPath))
                    {
                        byte[] localBytes = File.ReadAllBytes(jsonPath);

                        using (var sha = SHA256.Create())
                        {
                            var localHash = sha.ComputeHash(localBytes);
                            var remoteHash = sha.ComputeHash(remoteBytes);

                            // Compare hashes
                            needDownload = !StructuralComparisons.StructuralEqualityComparer.Equals(localHash, remoteHash);
                        }
                    }
                    else
                    {
                        needDownload = true;
                    }

                    if (needDownload)
                    {
                        File.WriteAllBytes(jsonPath, remoteBytes);
                        Logger.LogInfo($"Downloaded/Updated {tableName}.json from GitHub (hash mismatch or missing).");
                    }
                    else
                    {
                        Logger.LogInfo($"{tableName}.json is up to date (hash match).");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to download or check {tableName}.json from GitHub: {ex}");
                }
            }

            var harmony = new Harmony("TextTablePatcher");
            harmony.PatchAll();
            Logger.LogInfo("TextTable overwrite patch applied.");
        }
    }

    [HarmonyPatch(typeof(PixelCrushers.TextTable), "OnAfterDeserialize")]
    class TextTable_OnAfterDeserialize_Patch
    {
        static void Postfix(PixelCrushers.TextTable __instance)
        {
            try
            {
                string tableName = __instance.name;
                string localAssetsDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "assets", "TextTables");
                string jsonPath = Path.Combine(localAssetsDir, $"{tableName}.json");

                if (!File.Exists(jsonPath)) return;

                var json = File.ReadAllText(jsonPath);
                var data = JsonConvert.DeserializeObject<TextTableJsonData>(json);

                BepInEx.Logging.Logger.CreateLogSource("KG3TextTableExport").LogInfo("KG3TextTableExport: Injecting TextTable data from " + tableName);

                // Overwrite languages
                __instance.languages = new Dictionary<string, int>(data.languages);

                // Overwrite fields
                var fieldsDict = new Dictionary<int, PixelCrushers.TextTableField>();
                foreach (var kv in data.fields)
                {
                    int fieldId = int.Parse(kv.Key);
                    var fieldData = kv.Value;
                    var field = new PixelCrushers.TextTableField(fieldData.fieldName);

                    // Set texts for each language
                    foreach (var textKv in fieldData.texts)
                    {
                        int langId = int.Parse(textKv.Key);
                        field.texts[langId] = textKv.Value;
                    }
                    fieldsDict[fieldId] = field;
                }
                __instance.fields = fieldsDict;
            }
            catch (Exception ex)
            {
                BepInEx.Logging.Logger.CreateLogSource("KG3TextTableExport").LogError("Failed to inject TextTable: " + ex);
            }
        }
    }
}

// Helper classes for JSON deserialization
public class TextTableFieldData
{
    public string fieldName;
    public Dictionary<string, string> texts;
}

public class TextTableJsonData
{
    public Dictionary<string, int> languages;
    public Dictionary<string, TextTableFieldData> fields;
}