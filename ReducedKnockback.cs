using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using AnimationState = ItemDrop.ItemData.AnimationState;
using ItemType = ItemDrop.ItemData.ItemType;
using SharedData = ItemDrop.ItemData.SharedData;

namespace ReducedKnockback
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    public class ReducedKnockbackPlugin : BaseUnityPlugin
    {
        public const string PluginGUID = "com.jerem.reducedknockback";
        public const string PluginName = "Reduced Knockback";
        public const string PluginVersion = "1.1.0";

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

        // Two-handed item types that are ranged/magic/utility rather than melee weapons.
        private static readonly AnimationState[] ExcludedStates =
        {
            AnimationState.Unarmed,
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

        internal static float PrimaryMult => _refPrimaryMult;
        internal static float SecondaryMult => _refPrimaryMult * ReducedKnockbackPlugin.SecondaryMultiplier.Value;

        internal static float TargetPushForce(bool secondary) => _refForce * (secondary ? SecondaryMult : PrimaryMult);

        internal static bool IsTwoHandedMelee(SharedData shared)
        {
            if (shared == null)
                return false;

            if (shared.m_itemType != ItemType.TwoHandedWeapon && shared.m_itemType != ItemType.TwoHandedWeaponLeft)
                return false;

            // Fist weapons are classed as two-handed but are intentionally left alone.
            if (shared.m_skillType == Skills.SkillType.Unarmed)
                return false;

            foreach (var s in ExcludedStates)
                if (shared.m_animationState == s)
                    return false;
            return true;
        }

        private static SharedData GetShared(GameObject prefab)
        {
            var drop = prefab.GetComponent<ItemDrop>();
            return drop == null ? null : drop.m_itemData?.m_shared;
        }

        private static bool TryResolveReference(ObjectDB db)
        {
            string refName = ReducedKnockbackPlugin.ReferenceWeapon.Value?.Trim();
            if (db == null || db.m_items == null || string.IsNullOrEmpty(refName))
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

        internal static void EnsureReference(ObjectDB db)
        {
            if (_referenceFound)
                return;

            if (TryResolveReference(db))
            {
                _referenceFound = true;
                return;
            }

            if (!_fallbackWarned && db != null && db.m_items != null && db.m_items.Count > 0)
            {
                _fallbackWarned = true;
                ReducedKnockbackPlugin.Log.LogWarning(
                    $"Reference weapon '{ReducedKnockbackPlugin.ReferenceWeapon.Value}' not found yet, " +
                    $"using fallback force {FallbackForce}, multiplier {FallbackMult} until it is.");
            }
        }

        // Writes the normalized values into the item's shared data. Returns false if the
        // primary and secondary attacks are the same object (secondary multiplier skipped).
        internal static bool Normalize(SharedData shared)
        {
            shared.m_attackForce = _refForce;

            if (shared.m_attack != null)
                shared.m_attack.m_forceMultiplier = PrimaryMult;

            if (shared.m_secondaryAttack == null)
                return true;
            if (shared.m_secondaryAttack == shared.m_attack)
                return false;

            shared.m_secondaryAttack.m_forceMultiplier = SecondaryMult;
            return true;
        }

        internal static void Apply(ObjectDB db)
        {
            if (db == null || db.m_items == null)
                return;

            EnsureReference(db);
            int tuned = 0;

            foreach (GameObject prefab in db.m_items)
            {
                if (prefab == null)
                    continue;

                var shared = GetShared(prefab);
                if (!IsTwoHandedMelee(shared))
                    continue;

                if (!Normalize(shared))
                    ReducedKnockbackPlugin.Log.LogWarning(
                        $"{prefab.name}: primary and secondary attacks share one object; secondary multiplier not applied.");

                tuned++;
            }

            if (tuned == _lastTunedCount)
                return;

            _lastTunedCount = tuned;
            ReducedKnockbackPlugin.Log.LogInfo(
                $"Normalized knockback on {tuned} two-handed weapon(s): " +
                $"force {_refForce}, primary mult {PrimaryMult}, secondary mult {SecondaryMult}.");
        }
    }

    // Runs after other mods (Jotunn, Therzie, etc.) register their items. Mainly keeps
    // tooltips accurate; the per-attack patch below is what guarantees the values.
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    [HarmonyPriority(Priority.Last)]
    internal static class ObjectDBAwakePatch
    {
        private static void Postfix(ObjectDB __instance)
        {
            KnockbackTuner.Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    [HarmonyPriority(Priority.Last)]
    internal static class ZNetSceneAwakePatch
    {
        private static void Postfix()
        {
            KnockbackTuner.Apply(ObjectDB.instance);
        }
    }

    // Enforces the values once per attack, so mods that rewrite weapon stats later (e.g. Valheim
    // Armory re-applying its config on sync/reload) can't undo them. Only the per-attack clone is
    // touched; the weapon's shared data is left to whichever mod owns it.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    [HarmonyPriority(Priority.Last)]
    internal static class StartAttackPatch
    {
        private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack =
            AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");

        private static void Postfix(Humanoid __instance, bool secondaryAttack, bool __result)
        {
            if (!__result || !(__instance is Player))
                return;

            var shared = __instance.GetCurrentWeapon()?.m_shared;
            if (!KnockbackTuner.IsTwoHandedMelee(shared) || shared.m_attackForce <= 0f)
                return;

            var attack = CurrentAttack(__instance);
            if (attack == null)
                return;

            // Push force = weapon m_attackForce * attack m_forceMultiplier, so scale the multiplier
            // to hit the target regardless of what the weapon's base knockback currently is.
            KnockbackTuner.EnsureReference(ObjectDB.instance);
            attack.m_forceMultiplier = KnockbackTuner.TargetPushForce(secondaryAttack) / shared.m_attackForce;
        }
    }

    // Effects a weapon spawns on hit/trigger without hit data (e.g. lightning AoEs) use their
    // own prefab knockback. Cap it so the extra effect can't out-push the weapon itself.
    [HarmonyPatch]
    internal static class SpawnedEffectPatch
    {
        private static readonly AccessTools.FieldRef<Humanoid, bool> IsSecondary =
            AccessTools.FieldRefAccess<Humanoid, bool>("m_currentAttackIsSecondary");

        private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(Aoe), nameof(Aoe.Setup));
            yield return AccessTools.Method(typeof(Projectile), nameof(Projectile.Setup));
        }

        private static void Postfix(object __instance, Character owner, HitData hitData, ItemDrop.ItemData item)
        {
            if (hitData != null || item == null || !(owner is Player player))
                return;
            if (!KnockbackTuner.IsTwoHandedMelee(item.m_shared))
                return;

            float cap = KnockbackTuner.TargetPushForce(IsSecondary(player));
            switch (__instance)
            {
                case Aoe aoe when aoe.m_attackForce > cap:
                    aoe.m_attackForce = cap;
                    break;
                case Projectile projectile when projectile.m_attackForce > cap:
                    projectile.m_attackForce = cap;
                    break;
            }
        }
    }
}
