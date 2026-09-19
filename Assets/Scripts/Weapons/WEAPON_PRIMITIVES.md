# Weapon behaviors (recipe version 2)

Exactly seven composable behavior IDs:

| ID | Behavior |
|---|---|
| projectile | Travels and applies damage / onHit children at collision. |
| beam | One instantaneous ray pulse per activation. |
| explosion | Radial damage / onHit children, deduplicated per receiver. |
| spawn_object | Places a proximity mine; arms after 0.4s, fires onHit once, expires after lifetime. |
| apply_force | Rigidbody impulse on the impacted or aimed target. |
| apply_status | Burning (red), freezing (blue), stunned (purple). Tint restores on expiry. |
| melee | One forward arc attack per activation. |

All weapons repeat while held, respecting cooldown; release stops repetition.
Older version 2 saves are normalized to this behavior. No modifiers, homing,
bouncing, piercing or multishot. Damage is a parameter, not an eighth behavior.
Compose primitives using onHit, for example projectile → explosion → apply_force.
Force/status nodes are leaves. Up to 16 nodes and three nested levels are allowed.
Status freezes/slows expose MovementMultiplier for enemy movement code to consume;
burning applies damage over time. Stun tint takes precedence over freeze, then burn.

```json
{
  "version": 2,
  "displayName": "Impact cannon",
  "cooldown": 0.35,
  "presentation": {
    "asset": "bolt", "color": "#68E8FF", "scale": 1,
    "useDrawingAsProjectile": false, "drawingForwardDegrees": 0
  },
  "behaviors": [{
    "type": "projectile", "damage": 0,
    "onHit": [{
      "type": "explosion", "radius": 3, "damage": 25,
      "onHit": [{"type": "apply_force", "force": 8}]
    }]
  }]
}
```

Open Tools → Weapons → Open Playground, then Play. Draw, interpret, review, equip.
Specify which way the barrel/blade points on paper (Right/Up/Left/Down). Its direction
is rotated toward the camera's aim in world space; the weapon is no longer a flat
screen overlay. Guns fire separate bullets by default. Enable drawn ammunition only
for objects you want to launch, such as arrows. Spawned mines use the drawing.
The same simple physics collider sizes are independent of the artwork.

WASD moves, mouse aims, click attacks, Tab draws, R resets targets. Touch press also
uses the same repeat-while-held behavior, but complete mobile movement/aim UI is not provided.
Cooldown limits the firing rate. Release stops repeating; there is no queued burst.

The image backend returns version 2 recipes using the same seven behavior IDs.
See Services/drawing/README.md for provider setup. Old saved recipes are not executed:
Load last drawing preserves their PNG and asks you to choose a new interpretation.

Verification: Tools → Weapons → Run Recipe Checks; in Play mode, Run Drawing Checks
and Run Play Mode Combat Checks. No batchmode. Mines/projectiles are capped at 256
combined and are removed when their owning weapon is replaced.
