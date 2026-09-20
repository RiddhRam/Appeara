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
            muzzle = new Vector3(Sane(spec.muzzleX), Sane(spec.muzzleY),
                spec.muzzleZ <= 0f ? Furthest(parts) : Sane(spec.muzzleZ));
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

        /// <summary>Front of the weapon, used when the model forgets to place a muzzle.</summary>
        public static float Furthest(List<MeshPart> parts)
        {
            float furthest = 0.2f;
            foreach (var part in parts) furthest = Mathf.Max(furthest, part.Position.z + part.Scale.z * 0.5f);
            return furthest;
        }

        private static float Sane(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, -MaxCoordinate, MaxCoordinate);

        private static float Wrap(float degrees) => float.IsNaN(degrees) || float.IsInfinity(degrees) ? 0f : Mathf.Repeat(degrees, 360f);
    }
}
