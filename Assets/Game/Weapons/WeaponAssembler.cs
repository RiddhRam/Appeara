using System.Collections;
using Armory.Core;
using UnityEngine;

namespace Armory
{
    /// <summary>Builds a weapon from predefined placeholder parts (body × barrel × colour) and animates it into the hand.</summary>
    public static class WeaponAssembler
    {
        public static Weapon Build(ParsedWeapon spec, Transform aim)
        {
            var root = new GameObject("Weapon: " + spec.Name);
            root.transform.SetParent(aim, false);
            root.transform.localPosition = new Vector3(0f, -0.03f, 0.02f);

            var dark = Mats.Lit(Color.Lerp(spec.Color, new Color(0.12f, 0.13f, 0.16f), 0.75f));
            var accent = Mats.Lit(spec.Color, 1.2f);
            var parts = new GameObject("Parts").transform;
            parts.SetParent(root.transform, false);

            float bodyLength = BuildBody(spec.Body, parts, dark, accent);
            Transform muzzle = BuildBarrel(spec.Barrel, parts, dark, accent, bodyLength, spec.FireMode);

            var weapon = root.AddComponent<Weapon>();
            weapon.Spec = spec;
            weapon.Muzzle = muzzle;

            var label = UiKit.Text(root.transform, "Label", new Vector3(0f, 0.075f, 0.05f), 0.011f, UiKit.Label, Color.Lerp(spec.Color, Color.white, 0.5f), TMPro.TextAlignmentOptions.Center, width: 0.4f, tracking: 10f, uppercase: true);
            label.text = spec.Name;
            label.gameObject.AddComponent<Billboard>();

            root.AddComponent<PopIn>();
            return weapon;
        }

        /// <summary>
        /// Replaces the blocky placeholder parts with the AI's own art: an alpha-cut cutout of the transparent
        /// render, stood up in the weapon's forward plane so the silhouette in your hand is the generated design.
        /// </summary>
        public static void ApplyGeneratedArt(Weapon weapon, Texture2D art)
        {
            if (weapon == null || art == null) return;
            var parts = weapon.transform.Find("Parts");
            if (parts != null) parts.gameObject.SetActive(false);

            var shader = Shader.Find("Armory/WeaponCutout");
            var material = shader != null ? new Material(shader) : Mats.Glow(Color.white);
            material.mainTexture = art;
            if (shader != null) material.SetColor("_Rim", weapon.Spec != null ? weapon.Spec.Color * 1.4f : Color.cyan);

            const float length = 0.46f;
            float aspect = art.height / (float)Mathf.Max(1, art.width);
            var cutout = Mats.Shape(PrimitiveType.Quad, weapon.transform, new Vector3(0f, 0.02f, length * 0.28f),
                new Vector3(length, length * aspect, 1f), material, name: "Generated Art");
            // Turned side-on: the art's left-to-right axis becomes the weapon's forward axis.
            cutout.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);

            var muzzle = new GameObject("Art Muzzle").transform;
            muzzle.SetParent(weapon.transform, false);
            muzzle.localPosition = new Vector3(0f, 0.02f, length * 0.78f);
            weapon.Muzzle = muzzle;
        }

        private static float BuildBody(int variant, Transform parent, Material dark, Material accent)
        {
            switch (variant)
            {
                case 0: // boxy rifle
                    Mats.Shape(PrimitiveType.Cube, parent, new Vector3(0f, 0f, 0.06f), new Vector3(0.05f, 0.07f, 0.22f), dark);
                    Mats.Shape(PrimitiveType.Cube, parent, new Vector3(0f, 0.04f, 0.06f), new Vector3(0.02f, 0.015f, 0.18f), accent);
                    return 0.17f;
                case 1: // cylinder cannon
                    Mats.Shape(PrimitiveType.Cylinder, parent, new Vector3(0f, 0f, 0.06f), new Vector3(0.08f, 0.12f, 0.08f), dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Mats.Shape(PrimitiveType.Cylinder, parent, new Vector3(0f, 0f, 0.06f), new Vector3(0.085f, 0.02f, 0.085f), accent).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    return 0.18f;
                case 2: // orb core
                    Mats.Shape(PrimitiveType.Sphere, parent, new Vector3(0f, 0.01f, 0.05f), Vector3.one * 0.1f, accent);
                    Mats.Shape(PrimitiveType.Cube, parent, new Vector3(0f, -0.02f, 0.03f), new Vector3(0.04f, 0.04f, 0.16f), dark);
                    return 0.1f;
                case 3: // capsule blaster
                    Mats.Shape(PrimitiveType.Capsule, parent, new Vector3(0f, 0f, 0.07f), new Vector3(0.07f, 0.12f, 0.07f), dark).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    Mats.Shape(PrimitiveType.Sphere, parent, new Vector3(0f, 0.035f, 0.02f), Vector3.one * 0.035f, accent);
                    return 0.19f;
                case 4: // twin rails
                    Mats.Shape(PrimitiveType.Cube, parent, new Vector3(-0.025f, 0f, 0.08f), new Vector3(0.02f, 0.05f, 0.26f), dark);
                    Mats.Shape(PrimitiveType.Cube, parent, new Vector3(0.025f, 0f, 0.08f), new Vector3(0.02f, 0.05f, 0.26f), dark);
                    Mats.Shape(PrimitiveType.Cube, parent, new Vector3(0f, 0f, 0.08f), new Vector3(0.015f, 0.015f, 0.24f), accent);
                    return 0.21f;
                default: // bulky launcher
                    Mats.Shape(PrimitiveType.Cube, parent, new Vector3(0f, 0.02f, 0.05f), new Vector3(0.1f, 0.1f, 0.2f), dark);
                    Mats.Shape(PrimitiveType.Cube, parent, new Vector3(0f, -0.045f, 0.0f), new Vector3(0.05f, 0.04f, 0.08f), accent);
                    return 0.15f;
            }
        }

        private static Transform BuildBarrel(int variant, Transform parent, Material dark, Material accent, float z, FireMode mode)
        {
            Transform Tube(float x, float y, float radius, float length, Material material)
            {
                var tube = Mats.Shape(PrimitiveType.Cylinder, parent, new Vector3(x, y, z + length * 0.5f), new Vector3(radius, length * 0.5f, radius), material);
                tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                return tube.transform;
            }

            float tip;
            switch (variant)
            {
                case 0: Tube(0f, 0f, 0.025f, 0.14f, dark); tip = 0.14f; break;
                case 1: Tube(-0.018f, 0f, 0.02f, 0.13f, dark); Tube(0.018f, 0f, 0.02f, 0.13f, dark); tip = 0.13f; break;
                case 2: Tube(0f, 0f, 0.06f, 0.08f, dark); Tube(0f, 0f, 0.065f, 0.015f, accent); tip = 0.08f; break;
                case 3: for (int i = 0; i < 3; i++) { float a = i * 2.094f; Tube(Mathf.Cos(a) * 0.02f, Mathf.Sin(a) * 0.02f, 0.014f, 0.16f, dark); } tip = 0.16f; break;
                case 4: Tube(0f, 0.018f, 0.01f, 0.24f, accent); Tube(0f, -0.018f, 0.01f, 0.24f, accent); tip = 0.24f; break;
                default: Tube(0f, 0f, 0.03f, 0.06f, dark); Mats.Shape(PrimitiveType.Cylinder, parent, new Vector3(0f, 0f, z + 0.07f), new Vector3(0.1f, 0.005f, 0.1f), accent).transform.localRotation = Quaternion.Euler(90f, 0f, 0f); tip = 0.07f; break;
            }
            if (mode == FireMode.Beam) Mats.Shape(PrimitiveType.Sphere, parent, new Vector3(0f, 0f, z + tip), Vector3.one * 0.03f, accent);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(parent, false);
            muzzle.localPosition = new Vector3(0f, 0f, z + tip + 0.02f);
            return muzzle;
        }

        /// <summary>Staggered scale-in of each part: reads as "constructed from modules".</summary>
        private sealed class PopIn : MonoBehaviour
        {
            private IEnumerator Start()
            {
                var parts = transform.Find("Parts");
                var scales = new Vector3[parts.childCount];
                for (int i = 0; i < parts.childCount; i++)
                {
                    scales[i] = parts.GetChild(i).localScale;
                    parts.GetChild(i).localScale = Vector3.zero;
                }
                for (int i = 0; i < parts.childCount; i++)
                {
                    StartCoroutine(Grow(parts.GetChild(i), scales[i]));
                    yield return new WaitForSeconds(0.07f);
                }
            }

            private static IEnumerator Grow(Transform part, Vector3 target)
            {
                for (float t = 0f; t < 1f; t += Time.deltaTime / 0.25f)
                {
                    if (part == null) yield break;
                    float s = 1f + 2.2f * Mathf.Pow(t - 1f, 3f) + 1.2f * Mathf.Pow(t - 1f, 2f); // ease-out-back
                    part.localScale = target * s;
                    yield return null;
                }
                if (part != null) part.localScale = target;
            }
        }
    }
}
