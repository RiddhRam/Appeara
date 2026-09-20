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

        /// <summary>How far behind the hand the back of the weapon sits, so the grip is in the palm not in front of it.</summary>
        public const float GripSetBack = 0.06f;
        /// <summary>How far a declared muzzle may sit from the weapon's own length axis and still be believed.</summary>
        public const float MuzzleTrustDegrees = 35f;

        /// <summary>
        /// Everything needed to put a generated weapon in the hand pointing at the enemy: orient it, scale it,
        /// then anchor it on the grip. Callers should use this rather than the steps individually.
        /// </summary>
        public static void Fit(List<MeshPart> parts, ref Vector3 muzzle, float targetLength = TargetLength)
        {
            Orient(parts, ref muzzle);
            Normalize(parts, ref muzzle, targetLength);
        }

        /// <summary>
        /// Rotates the weapon into the convention the rest of the game assumes: barrel along +Z, up along +Y.
        /// The model is asked to answer that way and routinely does not - it lays a rifle along X, or stands it
        /// on end along Y - which is why generated guns arrived in the hand sideways. Rather than trusting the
        /// answer, the axes are read off the geometry, using three things that hold for every gun, bow and blade
        /// the model has produced: the longest dimension is the length of the weapon, the muzzle end is the
        /// thinner end, and the grip is the mass hanging off the barrel line.
        /// </summary>
        public static Quaternion Orient(List<MeshPart> parts, ref Vector3 muzzle)
        {
            if (parts == null || parts.Count < 2) return Quaternion.identity;
            Vector3 centre = Centroid(parts);
            // Principal axes, not bounding-box axes. A weapon returned at forty-five degrees has a bounding box
            // that is square in two of its dimensions, so ranking box edges picks an arbitrary one and the
            // weapon ends up wedged across the player's view. The spread of the actual mass has no such problem.
            Covariance(parts, centre, out var covariance);
            Vector3 first = Dominant(covariance, Vector3.forward);
            Vector3 second = DominantOrthogonalTo(covariance, first);
            Vector3 third = Vector3.Cross(first, second).normalized;

            Vector3 forward = ForwardAxis(parts, centre, muzzle, first, second, third);
            Vector3 up = UpAxis(parts, centre, forward, first, second, third);
            // Every part sitting on one line leaves nothing to orient by; the model's own frame is as good as any.
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f) return Quaternion.identity;

            var rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            foreach (var part in parts)
            {
                part.Position = rotation * part.Position;
                // Composed, not replaced: a part's own scale is applied along its own axes, so turning the
                // weapon has to turn each part with it or the barrel becomes a slab.
                part.Rotation = (rotation * Quaternion.Euler(part.Rotation)).eulerAngles;
            }
            muzzle = rotation * muzzle;
            return rotation;
        }

        /// <summary>
        /// Rescales the whole weapon so it is always a weapon-sized object in the hand, whatever units the model
        /// answered in (centimetres and "3.0" blocks both happen), then anchors it on the grip.
        /// </summary>
        public static void Normalize(List<MeshPart> parts, ref Vector3 muzzle, float targetLength = TargetLength)
        {
            if (parts == null || parts.Count == 0) return;
            Bounds(parts, out var min, out var max);
            Vector3 size = max - min;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (longest <= 0.0001f) return;

            float factor = targetLength / longest;
            Vector3 centre = (min + max) * 0.5f;
            // The hand closes on the grip, so the grip is the origin. Centring on Y hung a rifle half above and
            // half below the palm, which is what made generated guns look stuck to the side of the controller.
            Vector3 anchor = new Vector3(centre.x, GripHeight(parts, centre.y), min.z);
            foreach (var part in parts)
            {
                part.Position = new Vector3((part.Position.x - anchor.x) * factor,
                                            (part.Position.y - anchor.y) * factor,
                                            (part.Position.z - anchor.z) * factor - GripSetBack);
                part.Scale *= factor;
            }
            muzzle = new Vector3((muzzle.x - anchor.x) * factor, (muzzle.y - anchor.y) * factor,
                                 (muzzle.z - anchor.z) * factor - GripSetBack);
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
        /// The grip hangs off the barrel, so the side that reaches further from the weapon's middle is down.
        /// Reach, not weight: volume-weighted moments about the centroid cancel to zero by the definition of a
        /// centroid, so comparing them decided which way up a pistol went on floating-point noise alone.
        /// </summary>
        private static float UpSign(List<MeshPart> parts, Vector3 centre, Vector3 upAxis)
        {
            float above = 0f, below = 0f;
            foreach (var part in parts)
            {
                float offset = Vector3.Dot(part.Position - centre, upAxis);
                float half = Extent(part, upAxis) * 0.5f;
                above = Mathf.Max(above, offset + half);
                below = Mathf.Max(below, half - offset);
            }
            // A blade or an orb has no grip to find, and either way up is as good as the other.
            return below >= above ? 1f : -1f;
        }

        private static float Volume(MeshPart part) => part.Scale.x * part.Scale.y * part.Scale.z;

        /// <summary>How wide one part reads along an arbitrary direction, with its own rotation taken into account.</summary>
        private static float Extent(MeshPart part, Vector3 direction)
        {
            var rotation = Quaternion.Euler(part.Rotation);
            return Mathf.Abs(Vector3.Dot(rotation * new Vector3(part.Scale.x, 0f, 0f), direction))
                 + Mathf.Abs(Vector3.Dot(rotation * new Vector3(0f, part.Scale.y, 0f), direction))
                 + Mathf.Abs(Vector3.Dot(rotation * new Vector3(0f, 0f, part.Scale.z), direction));
        }

        private static float Span(List<MeshPart> parts, Vector3 centre, Vector3 direction)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var part in parts)
            {
                float along = Vector3.Dot(part.Position - centre, direction);
                float half = Extent(part, direction) * 0.5f;
                min = Mathf.Min(min, along - half);
                max = Mathf.Max(max, along + half);
            }
            return max > min ? max - min : 0f;
        }

        private static Vector3 Centroid(List<MeshPart> parts)
        {
            Vector3 sum = Vector3.zero;
            float total = 0f;
            foreach (var part in parts)
            {
                float volume = Mathf.Max(Volume(part), 1e-6f);
                sum += part.Position * volume;
                total += volume;
            }
            return total > 0f ? sum / total : Vector3.zero;
        }

        /// <summary>
        /// Volume-weighted spread of the parts. Each part contributes both where it sits and how it is shaped,
        /// so a weapon that is one long rotated barrel is described as well as one built from many blocks.
        /// </summary>
        private static void Covariance(List<MeshPart> parts, Vector3 centre, out Vector3[] matrix)
        {
            matrix = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
            foreach (var part in parts)
            {
                float volume = Mathf.Max(Volume(part), 1e-6f);
                Vector3 offset = part.Position - centre;
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                        matrix[r][c] += volume * offset[r] * offset[c];

                // A solid box's own spread about its centre, turned into the weapon's frame.
                var rotation = Quaternion.Euler(part.Rotation);
                for (int a = 0; a < 3; a++)
                {
                    Vector3 axis = rotation * Axis(a);
                    float half = part.Scale[a] * 0.5f;
                    float weight = volume * half * half / 3f;
                    for (int r = 0; r < 3; r++)
                        for (int c = 0; c < 3; c++)
                            matrix[r][c] += weight * axis[r] * axis[c];
                }
            }

            // Scaled to a trace of one before anyone iterates on it. A weapon measured in metres produces
            // entries around 1e-6, and Vector3.Normalize quietly returns zero below 1e-5, so the power
            // iteration was being wiped on its first step and falling back to the world axes. That fallback
            // happens to be right whenever the model answered axis-aligned, which is exactly why this hid.
            float trace = matrix[0][0] + matrix[1][1] + matrix[2][2];
            if (trace <= 0f) return;
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    matrix[r][c] /= trace;
        }

        /// <summary>Power iteration: enough for the one dominant direction, and no eigen solver to get wrong.</summary>
        private static Vector3 Dominant(Vector3[] matrix, Vector3 seed)
        {
            Vector3 best = Vector3.zero;
            foreach (var start in new[] { seed, Vector3.right, Vector3.up })
            {
                Vector3 v = start;
                for (int i = 0; i < 64; i++)
                {
                    v = Multiply(matrix, v);
                    if (v.sqrMagnitude < 1e-20f) { v = Vector3.zero; break; }
                    v.Normalize();
                }
                if (v.sqrMagnitude > 0.5f && Vector3.Dot(Multiply(matrix, v), v) > Vector3.Dot(Multiply(matrix, best), best))
                    best = v;
            }
            return best.sqrMagnitude > 0.5f ? best : Vector3.forward;
        }

        private static Vector3 DominantOrthogonalTo(Vector3[] matrix, Vector3 axis)
        {
            Vector3 best = Vector3.zero;
            foreach (var start in new[] { Vector3.up, Vector3.right, Vector3.forward })
            {
                Vector3 v = Vector3.ProjectOnPlane(start, axis);
                if (v.sqrMagnitude < 1e-8f) continue;
                v.Normalize();
                for (int i = 0; i < 64; i++)
                {
                    v = Vector3.ProjectOnPlane(Multiply(matrix, v), axis);
                    if (v.sqrMagnitude < 1e-20f) { v = Vector3.zero; break; }
                    v.Normalize();
                }
                if (v.sqrMagnitude > 0.5f && Vector3.Dot(Multiply(matrix, v), v) > Vector3.Dot(Multiply(matrix, best), best))
                    best = v;
            }
            if (best.sqrMagnitude <= 0.5f)
            {
                // Perfectly round about its length: any perpendicular will do.
                best = Vector3.ProjectOnPlane(Vector3.up, axis);
                if (best.sqrMagnitude < 1e-8f) best = Vector3.ProjectOnPlane(Vector3.right, axis);
                best.Normalize();
            }
            return best;
        }

        private static Vector3 Multiply(Vector3[] matrix, Vector3 v) =>
            new Vector3(Vector3.Dot(matrix[0], v), Vector3.Dot(matrix[1], v), Vector3.Dot(matrix[2], v));

        private static Vector3 Axis(int index) => index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;

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
