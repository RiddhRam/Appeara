using UnityEngine;

public sealed class WeaponTarget : MonoBehaviour
{
    public float health=100;
    public float MovementMultiplier => Time.time<stunUntil ? 0 : Time.time<freezeUntil ? 0.4f : 1;
    private float burnUntil,freezeUntil,stunUntil,burnDamage;
    private Renderer[] renderers;
    private MaterialPropertyBlock[] originals;
    private MaterialPropertyBlock tint;
    private int lastTint=-1;
    private void Awake()
    {
        tint=new MaterialPropertyBlock();
        renderers=GetComponentsInChildren<Renderer>(); originals=new MaterialPropertyBlock[renderers.Length];
        for(int i=0;i<renderers.Length;i++) { originals[i]=new MaterialPropertyBlock(); renderers[i].GetPropertyBlock(originals[i]); }
    }
    public void Damage(float amount) { health-=amount; if(health<=0) Destroy(gameObject); }
    public void ApplyStatus(string status,float duration,float magnitude)
    {
        switch(status)
        {
            case "burning": burnUntil=Time.time+duration; burnDamage=magnitude; break;
            case "freezing": freezeUntil=Time.time+duration; break;
            case "stunned": stunUntil=Time.time+duration; break;
        }
        UpdateTint();
    }
    private void Update()
    {
        if(Time.time<burnUntil) Damage(burnDamage*Time.deltaTime);
        UpdateTint();
    }
    private void UpdateTint()
    {
        int state=Time.time<stunUntil ? 3 : Time.time<freezeUntil ? 2 : Time.time<burnUntil ? 1 : 0;
        if(state==lastTint || renderers==null) return; lastTint=state;
        var color=state==1 ? Color.red : state==2 ? new Color(0.2f,0.6f,1) : new Color(0.7f,0.3f,1);
        for(int i=0;i<renderers.Length;i++) if(renderers[i]!=null)
        {
            if(state==0) renderers[i].SetPropertyBlock(originals[i]);
            else { renderers[i].GetPropertyBlock(tint); tint.SetColor("_BaseColor",color); tint.SetColor("_Color",color); renderers[i].SetPropertyBlock(tint); }
        }
    }
    private void OnDisable() { if(renderers!=null) for(int i=0;i<renderers.Length;i++) if(renderers[i]!=null) renderers[i].SetPropertyBlock(originals[i]); lastTint=-1; }
}
