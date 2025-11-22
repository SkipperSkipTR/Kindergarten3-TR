using BepInEx;
using BepInEx.Unity.Mono;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;
using TMPro;
using UnityEngine.EventSystems;
using System.Reflection;
using BepInEx.Logging;

namespace LanguageButton;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class LanguageButtonInjector : BaseUnityPlugin
{
    private void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "TitleScreen") return;

        StartCoroutine(InjectButtonRoutine());
    }

    private IEnumerator InjectButtonRoutine()
    {
        // wait a frame so UI gets built
        yield return null;

        var langPanelGO = FindInactive("LanguagePanel");
        if (langPanelGO == null)
        {
            Logger.LogError("LanguagePanel not found.");
            yield break;
        }

        // find a template button to clone
        var template = FindChildDeep(langPanelGO.transform, "EnglishButton");
        if (template == null)
        {
            Logger.LogError("EnglishButton not found under LanguagePanel.");
            yield break;
        }

        // avoid double-inject
        if (FindChildDeep(langPanelGO.transform, "TurkishButton") != null)
        {
            Logger.LogInfo("TurkishButton already exists, skipping.");
            yield break;
        }

        // clone
        var newBtnGO = Instantiate(template.gameObject, template.parent);
        newBtnGO.name = "TurkishButton";

        // position: push down a bit relative to template
        var rt = newBtnGO.GetComponent<RectTransform>();
        var templateRT = template.GetComponent<RectTransform>();
        rt.anchoredPosition = templateRT.anchoredPosition + new Vector2(0f, -80f);

        // label
        var tmp = newBtnGO.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
            tmp.text = "Türkçe";

        SetButtonBackgroundColor(newBtnGO, new Color(0.7f, 0.6f, 0.9f, 0.5f));
        // Wire click on the actual raycast target (often ButtonColor)
        var clickTarget = newBtnGO;
        WireClickViaEventTrigger(clickTarget, "tr");

        Logger.LogInfo("Injected Turkish button.");
    }

    private void WireClickViaEventTrigger(GameObject targetGO, string langKey)
    {
        var trigger = targetGO.GetComponent<EventTrigger>();
        if (trigger == null)
            trigger = targetGO.GetComponentInChildren<EventTrigger>(true);

        if (trigger == null)
        {
            Logger.LogWarning("No EventTrigger found, adding one.");
            trigger = targetGO.AddComponent<EventTrigger>();
            trigger.triggers = new System.Collections.Generic.List<EventTrigger.Entry>();
        }

        // Remove old click-like events that might still set English
        trigger.triggers.Clear();

        void Add(EventTriggerType type)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(_ =>
            {
                var controller = FindObjectOfType<TitleScreenController>();
                controller?.SetLanguage(langKey);
            });
            trigger.triggers.Add(entry);
        }

        // Mouse/touch click
        Add(EventTriggerType.PointerClick);

        // Keyboard/gamepad confirm (Enter/Space/A)
        Add(EventTriggerType.Submit);
    }

    void SetButtonBackgroundColor(GameObject btnGO, Color color)
    {
        var col = FindChildDeep(btnGO.transform, "ButtonColor");

        Image img = null;

        if (col != null) img = col.GetComponent<Image>();

        if (img != null)
            img.color = color;
        else
            Logger.LogWarning("No Image found to tint.");
    }

    // Finds inactive objects too
    private GameObject FindInactive(string name)
    {
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var g in all)
            if (g.name == name) return g;
        return null;
    }

    // Recursively find child by name in any depth
    private Transform FindChildDeep(Transform root, string name)
    {
        foreach (Transform child in root)
        {
            if (child.name == name) return child;
            var found = FindChildDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }
}
