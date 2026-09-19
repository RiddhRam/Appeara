using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class WeaponChecks
{
    [MenuItem("Tools/Weapons/Run Mine and Status Checks (Play Mode)")]
    public static void MineStatus()
    {
        if(!Application.isPlaying) { Debug.LogWarning("Enter Play mode first."); return; }
        var target=GameObject.CreatePrimitive(PrimitiveType.Cube); target.transform.position=new Vector3(300,50,303);
        var health=target.AddComponent<WeaponTarget>();
        health.ApplyStatus("freezing",0.2f,0);
        var mine=new WeaponBehavior { type="spawn_object",radius=2,damage=0,onHit=new[] { new WeaponBehavior { type="explosion",radius=3,damage=15 } } };
        var weapon=WeaponComposer.Compose(new WeaponRecipe { version=2,behaviors=new[] { mine } });
        Physics.SyncTransforms(); weapon.TryUse(new Vector3(300,50,300),Vector3.forward);
        var deadline=EditorApplication.timeSinceStartup+1.2;
        void Complete()
        {
            if(EditorApplication.timeSinceStartup<deadline) return;
            EditorApplication.update-=Complete;
            try
            {
                Require(health.health==85,"Mine detonates once with area damage");
                Require(health.MovementMultiplier==1,"Status expires");
                var block=new MaterialPropertyBlock(); target.GetComponent<Renderer>().GetPropertyBlock(block);
                Require(block.isEmpty,"Original appearance restored");
                Debug.Log("Mine/status checks PASS: arming, single detonation, AOE damage, status expiration, tint restoration.");
            }
            finally { UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(weapon.gameObject); }
        }
        EditorApplication.update+=Complete;
    }

    [MenuItem("Tools/Weapons/Run Drawing Checks (Play Mode)")]
    public static void Drawing()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode first."); return; }
        using (var canvas = new WeaponDrawing())
        {
            Require(!canvas.HasInk,"Empty canvas");
            canvas.BeginStroke(new Vector2(100,100),Color.red,4);
            canvas.Stroke(new Vector2(250,250),Color.red,4); canvas.EndStroke();
            Require(canvas.HasInk,"Paint stroke");
            var png = canvas.Encode();
            canvas.Undo(); Require(!canvas.HasInk,"Undo stroke");
            var sprite = WeaponDrawing.DecodeSprite(png);
            Require(sprite.rect.width < WeaponDrawing.Size,"Artwork is cropped");
            Require(sprite.texture.GetPixel(0,0).a == 0,"Transparent background preserved");
            UnityEngine.Object.Destroy(sprite.texture); UnityEngine.Object.Destroy(sprite);
            var saved = WeaponJson.FromJson<SavedDrawingWeapon>(WeaponJson.ToJson(new SavedDrawingWeapon {
                imageBase64 = png, recipe = WeaponDescription.Interpret("A knife")
            }));
            var weapon = WeaponComposer.Compose(saved.recipe);
            try
            {
                weapon.SetArtwork(saved.imageBase64);
                Require(weapon.Artwork != null,"Save roundtrip keeps drawing");
                var visual = WeaponVisuals.CreateDrawing(weapon.transform,weapon.Artwork,1);
                Require(visual.GetComponent<SpriteRenderer>().sprite == weapon.Artwork,"Visual uses player artwork");
                Require(visual.GetComponent<Collider>() == null,"Drawing has no visual collider");
            }
            finally { UnityEngine.Object.Destroy(weapon.gameObject); }
        }
        Debug.Log("Drawing checks PASS: paint, undo, transparency, cropped sprite, saved artwork, sprite renderer.");
    }

    [MenuItem("Tools/Weapons/Run Play Mode Combat Checks")]
    public static void Combat()
    {
        if (!Application.isPlaying) { Debug.LogWarning("Enter Play mode first."); return; }
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        target.name = "Temporary combat check";
        var origin = new Vector3(200,50,200);
        target.transform.position = origin + Vector3.forward*3;
        var health = target.AddComponent<WeaponTarget>();
        var weapon = WeaponComposer.Compose(WeaponDescription.Interpret("A projectile gun"));
        Physics.SyncTransforms();
        weapon.TryUse(origin,Vector3.forward);
        var deadline = EditorApplication.timeSinceStartup + 1;
        void Complete()
        {
            if (EditorApplication.timeSinceStartup < deadline) return;
            EditorApplication.update -= Complete;
            try
            {
                Require(health != null && health.health == 85,"Projectile damage");
                weapon.Initialize(WeaponDescription.Interpret("A knife"));
                Require(weapon.TryUse(origin+Vector3.forward,Vector3.forward),"Melee activation");
                Require(health.health == 50,"Melee knife damage");
                foreach(var type in new[] { "beam", "explosion", "apply_force", "apply_status" })
                {
                    var b=new WeaponBehavior { type=type,damage=0,radius=4,status="freezing" };
                    var other=WeaponComposer.Compose(new WeaponRecipe { version=2,behaviors=new[] { b } });
                    if(type=="apply_force") target.AddComponent<Rigidbody>().useGravity=false;
                    other.TryUse(origin,Vector3.forward);
                    if(type=="apply_status")
                    {
                        Require(health.MovementMultiplier<1,"Freeze movement multiplier");
                        var block=new MaterialPropertyBlock(); target.GetComponent<Renderer>().GetPropertyBlock(block);
                        Require(block.GetColor("_BaseColor").b>0.9f,"Freeze tint");
                    }
                    UnityEngine.Object.Destroy(other.gameObject);
                }
                Debug.Log("Weapon combat checks PASS: projectile collision/damage, melee knife collision/damage.");
            }
            finally
            {
                if (target != null) UnityEngine.Object.Destroy(target);
                if (weapon != null) UnityEngine.Object.Destroy(weapon.gameObject);
            }
        }
        EditorApplication.update += Complete;
    }

    [MenuItem("Tools/Weapons/Open Playground")]
    public static void Open()
    {
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
    }

    [MenuItem("Tools/Weapons/Run Recipe Checks")]
    public static void Run()
    {
        Require(WeaponPrimitiveIds.All.Length==7,"Seven behavior types");
        foreach(var type in WeaponPrimitiveIds.All)
        {
            var recipe=new WeaponRecipe { version=2,behaviors=new[] { new WeaponBehavior { type=type } } };
            Require(WeaponRecipe.TryParse(WeaponJson.ToJson(recipe),out _,out _),"Roundtrip "+type);
        }
        Require(!WeaponRecipe.TryParse("{}",out _,out _),"Reject legacy recipes");
        var knife=WeaponDescription.Interpret("A flaming knife");
        Require(knife.behaviors[0].onHit[0].type=="apply_status","Composed status");
        var gun=WeaponDescription.Interpret("A gun");
        Require(!gun.presentation.useDrawingAsProjectile,"Gun uses bullets");
        for(int i=0;i<4;i++)
        {
            var angle=i*90f;
            var local=Quaternion.Euler(0,0,angle)*Vector3.right;
            Require(Vector3.Dot(WeaponPlayground.HeldRotation(angle)*local,Vector3.forward)>0.999f,"Aim alignment");
        }
        Debug.Log("Weapon checks PASS: seven behaviors, v2 validation, status composition, gun ammunition, four aim directions.");
    }

    private static void Require(bool condition,string label) { if (!condition) throw new Exception("Weapon check failed: " + label); }
}
