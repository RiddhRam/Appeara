using System;
using System.Collections.Generic;
using UnityEngine;

namespace Armory.Core
{
    public enum PartShape { Box, Cylinder, Sphere, Capsule, Cone, Disc }

    /// <summary>One primitive of a generated weapon, in weapon-local metres (forward = +Z, up = +Y).</summary>
    [Serializable]
    public sealed class MeshPartSpec
    {
        public string shape;
        public float x, y, z;
        public float rx, ry, rz;
        public float sx = 0.05f, sy = 0.05f, sz = 0.05f;
        public string color;
        public bool glow;
    }

    [Serializable]
    public sealed class WeaponMeshSpec
    {
        public MeshPartSpec[] parts;
        public float muzzleX, muzzleY, muzzleZ;
    }

    public sealed class MeshPart
    {
        public PartShape Shape;
        public Vector3 Position;
        public Vector3 Rotation;
        public Vector3 Scale;
        public Color Color;
        public bool Glow;
    }

    /// <summary>
    /// The model GPT builds after reading its own blueprint: a list of primitives Unity assembles into the held
    /// weapon. Everything is clamped to a weapon-sized box so a bad answer cannot produce a 4 m rifle clipping
    /// through the player's face.
    /// </summary>
    public static class WeaponMesh
    {
        public const int MaxParts = 22;
        /// <summary>Final size in the hand; Normalize scales whatever units the model used down to this.</summary>
        public const float TargetLength = 0.44f;
        /// <summary>Parsing keeps proportions in the model's own units; only sanity bounds are applied here.</summary>
        public const float MaxCoordinate = 50f;
        public const float MinPart = 0.001f;
        public const float MinPolishedPart = 0.008f;
        public const int MaxGlowParts = 4;

        private static readonly Color StructureColor = new Color(0.08f, 0.09f, 0.12f, 1f);

        /// <summary>Readable held scale by silhouette, without requiring another field in the model schema.</summary>
        public static float TargetLengthFor(ParsedWeapon weapon)
        {
            if (weapon == null) return TargetLength;
            string description = string.IsNullOrWhiteSpace(weapon.DesignPrompt) ? weapon.Name : weapon.DesignPrompt;
            if (NameContains(description, "pistol", "handgun", "sidearm", "revolver")) return 0.30f;
            if (NameContains(description, "launcher", "bazooka", "rocket", "cannon", "mortar")) return 0.56f;
            if (NameContains(description, "machine gun", "machinegun", "minigun", "gatling")) return 0.48f;

            switch (weapon.FireMode)
            {
                case FireMode.Thrown: return 0.22f;
                case FireMode.Melee: return 0.64f;
                case FireMode.Bow: return 0.58f;
                case FireMode.Beam: return 0.50f;
                default: return weapon.Payload == Payload.Explosive ? 0.54f : TargetLength;
            }
        }

        public static List<MeshPart> Parse(string json, Color fallbackColor, out Vector3 muzzle)
        {
            muzzle = new Vector3(0f, 0f, 0.3f);
            var parts = new List<MeshPart>();
            if (string.IsNullOrWhiteSpace(json)) return parts;
            WeaponMeshSpec spec;
            try { spec = JsonUtility.FromJson<WeaponMeshSpec>(json); }
            catch (ArgumentException) { return parts; }
            if (spec?.parts == null) return parts;

            foreach (var raw in spec.parts)
            {
                if (parts.Count >= MaxParts) break;
                if (raw == null) continue;
                parts.Add(new MeshPart
                {
                    Shape = WeaponSpecParser.ParseEnum(raw.shape, PartShape.Box),
                    Position = new Vector3(
                        Sane(raw.x), Sane(raw.y), Sane(raw.z)),
                    Rotation = new Vector3(Wrap(raw.rx), Wrap(raw.ry), Wrap(raw.rz)),
                    Scale = new Vector3(
                        Mathf.Clamp(Mathf.Abs(Sane(raw.sx)), MinPart, MaxCoordinate),
                        Mathf.Clamp(Mathf.Abs(Sane(raw.sy)), MinPart, MaxCoordinate),
                        Mathf.Clamp(Mathf.Abs(Sane(raw.sz)), MinPart, MaxCoordinate)),
                    Color = WeaponSpecParser.ParseColor(raw.color) is var parsed && !string.IsNullOrWhiteSpace(raw.color) ? parsed : fallbackColor,
                    Glow = raw.glow,
                });
            }
            muzzle = new Vector3(Sane(spec.muzzleX), Sane(spec.muzzleY), Sane(spec.muzzleZ));
            // A muzzle on X, Y, or -Z is still useful: it tells us which way the model considered forward.
            // Only an entirely absent/zero muzzle needs the legacy +Z fallback.
            if (muzzle.sqrMagnitude <= MinPart * MinPart)
                muzzle = new Vector3(0f, 0f, Furthest(parts));
            return parts;
        }

        /// <summary>
        /// Rescales and recentres the whole weapon so it is always a weapon-sized object in the player's hand,
        /// whatever units the model answered in (centimetres and "3.0" blocks both happen). The grip ends up just
        /// behind the origin and the barrel runs forward along +Z.
        /// </summary>
        public static void Normalize(List<MeshPart> parts, ref Vector3 muzzle, float targetLength = TargetLength)
        {
            if (parts == null || parts.Count == 0) return;
            CanonicalizeForward(parts, ref muzzle);
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var part in parts)
            {
                min = Vector3.Min(min, part.Position - part.Scale * 0.5f);
                max = Vector3.Max(max, part.Position + part.Scale * 0.5f);
            }
            Vector3 size = max - min;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (longest <= 0.0001f) return;

            float factor = targetLength / longest;
            Vector3 centre = (min + max) * 0.5f;
            foreach (var part in parts)
            {
                // Centre on X and Y, keep the back of the weapon near the hand on Z.
                part.Position = new Vector3((part.Position.x - centre.x) * factor,
                                            (part.Position.y - centre.y) * factor,
                                            (part.Position.z - min.z) * factor - 0.06f);
                part.Scale *= factor;
            }
            muzzle = new Vector3((muzzle.x - centre.x) * factor, (muzzle.y - centre.y) * factor, (muzzle.z - min.z) * factor - 0.06f);
            if (muzzle.z < 0.05f) muzzle.z = Furthest(parts);
        }

        /// <summary>
        /// Applies the station's shared visual language after normalization: clean angles, readable pieces, a
        /// restrained amount of emission, and a three-colour palette derived from the requested weapon colour.
        /// </summary>
        public static void Polish(List<MeshPart> parts, Color accent)
        {
            if (parts == null || parts.Count == 0) return;

            MeshPart largest = parts[0];
            foreach (var part in parts)
            {
                if (LargestDimension(part) > LargestDimension(largest)) largest = part;
                part.Rotation = new Vector3(SnapAngle(part.Rotation.x), SnapAngle(part.Rotation.y), SnapAngle(part.Rotation.z));
            }

            // Keep at least one part even when a pathological response consists entirely of tiny details.
            for (int i = parts.Count - 1; i >= 0; i--)
                if (parts[i] != largest && LargestDimension(parts[i]) < MinPolishedPart)
                    parts.RemoveAt(i);

            var glowing = parts.FindAll(part => part.Glow);
            glowing.Sort((a, b) => Volume(b).CompareTo(Volume(a)));
            for (int i = MaxGlowParts; i < glowing.Count; i++) glowing[i].Glow = false;

            accent.a = 1f;
            Color shell = Color.Lerp(StructureColor, accent, 0.45f);
            shell.a = 1f;
            Color energy = Color.Lerp(accent, Color.white, 0.18f);
            energy.a = 1f;
            foreach (var part in parts)
            {
                if (part.Glow) part.Color = energy;
                else part.Color = part.Color.maxColorComponent >= 0.45f ? shell : StructureColor;
            }
        }

        /// <summary>
        /// Adds a few unmistakable category-defining forms after the vision model's reconstruction. This prevents
        /// visually different blueprints from collapsing into the same generic primitive gun.
        /// </summary>
        public static void ApplyArchetypeSignature(List<MeshPart> parts, ParsedWeapon weapon, ref Vector3 muzzle)
        {
            if (parts == null || weapon == null) return;
            string description = string.IsNullOrWhiteSpace(weapon.DesignPrompt) ? weapon.Name : weapon.DesignPrompt;

            if (NameContains(description, "launcher", "bazooka", "rocket", "cannon", "mortar"))
            {
                MakeRoom(parts, 2);
                parts.Add(new MeshPart
                {
                    Shape = PartShape.Cylinder,
                    Position = new Vector3(0f, 0.025f, 0.28f),
                    Rotation = new Vector3(90f, 0f, 0f),
                    Scale = new Vector3(0.14f, 0.44f, 0.14f),
                    Color = Color.white,
                });
                parts.Add(new MeshPart
                {
                    Shape = PartShape.Disc,
                    Position = new Vector3(0f, 0.025f, 0.51f),
                    Rotation = new Vector3(90f, 0f, 0f),
                    Scale = new Vector3(0.18f, 0.035f, 0.18f),
                    Color = Color.black,
                });
                muzzle = new Vector3(0f, 0.025f, 0.53f);
                return;
            }

            if (NameContains(description, "machine gun", "machinegun", "minigun", "gatling"))
            {
                MakeRoom(parts, 4);
                for (int i = -1; i <= 1; i++)
                {
                    parts.Add(new MeshPart
                    {
                        Shape = PartShape.Cylinder,
                        Position = new Vector3(i * 0.028f, 0.025f, 0.37f),
                        Rotation = new Vector3(90f, 0f, 0f),
                        Scale = new Vector3(0.024f, 0.23f, 0.024f),
                        Color = Color.black,
                    });
                }
                parts.Add(new MeshPart
                {
                    Shape = PartShape.Box,
                    Position = new Vector3(0f, -0.075f, 0.13f),
                    Rotation = new Vector3(345f, 0f, 0f),
                    Scale = new Vector3(0.10f, 0.15f, 0.11f),
                    Color = Color.white,
                });
                muzzle = new Vector3(0f, 0.025f, 0.49f);
            }
        }

        /// <summary>
        /// Uses the semantic muzzle point to correct the common vision-model mistake of building the weapon along
        /// X, Y, or -Z. Snapping to a cardinal axis avoids introducing a small roll or pitch when the muzzle is
        /// intentionally a little above or to the side of the grip.
        /// </summary>
        private static void CanonicalizeForward(List<MeshPart> parts, ref Vector3 muzzle)
        {
            if (muzzle.sqrMagnitude <= MinPart * MinPart) return;

            Vector3 absolute = new Vector3(Mathf.Abs(muzzle.x), Mathf.Abs(muzzle.y), Mathf.Abs(muzzle.z));
            Vector3 source;
            if (absolute.x > absolute.y && absolute.x > absolute.z)
                source = muzzle.x >= 0f ? Vector3.right : Vector3.left;
            else if (absolute.y > absolute.z)
                source = muzzle.y >= 0f ? Vector3.up : Vector3.down;
            else
                source = muzzle.z >= 0f ? Vector3.forward : Vector3.back;

            if (source == Vector3.forward) return;
            Quaternion correction = Quaternion.FromToRotation(source, Vector3.forward);
            foreach (var part in parts)
            {
                part.Position = correction * part.Position;
                part.Rotation = (correction * Quaternion.Euler(part.Rotation)).eulerAngles;
            }
            muzzle = correction * muzzle;
        }

        /// <summary>Front of the weapon, used when the model forgets to place a muzzle.</summary>
        public static float Furthest(List<MeshPart> parts)
        {
            float furthest = 0.2f;
            foreach (var part in parts) furthest = Mathf.Max(furthest, part.Position.z + part.Scale.z * 0.5f);
            return furthest;
        }

        private static float LargestDimension(MeshPart part) =>
            Mathf.Max(part.Scale.x, Mathf.Max(part.Scale.y, part.Scale.z));

        private static float Volume(MeshPart part) => part.Scale.x * part.Scale.y * part.Scale.z;

        private static float SnapAngle(float degrees) => Mathf.Repeat(Mathf.Round(degrees / 15f) * 15f, 360f);

        private static bool NameContains(string name, params string[] terms)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (string term in terms)
                if (name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static void MakeRoom(List<MeshPart> parts, int additions)
        {
            while (parts.Count > MaxParts - additions)
            {
                MeshPart smallest = parts[0];
                foreach (var part in parts)
                    if (Volume(part) < Volume(smallest)) smallest = part;
                parts.Remove(smallest);
            }
        }

        private static float Sane(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, -MaxCoordinate, MaxCoordinate);

        private static float Wrap(float degrees) => float.IsNaN(degrees) || float.IsInfinity(degrees) ? 0f : Mathf.Repeat(degrees, 360f);
    }
}
