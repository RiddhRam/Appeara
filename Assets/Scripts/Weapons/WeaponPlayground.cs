using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

/// <summary>Drawing -> interpretation -> confirmation -> paper weapon -> target damage.</summary>
public sealed class WeaponPlayground : MonoBehaviour
{
    [Tooltip("Recognition server accepting DrawingRequest and returning DrawingResponse. Keep provider credentials on the server.")]
    public string generationEndpoint = "";
    private Camera view;
    private WeaponRuntime weapon;
    private Transform held;
    private string status = "Draw anything. Turn it into something playable.";
    private bool editing = true, busy, settings, choosing;
    private float yaw, pitch, swing;
    private Vector2 scroll;
    private WeaponDrawing drawing;
    private DrawingCandidate[] candidates;
    private string proposedImage;
    private int colorIndex;
    private bool erasing;
    private float brushSize = 4;
    private GUIStyle titleStyle, textStyle, buttonStyle;
    private Texture2D buttonBackground, buttonHover;
    private readonly Color[] colors = { new Color(0.12f,0.15f,0.22f), new Color(0.91f,0.25f,0.27f), new Color(1,0.65f,0.12f), new Color(0.2f,0.65f,0.47f), new Color(0.22f,0.46f,0.9f), new Color(0.67f,0.35f,0.82f), Color.white };
    private readonly string[] choices = { "Melee blade", "Projectile gun", "Drawn projectile", "Explosive projectile", "Beam", "Explosion / AOE", "Spawn mine", "Apply force", "Apply status" };
    private readonly string[] choicePrompts = { "A knife", "A gun", "A drawn projectile", "An explosive grenade", "A laser", "An explosion", "A mine", "Apply force", "Apply status" };
    private string SavePath => Path.Combine(Application.persistentDataPath,"drawn-weapon.json");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // Only the empty starter scene gets the playground. Other scenes opt in by adding this component.
        if (SceneManager.GetActiveScene().name == "SampleScene" && FindFirstObjectByType<WeaponPlayground>() == null)
            new GameObject("Weapon Playground").AddComponent<WeaponPlayground>();
    }

    private void Start()
    {
        view = Camera.main;
        if (view == null) { view = new GameObject("Main Camera").AddComponent<Camera>(); view.tag = "MainCamera"; }
        view.transform.SetPositionAndRotation(new Vector3(0,1.7f,-7),Quaternion.identity);
        view.nearClipPlane = 0.03f;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Weapon test floor"; floor.transform.SetParent(transform);
        floor.transform.position = new Vector3(0,-0.2f,8); floor.transform.localScale = new Vector3(40,0.4f,40);
        floor.GetComponent<Renderer>().sharedMaterial = WeaponVisuals.Material(new Color(0.11f,0.15f,0.21f));
        SpawnTargets();
        drawing = new WeaponDrawing();
    }

    private void SpawnTargets()
    {
        foreach (var target in GetComponentsInChildren<WeaponTarget>()) Destroy(target.gameObject);
        for (var i = 0; i < 9; i++)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            target.name = "Target " + (i+1); target.transform.SetParent(transform);
            target.transform.position = new Vector3((i%3-1)*3,1, i/3*5);
            target.GetComponent<Renderer>().sharedMaterial = WeaponVisuals.Material(new Color(0.9f,0.3f,0.25f));
            target.AddComponent<WeaponTarget>();
            var body = target.AddComponent<Rigidbody>(); body.constraints = RigidbodyConstraints.FreezeRotation;
        }
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame) SetEditing(!editing);
        if (editing || view == null) return;
        var mouse = Mouse.current; var keys = Keyboard.current;
        if (mouse != null)
        {
            var delta = mouse.delta.ReadValue(); yaw += delta.x*0.12f; pitch = Mathf.Clamp(pitch-delta.y*0.12f,-80,80);
            view.transform.rotation = Quaternion.Euler(pitch,yaw,0);
            if (weapon != null && mouse.leftButton.isPressed)
            {
                Attack();
            }
        }
        if (Touchscreen.current != null && weapon != null &&
            Touchscreen.current.primaryTouch.press.isPressed) Attack();
        if (keys != null)
        {
            var move = new Vector3((keys.dKey.isPressed?1:0)-(keys.aKey.isPressed?1:0),0,(keys.wKey.isPressed?1:0)-(keys.sKey.isPressed?1:0));
            view.transform.position += Quaternion.Euler(0,yaw,0)*Vector3.ClampMagnitude(move,1)*5*Time.deltaTime;
            if (keys.rKey.wasPressedThisFrame) SpawnTargets();
        }
        swing = Mathf.MoveTowards(swing,0,Time.deltaTime*5);
        if (held != null) held.localRotation = Quaternion.Euler(0,-swing*35,0) * HeldRotation(weapon.Recipe.presentation.drawingForwardDegrees);
    }

    public static Quaternion HeldRotation(float degrees) => Quaternion.Euler(0,-90,0)*Quaternion.Euler(0,0,-degrees);
    private void Attack()
    {
        var origin=view.transform.position+view.transform.forward*0.3f;
        var direction=view.transform.forward;
        if(held!=null && (weapon.Recipe.behaviors[0].type=="projectile" || weapon.Recipe.behaviors[0].type=="beam"))
        {
            var target=weapon.Ray(view.transform.position,direction,100,out var hit) ? hit.point : view.transform.position+direction*100;
            origin=held.position+view.transform.forward*0.15f;
            // Never launch through a surface between the eye and the muzzle.
            var offset=origin-view.transform.position;
            if(weapon.Ray(view.transform.position,offset.normalized,offset.magnitude,out var obstruction)) origin=obstruction.point-offset.normalized*0.15f;
            direction=(target-origin).normalized;
        }
        if(weapon.TryUse(origin,direction)) swing=1;
    }

    private void SetEditing(bool value)
    {
        editing = value; Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = value;
    }

    private void Equip(WeaponRecipe recipe, string image)
    {
        // Validate and build before replacing the currently usable weapon.
        var replacement = WeaponComposer.Compose(recipe,view.transform);
        try { replacement.SetArtwork(image); }
        catch { Destroy(replacement.gameObject); throw; }
        replacement.Owner = view.transform;
        if (weapon != null) Destroy(weapon.gameObject);
        weapon = replacement;
        held = WeaponVisuals.CreateDrawing(weapon.transform,weapon.Artwork,0.28f*recipe.presentation.scale);
        held.localPosition = new Vector3(0.08f,-0.12f,0.7f);
        held.localRotation = HeldRotation(recipe.presentation.drawingForwardDegrees);
        weapon.Hit += hit => status = hit.target != null ? "Hit " + hit.target.name : "Hit";
    }

    private IEnumerator Generate()
    {
        if (busy || drawing == null) yield break;
        if (!drawing.HasInk) { status = "Draw something on the paper first."; yield break; }
        proposedImage = drawing.Encode();
        candidates = null;
        if (string.IsNullOrWhiteSpace(generationEndpoint))
        {
            choosing = true; status = "AI is not connected. Choose what you drew to keep playing.";
            yield break;
        }
        if (!Uri.TryCreate(generationEndpoint,UriKind.Absolute,out var uri) ||
            (uri.Scheme != "https" && !(uri.IsLoopback && uri.Scheme == "http")))
        { status = "Use HTTPS, or HTTP on localhost."; yield break; }
        busy = true; choosing = false; status = "Looking at your drawing…";
        using (var request = new UnityWebRequest(generationEndpoint,"POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(WeaponJson.ToJson(new DrawingRequest { imageBase64 = proposedImage })));
            request.downloadHandler = new DownloadHandlerBuffer(); request.timeout = 60;
            request.SetRequestHeader("Content-Type","application/json");
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                status = "AI unavailable. You can still choose what you drew.";
                choosing = true;
            }
            else
            {
                try
                {
                    if (request.downloadHandler.text.Length > 32000) throw new ArgumentException("Response too large.");
                    var response = WeaponJson.FromJson<DrawingResponse>(request.downloadHandler.text);
                    if (response?.candidates == null || response.candidates.Length < 1 || response.candidates.Length > 3)
                        throw new ArgumentException("Expected 1–3 interpretations.");
                    foreach (var candidate in response.candidates)
                    {
                        if (candidate == null || string.IsNullOrWhiteSpace(candidate.label) || candidate.label.Length > 80 ||
                            candidate.summary == null || candidate.summary.Length > 300 || candidate.recipe == null)
                            throw new ArgumentException("Incomplete interpretation.");
                        if (!WeaponRecipe.TryParse(WeaponJson.ToJson(candidate.recipe),out _,out var errors))
                            throw new ArgumentException(string.Join("; ",errors));
                    }
                    candidates = response.candidates;
                    status = "Does this match your idea? Accept one, or choose something else.";
                }
                catch (Exception) { choosing = true; status = "The AI response wasn't usable. Choose an interpretation below."; }
            }
        }
        busy = false;
    }

    private void ManualChoice(int index)
    {
        var recipe = WeaponDescription.Interpret(choicePrompts[index]);
        recipe.displayName = choices[index];
        candidates = new[] { new DrawingCandidate { label = choices[index], recipe = recipe,
            summary = "Hold to repeat: " + choices[index] + "." } };
        status = "Your selection. Review the behavior, then bring it to life.";
        choosing = false;
    }

    private void Accept(DrawingCandidate candidate)
    {
        try
        {
            Equip(candidate.recipe,proposedImage);
            status = candidate.label + " is ready.";
            try { File.WriteAllText(SavePath,WeaponJson.ToJson(new SavedDrawingWeapon { imageBase64 = proposedImage, recipe = candidate.recipe })); }
            catch (IOException) { status += " Saving wasn't available."; }
            SetEditing(false);
        }
        catch (Exception exception) { status = "Couldn't equip: " + exception.Message; }
    }

    private void DrawCanvas(Rect rect)
    {
        GUI.DrawTexture(rect,Texture2D.whiteTexture);
        drawing.Flush();
        GUI.DrawTexture(rect,drawing.Texture,ScaleMode.StretchToFill,true);
        if (busy) return;
        var current = Event.current;
        var id = GUIUtility.GetControlID(FocusType.Passive);
        var inside = rect.Contains(current.mousePosition);
        var point = new Vector2((current.mousePosition.x-rect.x)/rect.width*WeaponDrawing.Size,
            (1-(current.mousePosition.y-rect.y)/rect.height)*WeaponDrawing.Size);
        if (current.type == EventType.MouseDown && current.button == 0 && inside)
        {
            GUIUtility.hotControl = id;
            drawing.BeginStroke(point,erasing ? Color.clear : colors[colorIndex],Mathf.RoundToInt(brushSize));
            Invalidate(); current.Use();
        }
        else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == id)
        {
            if (inside) { drawing.Stroke(point,erasing ? Color.clear : colors[colorIndex],Mathf.RoundToInt(brushSize)); Invalidate(); }
            else drawing.EndStroke();
            current.Use();
        }
        else if (current.type == EventType.MouseUp && GUIUtility.hotControl == id)
        { GUIUtility.hotControl = 0; drawing.EndStroke(); current.Use(); }
    }

    private void Invalidate() { candidates = null; choosing = false; proposedImage = null; }

    private void OnGUI()
    {
        if (drawing == null) return;
        var scale = Mathf.Max(0.5f,Mathf.Min(Screen.width/640f,Screen.height/850f));
        GUI.matrix = Matrix4x4.Scale(Vector3.one*scale);
        var width = Screen.width/scale; var height = Screen.height/scale;
        if (!editing)
        {
            GUI.Label(new Rect(width/2-5,height/2-10,20,20),"+");
            GUI.Box(new Rect(12,12,Mathf.Min(600,width-24),65), (weapon != null ? weapon.Recipe.displayName : "Draw your first weapon with Tab") +
                "\nWASD move · mouse aim · click attack · Tab draw · R reset targets\n" + status);
            return;
        }
        if (titleStyle == null)
        {
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold };
            titleStyle.normal.textColor = colors[0];
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 18, wordWrap = true };
            textStyle.normal.textColor = colors[0]; textStyle.richText = false;
            buttonBackground = new Texture2D(1,1); buttonBackground.SetPixel(0,0,colors[0]); buttonBackground.Apply();
            buttonHover = new Texture2D(1,1); buttonHover.SetPixel(0,0,new Color(0.25f,0.35f,0.5f)); buttonHover.Apply();
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 18, padding = new RectOffset(12,12,10,10), wordWrap = true, richText = false };
            buttonStyle.normal.background = buttonBackground; buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.background = buttonHover; buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.background = buttonHover; buttonStyle.active.textColor = Color.white;
        }
        var panel = new Rect((width-Mathf.Min(610,width-24))/2,12,Mathf.Min(610,width-24),height-24);
        GUI.color = new Color(0.96f,0.94f,0.88f); GUI.DrawTexture(panel,Texture2D.whiteTexture); GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(panel.x+16,panel.y+12,panel.width-32,panel.height-24));
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label("INK TO LIFE",titleStyle);
        GUILayout.Label("01  Draw it.   02  Choose what it does.   03  Play.",textStyle);
        GUILayout.Space(8);
        GUI.enabled = !busy;
        GUILayout.BeginHorizontal();
        for (var i=0;i<colors.Length;i++)
        {
            GUI.backgroundColor = colors[i];
            if (GUILayout.Button(colorIndex == i && !erasing ? "●" : " ",GUILayout.Width(32),GUILayout.Height(28))) { colorIndex=i; erasing=false; }
        }
        GUI.backgroundColor = Color.white;
        erasing = GUILayout.Toggle(erasing,"Eraser","Button",GUILayout.Height(28));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Brush",textStyle,GUILayout.Width(48));
        brushSize = GUILayout.HorizontalSlider(brushSize,2,16,GUILayout.Width(130));
        if (GUILayout.Button("Undo")) { drawing.Undo(); Invalidate(); }
        if (GUILayout.Button("Clear")) { drawing.Clear(); Invalidate(); }
        GUILayout.EndHorizontal();
        var canvasSize = Mathf.Min(460,panel.width-48);
        var canvasRect = GUILayoutUtility.GetRect(canvasSize,canvasSize,GUILayout.ExpandWidth(false));
        DrawCanvas(canvasRect);
        GUILayout.Space(8);
        if (GUILayout.Button(busy ? "Looking at your drawing…" : "Bring my drawing to life",buttonStyle)) StartCoroutine(Generate());
        GUILayout.Label(status,textStyle);
        if (choosing)
        {
            foreach (var name in choices)
            {
                var index = Array.IndexOf(choices,name);
                if (GUILayout.Button(name,buttonStyle)) ManualChoice(index);
            }
        }
        if (candidates != null)
        {
            // Actions can invalidate candidates during this IMGUI pass; iterate a snapshot.
            var shown = candidates;
            foreach (var candidate in shown)
            {
                GUILayout.Space(8);
                GUILayout.Label(candidate.label,textStyle); GUILayout.Label(candidate.summary,textStyle);
                GUILayout.Label("Hold to repeat · " + candidate.recipe.cooldown.ToString("0.00") + "s between uses",textStyle);
                GUILayout.Label("Which way does the barrel / blade point in your drawing?",textStyle);
                var facing=Mathf.RoundToInt(candidate.recipe.presentation.drawingForwardDegrees/90)%4;
                candidate.recipe.presentation.drawingForwardDegrees=GUILayout.SelectionGrid(facing,new[] { "Right", "Up", "Left", "Down" },4)*90;
                candidate.recipe.presentation.useDrawingAsProjectile=GUILayout.Toggle(candidate.recipe.presentation.useDrawingAsProjectile,"Use drawing as ammunition (otherwise a bullet)");
                if (GUILayout.Button("Yes — equip this drawing",buttonStyle)) Accept(candidate);
            }
            if (GUILayout.Button("That's not it — let me choose",buttonStyle)) { candidates=null; choosing=true; }
        }
        GUILayout.Space(10);
        if (weapon != null && GUILayout.Button("Back to playing",buttonStyle)) SetEditing(false);
        if (GUILayout.Button("Load last drawing",buttonStyle))
        {
            try
            {
                var saved = WeaponJson.FromJson<SavedDrawingWeapon>(File.ReadAllText(SavePath));
                if(saved.recipe==null || saved.recipe.version!=2)
                {
                    drawing.Load(saved.imageBase64); proposedImage=saved.imageBase64; candidates=null; choosing=true;
                    status="Your drawing is preserved. Choose a behavior for the new weapon system.";
                }
                else { Equip(saved.recipe,saved.imageBase64); status = "Loaded your drawing."; SetEditing(false); }
            }
            catch (Exception) { status = "No valid saved drawing yet."; }
        }
        if (GUILayout.Button(settings ? "Hide connection settings" : "Connection settings",buttonStyle)) settings = !settings;
        if (settings)
        {
            GUILayout.Label("Recognition URL (API keys stay on the server)",textStyle);
            generationEndpoint = GUILayout.TextField(generationEndpoint,512);
            GUILayout.Label("No server? You can draw and choose an interpretation manually.",textStyle);
        }
        GUI.enabled = true;
        GUILayout.EndScrollView(); GUILayout.EndArea();
    }

    private void OnDisable() { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
    private void OnDestroy()
    {
        drawing?.Dispose();
        if (buttonBackground != null) Destroy(buttonBackground);
        if (buttonHover != null) Destroy(buttonHover);
    }
}
