using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Armory.Editor
{
    /// <summary>Authoring only. Generated effects contain standard Unity renderers and particles, no behaviours.</summary>
    public static class ElementalAssetBuilder
    {
        private const string Root = "Assets/Game/Art/";
        private static readonly string[] Elements = { "Plasma", "Cryo", "Electric", "Explosive", "Kinetic" };
        private static readonly Color[] Palette =
        {
            new Color(1f, .28f, .08f), new Color(.42f, .88f, 1f),
            new Color(1f, .85f, .18f), new Color(1f, .49f, .12f), new Color(1f, .92f, .69f)
        };

        [MenuItem("Armory/Art/Build Elemental Effects")]
        public static void Build()
        {
            foreach (var folder in new[] { "Status", "Effects", "Effects/Materials", "Effects/Textures", "Trails" })
                EnsureFolder(Root + folder);
            var glow = Texture("Glow", 0);
            var shard = Texture("Shard", 1);
            var arc = Texture("Arc", 2);
            var ring = Texture("Ring", 3);
            var smoke = Texture("Smoke", 4);
            var textures = new[] { glow, shard, arc, smoke, shard };
            var materials = new Material[5];
            for (var i = 0; i < Elements.Length; i++)
            {
                materials[i] = Material(Root + "Effects/Materials/" + Elements[i] + ".mat", textures[i], i != 3);
                Impact(Elements[i], i, Palette[i], materials[i]);
                Muzzle(Elements[i], Palette[i], materials[i]);
                Trail(Elements[i], Palette[i], textures[i], i);
            }
            var ringMaterial = Material(Root + "Effects/Materials/Ring.mat", ring, true);
            var smokeMaterial = Material(Root + "Effects/Materials/Smoke.mat", smoke, false);
            Status(materials, smokeMaterial, ringMaterial);
            Deaths(materials, ringMaterial);
            Boss(materials, ringMaterial);
            AssetDatabase.SaveAssets();
            Debug.Log("Elemental art built: 3 status loops, 5 impacts, 5 muzzle flashes, 5 trails, 5 deaths, 4 boss visuals.");
        }

        private static void Status(Material[] materials, Material smoke, Material ring)
        {
            Save("Status/Burning", root =>
            {
                for (var i = 0; i < 3; i++)
                {
                    var p = Particles(root, "Flame_" + i, materials[0], Palette[0], true, 10, .6f, .18f, .35f);
                    p.transform.localPosition = new Vector3((i - 1) * .22f, .5f + i * .28f, 0f);
                    Upward(p, .5f);
                }
                var haze = Particles(root, "ThinSmoke", smoke, new Color(.3f, .24f, .28f, .2f), true, 8, .8f, .3f, .1f);
                haze.transform.localPosition = Vector3.up * 1.2f;
                Upward(haze, .3f);
            });
            Save("Status/Chilled", root =>
            {
                var frost = Particles(root, "FrostFacets", materials[1], Palette[1], true, 20, 1f, .12f, 0f);
                frost.transform.localPosition = Vector3.up * .8f;
                var shape = frost.shape;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(.6f, 1.3f, .4f);
                var shards = Particles(root, "FootCrystals", materials[1], Palette[1], true, 12, .9f, .18f, .06f);
                shards.transform.localPosition = Vector3.up * .12f;
                var fog = Particles(root, "ColdMist", smoke, new Color(.6f, .85f, 1f, .16f), true, 6, .7f, .32f, .08f);
                fog.transform.localPosition = Vector3.up * 1.35f;
            });
            Save("Status/Stunned", root =>
            {
                for (var i = 0; i < 3; i++)
                {
                    var p = Particles(root, "BodyArc_" + i, materials[2], Palette[2], true, 8, .12f, .45f, 0f);
                    p.transform.localPosition = new Vector3((i - 1) * .2f, .5f + .4f * i, 0);
                }
                var halo = Particles(root, "SparkHalo", ring, Palette[2], true, 6, .25f, .55f, 0f);
                halo.transform.localPosition = Vector3.up * 1.6f;
                halo.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            });
        }

        private static void Impact(string element, int index, Color color, Material material)
        {
            Save("Effects/" + element + "Impact", root =>
            {
                Lifetime(root, .5f);
                var p = Particles(root, "Impact", material, color, false, index == 3 ? 20 : 12, .4f,
                    index == 3 ? .3f : .1f, index == 3 ? 2.2f : 1.4f);
                if (index == 1 || index == 4)
                {
                    var renderer = p.GetComponent<ParticleSystemRenderer>();
                    renderer.renderMode = ParticleSystemRenderMode.Stretch;
                    renderer.lengthScale = 2f;
                    renderer.velocityScale = .04f;
                }
                if (index == 2)
                {
                    var noise = p.noise;
                    noise.enabled = true;
                    noise.strength = .12f;
                    noise.frequency = 8f;
                    noise.quality = ParticleSystemNoiseQuality.Low;
                }
            });
        }

        private static void Muzzle(string element, Color color, Material material)
        {
            Save("Effects/MuzzleFlash_" + element, root =>
            {
                Lifetime(root, .16f);
                var p = Particles(root, "Flash", material, color, false, 5, .09f, .045f, .4f);
                var shape = p.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 10f;
                shape.radius = .005f;
            });
        }

        private static void Trail(string element, Color color, Texture2D texture, int index)
        {
            var material = Material(Root + "Trails/" + element + "Trail.mat", texture, index != 3);
            Save("Trails/" + element + "Trail", root =>
            {
                var trail = root.AddComponent<TrailRenderer>();
                trail.sharedMaterial = material;
                trail.time = index == 3 ? .35f : .16f;
                trail.minVertexDistance = .06f;
                trail.widthMultiplier = index == 4 ? .015f : .055f;
                trail.widthCurve = AnimationCurve.Linear(0, 1, 1, 0);
                trail.colorGradient = Fade(color);
                trail.textureMode = LineTextureMode.Tile;
                trail.numCapVertices = 2;
                trail.alignment = LineAlignment.View;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.generateLightingData = false;
                trail.autodestruct = false;
            });
        }

        private static void Deaths(Material[] materials, Material ring)
        {
            var kinds = new[] { "Grunt", "Swarm", "Armored", "Fast", "Shielded" };
            for (var i = 0; i < kinds.Length; i++)
            {
                var index = i;
                Save("Effects/Death_" + kinds[i], root =>
                {
                    Lifetime(root, .7f);
                    var p = Particles(root, "Fragments", materials[index == 4 ? 1 : index],
                        index == 4 ? Palette[1] : new Color(1f, .35f, .43f), false,
                        index == 1 ? 10 : 20, .6f, index == 1 ? .08f : .16f, index == 3 ? 2.4f : 1.4f);
                    p.transform.localPosition = Vector3.up * (index == 1 ? .3f : .8f);
                    if (index == 4)
                    {
                        var shield = Particles(root, "ShieldBreak", ring, Palette[1], false, 1, .5f, 1.3f, 0);
                        shield.transform.localPosition = Vector3.up * .8f;
                    }
                });
            }
        }

        private static void Boss(Material[] materials, Material ring)
        {
            Save("Effects/BossShockwave", root =>
            {
                Lifetime(root, 1.2f);
                var wave = Particles(root, "ExpandingGroundRing", ring, Palette[0], false, 1, 1f, 12f / .78f, 0f);
                wave.transform.localPosition = Vector3.up * .045f;
                var shape = wave.shape;
                shape.enabled = false;
                wave.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
                var size = wave.sizeOverLifetime;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, .03f, 1, 1));
            });
            Save("Effects/BossFireball", root =>
            {
                Particles(root, "HotCore", materials[0], new Color(1f, .7f, .2f), true, 12, .18f, .6f, .12f);
                var embers = Particles(root, "Embers", materials[0], Palette[0], true, 20, .5f, .12f, .5f);
                var main = embers.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
            });
            Save("Effects/BossLaserCharge", root =>
            {
                var p = Particles(root, "GatheringEnergy", materials[2], Palette[2], true, 24, .4f, .12f, -.6f);
                var shape = p.shape;
                shape.radius = .35f;
                shape.radiusThickness = 0f;
            });
            Save("Effects/BossLaserBeam", root =>
            {
                var beam = root.AddComponent<LineRenderer>();
                beam.useWorldSpace = false;
                beam.positionCount = 2;
                beam.SetPositions(new[] { Vector3.zero, Vector3.forward * 8f });
                beam.widthMultiplier = .16f;
                beam.sharedMaterial = materials[0];
                beam.startColor = new Color(1f, .8f, .35f);
                beam.endColor = Palette[0];
                beam.numCapVertices = 3;
                beam.shadowCastingMode = ShadowCastingMode.Off;
                beam.receiveShadows = false;
            });
        }

        private static ParticleSystem Particles(GameObject root, string name, Material material, Color color,
            bool loop, int budget, float lifetime, float size, float speed)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            var p = child.AddComponent<ParticleSystem>();
            p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = p.main;
            main.loop = loop;
            main.duration = loop ? 1f : .05f;
            main.startLifetime = lifetime;
            main.startSpeed = speed;
            main.startSize = size;
            main.startColor = color;
            main.maxParticles = budget;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = true;
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var emission = p.emission;
            emission.rateOverTime = loop ? budget / lifetime * .7f : 0f;
            if (!loop) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)budget) });
            var shape = p.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = .12f;
            var fade = p.colorOverLifetime;
            fade.enabled = true;
            // Color is authored in startColor; this gradient only modulates opacity.
            fade.color = Fade(Color.white);
            var scale = p.sizeOverLifetime;
            scale.enabled = true;
            scale.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, .15f));
            var renderer = p.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = .5f;
            return p;
        }

        private static void Upward(ParticleSystem particles, float speed)
        {
            var velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.y = speed;
        }

        private static void Lifetime(GameObject root, float seconds)
        {
            var timer = root.AddComponent<ParticleSystem>();
            timer.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = timer.main;
            main.loop = false;
            main.duration = .01f;
            main.startLifetime = seconds;
            main.startSize = 0f;
            main.startSpeed = 0f;
            main.maxParticles = 1;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var emission = timer.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });
            // A renderer is unnecessary on the lifetime-only parent.
            Object.DestroyImmediate(timer.GetComponent<ParticleSystemRenderer>());
        }

        private static Gradient Fade(Color color)
        {
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) },
                new[] { new GradientAlphaKey(color.a, 0), new GradientAlphaKey(color.a * .8f, .3f), new GradientAlphaKey(0, 1) });
            return gradient;
        }

        private static Material Material(string path, Texture2D texture, bool additive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!shader) throw new InvalidOperationException("URP Particles/Unlit shader is required.");
            var fresh = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
            fresh.SetTexture("_BaseMap", texture);
            fresh.SetColor("_BaseColor", Color.white);
            fresh.SetFloat("_Surface", 1);
            fresh.SetFloat("_Blend", additive ? 2 : 0);
            fresh.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            fresh.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            fresh.SetFloat("_ZWrite", 0);
            fresh.SetFloat("_Cull", (float)CullMode.Off);
            fresh.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            fresh.SetOverrideTag("RenderType", "Transparent");
            fresh.renderQueue = (int)RenderQueue.Transparent;
            return SaveAsset(fresh, path);
        }

        private static Texture2D Texture(string name, int style)
        {
            const int resolution = 64;
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            { name = name, wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (var y = 0; y < resolution; y++)
                for (var x = 0; x < resolution; x++)
                {
                    var u = (x + .5f) / resolution * 2 - 1;
                    var v = (y + .5f) / resolution * 2 - 1;
                    var radius = Mathf.Sqrt(u * u + v * v);
                    var alpha = Mathf.Pow(Mathf.Clamp01(1 - radius), 2);
                    if (style == 1) alpha = Mathf.Clamp01((1 - Mathf.Abs(u) * 3 - Mathf.Abs(v)) * 5);
                    if (style == 2) alpha = Mathf.Clamp01(1 - Mathf.Abs(u - Mathf.Sin(v * 13) * .18f) * 20) * Mathf.Clamp01((1 - Mathf.Abs(v)) * 5);
                    if (style == 3) alpha = Mathf.Clamp01(1 - Mathf.Abs(radius - .78f) * 16);
                    if (style == 4) alpha *= .45f + .55f * Mathf.PerlinNoise(x * .13f, y * .13f);
                    texture.SetPixel(x, y, new Color(1, 1, 1, alpha));
                }
            texture.Apply();
            return SaveAsset(texture, Root + "Effects/Textures/" + name + ".asset");
        }

        private static T SaveAsset<T>(T fresh, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!existing) { AssetDatabase.CreateAsset(fresh, path); return fresh; }
            EditorUtility.CopySerialized(fresh, existing);
            EditorUtility.SetDirty(existing);
            Object.DestroyImmediate(fresh);
            return existing;
        }

        private static void Save(string relativePath, Action<GameObject> configure)
        {
            var root = new GameObject(Path.GetFileName(relativePath));
            try
            {
                configure(root);
                PrefabUtility.SaveAsPrefabAsset(root, Root + relativePath + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
