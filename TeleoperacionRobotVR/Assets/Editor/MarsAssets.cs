using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

/// Genera por código los recursos visuales de la temática "Base de exploración en Marte":
/// texturas (suelo, roca, metal, franjas de peligro, celdas solares), mallas low-poly (rocas, terreno),
/// cielo, materiales y perfil de post-procesado. Todo queda guardado en Assets/Generated.
public static class MarsAssets
{
    public const string Root = "Assets/Generated";
    const string TexDir = Root + "/Textures";
    const string MeshDir = Root + "/Meshes";
    const string MatDir = Root + "/Materials";

    static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

    public static void Reset()
    {
        cache.Clear();
        meshCache.Clear();
        Ensure("Assets", "Generated");
        Ensure(Root, "Textures");
        Ensure(Root, "Meshes");
        Ensure(Root, "Materials");
    }

    static void Ensure(string parent, string name)
    {
        if (!AssetDatabase.IsValidFolder(parent + "/" + name)) AssetDatabase.CreateFolder(parent, name);
    }

    // ================================================================== ruido periódico (texturas sin costuras)

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 982451653;
            h = (h ^ (h >> 13)) * 1274126177;
            return ((h ^ (h >> 16)) & 0xffffff) / (float)0xffffff;
        }
    }

    static float ValueNoise(float x, float y, int period, int seed)
    {
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        int Wrap(int v) => ((v % period) + period) % period;
        float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
        float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
    }

    /// fBm periódico: u, v en [0,1) -> [0,1]
    static float Fbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        float sum = 0, amp = 0.5f, norm = 0;
        int period = basePeriod;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * ValueNoise(u * period, v * period, period, seed + o * 31);
            norm += amp;
            amp *= 0.5f;
            period *= 2;
        }
        return sum / norm;
    }

    static float Noise3(Vector3 p)
    {
        return (Mathf.PerlinNoise(p.x, p.y) + Mathf.PerlinNoise(p.y, p.z) + Mathf.PerlinNoise(p.z, p.x) +
                Mathf.PerlinNoise(p.y + 31.7f, p.x) + Mathf.PerlinNoise(p.z + 17.3f, p.y) + Mathf.PerlinNoise(p.x + 7.1f, p.z)) / 6f;
    }

    // ================================================================== texturas

    delegate Color Pixel(float u, float v);

    static Texture2D SaveTexture(string name, int size, Pixel f, bool normalFromHeight = false, Func<float, float, float> height = null, float bump = 4f)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = f(x / (float)size, y / (float)size);
        tex.SetPixels(px);
        tex.Apply();
        string path = $"{TexDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.anisoLevel = 4;
        imp.mipmapEnabled = true;
        imp.SaveAndReimport();

        if (normalFromHeight && height != null)
            SaveNormal(name + "_N", size, height, bump);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void SaveNormal(string name, int size, Func<float, float, float> h, float bump)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        var px = new Color[size * size];
        float e = 1f / size;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x * e, v = y * e;
                float dx = (h((u + e) % 1f, v) - h((u - e + 1f) % 1f, v)) * bump;
                float dy = (h(u, (v + e) % 1f) - h(u, (v - e + 1f) % 1f)) * bump;
                var n = new Vector3(-dx, -dy, 1f).normalized;
                px[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
            }
        tex.SetPixels(px);
        tex.Apply();
        string path = $"{TexDir}/{name}.png";
        File.WriteAllBytes(path, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.NormalMap;
        imp.wrapMode = TextureWrapMode.Repeat;
        imp.anisoLevel = 4;
        imp.SaveAndReimport();
    }

    public static Texture2D Normal(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}_N.png");

    public static void BuildTextures()
    {
        // Suelo marciano: polvo rojizo con manchas, grietas y piedritas
        Func<float, float, float> groundH = (u, v) =>
        {
            float big = Fbm(u, v, 4, 5, 11);
            float pebbles = Mathf.Clamp01(Mathf.Pow(Fbm(u, v, 32, 2, 23), 8f) * 3f) * 0.6f;
            return big * 0.6f + pebbles;
        };
        SaveTexture("Suelo", 1024, (u, v) =>
        {
            float big = Fbm(u, v, 4, 5, 11);
            float mid = Fbm(u, v, 16, 3, 5);
            float fine = Fbm(u, v, 64, 2, 91);
            float peb = Mathf.Clamp01(Mathf.Pow(Fbm(u, v, 32, 2, 23), 8f) * 3f);
            var dark = new Color(0.46f, 0.21f, 0.11f);
            var light = new Color(0.80f, 0.47f, 0.28f);
            var c = Color.Lerp(dark, light, Mathf.SmoothStep(0.15f, 0.9f, big * 0.6f + mid * 0.3f + fine * 0.1f));
            c = Color.Lerp(c, new Color(0.38f, 0.2f, 0.13f), peb * 0.55f);   // piedritas: más oscuras, pero no negras
            return c;
        }, true, groundH, 3.5f);

        // Roca: gris rojiza con vetas
        Func<float, float, float> rockH = (u, v) => Fbm(u, v, 8, 5, 77);
        SaveTexture("Roca", 512, (u, v) =>
        {
            float n = Fbm(u, v, 8, 5, 77);
            float vein = Mathf.Abs(Mathf.Sin((u * 6 + n * 3) * Mathf.PI));
            var c = Color.Lerp(new Color(0.30f, 0.17f, 0.12f), new Color(0.62f, 0.38f, 0.27f), n);
            return Color.Lerp(c * 0.8f, c, Mathf.Pow(vein, 0.3f));
        }, true, rockH, 5f);

        // Paneles metálicos con juntas y remaches (para la base y el rover)
        Func<float, float, float> panelH = (u, v) =>
        {
            float gu = Mathf.Abs(((u * 4) % 1f) - 0.5f), gv = Mathf.Abs(((v * 4) % 1f) - 0.5f);
            float seam = (gu > 0.485f || gv > 0.485f) ? 0f : 1f;
            float rivet = Mathf.Max(0, 1 - Mathf.Min(Vector2.Distance(new Vector2(gu, gv), new Vector2(0.43f, 0.43f)) * 40f, 1f));
            return seam * 0.5f + rivet * 0.3f;
        };
        SaveTexture("Panel", 512, (u, v) =>
        {
            float h = panelH(u, v);
            float grime = Fbm(u, v, 8, 4, 41);
            var c = new Color(0.86f, 0.86f, 0.84f) * (0.85f + 0.15f * h);
            return Color.Lerp(c, new Color(0.62f, 0.45f, 0.36f), Mathf.SmoothStep(0.55f, 0.9f, grime) * 0.55f);
        }, true, panelH, 3f);

        // Franjas de peligro amarillo / negro
        SaveTexture("Peligro", 256, (u, v) =>
        {
            bool stripe = ((u + v) * 4f % 1f) < 0.5f;
            float wear = Fbm(u, v, 8, 3, 9);
            var c = stripe ? new Color(0.95f, 0.72f, 0.1f) : new Color(0.08f, 0.08f, 0.08f);
            return Color.Lerp(c, new Color(0.5f, 0.3f, 0.2f), Mathf.SmoothStep(0.65f, 0.95f, wear) * 0.6f);
        });

        // Celdas solares
        SaveTexture("Solar", 256, (u, v) =>
        {
            float gu = (u * 6) % 1f, gv = (v * 4) % 1f;
            bool line = gu < 0.04f || gv < 0.05f;
            return line ? new Color(0.75f, 0.78f, 0.82f) : Color.Lerp(new Color(0.04f, 0.07f, 0.2f), new Color(0.1f, 0.18f, 0.42f), v);
        });

        // Punto suave para partículas
        SaveTexture("Particula", 64, (u, v) =>
        {
            float d = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
            float a = Mathf.Clamp01(1 - d);
            return new Color(1, 1, 1, a * a);
        });
        var pImp = (TextureImporter)AssetImporter.GetAtPath($"{TexDir}/Particula.png");
        pImp.alphaIsTransparency = true;
        pImp.wrapMode = TextureWrapMode.Clamp;
        pImp.SaveAndReimport();
    }

    public static Texture2D Tex(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{name}.png");

    // ================================================================== materiales

    public static Material Mat(string name, Color color, float smooth = 0.3f, float metal = 0f,
                               string tex = null, Vector2? tiling = null, string normal = null, float bump = 1f)
    {
        if (cache.TryGetValue(name, out var m)) return m;
        string path = $"{MatDir}/{name}.mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = color;
        m.SetFloat("_Glossiness", smooth);
        m.SetFloat("_Metallic", metal);
        if (tex != null)
        {
            m.mainTexture = Tex(tex);
            m.mainTextureScale = tiling ?? Vector2.one;
        }
        if (normal != null)
        {
            m.SetTexture("_BumpMap", Normal(normal));
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
        }
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        cache[name] = m;
        return m;
    }

    public static Material Emissive(string name, Color color, float intensity, Color? albedo = null)
    {
        var m = Mat(name, albedo ?? color * 0.3f, 0.6f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", color * intensity);
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }

    public static Material Particles(string name, bool additive)
    {
        if (cache.TryGetValue(name, out var m)) return m;
        string path = $"{MatDir}/{name}.mat";
        m = AssetDatabase.LoadAssetAtPath<Material>(path);
        var shader = Shader.Find(additive ? "Legacy Shaders/Particles/Additive" : "Legacy Shaders/Particles/Alpha Blended")
                     ?? Shader.Find("Sprites/Default");
        if (m == null)
        {
            m = new Material(shader);
            AssetDatabase.CreateAsset(m, path);
        }
        m.shader = shader;
        m.mainTexture = Tex("Particula");
        if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        EditorUtility.SetDirty(m);
        cache[name] = m;
        return m;
    }

    public static Material Skybox()
    {
        string path = $"{MatDir}/CieloMarte.mat";
        AssetDatabase.DeleteAsset(path);   // material nuevo: no hereda valores de otro shader
        var m = new Material(Shader.Find("Teleop/MarsSky"));
        m.SetColor("_Zenith", new Color(0.36f, 0.22f, 0.17f));
        m.SetColor("_Horizon", new Color(0.92f, 0.66f, 0.48f));
        m.SetColor("_Ground", new Color(0.42f, 0.26f, 0.18f));
        m.SetFloat("_HorizonSharp", 3f);
        m.SetColor("_SunColor", new Color(1f, 0.95f, 0.88f));
        m.SetColor("_HaloColor", new Color(0.62f, 0.74f, 0.9f));
        m.SetFloat("_SunSize", 0.9993f);
        m.SetFloat("_HaloPower", 18f);
        m.SetFloat("_HaloStrength", 0.55f);
        m.SetFloat("_Exposure", 1f);
        AssetDatabase.CreateAsset(m, path);
        EditorUtility.SetDirty(m);
        return m;
    }

    // ================================================================== mallas low-poly

    static Mesh SaveMesh(Mesh mesh, string name)
    {
        string path = $"{MeshDir}/{name}.asset";
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }

    /// Convierte una malla indexada en "flat shading" (caras planas: estilo low-poly).
    static Mesh Flatten(List<Vector3> verts, List<int> tris, Func<Vector3, Vector2> uv, string name)
    {
        var v = new List<Vector3>(tris.Count);
        var u = new List<Vector2>(tris.Count);
        var t = new List<int>(tris.Count);
        for (int i = 0; i < tris.Count; i++)
        {
            v.Add(verts[tris[i]]);
            u.Add(uv(verts[tris[i]]));
            t.Add(i);
        }
        var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        m.SetVertices(v);
        m.SetUVs(0, u);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateTangents();
        m.RecalculateBounds();
        return m;
    }

    /// Roca low-poly: icosfera deformada con ruido, aplanada en la base.
    static readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();

    public static Mesh Rock(int seed, int subdivisions = 1, float flatten = 0.75f)
    {
        string key = "Roca_" + seed + "_" + subdivisions;
        if (meshCache.TryGetValue(key, out var cached) && cached) return cached;
        var mesh = BuildRock(seed, subdivisions, flatten);
        meshCache[key] = mesh;
        return mesh;
    }

    static Mesh BuildRock(int seed, int subdivisions, float flatten)
    {
        float phi = (1f + Mathf.Sqrt(5f)) / 2f;
        var verts = new List<Vector3>
        {
            new Vector3(-1, phi, 0), new Vector3(1, phi, 0), new Vector3(-1, -phi, 0), new Vector3(1, -phi, 0),
            new Vector3(0, -1, phi), new Vector3(0, 1, phi), new Vector3(0, -1, -phi), new Vector3(0, 1, -phi),
            new Vector3(phi, 0, -1), new Vector3(phi, 0, 1), new Vector3(-phi, 0, -1), new Vector3(-phi, 0, 1)
        };
        var tris = new List<int>
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };
        for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
        for (int s = 0; s < subdivisions; s++)
        {
            var mid = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mid.TryGetValue(key, out int idx)) return idx;
                verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                mid[key] = verts.Count - 1;
                return verts.Count - 1;
            }
            var nt = new List<int>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nt.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            tris = nt;
        }
        var off = new Vector3(seed * 3.17f, seed * 1.91f, seed * 2.53f);
        for (int i = 0; i < verts.Count; i++)
        {
            var d = verts[i];
            float r = 0.75f + 0.45f * Noise3(d * 1.4f + off) + 0.12f * Noise3(d * 4f + off * 2f);
            var p = d * r;
            p.y *= flatten;
            if (p.y < -0.25f) p.y = -0.25f;   // base plana para que asiente en el suelo
            verts[i] = p;
        }
        return SaveMesh(Flatten(verts, tris, p => new Vector2(p.x + p.y * 0.5f, p.z - p.y * 0.5f) * 0.5f, "Roca_" + seed + "_" + subdivisions), "Roca_" + seed + "_" + subdivisions);
    }

    /// Terreno low-poly alrededor de la arena: plano dentro (|x|,|z| < flatHalf), dunas y montañas fuera.
    public static Mesh Terrain(float size, float cell, float flatHalf)
    {
        int n = Mathf.RoundToInt(size / cell);
        var verts = new List<Vector3>((n + 1) * (n + 1));
        var tris = new List<int>(n * n * 6);
        float half = size / 2f;
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                float px = -half + x * cell, pz = -half + z * cell;
                float edge = Mathf.Max(Mathf.Abs(px), Mathf.Abs(pz)) - flatHalf;
                float h = 0f;
                if (edge > 0f)
                {
                    float rise = Mathf.SmoothStep(0f, 1f, edge / 14f);
                    float dunes = Mathf.PerlinNoise(px * 0.06f + 10f, pz * 0.06f + 3f) * 3.5f;
                    float ridge = Mathf.Pow(Mathf.PerlinNoise(px * 0.025f + 50f, pz * 0.025f + 20f), 2f) * 22f;
                    float far = Mathf.SmoothStep(0f, 1f, (edge - 20f) / 40f);
                    h = rise * (0.6f + dunes) + far * ridge;
                }
                else
                {
                    // Pequeña rugosidad visual dentro de la arena (sin afectar la física, que usa un plano)
                    h = 0f;
                }
                verts.Add(new Vector3(px, h, pz));
            }
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int i = z * (n + 1) + x;
                if (((x + z) & 1) == 0) tris.AddRange(new[] { i, i + n + 1, i + 1, i + 1, i + n + 1, i + n + 2 });
                else tris.AddRange(new[] { i, i + n + 1, i + n + 2, i, i + n + 2, i + 1 });
            }
        return SaveMesh(Flatten(verts, tris, p => new Vector2(p.x, p.z) / 6f, "Terreno"), "Terreno");
    }

    // ================================================================== post-procesado

    public static PostProcessProfile PostProfile()
    {
        string path = Root + "/PostProcesado.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<PostProcessProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var bloom = profile.AddSettings<Bloom>();
        bloom.intensity.Override(2.2f);
        bloom.threshold.Override(1.05f);
        bloom.softKnee.Override(0.6f);
        bloom.diffusion.Override(7f);

        var cg = profile.AddSettings<ColorGrading>();
        cg.gradingMode.Override(GradingMode.HighDefinitionRange);
        cg.tonemapper.Override(Tonemapper.ACES);
        cg.postExposure.Override(0.45f);
        cg.temperature.Override(6f);
        cg.saturation.Override(8f);
        cg.contrast.Override(10f);

        var ao = profile.AddSettings<AmbientOcclusion>();
        ao.mode.Override(AmbientOcclusionMode.MultiScaleVolumetricObscurance);
        ao.intensity.Override(0.6f);

        var vig = profile.AddSettings<Vignette>();
        vig.intensity.Override(0.3f);
        vig.smoothness.Override(0.45f);

        var grain = profile.AddSettings<Grain>();
        grain.intensity.Override(0.12f);
        grain.size.Override(0.8f);

        foreach (var s in profile.settings) AssetDatabase.AddObjectToAsset(s, profile);
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    public static PostProcessResources PostResources()
    {
        var r = AssetDatabase.LoadAssetAtPath<PostProcessResources>("Packages/com.unity.postprocessing/PostProcessing/PostProcessResources.asset");
        if (r) return r;
        foreach (var guid in AssetDatabase.FindAssets("t:PostProcessResources"))
            return AssetDatabase.LoadAssetAtPath<PostProcessResources>(AssetDatabase.GUIDToAssetPath(guid));
        return null;
    }

    public static PostProcessLayer AddPostLayer(Camera cam)
    {
        var layer = cam.gameObject.GetComponent<PostProcessLayer>();
        if (layer == null) layer = cam.gameObject.AddComponent<PostProcessLayer>();
        var res = PostResources();
        if (res) layer.Init(res);
        layer.volumeLayer = 1;   // capa Default
        layer.antialiasingMode = PostProcessLayer.Antialiasing.FastApproximateAntialiasing;
        layer.volumeTrigger = cam.transform;
        return layer;
    }
}
