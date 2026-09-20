using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace Armory
{
    /// <summary>
    /// Quest Link rig (plain OpenXR + Input System, same pattern as the proven Inkborn QuestLinkRig) with a
    /// desktop fallback so the game is testable without the headset.
    /// XR: right trigger fire · left grip talk · left X recenter · right A starts the wave · right stick snap turn.
    /// Desktop: LMB fire · RMB look · V talk · T type · 1-5 canned prompts · Enter starts the wave.
    /// </summary>
    public sealed class ArmoryRig : MonoBehaviour
    {
        public const float DesktopEyeHeight = 1.65f;
        [Tooltip("Degrees rotated per mouse-delta unit while holding right click in desktop mode.")]
        public float DesktopLookSensitivity = 0.45f;

        public Camera Head { get; private set; }
        public Transform RightHand { get; private set; }
        public Transform LeftHand { get; private set; }
        /// <summary>Aim pose: position is the muzzle anchor, forward is where shots go.</summary>
        public Transform Aim { get; private set; }
        public bool IsXR { get; private set; }
        public bool FireHeld { get; private set; }
        public bool TalkHeld { get; private set; }
        public bool RecenterPressed { get; private set; }
        /// <summary>Right A button (or Enter on desktop): starts the wave from the armory phase.</summary>
        public bool ReadyPressed { get; private set; }
        /// <summary>Right Y/B-side button (or M on desktop): opens the drydock menu.</summary>
        public bool MenuPressed { get; private set; }
        public Vector2 LeftStick { get; private set; }
        /// <summary>Walk input: left stick in VR, WASD on desktop.</summary>
        public Vector2 MoveAxis { get; private set; }
        /// <summary>Thruster sprint: left stick click in VR, Shift on desktop.</summary>
        public bool SprintHeld { get; private set; }
        /// <summary>Floor-level position under the head, for pad detection.</summary>
        public Vector3 FeetPosition
        {
            get
            {
                var feet = Head.transform.position;
                feet.y = transform.position.y;
                return feet;
            }
        }
        public Vector2 RightStick { get; private set; }
        public int CannedPromptPressed { get; private set; } = -1;
        public int DesktopTeleportStep { get; private set; }
        public bool TypePressed { get; private set; }
        public string Status { get; private set; } = "Starting";

        private readonly List<XRInputSubsystem> inputs = new List<XRInputSubsystem>();
        private Transform trackingSpace;
        private bool xrReady;
        private bool ownsLoader;
        private bool startedSubsystems;
        private bool originConfigured;
        private bool rightTracked;
        private bool leftTracked;
        private float desktopYaw;
        private float desktopPitch;
        private bool recenterHeld;
        private bool readyHeld;
        private bool menuHeld;
        private InputAction trigger, grip, recenter, ready, menu, sprint, leftStick, rightStick;

        private static readonly InputFeatureUsage<Vector3> PointerPosition = new InputFeatureUsage<Vector3>("PointerPosition");
        private static readonly InputFeatureUsage<Quaternion> PointerRotation = new InputFeatureUsage<Quaternion>("PointerRotation");

        public bool TextEntryActive { get; set; }

        private void Awake()
        {
            trackingSpace = new GameObject("Tracking Space").transform;
            trackingSpace.SetParent(transform, false);

            var head = new GameObject("Head", typeof(Camera), typeof(AudioListener));
            head.tag = "MainCamera";
            head.transform.SetParent(trackingSpace, false);
            head.transform.localPosition = Vector3.up * DesktopEyeHeight;
            Head = head.GetComponent<Camera>();
            Head.nearClipPlane = 0.03f;
            Head.farClipPlane = 400f;
            // Skybox, not a flat fill: the station's windows look out onto the generated starfield.
            Head.clearFlags = CameraClearFlags.Skybox;
            Head.backgroundColor = new Color(0.01f, 0.01f, 0.03f);

            RightHand = MakeHand("Right Hand", new Color(0.2f, 0.8f, 1f));
            LeftHand = MakeHand("Left Hand", new Color(1f, 0.6f, 0.2f));
            Aim = new GameObject("Aim").transform;
            Aim.SetParent(trackingSpace, false);

            trigger = Button("<XRController>{RightHand}/triggerPressed");
            grip = Button("<XRController>{LeftHand}/gripPressed");
            recenter = Button("<XRController>{LeftHand}/primaryButton");
            ready = Button("<XRController>{RightHand}/primaryButton");
            menu = Button("<XRController>{LeftHand}/secondaryButton");
            sprint = Button("<XRController>{LeftHand}/thumbstickClicked");
            leftStick = Stick("<XRController>{LeftHand}/thumbstick", "<XRController>{LeftHand}/primary2DAxis");
            rightStick = Stick("<XRController>{RightHand}/thumbstick", "<XRController>{RightHand}/primary2DAxis");
        }

        private Transform MakeHand(string handName, Color color)
        {
            var hand = new GameObject(handName).transform;
            hand.SetParent(trackingSpace, false);
            Mats.Shape(PrimitiveType.Cube, hand, new Vector3(0f, -0.01f, -0.03f), new Vector3(0.05f, 0.05f, 0.1f), Mats.Lit(color * 0.6f, 0.4f), name: "Hand Visual");
            hand.gameObject.SetActive(false);
            return hand;
        }

        private static InputAction Button(string path)
        {
            var action = new InputAction(path, InputActionType.Button, path);
            action.Enable();
            return action;
        }

        private static InputAction Stick(params string[] paths)
        {
            var action = new InputAction(paths[0], InputActionType.Value, expectedControlType: "Vector2");
            foreach (var path in paths) action.AddBinding(path);
            action.Enable();
            return action;
        }

        private void OnEnable()
        {
            Application.onBeforeRender += UpdatePoses;
            StartCoroutine(StartXR());
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= UpdatePoses;
            StopXR();
        }

        /// <summary>
        /// Leaves XR the way we found it. The editor crashed on exiting Play mode inside OpenXR's
        /// Internal_DestroySession because the session was torn down twice: once by Unity's own shutdown and once
        /// by the loader we started here. Stop only what this rig started, and let Unity own the rest.
        /// </summary>
        private void StopXR()
        {
            xrReady = false;
            inputs.Clear();
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null || !startedSubsystems) return;
            startedSubsystems = false;
            // Only tear down a session this rig started. Shutting down Unity's own session here is what crashed
            // the editor inside Internal_DestroySession when leaving Play mode.
            if (!ownsLoader) return;
            ownsLoader = false;
            manager.StopSubsystems();
            manager.DeinitializeLoader();
        }

        private void OnDestroy()
        {
            foreach (var action in new[] { trigger, grip, recenter, ready, menu, sprint, leftStick, rightStick }) action?.Dispose();
        }

        private IEnumerator StartXR()
        {
            yield return null;
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;

            // "Initialize XR on Startup" is on, so Unity is already bringing the loader up. Starting a second one
            // from here meant two owners of one OpenXR session, which crashed the editor natively on entering Play.
            // Wait for Unity's loader; only start one ourselves if it never arrives.
            float waitUntil = Time.realtimeSinceStartup + 4f;
            while (manager != null && manager.activeLoader == null && Time.realtimeSinceStartup < waitUntil)
                yield return null;

            if (manager != null && manager.activeLoader == null)
            {
                ownsLoader = true;
                yield return manager.InitializeLoader();
            }
            if (manager == null || manager.activeLoader == null)
            {
                ownsLoader = false;
                Status = "Desktop mode (no OpenXR). Start Quest Link for VR.";
                yield break;
            }
            if (!manager.isInitializationComplete) yield return null;
            manager.StartSubsystems();
            startedSubsystems = true;
            SubsystemManager.GetSubsystems(inputs);
            ConfigureOrigin();
            xrReady = true;
            Status = "OpenXR running";
        }

        private void ConfigureOrigin()
        {
            bool floor = false;
            originConfigured = false;
            foreach (var input in inputs)
            {
                if (!input.running) continue;
                originConfigured = true;
                input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                floor |= input.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor;
            }
            trackingSpace.localPosition = floor ? Vector3.zero : Vector3.up * DesktopEyeHeight;
        }

        private void UpdatePoses()
        {
            if (!xrReady) return;
            bool headTracked = ApplyPose(XRNode.Head, Head.transform);
            IsXR = headTracked;
            if (!IsXR) return;
            leftTracked = ApplyPose(XRNode.LeftHand, LeftHand);
            rightTracked = ApplyPose(XRNode.RightHand, RightHand);
            LeftHand.gameObject.SetActive(leftTracked);
            RightHand.gameObject.SetActive(rightTracked);

            // Prefer the controller's aim (pointer) pose; the grip pose points ~40° too high for shooting.
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (device.TryGetFeatureValue(PointerPosition, out var pointerPos) && device.TryGetFeatureValue(PointerRotation, out var pointerRot))
                Aim.SetLocalPositionAndRotation(pointerPos, pointerRot);
            else
                Aim.SetLocalPositionAndRotation(RightHand.localPosition, RightHand.localRotation * Quaternion.Euler(35f, 0f, 0f));
        }

        private static bool ApplyPose(XRNode node, Transform target)
        {
            var device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid
                || !device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked
                || !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position)
                || !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation)) return false;
            target.SetLocalPositionAndRotation(position, rotation);
            return true;
        }

        private void Update()
        {
            if (xrReady && !originConfigured) ConfigureOrigin();
            UpdatePoses();
            CannedPromptPressed = -1;
            DesktopTeleportStep = 0;
            TypePressed = false;

            bool readyNow = false, menuNow = false;
            bool recenterNow;
            if (IsXR)
            {
                FireHeld = rightTracked && trigger.IsPressed();
                TalkHeld = leftTracked && grip.IsPressed();
                recenterNow = recenter.IsPressed();
                LeftStick = leftStick.ReadValue<Vector2>();
                RightStick = rightStick.ReadValue<Vector2>();
                readyNow = ready.IsPressed();
                menuNow = menu.IsPressed();
                MoveAxis = LeftStick;
                SprintHeld = sprint.IsPressed();
            }
            else
            {
                UpdateDesktop(out recenterNow);
            }

            if (!IsXR && Keyboard.current != null && !TextEntryActive)
            {
                readyNow = Keyboard.current.enterKey.isPressed || Keyboard.current.numpadEnterKey.isPressed;
                menuNow = Keyboard.current.mKey.isPressed;
            }
            MenuPressed = menuNow && !menuHeld;
            menuHeld = menuNow;
            ReadyPressed = readyNow && !readyHeld;
            readyHeld = readyNow;
            RecenterPressed = recenterNow && !recenterHeld;
            recenterHeld = recenterNow;
            if (RecenterPressed) Recenter();
        }

        private void UpdateDesktop(out bool recenterNow)
        {
            recenterNow = false;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null || keyboard == null) return;

            if (mouse.rightButton.isPressed)
            {
                var delta = mouse.delta.ReadValue() * DesktopLookSensitivity;
                desktopYaw += delta.x;
                desktopPitch = Mathf.Clamp(desktopPitch - delta.y, -80f, 80f);
            }
            Head.transform.localPosition = Vector3.up * DesktopEyeHeight;
            Head.transform.localRotation = Quaternion.Euler(desktopPitch, desktopYaw, 0f);

            // Aim through the cursor from a "hand" just below and right of the eye.
            var ray = Head.ScreenPointToRay(mouse.position.ReadValue());
            Vector3 handWorld = Head.transform.TransformPoint(new Vector3(0.18f, -0.22f, 0.35f));
            Vector3 target = ray.origin + ray.direction * 30f;
            if (Physics.Raycast(ray, out var hit, 200f)) target = hit.point;
            Aim.position = handWorld;
            Aim.rotation = Quaternion.LookRotation(target - handWorld);
            RightHand.gameObject.SetActive(true);
            RightHand.SetPositionAndRotation(Aim.position, Aim.rotation);
            LeftHand.gameObject.SetActive(true);
            LeftHand.position = Head.transform.TransformPoint(new Vector3(-0.25f, -0.3f, 0.45f));
            LeftHand.rotation = Quaternion.LookRotation(Head.transform.position - LeftHand.position, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);

            if (TextEntryActive)
            {
                FireHeld = TalkHeld = false;
                MoveAxis = Vector2.zero;
                return;
            }
            FireHeld = mouse.leftButton.isPressed;
            TalkHeld = keyboard.vKey.isPressed;
            TypePressed = keyboard.tKey.wasPressedThisFrame;
            if (keyboard.qKey.wasPressedThisFrame) DesktopTeleportStep = -1;
            if (keyboard.eKey.wasPressedThisFrame) DesktopTeleportStep = 1;
            var move = Vector2.zero;
            if (keyboard.wKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.aKey.isPressed) move.x -= 1f;
            MoveAxis = Vector2.ClampMagnitude(move, 1f);
            SprintHeld = keyboard.leftShiftKey.isPressed;

            Key[] digits = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5 };
            for (int i = 0; i < digits.Length; i++)
                if (keyboard[digits[i]].wasPressedThisFrame) CannedPromptPressed = i;
        }

        /// <summary>Moves the rig so the player's feet land on <paramref name="feet"/>, facing <paramref name="forward"/>.</summary>
        public void TeleportTo(Vector3 feet, Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            // Rotate so the head's current yaw ends up along forward, then cancel the head's horizontal offset.
            float headYaw = IsXR ? Head.transform.localEulerAngles.y : 0f;
            transform.rotation = Quaternion.LookRotation(forward) * Quaternion.Euler(0f, -headYaw, 0f);
            if (!IsXR) desktopYaw = 0f;
            Vector3 headOffset = Head.transform.position - transform.position;
            headOffset.y = 0f;
            transform.position = feet - headOffset;
        }

        public void SnapTurn(float degrees)
        {
            Vector3 pivot = Head.transform.position;
            transform.RotateAround(pivot, Vector3.up, degrees);
        }

        public void Recenter()
        {
            foreach (var input in inputs)
                if (input.running) input.TryRecenter();
            ConfigureOrigin();
        }
    }

    /// <summary>
    /// Applies a conservative, reversible render budget once a tracked XR headset is present.
    /// Desktop rendering keeps the project's normal PC settings.
    /// </summary>
    public sealed class XRPerformanceTuner : MonoBehaviour
    {
        public ArmoryRig Rig;

        [Range(0.5f, 1f)] public float RenderScale = 0.85f;
        [Range(0f, 1f)] public float FoveationStrength = 0.5f;
        public float FarClip = 120f;

        private readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        private bool applied;
        private float oldEyeTextureScale;
        private float oldLodBias;
        private bool oldHdr;
        private float oldFarClip;

        private void Update()
        {
            if (!applied && Rig != null && Rig.IsXR) Apply();
        }

        private void Apply()
        {
            applied = true;
            oldFarClip = Rig.Head.farClipPlane;
            oldHdr = Rig.Head.allowHDR;
            oldEyeTextureScale = XRSettings.eyeTextureResolutionScale;
            oldLodBias = QualitySettings.lodBias;
            Rig.Head.farClipPlane = FarClip;
            Rig.Head.allowHDR = false;
            Rig.Head.allowDynamicResolution = true;
            XRSettings.eyeTextureResolutionScale = Mathf.Min(oldEyeTextureScale, RenderScale);
            QualitySettings.lodBias = Mathf.Min(oldLodBias, 1.25f);

            SubsystemManager.GetSubsystems(displays);
            foreach (var display in displays)
            {
                if (!display.running) continue;
                display.foveatedRenderingLevel = FoveationStrength;
                display.foveatedRenderingFlags = XRDisplaySubsystem.FoveatedRenderingFlags.GazeAllowed;
            }

            Debug.Log($"Armory VR budget: {RenderScale:0.00} eye scale, {FarClip:0}m far clip, " +
                      $"LOD bias {QualitySettings.lodBias:0.00}, foveation {FoveationStrength:0.00}.");
        }

        private void OnDisable()
        {
            if (!applied) return;
            applied = false;
            if (Rig != null && Rig.Head != null)
            {
                Rig.Head.farClipPlane = oldFarClip;
                Rig.Head.allowHDR = oldHdr;
            }
            XRSettings.eyeTextureResolutionScale = oldEyeTextureScale;
            QualitySettings.lodBias = oldLodBias;
            foreach (var display in displays)
                if (display != null && display.running) display.foveatedRenderingLevel = 0f;
            displays.Clear();
        }
    }
}
