using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// "KESTREL DRYDOCK": the welcome screen and dev console. Opens on launch and on the right Y button (M on
    /// desktop). Point the right controller at a button and pull the trigger. While it is open the player cannot
    /// fire, and the wave director stays parked in the armory phase.
    /// </summary>
    public sealed class MissionDeck : MonoBehaviour
    {
        public static MissionDeck Instance { get; private set; }
        public static bool Open => Instance != null && Instance.root != null && Instance.root.gameObject.activeSelf;

        private const float Width = 1.5f, Height = 0.95f, Pad = 0.06f;

        public ArmoryRig Rig;

        private Transform root;
        private LineRenderer pointer;
        private TextMeshPro status;
        private readonly List<HudButton> buttons = new List<HudButton>();
        private HudButton hovered;
        private bool triggerHeld;

        private void Awake() => Instance = this;

        private void Start()
        {
            Build();
            Show(true);
        }

        private void Build()
        {
            root = new GameObject("Drydock").transform;
            root.SetParent(transform, false);

            var card = new GameObject("Card").transform;
            card.SetParent(root, false);
            var panel = UiKit.Panel(card, "Deck Panel", new Vector2(Width, Height), 0.11f);
            panel.SetColor("_Fill", new Color(0.012f, 0.02f, 0.045f, 0.93f));
            panel.SetFloat("_Chamfer", 0.07f);

            float left = -Width / 2f + Pad, top = Height / 2f;
            UiKit.Text(card, "Title", new Vector3(left, top - 0.028f, 0f), 0.05f, UiKit.Label, UiKit.Cyan,
                tracking: 22f, uppercase: true).text = "Kestrel Drydock";
            UiKit.Text(card, "Subtitle", new Vector3(Width / 2f - Pad, top - 0.036f, 0f), 0.022f, UiKit.Mono, UiKit.Muted,
                TextAlignmentOptions.TopRight, uppercase: true).text = "Vasa Reach · hive containment";
            UiKit.Text(card, "Blurb", new Vector3(left, top - 0.14f, 0f), 0.028f, UiKit.Body, UiKit.Ink, width: Width - Pad * 2f).text =
                "The refinery went dark eight hours ago. The hive learns from whatever kills it, so nothing you bring will work twice. " +
                "Talk to ARIA, fabricate something new, and hold the core.";

            // Row 1: waves.
            UiKit.Text(card, "Waves Label", new Vector3(left, top - 0.3f, 0f), 0.022f, UiKit.Label, UiKit.Muted,
                tracking: 14f, uppercase: true).text = "Deploy to wave";
            var director = WaveDirector.Instance;
            int waves = director != null ? director.Waves.Count : 5;
            float slot = (Width - Pad * 2f) / waves;
            for (int i = 0; i < waves; i++)
            {
                int index = i;
                string name = director != null ? director.Waves[i].Name : "Wave " + (i + 1);
                buttons.Add(HudButton.Create(card, new Vector3(left + slot * (i + 0.5f), top - 0.4f, -0.004f),
                    new Vector2(slot - 0.018f, 0.11f), $"{i + 1:00}", i == waves - 1 ? UiKit.Alien : UiKit.Cyan,
                    () => Deploy(index)));
                UiKit.Text(card, "Wave Name " + i, new Vector3(left + slot * (i + 0.5f), top - 0.47f, 0f), 0.018f,
                    UiKit.Mono, UiKit.Muted, TextAlignmentOptions.Top, width: slot, uppercase: true).text = name;
            }

            // Row 2: dev tools.
            UiKit.Text(card, "Dev Label", new Vector3(left, top - 0.56f, 0f), 0.022f, UiKit.Label, UiKit.Muted,
                tracking: 14f, uppercase: true).text = "Dev";
            float devSlot = (Width - Pad * 2f) / 4f;
            HudButton Dev(int column, string text, System.Action action) =>
                HudButton.Create(card, new Vector3(left + devSlot * (column + 0.5f), top - 0.66f, -0.004f),
                    new Vector2(devSlot - 0.018f, 0.1f), text, UiKit.Amber, action);

            buttons.Add(Dev(0, "Skip wave", () => { WaveDirector.Instance?.SkipWave(); Close("Skipped"); }));
            buttons.Add(Dev(1, "Restart run", () =>
            {
                Mothership.Instance?.Clear();
                WaveDirector.Instance?.RestartWave(0, 0f);
                Close("Run restarted");
            }));
            buttons.Add(Dev(2, "Mic test", () => { ShipAI.Instance?.ProbeMics(); status.text = "Speak: testing every microphone (see comms)"; }));
            buttons.Add(Dev(3, "Offline AI", () =>
            {
                var ai = ShipAI.Instance;
                if (ai == null) return;
                ai.Settings.Offline = !ai.Settings.Offline;
                status.text = ai.Settings.Offline ? "Offline mode: keyword fabrication (restart Play to apply)" : "Online mode (restart Play to apply)";
            }));

            buttons.Add(HudButton.Create(card, new Vector3(0f, -Height / 2f + 0.085f, -0.004f), new Vector2(Width - Pad * 2f, 0.12f),
                "Enter the station", UiKit.Go, () => Close("Good hunting")));

            status = UiKit.Text(card, "Status", new Vector3(0f, -Height / 2f + 0.018f, 0f), 0.02f, UiKit.Mono, UiKit.Muted,
                TextAlignmentOptions.Top, width: Width - Pad * 2f);
            status.text = "Point with the right controller · trigger to select · Y reopens";

            pointer = Mats.Line(root, UiKit.Cyan, 0.006f);
            pointer.enabled = false;
        }

        private void Deploy(int waveIndex)
        {
            Mothership.Instance?.Clear();
            WaveDirector.Instance?.RestartWave(waveIndex, 0f);
            Close("Deploying to wave " + (waveIndex + 1));
        }

        private void Close(string message)
        {
            if (!string.IsNullOrEmpty(message)) ShipAI.Instance?.SayShip(message, null);
            Show(false);
        }

        public void Show(bool on)
        {
            root.gameObject.SetActive(on);
            if (!on)
            {
                pointer.enabled = false;
                if (hovered != null) hovered.SetHovered(false);
                hovered = null;
                return;
            }
            // Park it in front of the player at eye height.
            var head = Rig.Head.transform;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            forward.Normalize();
            root.position = head.position + forward * 1.6f - Vector3.up * 0.15f;
            root.rotation = Quaternion.LookRotation(root.position - head.position);
        }

        public void Toggle() => Show(!root.gameObject.activeSelf);

        private void Update()
        {
            if (Rig == null || root == null) return;
            if (Rig.MenuPressed) Toggle();
            if (!root.gameObject.activeSelf) return;

            Vector3 origin = Rig.Aim.position;
            Vector3 direction = Rig.Aim.forward;
            pointer.enabled = true;
            Vector3 end = origin + direction * 4f;

            HudButton target = null;
            if (Physics.Raycast(origin, direction, out var hit, 6f, ~0, QueryTriggerInteraction.Ignore))
            {
                target = hit.collider.GetComponent<HudButton>();
                if (target != null) end = hit.point;
            }
            pointer.SetPosition(0, origin);
            pointer.SetPosition(1, end);
            pointer.widthMultiplier = target != null ? 0.01f : 0.005f;

            if (target != hovered)
            {
                if (hovered != null) hovered.SetHovered(false);
                hovered = target;
                if (hovered != null) hovered.SetHovered(true);
            }

            bool pressed = Rig.FireHeld;
            if (pressed && !triggerHeld && hovered != null) hovered.Press();
            triggerHeld = pressed;
        }
    }
}
