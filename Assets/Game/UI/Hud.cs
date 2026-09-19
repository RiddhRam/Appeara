using System.Text;
using UnityEngine;

namespace Armory
{
    /// <summary>Wrist panel (left hand: wave, core, weapon, adaptations) + floating subtitle panel that lazily follows the head.</summary>
    public sealed class Hud : MonoBehaviour
    {
        public ArmoryRig Rig;

        private TextMesh wrist;
        private TextMesh subtitles;
        private TextMesh banner;
        private Transform panel;
        private float bannerUntil;
        private float nextRefresh;

        private void Start()
        {
            wrist = WorldText.Create(Rig.LeftHand, new Vector3(0f, 0.07f, 0.03f), 0.0019f, new Color(0.75f, 0.95f, 1f));
            wrist.gameObject.AddComponent<Billboard>();
            Backplate(wrist.transform, new Vector3(0.3f, 0.13f, 1f));

            panel = new GameObject("Subtitle Panel").transform;
            panel.SetParent(transform, false);
            subtitles = WorldText.Create(panel, new Vector3(0f, -0.1f, 0f), 0.0075f, Color.white);
            banner = WorldText.Create(panel, new Vector3(0f, 0.22f, 0f), 0.014f, Color.cyan);
            Backplate(subtitles.transform, new Vector3(1.9f, 0.55f, 1f));
            panel.position = Rig.Head.transform.position + Rig.Head.transform.forward * 2.5f;
        }

        /// <summary>Dark translucent card behind text so it reads against the bright station.</summary>
        private static void Backplate(Transform text, Vector3 size)
        {
            var plate = Mats.Shape(PrimitiveType.Quad, text, new Vector3(0f, 0f, 0.01f), Vector3.one, Mats.Glow(new Color(0.02f, 0.03f, 0.06f), 0.75f), name: "Backplate");
            // Text meshes are scaled by characterSize, so undo that for a size in metres.
            var parentScale = text.lossyScale;
            plate.transform.localScale = new Vector3(size.x / Mathf.Max(parentScale.x, 1e-4f), size.y / Mathf.Max(parentScale.y, 1e-4f), 1f);
        }

        public void ShowBanner(string text, Color color)
        {
            if (banner == null) return;
            banner.text = text;
            banner.color = color;
            bannerUntil = Time.time + 4f;
        }

        private void LateUpdate()
        {
            if (Rig == null || panel == null) return;
            var head = Rig.Head.transform;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 target = head.position + forward.normalized * 2.5f + Vector3.down * 0.25f;
            panel.position = Vector3.Lerp(panel.position, target, 2.5f * Time.deltaTime);
            panel.rotation = Quaternion.LookRotation(panel.position - head.position);

            var c = banner.color;
            c.a = Mathf.Clamp01(bannerUntil - Time.time);
            banner.color = c;

            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.1f;
            Refresh();
        }

        private void Refresh()
        {
            var director = WaveDirector.Instance;
            var core = StationCore.Instance;
            var ai = ShipAI.Instance;
            var ms = Mothership.Instance;

            var w = new StringBuilder();
            if (director != null) w.AppendLine(director.State);
            if (director != null && director.WaveIndex >= 0) w.Append("Aliens: ").Append(director.Alive + director.PendingSpawns).Append("   ");
            if (core != null) w.Append("Core: ").Append(Mathf.CeilToInt(core.Health)).AppendLine("%");
            if (ai != null && ai.Current != null)
            {
                var spec = ai.Current.Spec;
                w.Append("Weapon: ").AppendLine(spec.Name);
                w.Append("  ").Append(spec.FireMode).Append(" / ").Append(spec.Payload).Append(" / ").AppendLine(spec.Mods.ToString());
            }
            if (ms != null) w.Append("Alien adaptations: ").AppendLine(ms.Describe());
            w.Append(Rig.IsXR ? "[L grip] talk  [R trig] fire  [L stick] teleport" : "[V] talk [T] type [1-5] presets [LMB] fire [RMB] look [Q/E] pads");
            wrist.text = w.ToString();

            var s = new StringBuilder();
            if (ai != null)
            {
                if (ai.Listening) s.AppendLine("<color=#7CFC00>● LISTENING...</color>");
                else if (!string.IsNullOrEmpty(ai.Status)) s.AppendLine(ai.Status);
                foreach (var (speaker, text) in ai.Subtitles)
                {
                    string color = speaker == "MOTHERSHIP" ? "#FF5A6A" : speaker == "YOU" ? "#FFFFFF" : "#6FE3FF";
                    s.Append("<color=").Append(color).Append('>').Append(speaker).Append(":</color> ").AppendLine(Wrap(text, 60));
                }
            }
            subtitles.text = s.ToString();
        }

        private static string Wrap(string text, int width)
        {
            var builder = new StringBuilder();
            int column = 0;
            foreach (var word in text.Split(' '))
            {
                if (column + word.Length > width && column > 0) { builder.Append("\n    "); column = 4; }
                builder.Append(word).Append(' ');
                column += word.Length + 1;
            }
            return builder.ToString();
        }
    }
}
