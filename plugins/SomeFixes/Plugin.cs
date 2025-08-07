using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using System.Globalization;

namespace SomeFixes;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private void Awake()
    {
        var harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);
        harmony.PatchAll();
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    #region Fixes
    // Fix For Linda Growl Taking Long Time to Finish
    [HarmonyPatch(typeof(NPCBehavior), "DelayStartInteract", new Type[] { typeof(string) })]
    public class NPCBehavior_DelayStartInteract_Patch
    {
        [HarmonyPrefix]
        public static bool Prefix(NPCBehavior __instance, string __0)
        {
            __instance.DelayStartInteract(float.Parse(__0, CultureInfo.InvariantCulture)); // Add InvariantCulture to parse the string correctly
            return false; // Skip original method
        }
    }
    #endregion
}
