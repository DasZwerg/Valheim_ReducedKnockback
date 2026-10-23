# Reduced Knockback

Two-handed weapons in Valheim knock smaller enemies so far away that your combo
whiffs after the first swing. This mod brings two-handed melee knockback down to
the level of the one-handed Iron Mace, so combos actually connect, and gives
secondary attacks a little extra push.

## Features

- **Normalized primary knockback:** all two-handed melee weapons (sledgehammers,
  greatswords, battleaxes, atgeirs, dual axes, scythes, pickaxes) use the Iron
  Mace's knockback, whatever their tier.
- **Stronger secondary attacks:** secondary attack knockback is 1.2x the
  normalized value (configurable).
- **Works with modded weapons:** weapons are detected by type, so two-handers
  from other mods (Therzie's Warfare/Armory/Monstrum, Valheim Armory, and
  others) are covered automatically.
- **Enforced on every swing:** values are re-applied each time you attack, so
  mods that change weapon stats after loading can't bring the old knockback back.
- **Spawned effects capped:** extra effects a two-handed weapon spawns (such as
  lightning bursts) can't push harder than the weapon itself.
- **Unaffected:** fist weapons, bows, crossbows, staves and magic items.

## Installation

Install with your mod manager (Gale, r2modman, Thunderstore Mod Manager), or
drop `ReducedKnockback.dll` into `BepInEx/plugins`.

## Configuration

The config file is created on first launch at
`BepInEx/config/com.jerem.reducedknockback.cfg`.

| Setting | Default | Description |
|---|---|---|
| `SecondaryAttackMultiplier` | `1.2` | Secondary attack knockback multiplier (0–5), applied on top of the normalized value. |
| `ReferenceWeaponPrefab` | `MaceIron` | Prefab whose knockback two-handed weapons are normalized to. |

Restart the game after changing settings.

## Compatibility

- No required mods besides BepInEx.
- Weapon mods don't need any patch: their two-handed weapons are tuned along
  with the vanilla ones.
- Weapon tooltips may still show another mod's knockback value if that mod
  rewrites it after loading; the value actually used in combat is this mod's.
- Only affects attacks made by players. Creature attacks are untouched.
- Knockback is calculated by the attacking player, so every player should run
  the mod for consistent behaviour in multiplayer.

## Building from source

```
dotnet build -c Release
```

Paths default to the author's setup. Override them with the `VALHEIM_MANAGED`,
`BEPINEX_CORE` and `BEPINEX_PLUGINS_DEPLOY` environment variables.
