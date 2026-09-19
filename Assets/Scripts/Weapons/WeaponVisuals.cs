using System.Collections.Generic;
using UnityEngine;

/// <summary>Approved procedural asset catalog; all models are assembled at runtime.</summary>
public static class WeaponVisuals
{
    public const string Catalog = "knife: pointed blade and grip; grenade: round grenade; rocket: finned rocket; bolt: glowing sphere; gun: barrel and grip";
    private static readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
    public static bool IsKnown(string id) => id == "knife" || id == "grenade" || id == "rocket" || id == "bolt" || id == "gun";

    public static Transform CreateDrawing(Transform parent, Sprite artwork, float scale)
    {
        var root = new GameObject("Drawn weapon").transform;
        root.SetParent(parent,false); root.localScale = Vector3.one*scale;
        root.gameObject.AddComponent<SpriteRenderer>().sprite = artwork;
        return root;
    }

    public static Material Material(Color color)
    {
        if (materials.TryGetValue(color, out var cached) && cached != null) return cached;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { color = color };
        materials[color] = material;
        return material;
    }

    public static Transform Create(Transform parent, WeaponPresentation style, string overrideAsset = null)
    {
        var root = new GameObject(overrideAsset ?? style.asset).transform;
        root.SetParent(parent, false);
        root.localScale = Vector3.one * style.scale;
        ColorUtility.TryParseHtmlString(style.color, out var color);
        var brown = new Color(0.52f, 0.29f, 0.13f);
        switch (overrideAsset ?? style.asset)
        {
            case "knife":
                // A tapered, flat blade (local +Z is the tip).
                var blade = new GameObject("Blade");
                blade.transform.SetParent(root, false);
                var mesh = new Mesh { name = "Procedural knife blade" };
                mesh.vertices = new[] { new Vector3(-0.09f,0,0), new Vector3(0.09f,0,0), new Vector3(0,0,0.65f), new Vector3(0,0.035f,0.12f), new Vector3(0,-0.035f,0.12f) };
                mesh.triangles = new[] {0,3,2,3,1,2,0,1,3,0,2,4,4,2,1,0,4,1};
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                blade.AddComponent<MeshFilter>().sharedMesh = mesh;
                blade.AddComponent<MeshRenderer>().sharedMaterial = Material(color);
                blade.AddComponent<WeaponMeshCleanup>().mesh = mesh;
                Part(root, PrimitiveType.Cube, new Vector3(0,0,-0.16f), new Vector3(0.09f,0.1f,0.3f), brown);
                Part(root, PrimitiveType.Cube, Vector3.zero, new Vector3(0.27f,0.08f,0.06f), Color.gray);
                break;
            case "gun":
                Part(root,PrimitiveType.Cube,Vector3.zero,new Vector3(0.18f,0.2f,0.55f),color);
                Part(root,PrimitiveType.Cube,new Vector3(0,-0.16f,-0.15f),new Vector3(0.12f,0.3f,0.15f),Color.gray);
                Part(root,PrimitiveType.Cube,new Vector3(0,0,0.4f),new Vector3(0.09f,0.09f,0.3f),Color.gray);
                break;
            case "rocket":
                Part(root,PrimitiveType.Capsule,Vector3.zero,new Vector3(0.18f,0.18f,0.45f),color);
                Part(root,PrimitiveType.Cube,new Vector3(0,0,-0.2f),new Vector3(0.4f,0.04f,0.18f),color);
                break;
            case "grenade":
                Part(root,PrimitiveType.Sphere,Vector3.zero,Vector3.one*0.32f,color);
                Part(root,PrimitiveType.Cube,new Vector3(0,0.19f,0),new Vector3(0.08f,0.12f,0.08f),Color.gray);
                break;
            default: Part(root,PrimitiveType.Sphere,Vector3.zero,Vector3.one*0.15f,color); break;
        }
        return root;
    }

    private static void Part(Transform root, PrimitiveType type, Vector3 position, Vector3 scale, Color color)
    {
        var part = GameObject.CreatePrimitive(type);
        part.transform.SetParent(root,false); part.transform.localPosition = position; part.transform.localScale = scale;
        var collider = part.GetComponent<Collider>(); collider.enabled = false;
        if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
        part.GetComponent<Renderer>().sharedMaterial = Material(color);
    }

    public static void Trail(GameObject target, WeaponPresentation style)
    {
        if (style.trailTime <= 0) return;
        ColorUtility.TryParseHtmlString(style.color, out var color);
        var trail = target.AddComponent<TrailRenderer>();
        trail.sharedMaterial = Material(color); trail.time = style.trailTime;
        trail.startWidth = 0.07f * style.scale; trail.endWidth = 0;
        trail.minVertexDistance = 0.04f;
    }

    public static void Line(Vector3 from, Vector3 to, WeaponPresentation style)
    {
        var go = new GameObject("Weapon tracer");
        var line = go.AddComponent<LineRenderer>();
        ColorUtility.TryParseHtmlString(style.color, out var color);
        line.sharedMaterial = Material(color); line.startWidth = 0.045f; line.endWidth = 0.01f;
        line.positionCount = 2; line.SetPosition(0,from); line.SetPosition(1,to);
        Object.Destroy(go,0.09f);
    }
}

internal sealed class WeaponMeshCleanup : MonoBehaviour
{
    public Mesh mesh;
    private void OnDestroy() { if (mesh != null) Destroy(mesh); }
}
