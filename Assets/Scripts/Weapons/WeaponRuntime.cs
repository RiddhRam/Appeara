using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class WeaponRuntime : MonoBehaviour
{
    public WeaponRecipe Recipe { get; private set; }
    public Transform Owner { get; set; }
    public Sprite Artwork { get; private set; }
    public event Action<WeaponHitContext> Hit;
    private float nextUseTime;
    public void SetArtwork(string png) { var next = WeaponDrawing.DecodeSprite(png); ReleaseArtwork(); Artwork = next; }
    private void ReleaseArtwork() { if (Artwork != null) { Destroy(Artwork.texture); Destroy(Artwork); } }
    private void OnDestroy() { ReleaseArtwork(); }
    public void Initialize(WeaponRecipe recipe)
    {
        if (recipe == null) throw new ArgumentNullException(nameof(recipe));
        if (!WeaponRecipe.TryParse(WeaponJson.ToJson(recipe),out var copy,out var errors)) throw new ArgumentException(string.Join("\n",errors));
        Recipe = copy;
    }
    // Input repeats this call while held; cooldown limits every behavior's use rate.
    public bool TryUse(Vector3 origin,Vector3 direction)
    {
        if (Recipe == null || Time.time < nextUseTime) return false;
        nextUseTime = Time.time+Recipe.cooldown;
        Execute(Recipe.behaviors,origin,direction.sqrMagnitude > 0 ? direction.normalized : transform.forward,null);
        return true;
    }
    internal bool IsIgnored(Collider c) => c == null || c.transform.IsChildOf(transform) || (Owner != null && c.transform.IsChildOf(Owner));
    internal bool Ray(Vector3 origin,Vector3 direction,float range,out RaycastHit selected)
    {
        selected = default; float nearest = float.MaxValue;
        foreach (var h in Physics.RaycastAll(origin,direction,range,~0,QueryTriggerInteraction.Ignore))
            if (!IsIgnored(h.collider) && h.distance < nearest) { nearest = h.distance; selected = h; }
        return nearest < float.MaxValue;
    }
    internal void Execute(WeaponBehavior[] behaviors,Vector3 point,Vector3 direction,Collider target)
    {
        if (behaviors == null) return;
        foreach (var b in behaviors)
        {
            switch (b.type)
            {
                case "projectile": WeaponMovingObject.Create(this,b,point,direction,false); break;
                case "spawn_object":
                    var placement = target != null ? point : Ray(point,direction,Mathf.Min(b.range,8),out var ground) ? ground.point : point+direction*2;
                    WeaponMovingObject.Create(this,b,placement,direction,true); break;
                case "beam":
                    var end = point+direction*b.range;
                    if (Ray(point,direction,b.range,out var hit)) { end = hit.point; Impact(b,hit.collider,end,direction); }
                    WeaponVisuals.Line(point,end,Recipe.presentation); break;
                case "explosion": Area(b,point,direction,false); break;
                case "melee": Area(b,point,direction,true); break;
                case "apply_force":
                case "apply_status":
                    var receiver = target;
                    if (receiver == null && Ray(point,direction,b.range,out var direct)) receiver = direct.collider;
                    if (receiver == null || IsIgnored(receiver)) break;
                    if (b.type == "apply_force")
                    {
                        var body = receiver.attachedRigidbody;
                        if (body != null && !body.isKinematic) body.AddForce(direction*b.force,ForceMode.Impulse);
                    }
                    else receiver.GetComponentInParent<WeaponTarget>()?.ApplyStatus(b.status,b.duration,b.damage);
                    break;
            }
        }
    }
    private void Area(WeaponBehavior b,Vector3 point,Vector3 direction,bool melee)
    {
        var seen = new HashSet<int>();
        foreach (var c in Physics.OverlapSphere(point,melee ? b.range : b.radius,~0,QueryTriggerInteraction.Ignore))
        {
            if (IsIgnored(c)) continue;
            var to = c.ClosestPoint(point)-point;
            if (melee && to.sqrMagnitude > 0.0001f && Vector3.Angle(direction,to) > b.arcDegrees/2) continue;
            var receiver = c.GetComponentInParent<WeaponTarget>();
            var key = receiver != null ? receiver.GetInstanceID() : c.attachedRigidbody != null ? c.attachedRigidbody.GetInstanceID() : c.GetInstanceID();
            if (!seen.Add(key)) continue;
            Impact(b,c,c.ClosestPoint(point),melee ? direction : (c.bounds.center-point).normalized);
        }
        if (!melee)
        {
            // Short radial burst, no collider or long-lived effect objects.
            for (int i=0;i<12;i++) { var d = Quaternion.Euler(0,i*30,0)*Vector3.forward; WeaponVisuals.Line(point,point+d*b.radius,Recipe.presentation); }
        }
    }
    internal void Impact(WeaponBehavior b,Collider c,Vector3 point,Vector3 direction)
    {
        if (IsIgnored(c)) return;
        c.GetComponentInParent<WeaponTarget>()?.Damage(b.damage);
        Hit?.Invoke(new WeaponHitContext(this,c,point,direction,b));
        Execute(b.onHit,point,direction,c);
    }
}

public readonly struct WeaponHitContext
{
    public readonly WeaponRuntime weapon;
    public readonly Collider target;
    public readonly Vector3 point, direction;
    public readonly WeaponBehavior behavior;
    public WeaponHitContext(WeaponRuntime weapon,Collider target,Vector3 point,Vector3 direction,WeaponBehavior behavior)
    { this.weapon=weapon; this.target=target; this.point=point; this.direction=direction; this.behavior=behavior; }
}

public sealed class WeaponMovingObject : MonoBehaviour
{
    private WeaponRuntime weapon;
    private WeaponBehavior behavior;
    private Vector3 direction;
    private float expires, travelled, armedAt;
    private bool mine;
    private static int count;
    private Transform visual;
    public static void Create(WeaponRuntime weapon,WeaponBehavior b,Vector3 point,Vector3 direction,bool mine)
    {
        if (count >= 256) return;
        var go = new GameObject(mine ? "Spawned mine" : "Weapon projectile");
        go.transform.SetPositionAndRotation(point,Quaternion.LookRotation(direction));
        var moving = go.AddComponent<WeaponMovingObject>(); count++;
        moving.weapon=weapon; moving.behavior=b; moving.direction=direction; moving.mine=mine;
        moving.expires=Time.time+b.lifetime; moving.armedAt=Time.time+0.4f;
        if (weapon.Artwork != null && (mine || weapon.Recipe.presentation.useDrawingAsProjectile))
        {
            var facing = new GameObject("Paper facing").transform; facing.SetParent(go.transform,false);
            facing.gameObject.AddComponent<DrawingBillboard>();
            moving.visual=WeaponVisuals.CreateDrawing(facing,weapon.Artwork,weapon.Recipe.presentation.scale*0.5f);
        }
        else moving.visual=WeaponVisuals.Create(go.transform,weapon.Recipe.presentation,mine ? "grenade" : "bolt");
        if (!mine) WeaponVisuals.Trail(go,weapon.Recipe.presentation);
    }
    private void Update()
    {
        if (weapon == null || Time.time >= expires || travelled >= behavior.range) { Destroy(gameObject); return; }
        if (mine)
        {
            if (Time.time < armedAt) return;
            foreach (var c in Physics.OverlapSphere(transform.position,behavior.radius,~0,QueryTriggerInteraction.Ignore))
            {
                if (weapon.IsIgnored(c) || c.GetComponentInParent<WeaponTarget>() == null) continue;
                // Fire once, using the mine location as the event origin.
                weapon.Execute(behavior.onHit,transform.position,direction,c);
                Destroy(gameObject); return;
            }
            return;
        }
        var distance=Mathf.Min(behavior.speed*Time.deltaTime,behavior.range-travelled);
        Collider selected=null; Vector3 point=default; float nearest=float.MaxValue;
        // Include initial overlaps as sphere casts alone skip them.
        foreach (var c in Physics.OverlapSphere(transform.position,behavior.radius,~0,QueryTriggerInteraction.Ignore))
            if (!weapon.IsIgnored(c)) { selected=c; point=c.ClosestPoint(transform.position); nearest=0; break; }
        foreach (var h in Physics.SphereCastAll(transform.position,behavior.radius,direction,distance,~0,QueryTriggerInteraction.Ignore))
            if (!weapon.IsIgnored(h.collider) && h.distance<nearest) { selected=h.collider; point=h.point; nearest=h.distance; }
        if (selected != null) { weapon.Impact(behavior,selected,point,direction); Destroy(gameObject); return; }
        transform.position+=direction*distance; travelled+=distance;
        visual.Rotate(Vector3.forward,weapon.Recipe.presentation.spin*Time.deltaTime,Space.Self);
    }
    private void OnDestroy() { count=Mathf.Max(0,count-1); }
}
