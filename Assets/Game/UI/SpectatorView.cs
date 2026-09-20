using System.Text;
using Armory.Core;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Armory
{
    /// <summary>
    /// The monitor view for everyone who is not wearing the headset. A second camera chases the player in third
    /// person and carries a broadcast overlay (what the player said, what ARIA built, what the hive adapted), because
    /// the mirrored HMD eye is unreadable and the PC screen is what judges and the demo video actually see.
    /// </summary>
    public sealed class SpectatorView : MonoBehaviour
    {
        /// <summary>Unnamed layer 31: the overlay is world geometry, so only the spectator camera may be allowed to see it.</summary>
        private const int OverlayLayer = 31;
        private const float Distance = 4.2f, Height = 2.1f, ThreatRange = 60f, Damping = 3.5f;
        // Overlay space: the root is scaled so y runs -1..1 across the screen and x runs -aspect..aspect.
        private const float OverlayDistance = 1f, Margin = 0.07f;
        private const float WeaponW = 1.25f, WeaponH = 0.4f, HiveW = 1.15f, HiveH = 0.4f, SaidW = 2.4f, SaidH = 0.34f, BlueprintSize = 0.62f;

        private static readonly int HasTex = Shader.PropertyToID("_HasTex");
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");

        public static SpectatorView Instance { get; private set; }

        public ArmoryRig Rig;
        public Camera Cam { get; private set; }

        private Transform overlay, weaponCard, hiveCard, saidCard, blueprint;
        private TextMeshPro weaponName, weaponChips, waveLine, hiveLine, waveCounter, said;
        private Material blueprintMaterial;
        private BlueprintHologram blueprintSource;
        private Texture blueprintTexture;
        private string lastSaid = "";
        private float aspect = -1f, nextRefresh;
        private bool fullscreen, framed, xrDetected, renderInXR;

        /// <summary>Built from ArmoryGame once the rig exists; the rig camera keeps the headset.</summary>
        public static SpectatorView Create(ArmoryRig rig)
        {
            var view = new GameObject("Spectator View").AddComponent<SpectatorView>();
            view.transform.SetParent(rig.transform.parent, false);
            view.Rig = rig;
            return view;
        }

        private void Awake() => Instance = this;

        private void Start()
        {
            BuildCamera();
            BuildOverlay();
        }

        private void BuildCamera()
        {
            var camObject = new GameObject("Spectator Camera");
            camObject.transform.SetParent(transform, false);
            Cam = camObject.AddComponent<Camera>();
            // Display 1 is the Game view and the PC monitor; None keeps this render out of the HMD swapchain.
            Cam.targetDisplay = 0;
            Cam.stereoTargetEye = StereoTargetEyeMask.None;
            Cam.depth = Rig.Head.depth + 10f;
            Cam.fieldOfView = 55f;
            Cam.nearClipPlane = 0.05f;
            Cam.farClipPlane = 400f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = Rig.Head.backgroundColor;
            Rig.Head.cullingMask &= ~(1 << OverlayLayer);
        }

        private void BuildOverlay()
        {
            overlay = new GameObject("Overlay").transform;
            overlay.SetParent(Cam.transform, false);
            overlay.localPosition = Vector3.forward * OverlayDistance;

            weaponCard = Card("Weapon Card", new Vector2(WeaponW, WeaponH), 0.085f, UiKit.Cyan);
            UiKit.Text(weaponCard, "Header", new Vector3(-WeaponW / 2f + 0.05f, WeaponH / 2f - 0.026f, 0f), 0.032f, UiKit.Label, UiKit.CyanDim,
                tracking: 16f, uppercase: true).text = "Fabricated";
            weaponName = UiKit.Text(weaponCard, "Name", new Vector3(-WeaponW / 2f + 0.05f, WeaponH / 2f - 0.11f, 0f), 0.072f, UiKit.Body, UiKit.Ink, width: WeaponW - 0.1f);
            weaponChips = UiKit.Text(weaponCard, "Chips", new Vector3(-WeaponW / 2f + 0.05f, WeaponH / 2f - 0.235f, 0f), 0.036f, UiKit.MonoStrong, UiKit.Ink, width: WeaponW - 0.1f);

            hiveCard = Card("Hive Card", new Vector2(HiveW, HiveH), 0.085f, UiKit.Alien);
            UiKit.Text(hiveCard, "Header", new Vector3(-HiveW / 2f + 0.05f, HiveH / 2f - 0.026f, 0f), 0.032f, UiKit.Label, UiKit.Alien,
                tracking: 16f, uppercase: true).text = "Hive adaptation";
            waveCounter = UiKit.Text(hiveCard, "Wave Counter", new Vector3(HiveW / 2f - 0.05f, HiveH / 2f - 0.026f, 0f), 0.032f, UiKit.MonoStrong, UiKit.Ink, TextAlignmentOptions.TopRight);
            waveLine = UiKit.Text(hiveCard, "Wave", new Vector3(-HiveW / 2f + 0.05f, HiveH / 2f - 0.105f, 0f), 0.05f, UiKit.Body, UiKit.Ink, width: HiveW - 0.1f);
            hiveLine = UiKit.Text(hiveCard, "Adaptation", new Vector3(-HiveW / 2f + 0.05f, HiveH / 2f - 0.19f, 0f), 0.038f, UiKit.Mono, UiKit.Alien, width: HiveW - 0.1f, uppercase: true);

            saidCard = Card("Said Card", new Vector2(SaidW, SaidH), 0f, UiKit.Cyan);
            UiKit.Text(saidCard, "Speaker", new Vector3(-SaidW / 2f + 0.05f, SaidH / 2f - 0.022f, 0f), 0.03f, UiKit.Label, UiKit.CyanDim,
                tracking: 18f, uppercase: true).text = "You said";
            said = UiKit.Text(saidCard, "Said", new Vector3(0f, -0.018f, 0f), 0.082f, UiKit.Body, UiKit.Ink, TextAlignmentOptions.Center, width: SaidW - 0.14f);

            blueprintMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            blueprint = Mats.Shape(PrimitiveType.Quad, weaponCard, new Vector3(-WeaponW / 2f + BlueprintSize / 2f, -WeaponH / 2f - 0.05f - BlueprintSize / 2f, 0f),
                new Vector3(BlueprintSize, BlueprintSize, 1f), blueprintMaterial, name: "Blueprint").transform;
            blueprint.gameObject.SetActive(false);

            SetLayer(overlay);
        }

        private Transform Card(string cardName, Vector2 size, float headerHeight, Color line)
        {
            var card = new GameObject(cardName).transform;
            card.SetParent(overlay, false);
            var panel = UiKit.Panel(card, cardName + " Panel", size, headerHeight, line);
            // Opaque glass: the overlay sits over a lit arena and has to stay readable in a compressed video capture.
            panel.SetColor("_Fill", new Color(0.012f, 0.02f, 0.045f, 0.88f));
            panel.SetFloat("_Chamfer", 0.05f);
            return card;
        }

        private static void SetLayer(Transform root)
        {
            root.gameObject.layer = OverlayLayer;
            for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i));
        }

        private void LateUpdate()
        {
            if (Rig == null || Cam == null) return;
            if (Rig.IsXR) xrDetected = true;
            if (Keyboard.current != null && !Rig.TextEntryActive && Keyboard.current.f10Key.wasPressedThisFrame)
                renderInXR = !renderInXR;

            // A third full-world render is particularly expensive after the headset has already rendered two eyes.
            // By default Unity's XR mirror supplies the monitor image. F10 restores the cinematic spectator camera
            // for capture/demo sessions where its presentation is worth the GPU cost.
            bool shouldRender = !xrDetected || renderInXR;
            if (Cam.enabled != shouldRender) Cam.enabled = shouldRender;
            if (!shouldRender) return;

            if (Keyboard.current != null && !Rig.TextEntryActive && Keyboard.current.f9Key.wasPressedThisFrame) fullscreen = !fullscreen;
            // On desktop the first-person view is how the player aims, so the chase cam is a corner inset until F9.
            Cam.rect = Rig.IsXR || fullscreen ? new Rect(0f, 0f, 1f, 1f) : new Rect(0.68f, 0.03f, 0.3f, 0.3f);

            Frame();
            Layout();
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + 0.15f;
            Refresh();
        }

        /// <summary>Chase the player from behind and above, swinging round so the nearest threat stays in shot.</summary>
        private void Frame()
        {
            var head = Rig.Head.transform;
            Vector3 player = head.position;
            Vector3 facing = Flat(head.forward, Vector3.forward);
            var threat = Enemy.Nearest(player, ThreatRange);
            Vector3 view = threat != null ? Vector3.Slerp(facing, Flat(threat.Center - player, facing), 0.55f) : facing;

            Vector3 target = player - view * Distance + Vector3.up * Height;
            target.y = Mathf.Max(target.y, Rig.transform.position.y + 1.4f);
            Vector3 look = threat != null ? Vector3.Lerp(player, threat.Center, 0.45f) : player + facing * 8f;

            var cam = Cam.transform;
            float t = framed ? 1f - Mathf.Exp(-Damping * Time.deltaTime) : 1f;
            framed = true;
            cam.position = Vector3.Lerp(cam.position, target, t);
            cam.rotation = Quaternion.Slerp(cam.rotation, Quaternion.LookRotation(look - cam.position), t);
        }

        private static Vector3 Flat(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            return direction.sqrMagnitude < 0.01f ? fallback : direction.normalized;
        }

        /// <summary>Anchors the cards to the screen edges; only runs when the viewport aspect actually changes.</summary>
        private void Layout()
        {
            if (Mathf.Abs(Cam.aspect - aspect) < 0.001f) return;
            aspect = Cam.aspect;
            float halfHeight = Mathf.Tan(Cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * OverlayDistance;
            overlay.localScale = Vector3.one * halfHeight;
            weaponCard.localPosition = new Vector3(-aspect + Margin + WeaponW / 2f, 1f - Margin - WeaponH / 2f, 0f);
            hiveCard.localPosition = new Vector3(aspect - Margin - HiveW / 2f, 1f - Margin - HiveH / 2f, 0f);
            saidCard.localPosition = new Vector3(0f, -1f + Margin + SaidH / 2f, 0f);
        }

        private void Refresh()
        {
            var ai = ShipAI.Instance;
            var director = WaveDirector.Instance;
            var ms = Mothership.Instance;

            if (ai != null)
            {
                for (int i = ai.Subtitles.Count - 1; i >= 0; i--)
                    if (ai.Subtitles[i].speaker == "YOU") { lastSaid = ai.Subtitles[i].text; break; }
                said.text = string.IsNullOrEmpty(lastSaid) ? "" : "“" + lastSaid + "”";
                saidCard.gameObject.SetActive(!string.IsNullOrEmpty(lastSaid));

                if (ai.Current != null)
                {
                    var spec = ai.Current.Spec;
                    weaponName.text = ai.Busy ? spec.Name + "  <color=#FFB847><size=60%>fabricating…</size></color>" : spec.Name;
                    weaponName.color = Color.Lerp(spec.Color, Color.white, 0.5f);
                    weaponChips.text = Chips(spec);
                }
            }

            if (director != null)
            {
                waveCounter.text = director.WaveIndex >= 0 ? $"W{director.WaveIndex + 1:00}/{director.Waves.Count:00}" : $"W00/{director.Waves.Count:00}";
                string name = director.Current != null ? director.Current.Name : "Standing by";
                waveLine.text = director.InArmory ? name + "  <color=#8AA0B4><size=65%>armory</size></color>" : name;
                waveCounter.color = director.Alive > 0 ? UiKit.Alien : UiKit.Ink;
                waveLine.text += $"  <color=#FF545F><size=65%>{director.Alive + director.PendingSpawns:00} hostiles</size></color>";
            }

            if (ms != null && ms.HasPendingAnalysis) hiveLine.text = $"analyzing {ms.PendingWeaponName} · {Mathf.CeilToInt(ms.AnalysisRemaining)}s";
            else hiveLine.text = ms == null || ms.ActivePackage == null ? "<color=#5A6678>nothing countered yet</color>" : ms.Describe();

            var texture = FindBlueprint();
            if (texture == blueprintTexture) return;
            blueprintTexture = texture;
            blueprintMaterial.SetTexture(BaseMap, texture);
            blueprintMaterial.SetTexture(MainTex, texture);
            blueprint.gameObject.SetActive(texture != null);
        }

        private static string Chips(ParsedWeapon spec)
        {
            var text = new StringBuilder();
            Chip(text, spec.FireMode.ToString(), UiKit.Cyan);
            Chip(text, spec.Payload.ToString(), UiKit.Amber);
            foreach (Mods mod in System.Enum.GetValues(typeof(Mods)))
                if (mod != Mods.None && spec.Has(mod)) Chip(text, mod.ToString(), UiKit.Go);
            return text.ToString();
        }

        /// <summary>TMP's mark tag fills a plate behind the run, so a chip costs no extra geometry.</summary>
        private static void Chip(StringBuilder text, string label, Color color)
        {
            text.Append("<mark=").Append(UiKit.Hex(color)).Append("28><color=").Append(UiKit.Hex(color))
                .Append("> ").Append(label.ToUpperInvariant()).Append(" </color></mark><space=0.5em>");
        }

        /// <summary>ShipAI keeps its holograms private, so the generated art is read off the big core projection.</summary>
        private Texture FindBlueprint()
        {
            if (blueprintSource == null)
                foreach (var hologram in FindObjectsByType<BlueprintHologram>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (hologram.name == "Core Blueprint") blueprintSource = hologram;
            var card = blueprintSource != null ? blueprintSource.GetComponentInChildren<MeshRenderer>(true) : null;
            var material = card != null ? card.sharedMaterial : null;
            if (material == null || !material.HasProperty(HasTex) || material.GetFloat(HasTex) < 0.5f) return null;
            return material.GetTexture(MainTex);
        }

        private void OnDestroy()
        {
            if (blueprintMaterial != null) Destroy(blueprintMaterial);
        }
    }
}
