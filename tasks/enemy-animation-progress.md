# Enemy animation implementation

Plan: `docs/superpowers/plans/2026-09-20-enemy-animation.md`.

- [x] Add ordinary-enemy wind-up, hit and recovery without changing boss behavior.
- [x] Keep Armored/Shielded models alive briefly after logical death, then return the complete body to its pool.
- [x] Configure and wire actual Mixamo downloads for Grunt/Swarm, preserving Moving.
- [x] Verify compilation, lifecycle tests, controller assets and live playback.

Decisions: work in the open Unity checkout on armory-vr so editor validation uses the actual project; preserve existing local settings and the earlier spawn fix. Attack/death runtime and editor import work have separate file ownership. The hover kinds retain their existing single-state controllers. Swarm retains immediate pooled particle deaths. No optional boss work.

The user supplied Zombie Attack, Zombie Death, Zombie Running and Standing React Small From Front in Downloads. Copied all four into the planned animation folder and imported as humanoid motion with compression off and baked root motion. Only Running loops. These skeletons use their own valid humanoid avatars for retargeting because they do not contain every bone in the AlienMonster source; blindly copying its avatar would be incompatible. The temporary older attack candidate was removed.

Baseline: 270 passing, six pre-existing failures (EnemyTint null key; Grunt/Swarm controllers lack Moving parameter; three WeaponMesh normalization cases). Controller repair is in scope; unrelated failures are not.

Shared interface: runtime binder safely fires Attack, Hit, Die only when controller parameters exist; editor integration adds these only with corresponding real clips. Both paths must reset correctly on pooled reuse.

Verification: Unity compiled successfully. Full EditMode suite: 293 passed, three pre-existing WeaponMesh normalization failures. Read-only controller/import tests pass. Inspected retargeted attack and death pose strips. Play Mode checks passed for no damage during windup, damage after windup while attacker survives, Grunt death remaining visible but untargetable, and pooled reuse restoring its animator/hitbox. Armored and Shielded delayed-pool checks also passed and retain the same model instances.

Review fixes: stun cancels a pending attack and requires a new full windup; Attack can retrigger its own state. Authored hit reactions do not replace an active attack tell. No swarm redesign or optional boss animations, per recording deadline.
