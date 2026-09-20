using System.Text;
using Armory.Core;
using TMPro;
using UnityEngine;

namespace Armory
{
    /// <summary>
    /// Wrist telemetry card (left hand) + comms card and wave banner that lazily follow the head.
    /// Type: IBM Plex Sans Condensed for labels/prose, IBM Plex Mono for readouts (see UiKit).
    /// </summary>
    public sealed class Hud : MonoBehaviour
    {
        public ArmoryRig Rig;

        // Wrist card
        private const float WristW = 0.24f, WristH = 0.155f, WristHeader = 0.022f, Pad = 0.012f, Column = 0.052f;
        private TextMeshPro waveCounter, waveName, coreValue, hostiles, weaponName, weaponTraits, adaptations, hint;
        private Material coreBar;
        private Material shieldBar;
        private TextMeshPro shieldValue;

        // Comms card
        private const float CommsW = 1.5f, CommsH = 0.5f, CommsHeader = 0.064f, CommsPad = 0.04f, CommsDistance = 2.0f, SpeakerColumn = 0.24f;
        private const int Rows = 3;
        private Transform follow;
        /// <summary>Anchor other world panels here so they move as one block instead of fighting for the same space.</summary>
        public Transform Follow => follow;
        private Transform commsCard;
        private TextMeshPro commsStatus;
        private Material commsBar;
        private readonly TextMeshPro[] speakers = new TextMeshPro[Rows];
        private readonly TextMeshPro[] lines = new TextMeshPro[Rows];

        // Banner
        private TextMeshPro banner;
        private Transform bannerRuleLeft, bannerRuleRight;
        private float bannerStart = -10f;
        private const float BannerSeconds = 4f;

        private float nextRefresh;

        private void Awake()
        {
            // Created in Awake so other panels can dock to it in their Start.
            follow = new GameObject("HUD Follow").transform;
            follow.SetParent(transform, false);
        }

        private void Start()
        {
            BuildWrist();
            BuildComms();
        }

        private void BuildWrist()
        {
            var card = new GameObject("Wrist Card").transform;
            card.SetParent(Rig.LeftHand, false);
            card.localPosition = new Vector3(0f, 0.1f, 0.02f);
            card.gameObject.AddComponent<Billboard>();
            UiKit.Panel(card, "Wrist Panel", new Vector2(WristW, WristH), WristHeader);

            float left = -WristW / 2f + Pad, right = WristW / 2f - Pad, top = WristH / 2f;
            UiKit.Text(card, "Header", new Vector3(left, top - 0.0065f, 0f), 0.0085f, UiKit.Label, UiKit.CyanDim, tracking: 14f, uppercase: true).text = "Station telemetry";
            waveCounter = UiKit.Text(card, "Wave Counter", new Vector3(right, top - 0.0065f, 0f), 0.0085f, UiKit.MonoStrong, UiKit.Cyan, TextAlignmentOptions.TopRight);
            waveName = UiKit.Text(card, "Wave Name", new Vector3(left, top - WristHeader - 0.006f, 0f), 0.016f, UiKit.Body, UiKit.Ink, width: WristW - Pad * 2f);

            float y = top - 0.06f;
            RowLabel(card, "Core", left, y);
            coreBar = UiKit.Bar(card, "Core Bar", new Vector3(left + Column, y - 0.0015f, 0f), new Vector2(0.12f, 0.0055f), 24, UiKit.Cyan);
            coreValue = UiKit.Text(card, "Core Value", new Vector3(right, y + 0.0005f, 0f), 0.0095f, UiKit.MonoStrong, UiKit.Ink, TextAlignmentOptions.TopRight);

            y -= 0.017f;
            RowLabel(card, "Shields", left, y);
            shieldBar = UiKit.Bar(card, "Shield Bar", new Vector3(left + Column, y - 0.0015f, 0f), new Vector2(0.12f, 0.0055f), 24, UiKit.Go);
            shieldValue = UiKit.Text(card, "Shield Value", new Vector3(right, y + 0.0005f, 0f), 0.0095f, UiKit.MonoStrong, UiKit.Ink, TextAlignmentOptions.TopRight);

            y -= 0.017f;
            RowLabel(card, "Hostiles", left, y);
            hostiles = UiKit.Text(card, "Hostiles", new Vector3(left + Column, y + 0.0005f, 0f), 0.0095f, UiKit.MonoStrong, UiKit.Ink);

            y -= 0.02f;
            RowLabel(card, "Armed", left, y);
            weaponName = UiKit.Text(card, "Weapon", new Vector3(left + Column, y + 0.002f, 0f), 0.0115f, UiKit.Body, UiKit.Ink, width: WristW - Pad * 2f - Column);
            weaponTraits = UiKit.Text(card, "Traits", new Vector3(left + Column, y - 0.0115f, 0f), 0.0072f, UiKit.Mono, UiKit.Muted, width: WristW - Pad * 2f - Column, uppercase: true);

            y -= 0.034f;
            RowLabel(card, "Adapted", left, y);
            adaptations = UiKit.Text(card, "Adaptations", new Vector3(left + Column, y + 0.0005f, 0f), 0.0072f, UiKit.Mono, UiKit.Alien, width: WristW - Pad * 2f - Column, uppercase: true);

            hint = UiKit.Text(card, "Hint", new Vector3(0f, -WristH / 2f - 0.005f, 0f), 0.0068f, UiKit.Mono, UiKit.Muted, TextAlignmentOptions.Top, width: WristW, uppercase: true);
        }

        private static void RowLabel(Transform card, string label, float x, float y)
        {
            UiKit.Text(card, label + " Label", new Vector3(x, y, 0f), 0.0072f, UiKit.Label, UiKit.Muted, tracking: 12f, uppercase: true).text = label;
        }

        private void BuildComms()
        {
            var card = new GameObject("Comms Card").transform;
            card.SetParent(follow, false);
            commsCard = card;
            var commsMaterial = UiKit.Panel(card, "Comms Panel", new Vector2(CommsW, CommsH), CommsHeader);
            // Darker glass at reading distance: the lit station floor otherwise washes it grey.
            commsMaterial.SetColor("_Fill", new Color(0.012f, 0.02f, 0.045f, 0.9f));
            commsMaterial.SetFloat("_Chamfer", 0.04f);
            commsMaterial.SetFloat("_Border", 0.0025f);
            float left = -CommsW / 2f + CommsPad, right = CommsW / 2f - CommsPad, top = CommsH / 2f;

            UiKit.Text(card, "Header", new Vector3(left, top - 0.02f, 0f), 0.024f, UiKit.Label, UiKit.CyanDim, tracking: 16f, uppercase: true).text = "Comms  <color=#3A5A70>//</color>  ARIA uplink";
            commsStatus = UiKit.Text(card, "Status", new Vector3(right, top - 0.019f, 0f), 0.024f, UiKit.MonoStrong, UiKit.Muted, TextAlignmentOptions.TopRight, uppercase: true);
            commsBar = UiKit.Bar(card, "Progress", new Vector3(right - 0.56f, top - 0.028f, 0f), new Vector2(0.2f, 0.009f), 16, UiKit.Amber);
            commsBar.SetFloat("_Pulse", 1f);

            for (int i = 0; i < Rows; i++)
            {
                float y = top - CommsHeader - 0.034f - i * 0.128f;
                speakers[i] = UiKit.Text(card, "Speaker " + i, new Vector3(left, y - 0.009f, 0f), 0.028f, UiKit.Label, UiKit.Cyan, tracking: 14f, uppercase: true, width: SpeakerColumn);
                lines[i] = UiKit.Text(card, "Line " + i, new Vector3(left + SpeakerColumn, y, 0f), 0.042f, UiKit.Body, UiKit.Ink, width: CommsW - CommsPad * 2f - SpeakerColumn);
            }

            var bannerRoot = new GameObject("Banner").transform;
            bannerRoot.SetParent(follow, false);
            bannerRoot.localPosition = new Vector3(0f, CommsH / 2f + 0.14f, 0f);
            banner = UiKit.Text(bannerRoot, "Banner Text", Vector3.zero, 0.1f, UiKit.Label, UiKit.Cyan, TextAlignmentOptions.Center, width: 3f, uppercase: true);
            bannerRuleLeft = Rule(bannerRoot);
            bannerRuleRight = Rule(bannerRoot);
            SetBannerAlpha(0f);
        }

        private static Transform Rule(Transform parent)
        {
            var rule = Mats.Shape(PrimitiveType.Quad, parent, Vector3.zero, new Vector3(0.3f, 0.002f, 1f), Mats.Glow(UiKit.Cyan, 0.8f), name: "Banner Rule");
            return rule.transform;
        }

        public void ShowBanner(string text, Color color)
        {
            if (banner == null) return;
            banner.text = text;
            banner.color = color;
            bannerStart = Time.time;
            banner.ForceMeshUpdate();
            float half = banner.preferredWidth * 0.5f + 0.08f;
            bannerRuleLeft.localPosition = new Vector3(-half - 0.15f, 0f, 0f);
            bannerRuleRight.localPosition = new Vector3(half + 0.15f, 0f, 0f);
            bannerRuleLeft.GetComponent<Renderer>().sharedMaterial = Mats.Glow(color, 0.8f);
            bannerRuleRight.GetComponent<Renderer>().sharedMaterial = Mats.Glow(color, 0.8f);
        }

        private void SetBannerAlpha(float alpha)
        {
            var c = banner.color;
            c.a = alpha;
            banner.color = c;
            bannerRuleLeft.gameObject.SetActive(alpha > 0.01f);
            bannerRuleRight.gameObject.SetActive(alpha > 0.01f);
        }

        private void LateUpdate()
        {
            if (Rig == null || follow == null) return;
            // The drydock replaces the comms block while it is open.
            bool deckOpen = MissionDeck.Open;
            if (commsCard != null && commsCard.gameObject.activeSelf == deckOpen) commsCard.gameObject.SetActive(!deckOpen);
            var head = Rig.Head.transform;
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            Vector3 target = head.position + forward.normalized * CommsDistance + Vector3.down * 0.45f;
            follow.position = Vector3.Lerp(follow.position, target, 2.5f * Time.deltaTime);
            follow.rotation = Quaternion.LookRotation(follow.position - head.position);

            // Banner: tracking tightens in (cinematic), holds, fades.
            float t = Time.time - bannerStart;
            if (t < BannerSeconds)
            {
                float intro = Mathf.Clamp01(t / 0.6f);
                banner.characterSpacing = Mathf.Lerp(60f, 18f, 1f - (1f - intro) * (1f - intro));
                SetBannerAlpha(Mathf.Min(intro * 1.5f, Mathf.Clamp01((BannerSeconds - t) / 0.8f)));
            }
            else if (banner.color.a > 0f) SetBannerAlpha(0f);

            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.1f;
            RefreshWrist();
            RefreshComms();
        }

        private void RefreshWrist()
        {
            var director = WaveDirector.Instance;
            var core = StationCore.Instance;
            var ai = ShipAI.Instance;
            var ms = Mothership.Instance;

            if (director != null)
            {
                int total = director.Waves.Count;
                waveCounter.text = director.WaveIndex >= 0 ? $"W{director.WaveIndex + 1:00}/{total:00}" : $"W00/{total:00}";
                waveName.text = director.Current != null ? director.Current.Name : "Standing by";
                hostiles.text = (director.Alive + director.PendingSpawns).ToString("00");
            }
            if (core != null)
            {
                float fraction = core.Health / core.MaxHealth;
                coreBar.SetFloat("_Fill", fraction);
                var color = fraction > 0.5f ? UiKit.Cyan : fraction > 0.25f ? UiKit.Amber : UiKit.Alien;
                coreBar.SetColor("_On", color);
                coreBar.SetFloat("_Pulse", fraction <= 0.25f ? 1f : 0f);
                coreValue.text = Mathf.CeilToInt(core.Health) + "%";
                coreValue.color = fraction > 0.25f ? UiKit.Ink : UiKit.Alien;
            }
            var game = ArmoryGame.Instance;
            if (game != null && shieldBar != null)
            {
                float shields = game.Vitals.Fraction;
                shieldBar.SetFloat("_Fill", shields);
                shieldBar.SetColor("_On", shields > 0.5f ? UiKit.Go : shields > 0.25f ? UiKit.Amber : UiKit.Alien);
                shieldBar.SetFloat("_Pulse", shields <= 0.25f ? 1f : 0f);
                shieldValue.text = Mathf.CeilToInt(game.Vitals.Shield) + "%";
                shieldValue.color = shields > 0.25f ? UiKit.Ink : UiKit.Alien;
            }
            if (ai != null && ai.Current != null)
            {
                var spec = ai.Current.Spec;
                weaponName.text = spec.Name;
                weaponName.color = Color.Lerp(spec.Color, Color.white, 0.55f);
                string mods = spec.Mods == Mods.None ? "" : " · " + spec.Mods.ToString().Replace(", ", " · ");
                weaponTraits.text = $"{spec.FireMode} · {spec.Payload}{mods}";
            }
            if (ms != null && ms.HasPendingAnalysis)
            {
                string waiting = ms.WaitingForModel ? " · uplink" : "";
                adaptations.text = $"<color=#FF4D5A>hive analyzing {ms.PendingWeaponName} · {Mathf.CeilToInt(ms.AnalysisRemaining)}s{waiting}</color>";
            }
            else adaptations.text = ms == null || ms.ActivePackage == null
                ? "<color=#5A6678>none detected</color>"
                : ms.ActivePackage.ToString();
            hint.text = Rig.IsXR
                ? "grip talk · trigger fire · stick move · click sprint · pads warp"
                : "WASD move · shift sprint · V talk · T type · 1-5 presets · M menu";
        }

        private void RefreshComms()
        {
            var ai = ShipAI.Instance;
            if (ai == null) return;

            bool fabricating = ai.Busy;
            // While talking the bar is a live input meter: if it never moves, the mic is not reaching Unity.
            float fill = ai.Listening ? Mathf.Clamp01(ai.MicLevel * 6f) : fabricating ? Mathf.Repeat(Time.time * 0.45f, 1f) : 0f;
            commsBar.SetFloat("_Fill", fill);
            commsBar.SetColor("_On", ai.Listening ? (ai.MicLevel > 0.01f ? UiKit.Go : UiKit.Alien) : UiKit.Amber);
            commsBar.SetColor("_Off", ai.Listening || fabricating ? new Color(0.25f, 0.3f, 0.35f, 0.3f) : new Color(0f, 0f, 0f, 0f));
            if (ai.Listening) SetStatus(ai.MicLevel > 0.01f ? "● Listening" : "● No input", ai.MicLevel > 0.01f ? UiKit.Go : UiKit.Alien);
            else if (fabricating) SetStatus(ai.Status, UiKit.Amber);
            else if (!string.IsNullOrEmpty(ai.Status) && ai.Status.StartsWith("Built")) SetStatus(ai.Status, UiKit.Cyan);
            else if (!string.IsNullOrEmpty(ai.Status)) SetStatus(ai.Status, UiKit.Muted);
            else SetStatus("Standby", UiKit.Muted);

            // Newest message at the bottom; older rows fade.
            var subs = ai.Subtitles;
            for (int i = 0; i < Rows; i++)
            {
                int index = subs.Count - Rows + i;
                if (index < 0) { speakers[i].text = ""; lines[i].text = ""; continue; }
                var (speaker, text) = subs[index];
                Color color = speaker == "MOTHERSHIP" ? UiKit.Alien : speaker == "YOU" ? UiKit.Ink : UiKit.Cyan;
                float age = (Rows - 1 - i);
                float alpha = 1f - age * 0.28f;
                speakers[i].text = speaker == "YOU" ? "You" : speaker == "MOTHERSHIP" ? "Hive" : "Aria";
                speakers[i].color = new Color(color.r, color.g, color.b, alpha);
                lines[i].text = speaker == "YOU" ? "“" + text + "”" : text;
                lines[i].color = new Color(UiKit.Ink.r, UiKit.Ink.g, UiKit.Ink.b, alpha * (speaker == "YOU" ? 0.8f : 1f));
            }
        }

        private void SetStatus(string text, Color color)
        {
            commsStatus.text = text;
            commsStatus.color = color;
        }
    }
}
