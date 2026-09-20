# Demo art validation

The art pass is reviewed independently of gameplay integration. The open Unity editor was kept out of Play Mode; no SpaceStation scene save was performed.

- Baseline: 113 EditMode tests passed before art changes.
- Final suite: **135 passed, 0 failed, 0 skipped** through the Unity EditMode bridge. Final script compilation reported OK; final asset generation completed cleanly.
- Art-only code: four editor builders/preview tools and three test files; no runtime C# edits.
- Normal models: five textured URP prefabs, normalized bounds, no colliders/rigidbodies/imported lights or gameplay scripts. AlienMonster Avatar reported valid and human. Wendy has an authored lowered-arm mesh, with its source retained.
- Boss: timed event contracts, existing controller parameters retained, extra mouth socket, sampled mesh deck-contact checks, measured stomp wrist lift, 120-degree head sweep and collapsed final death pose. Actor root motion is off.
- VFX: 27 prefab assets inspected as a rendered sheet; looping status budgets, one-shot cleanup, transparent URP materials, trail repeat and no realtime lights/collision verified by asset tests.
- Curve key reduction retained motion with bounded numerical tolerance; imported skeleton Armature, breathing and scale channels are retained. Ground-contact regression tests caught an omitted-transform error before handoff.

## Rendered evidence

Four frames per boss sheet, in time order. Camera is at standing eye height, roughly 15 m from the front of the creature. Temporary sampled mesh renderers avoid Unity's stale disabled-Animator preview skin; gameplay prefabs remain skinned.

- [Stomp](art-previews/Stomp.png): rest, raised foreclaws, impact, recovered.
- [Fireball](art-previews/Fireball.png), [laser](art-previews/Laser.png), [enrage](art-previews/Enrage.png), [death settle](art-previews/DeathSettle.png).
- [Grunt](art-previews/Grunt.png), [swarm](art-previews/Swarm.png), [armored](art-previews/Armored.png), [fast](art-previews/Fast.png), [shielded](art-previews/Shielded.png).
- [Effects sheet](art-previews/Effects.png): five columns in [path-index order](art-previews/Effects-index.txt), left-to-right then top-to-bottom. Different camera scales are used for muzzle, body status and large boss effects.

Regenerate via Armory > Art > Render Preview Sheets / Render Effect Sheet. Builders and rendering only modify their documented assets or Temp/ArtPreview, never the gameplay scene.

## Limits still requiring integration/playtesting

The new boss attacks and ordinary enemy/VFX wiring are not implemented. These are asset contracts, not evidence of playable damage/telegraphs. The current boss lifetime cuts off the new 4.5-second death unless Claude extends it. Existing normal-enemy tint replacement must preserve textures before using these prefabs. Both-eye rendering, transitions, targeting/collision alignment after grounding and 45-enemy headset performance remain to be tested. Unknown licenses for user-supplied models remain recorded in their README.


Git diff checks are scoped to authored code/docs; generated project files and pre-existing editor/log changes are excluded from the commit. Source Unity YAML may retain Unity's conventional trailing spaces.
