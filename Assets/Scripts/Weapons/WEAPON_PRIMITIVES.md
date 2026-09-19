# Runtime Weapon Primitives

`WeaponComposer.TryCompose(json, parent, out weapon, out errors)` builds a usable
weapon from JSON during play. The JSON is deliberately constrained so an LLM can
author it without inventing a prefab, component, or C# type.

## Recipe schema

```json
{
  "id": "stable_machine_name",
  "displayName": "Player-facing name",
  "trigger": "press",
  "delivery": {
    "primitive": "hitscan",
    "range": 30,
    "cooldown": 0.2,
    "arcDegrees": 100,
    "radius": 0.5,
    "speed": 25,
    "lifetime": 3
  },
  "payloads": [{ "primitive": "damage", "magnitude": 15, "duration": 0 }],
  "modifiers": [{ "primitive": "pierce", "value": 2 }]
}
```

All numeric fields are optional unless their primitive needs them; the C# defaults
apply when omitted. `hitLayers` may also be supplied as a Unity layer-mask integer.

## Allowed primitive IDs

| Slot | IDs | Runtime behavior |
| --- | --- | --- |
| `trigger` | `press`, `hold` | Tells the input layer when to call `TryUse`. |
| delivery | `melee_arc`, `hitscan`, `projectile`, `thrown_projectile`, `beam`, `area_pulse` | Determines target acquisition and travel. |
| payload | `damage`, `knockback`, `ignite`, `slow`, `stun`, `heal` | Passed to the gameplay damage/status listener in `WeaponHitContext`. |
| modifier | `multishot`, `pierce`, `bounce`, `explode_on_impact`, `homing` | Alters a delivery. The modifier value is shot count, count, count, explosion radius, or turn speed respectively. |

## Integration

After composing, call `weapon.TryUse(origin, direction)` from the player/AI input
layer. Subscribe to `weapon.Hit` to send its payloads into the eventual health,
status, VFX, and audio systems. This separation is intentional: the primitive
system can author and launch any weapon before those game-specific systems exist.

`WeaponRecipeExamples` can be put on a scene object to try its JSON on left mouse
click. It includes a laser sword example and can be replaced with any valid recipe.
