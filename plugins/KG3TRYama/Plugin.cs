using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;
using PixelCrushers.DialogueSystem;
using Newtonsoft.Json;

namespace KG3TRYama
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
		// Config Entry to enable/disable plugin
		private ConfigEntry<bool> _pluginEnabled;
		
        // Static fields for database handling
        private static DialogueDatabase _cachedDatabase = null;
        private static string _currentVersion = "";

        // URLs and file paths
        private static readonly string _githubRawUrl = "https://raw.githubusercontent.com/SkipperSkipTR/Kindergarten3-TR/refs/heads/main/MasterDatabase/";
        private static readonly string _versionFile = "version.txt";
        private static readonly string _databaseFile = "kg3outtranslated.json";
        private static readonly string _translatedDatabaseFile = "translated_dialogue_system.json";
        private static readonly string _databaseFolder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "assets");

        private static readonly string _versionPath = Path.Combine(_databaseFolder, _versionFile);
        private static readonly string _dbPath = Path.Combine(_databaseFolder, _databaseFile);
        private static readonly string _translatedPath = Path.Combine(_databaseFolder, _translatedDatabaseFile);
        private static readonly string _originalDbPath = Path.Combine(_databaseFolder, "OriginalDatabase.json");

        // Flags for process control
        private bool _downloadComplete = false;
        private bool _processingComplete = false;
		private bool _shouldProcessTranslation = true;
		private bool _downloadFailed = false;

        // Serializable wrapper classes for Unity/JSON parsing
        [Serializable]
        private class DatabaseWrapper
        {
            public List<ConversationWrapper> conversations;
        }

        [Serializable]
        private class ConversationWrapper
        {
            public List<Field> fields;
            public List<DialogueEntryWrapper> dialogueEntries;
        }

        [Serializable]
        private class DialogueEntryWrapper
        {
            public int id;
            public List<Field> fields;
        }

        [Serializable]
        private class Field
        {
            public string title;
            public string value;
        }

        [Serializable]
        public class TranslationRoot
        {
            public List<TranslationConversation> conversations;
        }

        [Serializable]
        public class TranslationConversation
        {
            public string Title;
            public List<TranslationEntry> Dialogue;
        }

        [Serializable]
        public class TranslationEntry
        {
            public int entryId;

            [JsonProperty("Dialogue Text")]
            public string DialogueText;

            [JsonProperty("Menu Text")]
            public string MenuText;
        }

        private void Awake()
        {
			_pluginEnabled = Config.Bind("General", "EnablePlugin", true, "Set to false to disable the translation plugin entirely.");

			if (!_pluginEnabled.Value)
			{
				Logger.LogInfo("Translation plugin is disabled via config.");
				return;
			}
			
            Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loading...");

            if (!Directory.Exists(_databaseFolder))
                Directory.CreateDirectory(_databaseFolder);

            StartCoroutine(DownloadDatabase());
        }

        private void Start()
        {
			if (!_pluginEnabled.Value)
				return;
			
            StartCoroutine(ProcessAfterDownload());
        }

        private IEnumerator DownloadDatabase()
        {
            bool versionFileExists = File.Exists(_versionPath);
            bool dbFileExists = File.Exists(_dbPath);
            bool translatedFileExists = File.Exists(_translatedPath);

            // Read current version if exists
            if (versionFileExists)
            {
                _currentVersion = File.ReadAllText(_versionPath).Trim();
                Logger.LogInfo($"Found existing version: {_currentVersion}");
            }

            // Fetch latest version from GitHub
            using (UnityWebRequest versionRequest = UnityWebRequest.Get($"{_githubRawUrl}{_versionFile}"))
            {
                versionRequest.timeout = 10;
                yield return versionRequest.SendWebRequest();

                if (versionRequest.result != UnityWebRequest.Result.Success)
                {
                    Logger.LogError($"Version check failed: {versionRequest.error}");
                    _downloadFailed = true;
                    yield break;
                }

                string latestVersion = versionRequest.downloadHandler.text.Trim();

                // Skip download and processing if files exist and version matches
                if (latestVersion == _currentVersion && dbFileExists && translatedFileExists)
                {
                    Logger.LogInfo("All required files are present and up to date. Skipping download and translation processing.");
                    _shouldProcessTranslation = false;
                    _downloadComplete = true;
                    yield break;
                }

                // Skip re-downloading if version matches and DB file exists
                if (latestVersion == _currentVersion && dbFileExists)
                {
                    Logger.LogInfo("Database file already exists and is up to date. Skipping download.");
                    _downloadComplete = true;
                    yield break;
                }

                // Otherwise, download the database file
                using (UnityWebRequest dbRequest = UnityWebRequest.Get($"{_githubRawUrl}{_databaseFile}"))
                {
                    dbRequest.timeout = 15;
                    yield return dbRequest.SendWebRequest();

                    if (dbRequest.result != UnityWebRequest.Result.Success)
                    {
                        Logger.LogError($"Download failed: {dbRequest.error}");
                        _downloadFailed = true;
                        yield break;
                    }

                    File.WriteAllText(_dbPath, dbRequest.downloadHandler.text);
                    File.WriteAllText(_versionPath, latestVersion);

                    Logger.LogInfo($"Successfully downloaded and updated to version {latestVersion}");

                    _shouldProcessTranslation = true;
                    _downloadComplete = true;
                }
            }
        }

        private IEnumerator ProcessAfterDownload()
		{
			while (!_downloadComplete && !_downloadFailed)
				yield return new WaitForSeconds(0.5f);
			
			if (_downloadFailed)
			{
				Logger.LogError("Download failed. Stopping processing.");
				yield break;
			}

			yield return StartCoroutine(ExportOriginalDatabase());

			// Only process translations if needed
			if (_shouldProcessTranslation)
				yield return StartCoroutine(ProcessTranslation());
			else
				Logger.LogInfo("Translation already processed. Skipping translation step.");

			ApplyHarmonyPatch();
			_processingComplete = true;
		}

        private IEnumerator ExportOriginalDatabase()
        {
            // Remove unnecessary wait
            while (DialogueManager.MasterDatabase == null)
            {
                yield return new WaitForSeconds(0.1f);
                Logger.LogInfo("Waiting for MasterDatabase to be initialized...");
            }
            
            DialogueDatabase database = DialogueManager.MasterDatabase;

            if (database == null)
            {
                Logger.LogError("MasterDatabase is null during export!");
                yield break;
            }

            if (File.Exists(_originalDbPath))
            {
                Logger.LogInfo("Original database file already exists");
                yield break;
            }

            try
            {
                string json = JsonUtility.ToJson(database, true);
                File.WriteAllText(_originalDbPath, json);
                Logger.LogInfo($"Original database saved to: {_originalDbPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to export original database: {ex}");
            }
            yield return null;
        }

        private IEnumerator ProcessTranslation()
        {
            if (!File.Exists(_dbPath))
            {
                Logger.LogError("Database file not found for processing");
                yield break;
            }

            try
            {
                string jsonText = File.ReadAllText(_dbPath);
                TranslationRoot root = JsonConvert.DeserializeObject<TranslationRoot>(jsonText);

                if (root?.conversations == null)
                {
                    Logger.LogError("Failed to parse translation JSON");
                    yield break;
                }

                // Build translation lookup
                var translationMap = new Dictionary<string, Dictionary<int, TranslationEntry>>(root.conversations.Count);

                foreach (var conv in root.conversations)
                {
                    var entryMap = new Dictionary<int, TranslationEntry>(conv.Dialogue.Count);
                    foreach (var entry in conv.Dialogue)
                        entryMap[entry.entryId] = entry;

                    translationMap[conv.Title] = entryMap;
                }

                DialogueDatabase database = DialogueManager.MasterDatabase;

                foreach (var conv in database.conversations)
                {
                    if (!translationMap.TryGetValue(conv.Title, out var translatedEntries)) continue;

                    foreach (var entry in conv.dialogueEntries)
                    {
                        if (!translatedEntries.TryGetValue(entry.id, out var tEntry)) continue;

                        // Optimize field update by direct access
                        foreach (var field in entry.fields)
                        {
                            if (field.title == "Dialogue Text" && !string.IsNullOrEmpty(tEntry.DialogueText))
                                field.value = tEntry.DialogueText;
                            else if (field.title == "Menu Text" && !string.IsNullOrEmpty(tEntry.MenuText))
                                field.value = tEntry.MenuText;
                        }
                    }
                }

                string outputJson = JsonUtility.ToJson(database, true);
                File.WriteAllText(_translatedPath, outputJson);

                Logger.LogInfo($"Successfully merged translations into {_translatedPath}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Translation processing failed: {ex}");
            }

            yield return null;
        }

        private void ApplyHarmonyPatch()
        {
            var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
            var originalMethod = AccessTools.Method(typeof(DatabaseManager), "GetMasterDatabase");

            if (originalMethod != null)
            {
                harmony.Patch(originalMethod, new HarmonyMethod(typeof(Plugin), nameof(GetMasterDatabasePrefix)));
                Logger.LogInfo("Harmony patch applied successfully.");
            }
            else
            {
                Logger.LogError("Method GetMasterDatabase not found.");
            }
        }

        [HarmonyPrefix]
        public static bool GetMasterDatabasePrefix(ref DialogueDatabase __result)
        {
            if (_cachedDatabase != null)
            {
                __result = _cachedDatabase;
                return false;
            }

            string dbPath = Path.Combine(_databaseFolder, _translatedDatabaseFile);

            if (File.Exists(dbPath))
            {
                try
                {
                    _cachedDatabase = ScriptableObject.CreateInstance<DialogueDatabase>();
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(dbPath), _cachedDatabase);

                    _cachedDatabase.actors ??= new List<Actor>();
                    _cachedDatabase.conversations ??= new List<Conversation>();

                    __result = _cachedDatabase;
                    return false;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to load translated database: {ex.Message}");
                }
            }

            Debug.Log("Using original game database");
            return true;
        }
    }
}
