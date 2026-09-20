# Hive Avatar animation assets

Generated on the existing Generic skeleton, with root motion disabled. New clips are sampled, authored skeletal poses; they are not new motion-capture recordings. Rebuild with **Armory > Art > Build Hive Attack Animations**. Existing GUIDs are retained. Gameplay and SpaceStation are untouched.

| Controller trigger/state | Duration | Animation events (seconds) |
| --- | --- | --- |
| `Stomp` | 2.30 | `OnStompImpact` 1.50; `OnAttackRecovered` 2.28 |
| `Fireball` | 1.75 | `OnFireballRelease` 1.25; `OnAttackRecovered` 1.73 |
| `Laser` | 4.50 | `OnLaserStart` 1.50; `OnLaserEnd` 3.50; `OnAttackRecovered` 4.48 |
| `Enrage` | 1.50 | No hazard events; one-shot roar pose and plate flare |
| `Die` / `Death` | 4.50 | Holds the collapsed source pose, then settles; no recovery |
| `LaserSweepLoop` (direct state) | 4.00 loop | No events; two seconds left-to-right, two seconds back |

`Animations/` also contains grounded copies of imported idle, walk, claw and bite. Claw/bite copies emit recovery 0.02 seconds before their end. Source FBX clips stay intact. Root translation is excluded from authored curves. Pose sampling and mesh-derived deck correction keep the lowest body vertex on y=0; forelegs intentionally lift during stomp. The supporting wrist orientations are preserved by the offline leg solver.

The normal `Laser` trigger is a complete 1.5-second charge, 120-degree sweep and 1-second recovery. The optional `LaserSweepLoop` state allows extended beam motion; the script must explicitly enter/leave it and own beam start/end/recovery for that route. Its full round trip is four seconds to avoid snapping from +60 to -60 degrees. Do not wait for events on the loop itself.

Set trigger `Enrage` once below the chosen health threshold, and bool `Enraged=true` for 1.2x idle / 1.25x walk. Attack clips retain authored timing. Existing `Moving`, `Claw`, `Bite`, `Die` parameters remain. Every attack returns to idle; the `Enraged` bool selects the faster variant. Enrage locomotion currently changes speed, not a separate crouched walk cycle.

`Mouth_Socket` follows the existing mouth/tongue chain, points along local +Z in the neutral pose and has no collider. Existing four organ sockets remain unchanged. The animated head is `Bone.008`; `Bone.028` is a low-weight tongue/crest attachment, not the whole head.

## Claude's integration work

- Add the exact AnimationEvent methods to a component on the **Animator GameObject**, forwarding to the encounter. Events use `DontRequireReceiver` while this art package is unwired.
- Extend `Enemy.Die` boss destruction from 3.5s to at least 4.5s plus blend allowance. Current gameplay otherwise cuts off the finale.
- Clear pending triggers on death and gate all new attack scheduling with the existing dying flag. AnyState transitions are deliberately gameplay-owned; firing an attack trigger after death can leave Death.
- Spawn shockwave/fireball/laser art from the supplied Effects prefabs; implement telegraphs, collision, shield/core damage, organ gating and cleanup. Art has no damage or targeting scripts.
- For an extended laser, choose a safe sector and explicit loop exit timing. Verify sockets, colliders and movement speeds after the grounded art changes; collider rules remain unchanged.
- Headset review remains required for leg contact across blends, readability, both-eye particles and performance. Rendered pose sheets are inspection evidence, not a playtest of these attacks.

The existing boss FBX and textures retain their original provenance. No external downloads or new runtime dependencies were introduced.
