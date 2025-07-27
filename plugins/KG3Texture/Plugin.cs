using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using System;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using TMPro;
using UnityEngine.Networking;
using System.Collections;

namespace KG3Texture;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private void Start()
    {
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Logger.LogInfo($"Scene loaded: {scene.name}");
        ReplaceAllTextures();
        ReplaceAllSprites();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void ReplaceAllTextures()
    {
        string textureFolder = Path.Combine(Paths.GameRootPath, "assets", "Texture");

        foreach (var tex in Resources.FindObjectsOfTypeAll<Texture2D>())
        {
            string replacementPath = Path.Combine(textureFolder, tex.name + ".png");
            if (File.Exists(replacementPath))
            {
                try
                {
                    byte[] pngData = File.ReadAllBytes(replacementPath);
                    tex.LoadImage(pngData); // Overwrites the texture in memory
                    Logger.LogInfo($"Replaced texture at runtime: {tex.name}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to replace texture {tex.name}: {ex}");
                }
            }
        }
    }

    private void ReplaceAllSprites()
    {
        var targetSprites = new HashSet<string> { "NurseSign" };

        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (!targetSprites.Contains(sprite.name))
                continue;

            var tex = sprite.texture;
            string textureFolder = Path.Combine(Paths.GameRootPath, "assets", "Texture");
            string replacementPath = Path.Combine(textureFolder, tex.name + ".png");

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

                    foreach (var sr in Resources.FindObjectsOfTypeAll<SpriteRenderer>())
                    {
                        if (sr.sprite == sprite)
                            sr.sprite = newSprite;
                    }

                    foreach (var img in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>())
                    {
                        if (img.sprite == sprite)
                            img.sprite = newSprite;
                    }

                    Logger.LogInfo($"Replaced sprite at runtime: {sprite.name}");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to replace sprite {sprite.name}: {ex}");
                }
            }
        }

        // Check for cleanAppleBig locally first
        string cleanAppleBigPath = Path.Combine(Paths.GameRootPath, "assets", "Texture", "cleanAppleBig.png");
        if (File.Exists(cleanAppleBigPath))
        {
            try
            {
                byte[] pngData = File.ReadAllBytes(cleanAppleBigPath);
                Texture2D newTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                newTex.LoadImage(pngData);
                newTex.filterMode = FilterMode.Point;

                Sprite newSprite = Sprite.Create(
                    newTex,
                    new Rect(0, 0, newTex.width, newTex.height),
                    new Vector2(0.5f, 0.5f), // center pivot
                    100f,
                    0,
                    SpriteMeshType.Tight
                );

                foreach (var img in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>())
                {
                    if (img.sprite != null && img.sprite.name == "cleanAppleBig")
                    {
                        img.sprite = newSprite;
                        img.rectTransform.sizeDelta = new Vector2(400f, 400f);
                        img.rectTransform.anchoredPosition = new Vector2(0f, -260f);
                        AdjustLogoTMPPosition();
                    }
                }

            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to load local cleanAppleBig: {ex}");
            }
        }
        else
        {
            StartCoroutine(DownloadAndReplaceCleanAppleBig("https://raw.githubusercontent.com/SkipperSkipTR/Kindergarten3-TR/refs/heads/main/MasterDatabase/Texture/cleanAppleBig.png"));
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
                new Vector2(0.5f, 0.5f), // center pivot
                100f,
                0,
                SpriteMeshType.Tight
            );

            foreach (var img in Resources.FindObjectsOfTypeAll<UnityEngine.UI.Image>())
            {
                if (img.sprite != null && img.sprite.name == "cleanAppleBig")
                {
                    img.sprite = newSprite;
                    img.rectTransform.sizeDelta = new Vector2(400f, 400f);
                    img.rectTransform.anchoredPosition = new Vector2(0f, -260f);
                    AdjustLogoTMPPosition();
                }
            }
        }
    }

    private void AdjustLogoTMPPosition()
    {
        try
        {
            GameObject[] allGameObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (var go in allGameObjects)
            {
                if (go.name == "KG3Logo")
                {
                    Logger.LogInfo("Found KG3Logo GameObject.");

                    // Search for TMP component within its children
                    TextMeshProUGUI[] textComponents = go.GetComponentsInChildren<TextMeshProUGUI>(true);
                    foreach (var tmp in textComponents)
                    {
                        var rt = tmp.GetComponent<RectTransform>();
                        if (rt != null)
                        {
                            Logger.LogInfo($"Original TMP position: {rt.anchoredPosition}");

                            // Move TMP text up by 45 units
                            rt.anchoredPosition += new Vector2(0, 45f);

                            Logger.LogInfo("Moved KG3Logo's TMP text upward by 45 units.");
                        }
                        else
                        {
                            Logger.LogWarning("TMP component found without a RectTransform.");
                        }
                    }

                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError($"Error adjusting TMP text position: {ex}");
        }
    }
}