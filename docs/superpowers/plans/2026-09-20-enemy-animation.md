# Enemy animation: audit and plan

Written from the actual assets on `armory-vr`, not from the asset order. Every rig type below is read off the
FBX `.meta` `animationType` field (`2` = Generic, `3` = Humanoid), and every clip listed is one that exists.

## Audit

| Kind | Visual prefab | Source mesh | Rig | Controller | Clips it has | Driven at runtime? |
| --- | --- | --- | --- | --- | --- | --- |
| Grunt | `GruntVisual` | `AlienMonster.fbx` | **Humanoid** | `GruntRig` | `GruntIdle`, `GruntMoving` | Yes, `Moving` bool |
| Swarm | `SwarmVisual` | `AlienMonster.fbx` | **Humanoid** | `SwarmRig` | `SwarmIdle`, `SwarmMoving` | Yes, `Moving` bool |
| Armored | `ArmoredVisual` | `Gun_Bot.fbx` | Generic | `ArmoredHover` | `ArmoredHover` | No - table's `MovingParameter` is blank |
| Fast | `FastVisual` | `R01.fbx` (FloatingRobot) | Generic | `FastHover` | `FastHover` | No - blank |
| Shielded | `ShieldedVisual` | `ShieldedPosed_0.asset` | **None** (baked pose) | `ShieldedHover` | `ShieldedHover` | No - blank |
| Boss | `HiveAvatarVisual` | Alien Animal FBX | Generic | `HiveAvatarVisual` | idle, walk, claw, bite, `Stomp`, `Fireball`, `Laser`, `Enrage`, `DeathSettle` | Yes, wired to animation events |

Three findings that matter more than the table:

1. **Grunt and Swarm share one humanoid mesh.** That is the only Mixamo-eligible rig in the game, and it covers
   the two kinds the player sees most. Everything else is a robot or a baked pose - Mixamo cannot help there,
   and nothing on Mixamo would fit a hovering drone anyway.
2. **Nothing has a death or hit-react clip.** Not one kind, boss included apart from its own collapse.
3. **No enemy has an attack animation** because no enemy has an attack: an ordinary alien reaching the core
   calls `StationCore.TakeDamage` and dies in the same frame. There is no wind-up to animate yet.

## What has already been done procedurally

`EnemyPresence` now drives hit reactions, spawn-in and idle motion off the model transform for every kind,
including the ones with no rig. That closes the "statue" problem and most of the "shooting has no weight"
problem without a single new clip. It is deliberately transform-only and never touches the enemy root, because
the root carries the collider and health-bar anchor.

## Plan, in priority order by what a judge would notice

### 1. Wire `MovingParameter` for the three hover kinds - 0 min, already handled

Their controllers contain exactly one looping hover state, so there is no second state for a `Moving` bool to
select. Setting the column would achieve nothing. `EnemyPresence` covers the "is it alive" read instead.
**Verdict: leave the table alone.** This entry exists so nobody spends an hour rediscovering it.

### 2. Death beat for Armored and Shielded - ~40 min, procedural

These are the two the player walks up to and kills individually, so their deaths carry. The swarm dies in
clouds and already reads through the pooled `Death_*` particle bursts.

Blocked on a lifecycle decision, not on art: `Enemy.Die` returns the body to the factory pool immediately, so
there is no window for a collapse to play. Two options, in order of preference:

- Detach the model into a short-lived corpse object that plays the collapse and destroys itself, letting the
  body pool instantly. Costs one `Instantiate` per spawn afterwards, since the pooled body no longer keeps its
  model - which is most of what the model pooling was for.
- Delay `EnemyFactory.ReturnToPool` by the collapse length. Keeps pooling intact; needs care that a wave-clear
  check does not see a dying enemy as alive.

Prefer the second. Follow `MonsterArtBuilder` for authoring the clip if a real one is wanted rather than a
transform tip-and-sink.

### 3. Mixamo pass for Grunt and Swarm - ~45 min, NEEDS A HUMAN

**I cannot do this step.** Mixamo requires an interactive browser login and a manual per-clip download. What
follows is the whole procedure so it can be done at 4am without thinking.

1. Go to <https://www.mixamo.com/> and sign in with an Adobe account (free).
2. Upload `Assets/Game/Art/Enemies/AlienMonster/AlienMonster.fbx` via **Upload Character**. Mixamo will
   auto-rig it; check the placed markers if it complains about the silhouette.
3. Download these clips, which map onto states the game already needs:
   - `Zombie Attack` or `Mutant Swiping` - the attack that does not exist yet (see item 4)
   - `Zombie Death` or `Mutant Dying` - death
   - `Standing React Small From Front` - hit react, pairs with the existing flinch
   - `Zombie Running` - a faster locomotion variant for the Fast-style behaviour
4. For every download use: **Format FBX**, **Without Skin**, **30 fps**, **No keyframe reduction**.
   Without Skin matters - with skin you get a duplicate mesh per clip.
5. Drop them in `Assets/Game/Art/Enemies/AlienMonster/Animations/`.
6. For each imported FBX: Inspector > Rig > **Animation Type: Humanoid**, Avatar Definition
   **Copy From Other Avatar**, source = the `AlienMonster` avatar. Apply. Without this the clips will not
   retarget and will silently do nothing.
7. Inspector > Animation > tick **Loop Time** on the locomotion clips only, never on death or attack.
8. Add the states to `GruntRig.controller` and `SwarmRig.controller`, keeping the existing `Moving` bool so
   `EnemyVisualBinder.SetMoving` keeps working untouched. New triggers should be named `Attack`, `Die`, `Hit`.

### 4. Give ordinary enemies an actual attack - ~1 h, gameplay not art

Item 3's attack clip has nothing to play against until this exists. Today an alien that reaches the core
damages it and dies instantly, which is why the fight has no rhythm near the objective. A wind-up, a hit and a
recovery - the shape `HiveAvatar` already uses for the boss - would make the core feel defended rather than
leaked. Do this before downloading an attack clip, not after.

### 5. Not worth doing before the deadline

- Hand-authored death animations for Swarm. Fifteen to forty-five of them die at once as a cloud; the particle
  burst is the right level of detail and a per-body clip would cost frame rate for nothing.
- Retargeting anything onto `ShieldedVisual`. It is a baked static pose with no skeleton, so there is nothing
  to retarget onto. It would need a different source model, which is a bigger job than it sounds.
- `LaserSweepLoop` and the other optional boss states. The primary attacks have never been seen in a headset;
  expanding before that is building on an unverified base.

## Order I would actually work in

Item 4 (attack rhythm) buys the most feel per hour and unblocks item 3. Item 2 is the next best, and is a
lifecycle decision more than an art task. Item 3 is the only one needing a human at a browser, so start the
Mixamo uploads early and let them process while working on 4 - the retarget is quick once the files exist.
