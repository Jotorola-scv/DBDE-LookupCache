// DBDE Lookup Cache - https://github.com/Jotorola-scv/DBDE-LookupCache (MIT)
// Code written by Claude (Anthropic's AI assistant). Problem report, direction and in-game testing by Jotorola-scv.
// Performance fix for DynamicBoneDistributionEditor (org.njaecha.plugins.dbde) 1.5.1, with experimental 2.0.0 support.
// Only activates on DBDE versions whose code was checked (see TestedVersions / ExperimentalVersions); other versions are
// left untouched.
// DBDECharaController.Update calls UpdateActiveStack on every edit of the current outfit every frame, which resolves the
// edit's dynamic bones through WouldYouBeSoKindTohandMeTheDynamicBonePlease. The garbage this creates every frame
// drives the periodic ~0.4 s stop-the-world Mono GC pauses in the maker:
//  - misses are not cached: an edit whose bones are not on the character re-runs GetComponentsInChildren<DynamicBone>
//    over the whole character plus LINQ every frame -> misses are cached here for a short time per controller;
//  - hits are validated with List.Any(lambda) (enumerator allocation per call) -> the same check without allocation;
//  - UpdateActiveStack uses FindAll(...).Count() (new list per edit per frame) -> same logic without allocation.
// Results are unchanged; the miss cache is dropped whenever DBDE itself would refresh (clothes/accessory/coordinate
// changes, bone list refresh, reload), and a newly spawned bone is found at most "Miss cache seconds" later.
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using DynamicBoneDistributionEditor;
using HarmonyLib;
using UnityEngine;

[assembly: AssemblyTitle("KK_DBDELookupCache")]
[assembly: AssemblyVersion("1.2.0.0")]

namespace DBDELookupCache
{
    [BepInPlugin(GUID, "DBDE Lookup Cache", Version)]
    [BepInDependency(DBDEGuid)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "jotorola.dbde.lookupcache";
        public const string Version = "1.2.0";
        private const string DBDEGuid = "org.njaecha.plugins.dbde";

        // DBDE versions whose WouldYouBeSoKindTohandMeTheDynamicBonePlease / UpdateActiveStack / cache validation were
        // checked to match the logic re-implemented below.
        // Tested in game: 1.5.1 (the version shipped by current HF Patch / BetterRepack).
        // Experimental: 2.0.0 - same logic and member names by source comparison, not tested in game; runs with a warning.
        private static readonly string[] TestedVersions = { "1.5.1" };
        private static readonly string[] ExperimentalVersions = { "2.0.0" };

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> MissSeconds;
        internal static ConfigEntry<bool> IgnoreVersionCheck;

        // controller -> slot -> identification name -> miss valid until (realtime)
        private static readonly Dictionary<object, Dictionary<int, Dictionary<string, float>>> Misses =
            new Dictionary<object, Dictionary<int, Dictionary<string, float>>>();

        private static FieldInfo _cacheField;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true, "Allocation-free DBDE bone lookups (cached misses, no LINQ on hits, no per-frame lists). Turn off to get DBDE's original behaviour.");
            MissSeconds = Config.Bind("General", "Miss cache seconds", 1f, "How long a failed lookup is remembered (cleared early on any clothes/accessory/coordinate change).");
            IgnoreVersionCheck = Config.Bind("Advanced", "Ignore DBDE version check", false, "Also activate on DBDE versions that were not checked (tested: " + string.Join(", ", TestedVersions) + "; experimental: " + string.Join(", ", ExperimentalVersions) + "). A newer DBDE may have changed the logic this plugin re-implements - use at your own risk.");

            string dbdeVersion = "unknown";
            BepInEx.PluginInfo dbde;
            if (BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(DBDEGuid, out dbde) && dbde != null && dbde.Metadata != null)
                dbdeVersion = dbde.Metadata.Version.ToString();
            if (Array.IndexOf(ExperimentalVersions, dbdeVersion) >= 0)
            {
                Log.LogWarning("DBDE " + dbdeVersion + " support is EXPERIMENTAL (checked against the source code, not tested in game). If you notice dynamic bone problems, set Enabled = false and please report it.");
            }
            else if (Array.IndexOf(TestedVersions, dbdeVersion) < 0)
            {
                if (!IgnoreVersionCheck.Value)
                {
                    Log.LogWarning("DBDE " + dbdeVersion + " is not a checked version (tested: " + string.Join(", ", TestedVersions) + "; experimental: " + string.Join(", ", ExperimentalVersions) + ") - doing nothing. Check for an update of DBDE Lookup Cache, or set [Advanced] Ignore DBDE version check.");
                    return;
                }
                Log.LogWarning("DBDE " + dbdeVersion + " is not a checked version - running anyway because 'Ignore DBDE version check' is on.");
            }

            var ctrl = AccessTools.TypeByName("DynamicBoneDistributionEditor.DBDECharaController");
            var lookup = ctrl == null ? null : AccessTools.Method(ctrl, "WouldYouBeSoKindTohandMeTheDynamicBonePlease");
            _cacheField = ctrl == null ? null : AccessTools.Field(ctrl, "_dynamicBoneCache");
            var stack = AccessTools.Method(typeof(DBDEDynamicBoneEdit), "UpdateActiveStack");
            if (lookup == null || _cacheField == null || stack == null)
            {
                Log.LogWarning("DBDE members not found (unsupported DBDE version) - doing nothing.");
                return;
            }
            var h = new Harmony(GUID);
            h.Patch(lookup, prefix: new HarmonyMethod(typeof(Plugin), nameof(LookupPrefix)), postfix: new HarmonyMethod(typeof(Plugin), nameof(LookupPostfix)));
            h.Patch(stack, prefix: new HarmonyMethod(typeof(Plugin), nameof(UpdateActiveStackPrefix)));
            int n = 0;
            var names = new HashSet<string> { "ClothesChanged", "CoordinateChangeEvent", "AccessoryChangedEvent", "AccessoryTransferredEvent",
                                              "AccessoryCopiedEvent", "RefreshBoneList", "OnReload", "OnCoordinateBeingLoaded", "ReloadCoordianteData" };
            foreach (var m in ctrl.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!names.Contains(m.Name) || m.IsAbstract)
                    continue;
                h.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(ClearPrefix)));
                n++;
            }
            Log.LogInfo("DBDE " + dbdeVersion + ": patched lookup, UpdateActiveStack and " + n + " invalidation points");
        }

        private static string Trim(string name)
        {
            return name != null && name.StartsWith("/") ? name.Substring(1) : name;
        }

        private static bool LookupPrefix(object __instance, string identificationName, int? slot, ref List<DynamicBone> __result)
        {
            if (!Enabled.Value)
                return true;
            string name = Trim(identificationName);
            int s = slot.HasValue ? slot.Value : -1;

            // DBDE's own cache hit, validated like the original but without LINQ.
            var cache = _cacheField.GetValue(__instance) as Dictionary<int, Dictionary<string, List<DynamicBone>>>;
            Dictionary<string, List<DynamicBone>> inner;
            List<DynamicBone> list;
            if (cache != null && name != null && cache.TryGetValue(s, out inner) && inner.TryGetValue(name, out list) && list != null && list.Count > 0)
            {
                bool valid = true;
                for (int i = 0; i < list.Count; i++)
                {
                    var d = list[i];
                    if (!d || !d.m_Root)
                    {
                        valid = false;
                        break;
                    }
                }
                if (valid)
                {
                    __result = list;
                    return false;
                }
            }

            // Recent miss.
            Dictionary<int, Dictionary<string, float>> bySlot;
            Dictionary<string, float> byName;
            float until;
            if (name != null && Misses.TryGetValue(__instance, out bySlot) && bySlot.TryGetValue(s, out byName) &&
                byName.TryGetValue(name, out until) && Time.realtimeSinceStartup < until)
            {
                __result = null;
                return false;
            }
            return true;
        }

        private static void LookupPostfix(object __instance, string identificationName, int? slot, List<DynamicBone> __result)
        {
            if (!Enabled.Value || (__result != null && __result.Count > 0))
                return;
            string name = Trim(identificationName);
            if (name == null)
                return;
            int s = slot.HasValue ? slot.Value : -1;
            Dictionary<int, Dictionary<string, float>> bySlot;
            if (!Misses.TryGetValue(__instance, out bySlot))
                Misses[__instance] = bySlot = new Dictionary<int, Dictionary<string, float>>();
            Dictionary<string, float> byName;
            if (!bySlot.TryGetValue(s, out byName))
                bySlot[s] = byName = new Dictionary<string, float>();
            byName[name] = Time.realtimeSinceStartup + MissSeconds.Value;
        }

        // Same decisions as DBDEDynamicBoneEdit.UpdateActiveStack, without FindAll/Count allocations.
        private static bool UpdateActiveStackPrefix(DBDEDynamicBoneEdit __instance, bool always)
        {
            if (!Enabled.Value)
                return true;
            List<DynamicBone> bones = __instance.DynamicBones;
            if (bones == null || bones.Count == 0)
                return false;
            bool active = __instance.active;
            if (active)
            {
                int enabled = 0;
                for (int i = 0; i < bones.Count; i++)
                    if (bones[i] && bones[i].enabled)
                        enabled++;
                if (enabled == 1)
                    return false;
            }
            if (bones.Count <= 1 && !always)
                return false;
            for (int i = 0; i < bones.Count; i++)
                if (bones[i])
                    bones[i].enabled = false;
            if (active)
            {
                var primary = __instance.PrimaryDynamicBone;
                if (primary)
                    primary.enabled = true;
            }
            return false;
        }

        private static void ClearPrefix(object __instance)
        {
            Misses.Remove(__instance);
            List<object> dead = null;
            foreach (var k in Misses.Keys)
            {
                var uo = k as UnityEngine.Object;
                if (uo == null)
                    (dead ?? (dead = new List<object>())).Add(k);
            }
            if (dead != null)
                foreach (var k in dead)
                    Misses.Remove(k);
        }
    }
}
