using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace KG3Texture;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    public static Plugin Instance { get; private set; }

    private Harmony _harmony;
    private static bool _useTurkishTextures;
    private Dictionary<string, Texture2D> _turkishTextures;
    private Dictionary<string, Sprite> _turkishSprites;

    // Track injected swappers and manual sprites
    private readonly HashSet<int> _injectedSwappers = new();
    private readonly HashSet<string> _manualSpritesReplaced = new();
    private readonly HashSet<int> _adjustedLogoInstances = new(); // Track which logos we've adjusted
    
    private bool _hasAppliedThisScene;
    private Coroutine _delayedApplyCo;

    // Sprites that need manual replacement (not handled by LanguageSpriteSwapper)
    private readonly HashSet<string> _manualSpriteNames = new HashSet<string> 
    { 
        "NurseSign",
        "cleanAppleBig"
        // Add more sprite names here as needed
    };

    // Reflection cache for LanguageSpriteSwapper
    private Type _languageSpriteSwapperType;
    private FieldInfo _swapperSpritesField;
    private FieldInfo _swapperRendererField;
    private MethodInfo _updateSpriteMethod;

    private void Awake()
    {
        Instance = this;
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

        _harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

        // Find and cache LanguageSpriteSwapper type
        if (CacheSwapperType())
        {
            // Patch LanguageSpriteSwapper.Awake
            try
            {
                var swapperAwake = AccessTools.Method(_languageSpriteSwapperType, "Awake");
                if (swapperAwake != null)
                {
                    _harmony.Patch(swapperAwake, 
                        prefix: new HarmonyMethod(typeof(Plugin), nameof(PrefixSwapperAwake)));
                    Logger.LogInfo("Patched LanguageSpriteSwapper.Awake");
                }
            }
            catch (Exception e)
            {
                Logger.LogError($"Failed to patch LanguageSpriteSwapper: {e}");
            }
        }

        // Patch DialogueManager.SetLanguage
        try
        {
            var setLang = AccessTools.Method(typeof(DialogueManager), "SetLanguage", new[] { typeof(string) });
            if (setLang != null)
            {
                _harmony.Patch(setLang, postfix: new HarmonyMethod(typeof(Plugin), nameof(PostSetLanguage)));
                Logger.LogInfo("Patched DialogueManager.SetLanguage");
            }
        }
        catch (Exception e)
        {
            Logger.LogError($"Failed to patch DialogueManager: {e}");
        }

        _useTurkishTextures = IsTurkishActive();
        Logger.LogInfo($"Turkish mode: {_useTurkishTextures}");

        // Load textures/sprites once at startup
        _turkishTextures = LoadReplacementTextures();
        _turkishSprites = new Dictionary<string, Sprite>();
        Logger.LogInfo($"Loaded {_turkishTextures.Count} Turkish textures");

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        _harmony?.UnpatchSelf();
        if (Instance == this) Instance = null;
    }

    private bool CacheSwapperType()
    {
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                _languageSpriteSwapperType = assembly.GetType("LanguageSpriteSwapper");
                if (_languageSpriteSwapperType != null)
                    break;
            }

            if (_languageSpriteSwapperType == null)
            {
                Logger.LogWarning("LanguageSpriteSwapper type not found - will only use manual replacement");
                return false;
            }

            _swapperSpritesField = AccessTools.Field(_languageSpriteSwapperType, "sprites");
            _swapperRendererField = AccessTools.Field(_languageSpriteSwapperType, "spriteRenderer");
            _updateSpriteMethod = AccessTools.Method(_languageSpriteSwapperType, "UpdateSprite");
            
            if (_swapperSpritesField == null || _swapperRendererField == null)
            {
                Logger.LogError("Failed to find LanguageSpriteSwapper fields");
                return false;
            }

            Logger.LogInfo($"Cached LanguageSpriteSwapper type from: {_languageSpriteSwapperType.Assembly.GetName().Name}");
            return true;
        }
        catch (Exception e)
        {
            Logger.LogError($"Reflection cache failed: {e}");
            return false;
        }
    }

    // ============ Harmony Patches ============

    public static bool PrefixSwapperAwake(object __instance)
    {
        if (Instance == null || !_useTurkishTextures) 
            return true;

        try
        {
            Instance.InjectTurkishSprite(__instance as MonoBehaviour);
        }
        catch (Exception e)
        {
            Logger.LogError($"Failed to inject Turkish sprite: {e}");
        }

        return true;
    }

    public static void PostSetLanguage(string language)
    {
        bool wasOn = _useTurkishTextures;
        _useTurkishTextures = IsTurkishKey(language);

        Logger.LogInfo($"Language -> '{language}' (Turkish: {_useTurkishTextures})");

        if (Instance == null) return;

        if (_useTurkishTextures && !wasOn)
        {
            // Switching TO Turkish - reapply
            Instance._injectedSwappers.Clear();
            Instance._manualSpritesReplaced.Clear();
            
            if (Instance._delayedApplyCo != null)
                Instance.StopCoroutine(Instance._delayedApplyCo);
                
            Instance._delayedApplyCo = Instance.StartCoroutine(Instance.DelayedFullApply());
        }
    }

    // ============ Scene Management ============

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Logger.LogInfo($"Scene loaded: {scene.name}");
        
        _injectedSwappers.Clear();
        _manualSpritesReplaced.Clear();
        _adjustedLogoInstances.Clear(); // Clear logo tracking for new scene
        _hasAppliedThisScene = false;

        if (_delayedApplyCo != null)
        {
            StopCoroutine(_delayedApplyCo);
            _delayedApplyCo = null;
        }

        if (_useTurkishTextures)
        {
            _delayedApplyCo = StartCoroutine(DelayedFullApply());
        }
    }

    private IEnumerator DelayedFullApply()
    {
        yield return new WaitForSeconds(0.3f);
        
        if (!_useTurkishTextures)
        {
            _delayedApplyCo = null;
            yield break;
        }

        // 1) Inject into LanguageSpriteSwappers
        if (_languageSpriteSwapperType != null)
        {
            var allComponents = Resources.FindObjectsOfTypeAll(_languageSpriteSwapperType);
            int injected = 0;
            
            foreach (var comp in allComponents)
            {
                var swapper = comp as MonoBehaviour;
                if (swapper != null && swapper.gameObject.scene.isLoaded)
                {
                    InjectTurkishSprite(swapper);
                    
                    // Trigger UpdateSprite
                    if (_updateSpriteMethod != null)
                    {
                        try
                        {
                            string currentLang = Localization.language;
                            _updateSpriteMethod.Invoke(swapper, new object[] { currentLang });
                        }
                        catch { }
                    }
                    injected++;
                }
            }
            
            Logger.LogInfo($"✓ Injected {injected} LanguageSpriteSwappers");
        }

        // 2) Manually replace sprites not handled by swappers
        ReplaceManualSprites();

        _hasAppliedThisScene = true;
        _delayedApplyCo = null;
    }

    // ============ LanguageSpriteSwapper Injection ============

    private void InjectTurkishSprite(MonoBehaviour swapper)
    {
        if (swapper == null)
            return;

        int instanceId = swapper.GetInstanceID();
        if (_injectedSwappers.Contains(instanceId))
            return;

        try
        {
            var spritesDict = _swapperSpritesField.GetValue(swapper) as Dictionary<string, Sprite>;
            var spriteRenderer = _swapperRendererField.GetValue(swapper) as SpriteRenderer;

            if (spritesDict == null || spriteRenderer == null)
                return;

            var currentSprite = spriteRenderer.sprite;
            if (currentSprite == null)
                return;

            string spriteName = currentSprite.name;
            
            Sprite turkishSprite = GetOrCreateTurkishSprite(spriteName, currentSprite);
            
            if (turkishSprite != null)
            {
                string[] turkishKeys = { "tr", "turkish", "Turkish", "türkçe", "Türkçe", "turkce", "Turkce" };
                
                bool injected = false;
                foreach (var key in turkishKeys)
                {
                    if (!spritesDict.ContainsKey(key))
                    {
                        spritesDict[key] = turkishSprite;
                        injected = true;
                    }
                }

                if (injected)
                {
                    _injectedSwappers.Add(instanceId);
                    Logger.LogInfo($"✓ Injected: {spriteName} (on {swapper.gameObject.name})");
                }
            }
        }
        catch (Exception e)
        {
            Logger.LogError($"Sprite injection error: {e}");
        }
    }

    // ============ Manual Sprite Replacement ============

    private void ReplaceManualSprites()
    {
        Logger.LogInfo("Replacing manual sprites...");

        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite == null || !_manualSpriteNames.Contains(sprite.name))
                continue;

            if (_manualSpritesReplaced.Contains(sprite.name))
                continue;

            var tex = sprite.texture;
            string textureFolder = Path.Combine(Paths.GameRootPath, "assets", "Texture");
            
            // Try both sprite name and texture name
            string replacementPath = Path.Combine(textureFolder, sprite.name + ".png");
            if (!File.Exists(replacementPath))
                replacementPath = Path.Combine(textureFolder, tex.name + ".png");

            if (File.Exists(replacementPath))
            {
                try
                {
                    byte[] pngData = File.ReadAllBytes(replacementPath);
                    Texture2D newTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    newTex.LoadImage(pngData);
                    newTex.filterMode = FilterMode.Point;

                    Sprite newSprite = Sprite.Create(
                        newTex,
                        new Rect(0, 0, newTex.width, newTex.height),
                        new Vector2(
                            sprite.pivot.x / sprite.rect.width,
                            sprite.pivot.y / sprite.rect.height
                        ),
                        sprite.pixelsPerUnit,
                        0,
                        SpriteMeshType.Tight,
                        sprite.border
                    );
                    newSprite.name = sprite.name;

                    // Replace in all SpriteRenderers
                    foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
                    {
                        if (sr != null && sr.sprite == sprite && sr.gameObject.scene.isLoaded)
                        {
                            sr.sprite = newSprite;
                        }
                    }

                    // Replace in all UI Images
                    foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
                    {
                        if (img != null && img.sprite == sprite && img.gameObject.scene.isLoaded)
                        {
                            img.sprite = newSprite;
                            
                            // Special handling for cleanAppleBig
                            if (sprite.name == "cleanAppleBig")
                            {
                                img.rectTransform.sizeDelta = new Vector2(400f, 400f);
                                img.rectTransform.anchoredPosition = new Vector2(0f, -260f);
                                AdjustLogoTMPPosition();
                            }
                        }
                    }

                    _manualSpritesReplaced.Add(sprite.name);
                    Logger.LogInfo($"✓ Manually replaced: {sprite.name}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to replace sprite {sprite.name}: {ex}");
                }
            }
        }

        // Special case: cleanAppleBig - always try to replace even if not found in Resources
        if (!_manualSpritesReplaced.Contains("cleanAppleBig"))
        {
            string cleanAppleBigPath = Path.Combine(Paths.GameRootPath, "assets", "Texture", "cleanAppleBig.png");
            if (File.Exists(cleanAppleBigPath))
            {
                ReplaceCleanAppleBigFromFile(cleanAppleBigPath);
            }
            else
            {
                StartCoroutine(DownloadAndReplaceCleanAppleBig(
                    "https://raw.githubusercontent.com/SkipperSkipTR/Kindergarten3-TR/refs/heads/main/MasterDatabase/Texture/cleanAppleBig.png"
                ));
            }
        }
    }

    private void ReplaceCleanAppleBigFromFile(string filePath)
    {
        try
        {
            byte[] pngData = File.ReadAllBytes(filePath);
            Texture2D newTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            newTex.LoadImage(pngData);
            newTex.filterMode = FilterMode.Point;

            Sprite newSprite = Sprite.Create(
                newTex,
                new Rect(0, 0, newTex.width, newTex.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.Tight
            );
            newSprite.name = "cleanAppleBig";

            bool replaced = false;
            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img != null && img.sprite != null && img.sprite.name == "cleanAppleBig" && img.gameObject.scene.isLoaded)
                {
                    img.sprite = newSprite;
                    img.rectTransform.sizeDelta = new Vector2(400f, 400f);
                    img.rectTransform.anchoredPosition = new Vector2(0f, -260f);
                    AdjustLogoTMPPosition();
                    replaced = true;
                }
            }

            if (replaced)
            {
                _manualSpritesReplaced.Add("cleanAppleBig");
                Logger.LogInfo("✓ Replaced cleanAppleBig from local file");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to load cleanAppleBig from file: {ex}");
        }
    }

    private IEnumerator DownloadAndReplaceCleanAppleBig(string imageUrl)
    {
        using (UnityWebRequest uwr = UnityWebRequestTexture.GetTexture(imageUrl))
        {
            yield return uwr.SendWebRequest();

            if (uwr.result != UnityWebRequest.Result.Success)
            {
                Logger.LogError($"Failed to download cleanAppleBig: {uwr.error}");
                yield break;
            }

            Texture2D newTex = DownloadHandlerTexture.GetContent(uwr);
            newTex.filterMode = FilterMode.Point;

            Sprite newSprite = Sprite.Create(
                newTex,
                new Rect(0, 0, newTex.width, newTex.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.Tight
            );
            newSprite.name = "cleanAppleBig";

            bool replaced = false;
            foreach (var img in Resources.FindObjectsOfTypeAll<Image>())
            {
                if (img != null && img.sprite != null && img.sprite.name == "cleanAppleBig" && img.gameObject.scene.isLoaded)
                {
                    img.sprite = newSprite;
                    img.rectTransform.sizeDelta = new Vector2(400f, 400f);
                    img.rectTransform.anchoredPosition = new Vector2(0f, -260f);
                    AdjustLogoTMPPosition();
                    replaced = true;
                }
            }

            if (replaced)
            {
                _manualSpritesReplaced.Add("cleanAppleBig");
                Logger.LogInfo("✓ Downloaded and replaced cleanAppleBig");
            }
        }
    }

    private void AdjustLogoTMPPosition()
    {
        try
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go.name == "KG3Logo" && go.scene.isLoaded)
                {
                    int logoInstanceId = go.GetInstanceID();
                    
                    // Check if we've already adjusted this specific logo instance
                    if (_adjustedLogoInstances.Contains(logoInstanceId))
                    {
                        Logger.LogInfo($"Logo already adjusted (ID: {logoInstanceId}), skipping");
                        continue;
                    }

                    var textComponents = go.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var tmp in textComponents)
                    {
                        var rt = tmp.GetComponent<RectTransform>();
                        if (rt != null)
                        {
                            rt.anchoredPosition += new Vector2(0, 45f);
                            Logger.LogInfo($"Adjusted KG3Logo TMP position (ID: {logoInstanceId})");
                        }
                    }
                    
                    _adjustedLogoInstances.Add(logoInstanceId);
                    break; // Only adjust first found logo
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error adjusting logo position: {ex}");
        }
    }

    // ============ Sprite Creation Helpers ============

    private Sprite GetOrCreateTurkishSprite(string spriteName, Sprite originalSprite)
    {
        if (_turkishSprites.TryGetValue(spriteName, out var cached))
            return cached;

        Texture2D repTex = null;
        if (_turkishTextures.TryGetValue(spriteName, out repTex) ||
            _turkishTextures.TryGetValue(originalSprite.texture.name, out repTex))
        {
            var newSprite = Sprite.Create(
                repTex,
                originalSprite.rect,
                new Vector2(originalSprite.pivot.x / originalSprite.rect.width, 
                           originalSprite.pivot.y / originalSprite.rect.height),
                originalSprite.pixelsPerUnit,
                0,
                SpriteMeshType.Tight,
                originalSprite.border
            );
            newSprite.name = spriteName;
            
            _turkishSprites[spriteName] = newSprite;
            return newSprite;
        }

        return null;
    }

    // ============ Language Helpers ============
    
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
        try { if (IsTurkishKey(Localization.language)) return true; }
        catch { }
        return IsTurkishKey(PlayerPrefs.GetString("Lang", "None"));
    }

    // ============ Texture Loading ============
    
    private Dictionary<string, Texture2D> LoadReplacementTextures()
    {
        string textureFolder = Path.Combine(Paths.GameRootPath, "assets", "Texture");
        var replacements = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        if (!Directory.Exists(textureFolder))
        {
            Logger.LogWarning($"Texture folder not found: {textureFolder}");
            return replacements;
        }

        foreach (var file in Directory.GetFiles(textureFolder, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            try
            {
                var data = File.ReadAllBytes(file);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                tex.LoadImage(data);
                tex.filterMode = FilterMode.Point;
                tex.name = name;
                replacements[name] = tex;
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed loading {file}: {ex.Message}");
            }
        }

        return replacements;
    }
}