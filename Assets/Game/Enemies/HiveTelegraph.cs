using UnityEngine;

namespace Armory
{
    /// <summary>A grounded hazard outline and countdown arc, owned by the encounter that created it.</summary>
    public sealed class HiveTelegraph : MonoBehaviour
    {
        private LineRenderer outline, progress;
        private float radius;
        private readonly Vector3[] points = new Vector3[65];

        public static HiveTelegraph Create(Transform owner, Vector3 center, float radius, Color color)
        {
            var go = new GameObject("Hive Attack Telegraph");
            go.transform.SetParent(owner, false);
            go.transform.position = center + Vector3.up * 0.08f;
            var marker = go.AddComponent<HiveTelegraph>();
            marker.radius = radius;
            marker.outline = Mats.Line(go.transform, color, 0.12f);
            marker.progress = Mats.Line(go.transform, Color.white, 0.2f);
            marker.Draw(marker.outline, 1f);
            return marker;
        }

        public void SetProgress(float value) => Draw(progress, Mathf.Clamp01(value));

        private void Draw(LineRenderer line, float fraction)
        {
            for (int i = 0; i < points.Length; i++)
            {
                float angle = i / 64f * fraction * Mathf.PI * 2f;
                points[i] = transform.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            }
            line.positionCount = points.Length;
            line.SetPositions(points);
        }
    }
}
