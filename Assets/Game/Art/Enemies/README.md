# Monster visual assets

Generate with `Armory/Import Enemy Models`, then `Armory/Art/Build Monster Visuals`.
The second menu runs `Armory.Editor.MonsterArtBuilder.Build()` and updates existing assets in place.
It assigns the authored prefabs to `Assets/Game/Enemies/Resources/EnemyVisuals.asset`.

| Kind | Prefab under `Authored/` | Height | Source | Motion |
|---|---|---|---|---|
| Grunt | `GruntVisual.prefab` | 2.1 m | AlienMonster | Supplied humanoid idle and embedded motion; `Moving` bool |
| Swarm | `SwarmVisual.prefab` | 0.9 m | AlienMonster | Smaller instance of same rig; `Moving` bool |
| Armored | `ArmoredVisual.prefab` | 3.2 m | GunBot | Authored rigid hover, 2.5 s loop |
| Fast | `FastVisual.prefab` | 1.6 m | FloatingRobot | Supplied rig motion plus subtle authored hover |
| Shielded | `ShieldedVisual.prefab` | 2.4 m | Wendy | Authored arms-down static pose and rigid hover, 2.5 s loop; no skeleton |

The root has identity scale and stays stationary. `Motion` carries hover; `Fit` applies scale and deck offset.
Each model's rest bounds sit on y=0 and are centered in x/z. All facing offsets start at zero and require
visual verification against +Z before gameplay integration. Hover raises the visual only by 3.5% of height;
it does not move a gameplay collider. There are no colliders, rigidbodies or gameplay MonoBehaviours in these prefabs.

URP/Lit materials preserve source color textures. FloatingRobot also uses its supplied emission texture.
No normal map is guessed from the ambiguously named GunBot images. No shaders, realtime lights or custom
runtime scripts are required. Imported render meshes and skeleton counts are unchanged; this is not a
measured VR performance optimization or a low-poly remesh.

AlienMonster's original humanoid auto-mapping and Idle2's explicit mappings are restored after staging import so Idle2 can
retarget. The builder excludes the camera take and uses the embedded model motion for `Moving` when no
named walk/run take exists. Loop copies are independent `.anim` assets; source FBX content is preserved.
Controllers report their chosen source clip names in the Unity log. Do not describe an unnamed take as a
verified walk until its playback has been inspected. GunBot/Wendy hover is rigid motion, not skeletal walking.
Wendy's `ShieldedPosed_0.asset` is an independent mesh copy: its outer arms turn down 35 degrees through a
smooth shoulder blend. Source mesh, triangle topology and UVs stay intact; foot vertices are unchanged.
Normals and tangents turn with the posed vertices. This removes the import T-pose without claiming a rig.

## Claude integration

Use `EnemyVisuals.For(kind).Prefab` when constructing the model child, preserving its fitted transforms.
The stored height matches the prefab's rest height; avoid adding an extra arbitrary scale.
Find the child Animator with the `Moving` parameter for Grunt/Swarm. Hovering entries intentionally leave
`MovingParameter` blank; their autonomous art animation can loop while gameplay movement owns the root.
Keep enemy targeting, hitboxes, deaths and status effects outside these visual prefabs. Current runtime
`Enemy.SetTint` must preserve authored materials/textures (for example, using MaterialPropertyBlock)
before these visuals are wired. Art alone does not make the new models spawn in waves.

## Provenance

These are the owner's existing downloads, not newly licensed acquisitions:

| Source | Archive | License status |
|---|---|---|
| AlienMonster | `3DAlienMonsterCharacter.unitypackage` (Codersan) | Terms not recorded; needs verification |
| FloatingRobot | `floating-robot.zip` | Terms not recorded; needs verification |
| GunBot | `akua3qsamc5c-Gun_Bot.zip` | Terms not recorded; needs verification |
| Wendy | `xlk2jg2qtkao-wendy.zip` | Terms not recorded; needs verification |

Source staging notes remain in `ThirdParty/EnemyModels/README.md`. Authored fitting, controllers and hover
curves were created for this project. No license clearance is implied for the underlying downloaded art.
