using TMPro;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// The armory phase is untimed, so it needs an obvious way out: this panel floats in front of the player with a
    /// START WAVE button (point and pull the trigger), and repeats the other ways to begin.
    /// </summary>
    public sealed class ArmoryPrompt : MonoBehaviour
    {
        public ArmoryRig Rig;

        private const float Distance = 1.9f;
        private Transform panel;
        private TextMeshPro title;
        private bool shown;

        private void Start()
        {
            panel = new GameObject("Prompt").transform;
            panel.SetParent(transform, false);

            var card = UiKit.Panel(panel, "Prompt Panel", new Vector2(0.72f, 0.3f), 0.05f, UiKit.Go);
            card.SetColor("_Fill", new Color(0.012f, 0.03f, 0.03f, 0.9f));

            title = UiKit.Text(panel, "Title", new Vector3(0f, 0.105f, -0.003f), 0.03f, UiKit.Label, UiKit.Go,
                TextAlignmentOptions.Top, width: 0.68f, tracking: 14f, uppercase: true);
            UiKit.Text(panel, "Hint", new Vector3(0f, 0.052f, -0.003f), 0.022f, UiKit.Mono, UiKit.Muted,
                TextAlignmentOptions.Top, width: 0.68f, uppercase: true).text = "say \"ready\" · press A · or shoot this";

            HudButton.Create(panel, new Vector3(0f, -0.06f, -0.004f), new Vector2(0.56f, 0.11f), "Start wave", UiKit.Go,
                () => WaveDirector.Instance?.RequestWaveStart());
            panel.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            var director = WaveDirector.Instance;
            if (director == null || Rig == null) return;

            bool want = director.InArmory && !MissionDeck.Open;
            if (want != shown)
            {
                shown = want;
                panel.gameObject.SetActive(want);
            }
            if (!want) return;

            if (director.Current != null) title.text = "Next: " + director.Current.Name;

            // Sits below eye line so it never covers the aliens' approach.
            var head = Rig.Head.transform;
            Vector3 forward = head.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
            Vector3 target = head.position + forward * Distance - Vector3.up * 0.55f;
            panel.position = Vector3.Lerp(panel.position, target, 4f * Time.deltaTime);
            panel.rotation = Quaternion.LookRotation(panel.position - head.position);
        }
    }
}
