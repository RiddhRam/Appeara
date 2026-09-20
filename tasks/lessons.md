# Art authoring lessons

- Unity preview scenes with disabled Animators can render stale skinned poses after SampleAnimation. BakeMesh(mesh, true) into temporary preview-only mesh renderers to inspect the actual sampled skin. Restore RenderTexture targets before releasing them.
- Verify weighted-bone influence before choosing attack controls: this alien's Bone.008 drives the head; Bone.028 is mostly a tongue attachment.
- The imported Die_1 returns toward its standing pose near the end. Author death from its collapsed interval and hold/settle that pose.
- Normalize from sampled vertex positions, not only imported renderer bounds. Curve reduction tolerances can shift ground contact; verify interpolated late-death frames as well as exact keys.
- Remove dependent URP additional-data components before removing imported lights/cameras.
- Tiled trails require U-repeat textures. Sprite quad size differs from visible ring diameter when the ring occupies only part of the texture.
- A persistent exact-key ground error came from omitted Armature/breathing transforms and source scale curves, not compression. Include every animated skeleton transform and scale when baking source poses.
