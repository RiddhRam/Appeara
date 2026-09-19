using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Transparent stroke canvas. PNG snapshots also serve as the actual weapon artwork.</summary>
public sealed class WeaponDrawing : IDisposable
{
    public const int Size = 384;
    public Texture2D Texture { get; private set; }
    public int Revision { get; private set; }
    public bool HasInk { get { foreach (var pixel in pixels) if (pixel.a != 0) return true; return false; } }
    private Color32[] pixels = new Color32[Size * Size];
    private readonly List<Color32[]> undo = new List<Color32[]>();
    private Vector2? previous;
    private bool dirty;

    public WeaponDrawing()
    {
        Texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "Player drawing", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        Upload();
    }
    public void Clear() { Remember(); Array.Clear(pixels,0,pixels.Length); Revision++; Upload(); }
    public void Undo()
    {
        if (undo.Count == 0) return;
        pixels = undo[undo.Count-1]; undo.RemoveAt(undo.Count-1); previous = null; Revision++; Upload();
    }
    private void Remember() { if (undo.Count == 20) undo.RemoveAt(0); undo.Add((Color32[])pixels.Clone()); }
    public void BeginStroke(Vector2 point, Color color, int radius)
    {
        Remember(); previous = point; Stroke(point,color,radius);
    }
    public void Stroke(Vector2 point, Color color, int radius)
    {
        var from = previous ?? point;
        var steps = Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(from,point)));
        for (var i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(from,point,i/(float)steps);
            var x = Mathf.RoundToInt(p.x); var y = Mathf.RoundToInt(p.y);
            for (var dy = -radius; dy <= radius; dy++) for (var dx = -radius; dx <= radius; dx++)
            {
                var px = x+dx; var py = y+dy;
                if (dx*dx+dy*dy <= radius*radius && px >= 0 && px < Size && py >= 0 && py < Size)
                    pixels[py*Size+px] = color;
            }
        }
        previous = point; Revision++; dirty = true;
    }
    public void EndStroke() { previous = null; }
    public void Load(string png)
    {
        var sprite=DecodeSprite(png);
        Remember(); pixels=sprite.texture.GetPixels32(); Revision++; Upload();
        UnityEngine.Object.Destroy(sprite.texture); UnityEngine.Object.Destroy(sprite);
    }
    public void Flush() { if (dirty) Upload(); }
    private void Upload() { Texture.SetPixels32(pixels); Texture.Apply(false); dirty = false; }
    public string Encode() { Flush(); return Convert.ToBase64String(Texture.EncodeToPNG()); }
    public void Dispose() { if (Texture != null) UnityEngine.Object.Destroy(Texture); }

    public static Sprite DecodeSprite(string base64)
    {
        if (string.IsNullOrEmpty(base64) || base64.Length > 900000) throw new ArgumentException("Drawing is missing or too large.");
        var bytes = Convert.FromBase64String(base64);
        // Check dimensions before allowing Unity to allocate the decoded image.
        if (bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71)
            throw new ArgumentException("Drawing must be PNG.");
        int IntAt(int index) => (bytes[index]<<24)|(bytes[index+1]<<16)|(bytes[index+2]<<8)|bytes[index+3];
        if (IntAt(16) != Size || IntAt(20) != Size) throw new ArgumentException("Drawing dimensions must be 384 by 384.");
        var texture = new Texture2D(2,2,TextureFormat.RGBA32,false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        try
        {
            if (!texture.LoadImage(bytes)) throw new ArgumentException("Invalid drawing PNG.");
            var data = texture.GetPixels32();
            int minX = Size, minY = Size, maxX = -1, maxY = -1;
            for (int y=0;y<Size;y++) for (int x=0;x<Size;x++) if (data[y*Size+x].a > 0)
            { minX = Mathf.Min(minX,x); minY = Mathf.Min(minY,y); maxX = Mathf.Max(maxX,x); maxY = Mathf.Max(maxY,y); }
            if (maxX < 0) throw new ArgumentException("Draw something first.");
            return Sprite.Create(texture,new Rect(minX,minY,maxX-minX+1,maxY-minY+1),new Vector2(0.5f,0.5f),Mathf.Max(maxX-minX+1,maxY-minY+1),0,SpriteMeshType.FullRect);
        }
        catch { UnityEngine.Object.Destroy(texture); throw; }
    }
}

[Serializable]
public sealed class DrawingRequest { public string imageBase64; }
[Serializable]
public sealed class DrawingResponse { public DrawingCandidate[] candidates; }
[Serializable]
public sealed class DrawingCandidate
{
    public string label;
    public string summary;
    public WeaponRecipe recipe;
}
[Serializable]
public sealed class SavedDrawingWeapon
{
    public string imageBase64;
    public WeaponRecipe recipe;
}

/// <summary>Sprites face the camera like paper cutouts, keeping the user's drawing readable.</summary>
public sealed class DrawingBillboard : MonoBehaviour
{
    private void LateUpdate()
    {
        if (Camera.main != null) transform.rotation = Camera.main.transform.rotation;
    }
}
