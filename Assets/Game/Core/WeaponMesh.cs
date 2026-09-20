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
            muzzle = new Vector3(Sane(spec.muzzleX), Sane(spec.muzzleY), Sane(spec.muzzleZ));
            // A muzzle on X, Y, or -Z is still useful: it tells us which way the model considered forward.
            // Only an entirely absent/zero muzzle needs the legacy +Z fallback.
            if (muzzle.sqrMagnitude <= MinPart * MinPart)
                muzzle = new Vector3(0f, 0f, Furthest(parts));
            return parts;
        }

        /// <summary>How far behind the hand the back of the weapon sits, so the grip is in the palm not in front of it.</summary>
        public const float GripSetBack = 0.06f;

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
        public static void Orient(List<MeshPart> parts, ref Vector3 muzzle)
        {
            if (parts == null || parts.Count < 2) return;
            Vector3 centre = Centroid(parts);
            // Principal axes, not bounding-box axes. A weapon returned at forty-five degrees has a bounding box
            // that is square in two of its dimensions, so ranking box edges picks an arbitrary one and the
            // weapon ends up wedged across the player's view. The spread of the actual mass has no such problem.
            Covariance(parts, centre, out var covariance);
            Vector3 lengthAxis = Dominant(covariance, Vector3.forward);
            Vector3 upAxis = DominantOrthogonalTo(covariance, lengthAxis);

            Vector3 forward = lengthAxis * ForwardSign(parts, centre, lengthAxis, upAxis, muzzle);
            Vector3 up = upAxis * UpSign(parts, centre, upAxis);
            // Every part sitting on one line leaves nothing to orient by; the model's own frame is as good as any.
            if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.99f) return;

            var rotation = Quaternion.Inverse(Quaternion.LookRotation(forward, up));
            foreach (var part in parts)
            {
                part.Position = rotation * part.Position;
                // Composed, not replaced: a part's own scale is applied along its own axes, so turning the
                // weapon has to turn each part with it or the barrel becomes a slab.
                part.Rotation = (rotation * Quaternion.Euler(part.Rotation)).eulerAngles;
            }
            muzzle = rotation * muzzle;
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

        public static void Bounds(List<MeshPart> parts, out Vector3 min, out Vector3 max)
        {
            min = Vector3.positiveInfinity;
            max = Vector3.negativeInfinity;
            foreach (var part in parts)
            {
                min = Vector3.Min(min, part.Position - part.Scale * 0.5f);
                max = Vector3.Max(max, part.Position + part.Scale * 0.5f);
            }
        }

        /// <summary>Where the hand closes: the centre of whatever hangs below the weapon's mass, else its middle.</summary>
        private static float GripHeight(List<MeshPart> parts, float centreY)
        {
            float weighted = 0f, total = 0f;
            foreach (var part in parts)
            {
                if (part.Position.y >= centreY) continue;
                float volume = Mathf.Max(Volume(part), 1e-6f);
                weighted += part.Position.y * volume;
                total += volume;
            }
            return total > 0f ? weighted / total : centreY;
        }

        /// <summary>Which end the shot leaves from. A placed muzzle settles it; otherwise barrels are the thin end.</summary>
        private static float ForwardSign(List<MeshPart> parts, Vector3 centre, Vector3 lengthAxis, Vector3 upAxis, Vector3 muzzle)
        {
            float span = Span(parts, centre, lengthAxis);
            float offset = Vector3.Dot(muzzle - centre, lengthAxis);
            if (Mathf.Abs(offset) > span * 0.05f) return Mathf.Sign(offset);

            Vector3 rightAxis = Vector3.Cross(upAxis, lengthAxis).normalized;
            float front = 0f, back = 0f;
            int frontCount = 0, backCount = 0;
            foreach (var part in parts)
            {
                // Thickness across the barrel, measured on the weapon's own axes rather than the model's.
                float section = Extent(part, upAxis) * Extent(part, rightAxis);
                if (Vector3.Dot(part.Position - centre, lengthAxis) >= 0f) { front += section; frontCount++; }
                else { back += section; backCount++; }
            }
            if (frontCount == 0 || backCount == 0) return 1f;
            return front / frontCount <= back / backCount ? 1f : -1f;
        }

        /// <summary>The grip hangs off the barrel line, so the side carrying more outlying mass is down.</summary>
        private static float UpSign(List<MeshPart> parts, Vector3 centre, Vector3 upAxis)
        {
            float above = 0f, below = 0f;
            foreach (var part in parts)
            {
                float offset = Vector3.Dot(part.Position - centre, upAxis);
                if (offset >= 0f) above += Volume(part) * offset;
                else below += Volume(part) * -offset;
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

        private static float Sane(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, -MaxCoordinate, MaxCoordinate);

        private static float Wrap(float degrees) => float.IsNaN(degrees) || float.IsInfinity(degrees) ? 0f : Mathf.Repeat(degrees, 360f);
    }
}
