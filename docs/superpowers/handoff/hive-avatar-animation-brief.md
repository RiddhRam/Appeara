# Hive Avatar — animation brief for Astra

Written for whoever animates the boss. Scripting is deliberately **not** done yet: the attacks below are waiting on
these clips, and the gameplay code will be written against the events listed here.

## What exists today

- Model: the project's rigged `Assets/AlienAnimal/Alien Animal_Fbx_7.4.fbx` (quadruped, ~28 m long at import scale,
  driven at roughly 12 m tall in game). Do not re-rig; add clips against this skeleton.
- Visual prefab: `Assets/Game/Art/HiveAvatar/HiveAvatarVisual.prefab` with four sockets already in place:
  `LeftClaw_Socket`, `RightClaw_Socket`, `SporeSac_Socket`, `Crest_Socket`.
- Controller: `Assets/Game/Art/HiveAvatar/HiveAvatarVisual.controller`.
  Parameters: `Moving` (bool), `Claw`, `Bite`, `Die` (triggers). Existing clips mapped: Idle_Aggressive, Walk-Cycle,
  Attack_Hit, Attack_Bite, Die_1, Jump, Rest.
- Gameplay hooks that already work: four shootable organs (each break disables one attack), adaptive plating,
  spore pods, thrown wreckage, floor telegraph rings, entrance walk-in, death.

## What to animate (priority order)

Each attack needs three phases so the player can read and dodge it: **telegraph → strike → recovery**. Times are
targets, not rules; tell me what you change and I will match the script to your clip.

### 1. Stomp + shockwave (highest value)
- **Telegraph (1.2 s):** rears back on hind legs, both fore-claws raised, head down at the player. Weight shifts
  visibly so the floor ring makes sense.
- **Strike (0.3 s):** both claws slam the deck. This frame is the impact.
- **Recovery (0.8 s):** settles, front legs splayed, brief vulnerable pose.
- Script will spawn an expanding ring hazard from the impact point on the strike event; the player escapes by
  walking or translocating, never by jumping.

### 2. Fireball spit
- **Telegraph (1.0 s):** throat and crest swell, head tracks the player, glow builds at the mouth.
- **Strike (0.25 s):** head snaps forward. Projectile leaves the mouth socket.
- **Recovery (0.5 s):** head shake.
- Needs a mouth/muzzle socket if one is not already on the skeleton; name it `Mouth_Socket`.

### 3. Sweeping laser
- **Charge (1.5 s):** crest opens, head lifts, body braces. Should look clearly different from the fireball wind-up.
- **Sweep (2.0 s, loopable):** head turns steadily through roughly 120° while the beam is on. The middle section
  must be a loop so the script can stretch or shorten the sweep.
- **Recovery (1.0 s):** crest closes, head drops, heavy breathing beat.
- If the crest organ is destroyed, this attack is disabled, so an obviously crest-driven look is good.

### 4. Enrage transition (below 35% health)
- One-shot roar, ~1.5 s, plates flaring. Plays once; after it, idle and walk should feel faster and lower.

### 5. Death (already exists, needs a pass)
- `Die_1` works but ends abruptly. A longer collapse with the body settling would sell the finale.

## Contract with the code

Please add **Animation Events** on the clips, named exactly:

| Event | Clip | Fires when |
|---|---|---|
| `OnStompImpact` | stomp | claws touch the deck |
| `OnFireballRelease` | fireball | projectile leaves the mouth |
| `OnLaserStart` | laser | beam becomes active |
| `OnLaserEnd` | laser | beam switches off |
| `OnAttackRecovered` | every attack | the vulnerable window ends |

And add these controller parameters: `Stomp`, `Fireball`, `Laser` (triggers), `Enraged` (bool).

The script will: play the trigger, wait for the named event, spawn the hazard, and re-enable movement on
`OnAttackRecovered`. If an organ is destroyed the matching trigger is never fired, so no clip needs to handle that.

## Constraints

- Root motion stays off; the code drives position.
- Keep the boss's feet on the deck plane (y = 0 in arena space) at every keyframe of ground attacks.
- Telegraphs must be readable from ~15 m away and from ground level, since that is where the player stands.
- Nothing may require the player to jump; there is no jump input.

## Open question for the team

The finale currently happens in the station atrium, where the central hologram and columns block sightlines. If the
boss moves to an open cargo deck, the stomp and laser get much more room. That decision affects how wide the sweep
should be, so flag it before animating the laser.
