# Enemy models (staged, not yet imported)

Downloaded models for the enemy kinds, staged outside `Assets/` on purpose: nothing here is imported until
someone runs `Armory/Import Enemy Models`, which copies these into `Assets/Game/Art/Enemies` and sets the
import settings. `Armory/Prepare Enemy Visuals` then builds the kind-to-model table.

## Provenance

Record the licence for each model before this ships. The source archives were downloaded by the project owner;
the terms were not captured at download time and still need filling in.

| Folder | Source archive | Format | Rig | Animation | Licence |
|---|---|---|---|---|---|
| `AlienMonster/` | `3DAlienMonsterCharacter.unitypackage` (Codersan) | FBX | yes | idle / walk / run / jump | **TODO** |
| `FloatingRobot/` | `floating-robot.zip` | FBX | yes | 2 takes | **TODO** |
| `GunBot/` | `akua3qsamc5c-Gun_Bot.zip` | FBX | skinned | none | **TODO** |
| `Wendy/` | `xlk2jg2qtkao-wendy.zip` | OBJ | no | none | **TODO** |

Only `Character/Model/` was taken from the AlienMonster package. The rest of it ships Unity StarterAssets
scripts, input actions and a demo scene, which would collide with this project's own input and scene setup.

`4j98fwxyc54w-alien_1.rar` was also downloaded but is not here: it is a Cinema 4D `.c4d` file, which Unity
cannot import without Cinema 4D installed. It needs converting to FBX before it is usable.
