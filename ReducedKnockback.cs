using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using AnimationState = ItemDrop.ItemData.AnimationState;
using ItemType = ItemDrop.ItemData.ItemType;

namespace ReducedKnockback
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ReducedKnockbackPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jerem.reducedknockback";
        public const string PluginName = "Reduced Knockback";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<float> SecondaryMultiplier;
        internal static ConfigEntry<string> ReferenceWeapon;

        private void Awake()
        {
            Log = Logger;

            SecondaryMultiplier = Config.Bind(
                "Knockback",
                "SecondaryAttackMultiplier",
                1.2f,
                new ConfigDescription(
                    "Knockback multiplier for two-handed weapons' secondary attacks, " +
                    "applied on top of the normalized (one-handed mace) knockback value.",
                    new AcceptableValueRange<float>(0f, 5f)));

            ReferenceWeapon = Config.Bind(
                "Knockback",
                "ReferenceWeaponPrefab",
                "MaceIron",
                "Prefab name of the one-handed weapon whose knockback two-handed " +
                "weapons should be normalized to.");

            new Harmony(PluginGUID).PatchAll();
            Log.LogInfo($"{PluginName} loaded.");
        }
    }

    internal static class KnockbackTuner
    {
        private const float FallbackForce = 30f;
        private const float FallbackMult = 1f;

        // Two-handed item types that are not melee weapons.
        private static readonly AnimationState[] ExcludedStates =
        {
            AnimationState.Bow,
            AnimationState.Crossbow,
            AnimationState.Staves,
            AnimationState.MagicItem,
            AnimationState.FishingRod,
            AnimationState.Torch,
            AnimationState.LeftTorch,
            AnimationState.Feaster,
        };

        private static bool _referenceFound;
        private static bool _fallbackWarned;
        private static float _refForce = FallbackForce;
        private static float _refPrimaryMult = FallbackMult;
        private static int _lastTunedCount = -1;

        private static bool IsTwoHandedMelee(ItemDrop.ItemData.SharedData shared)
        {
            if (shared.m_itemType != ItemType.TwoHandedWeapon && shared.m_itemType != ItemType.TwoHandedWeaponLeft)
                return false;

            // Pickaxes are two-handed but are tools, not combat weapons.
            if (shared.m_skillType == Skills.SkillType.Pickaxes)
                return false;

            foreach (var s in ExcludedStates)
                if (shared.m_animationState == s)
                    return false;
            return true;
        }

        private static ItemDrop.ItemData.SharedData GetShared(GameObject prefab)
        {
            var drop = prefab.GetComponent<ItemDrop>();
            return drop == null ? null : drop.m_itemData?.m_shared;
        }

        private static bool TryResolveReference(ObjectDB db)
        {
            string refName = ReducedKnockbackPlugin.ReferenceWeapon.Value?.Trim();
            if (string.IsNullOrEmpty(refName))
                return false;

            foreach (GameObject prefab in db.m_items)
            {
                if (prefab == null || prefab.name != refName)
                    continue;

                var shared = GetShared(prefab);
                if (shared?.m_attack == null)
                    return false;

                _refForce = shared.m_attackForce;
                _refPrimaryMult = shared.m_attack.m_forceMultiplier;
                return true;
            }
            return false;
        }

        internal static void Apply(ObjectDB db)
        {
            if (db == null || db.m_items == null)
                return;

            if (TryResolveReference(db))
                _referenceFound = true;

            if (!_referenceFound && !_fallbackWarned)
            {
                _fallbackWarned = true;
                ReducedKnockbackPlugin.Log.LogWarning(
                    $"Reference weapon '{ReducedKnockbackPlugin.ReferenceWeapon.Value}' not found, " +
                    $"using fallback force {FallbackForce}, multiplier {FallbackMult}.");
            }

            float secondaryMult = _refPrimaryMult * ReducedKnockbackPlugin.SecondaryMultiplier.Value;
            int tuned = 0;

            foreach (GameObject prefab in db.m_items)
            {
                if (prefab == null)
                    continue;

                var shared = GetShared(prefab);
                if (shared == null || !IsTwoHandedMelee(shared))
                    continue;

                shared.m_attackForce = _refForce;

                if (shared.m_attack != null)
                    shared.m_attack.m_forceMultiplier = _refPrimaryMult;

                if (shared.m_secondaryAttack != null && shared.m_secondaryAttack != shared.m_attack)
                    shared.m_secondaryAttack.m_forceMultiplier = secondaryMult;
                else if (shared.m_secondaryAttack != null)
                    ReducedKnockbackPlugin.Log.LogWarning(
                        $"{prefab.name}: primary and secondary attacks share one object; secondary multiplier not applied.");

                tuned++;
            }

            if (tuned == _lastTunedCount)
                return;

            _lastTunedCount = tuned;
            ReducedKnockbackPlugin.Log.LogInfo(
                $"Normalized knockback on {tuned} two-handed weapon(s): " +
                $"force {_refForce}, primary mult {_refPrimaryMult}, secondary mult {secondaryMult}.");
        }
    }

    // Runs after other mods (Jotunn, Therzie, etc.) register their items.
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    [HarmonyPriority(Priority.Last)]
    internal static class ObjectDBAwakePatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            KnockbackTuner.Apply(__instance);
        }
    }

    // Safety net: catches anything added after ObjectDB.Awake but before world load.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    [HarmonyPriority(Priority.Last)]
    internal static class ZNetSceneAwakePatch
    {
        private static void Postfix()
        {
            KnockbackTuner.Apply(ObjectDB.instance);
        }
    }
}
