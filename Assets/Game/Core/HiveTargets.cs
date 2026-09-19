using UnityEngine;

namespace Armory.Core
{
    /// <summary>
    /// Geometry for the Hive Avatar's shootable organs.
    /// The organs follow the model's bones, and those bones sit inside the torso volume, so without pushing them
    /// clear the body collider is always hit first and the organs cannot be shot at all (measured: body at 12.2 m,
    /// organ surface at 13.4 m). Everything here is pure so the layout is unit-tested.
    /// </summary>
    public static class HiveTargets
    {
        public const float TorsoRadius = 2f;
        public const float TorsoHeight = 6f;
        public const float TorsoCenterY = 6f;
        public const float OrganRadius = 1.05f;
        /// <summary>Gap between the torso surface and an organ's surface, so a ray always reaches the organ first.</summary>
        public const float Clearance = 0.8f;
        /// <summary>Body hits this close to an unbroken organ still count as organ hits (VR aiming is coarse).</summary>
        public const float RouteRadius = OrganRadius + 1.35f;
        /// <summary>Extra height and set-back for an axis organ so the front-mounted crest cannot shadow it.</summary>
        public const float AxisLift = 1.3f;
        public const float AxisSetBack = 2.2f;

        public static float MinAxisDistance => TorsoRadius + OrganRadius + Clearance;

        /// <summary>Highest point the torso capsule occupies, in local space.</summary>
        public static float TorsoTopY => TorsoCenterY + TorsoHeight * 0.5f + TorsoRadius;

        /// <summary>
        /// Moves an organ anchored to a bone out of the torso: sideways organs are pushed away from the body axis,
        /// and organs sitting on the axis (the spore sac) are lifted above the torso instead.
        /// </summary>
        public static Vector3 Protrude(Vector3 localAnchor)
        {
            var radial = new Vector2(localAnchor.x, localAnchor.z);
            if (radial.magnitude < 0.6f)
            {
                // On the axis: lift clear of the capsule, and sit it back so the crest does not block the line
                // of sight from the front (measured: the crest sphere was hit first before this set-back).
                float y = Mathf.Max(localAnchor.y, TorsoTopY + OrganRadius + Clearance) + AxisLift;
                return new Vector3(localAnchor.x, y, localAnchor.z - AxisSetBack);
            }
            float needed = Mathf.Max(radial.magnitude, MinAxisDistance);
            var pushed = radial.normalized * needed;
            return new Vector3(pushed.x, localAnchor.y, pushed.y);
        }

        /// <summary>True when the organ sits clear of the torso capsule and can be hit directly.</summary>
        public static bool IsClearOfTorso(Vector3 localPosition)
        {
            float axisDistance = new Vector2(localPosition.x, localPosition.z).magnitude;
            if (axisDistance >= MinAxisDistance - 0.001f) return true;
            return localPosition.y >= TorsoTopY + OrganRadius + Clearance - 0.001f;
        }
    }
}
