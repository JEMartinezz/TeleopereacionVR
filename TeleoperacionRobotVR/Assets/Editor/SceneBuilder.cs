using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.PostProcessing;

/// Construye la escena de la práctica con la temática "Base de exploración en Marte":
/// arena con barreras de seguridad, módulos de la base, rocas, paneles solares y tanques como obstáculos
/// (todos con collider), cristales de muestra como objetos manipulables, plataforma de entrega,
/// rover con garra, cámara de seguimiento, avatar astronauta, cielo marciano, polvo y post-procesado.
/// Menú: Teleoperación > Construir escena.
public static class SceneBuilder
{
    const string ScenePath = "Assets/Scenes/Teleoperacion.unity";
    const float Half = 10f;          // la arena mide 20 x 20 m
    const float WallH = 1.2f;

    // Llamado desde línea de comandos: -executeMethod SceneBuilder.BuildAll
    public static void BuildAll()
    {
        ConfigureProject();
        var xr = Type.GetType("XRProjectSetup, TeleopXRSetup");
        xr?.GetMethod("Configure")?.Invoke(null, null);
        BuildScene();
    }

    [MenuItem("Teleoperación/Configurar proyecto")]
    public static void ConfigureProject()
    {
        PlayerSettings.companyName = "UMNG";
        PlayerSettings.productName = "Teleoperacion Robot VR";
        PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone, ApiCompatibilityLevel.NET_Unity_4_8);
        PlayerSettings.runInBackground = true;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowResolution = ShadowResolution.High;
        QualitySettings.shadowDistance = 40f;
        QualitySettings.shadowCascades = 2;
        QualitySettings.antiAliasing = 2;
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Teleoperación/Construir escena")]
    public static void BuildScene()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
        MarsAssets.Reset();
        MarsAssets.BuildTextures();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---------- Materiales
        var mSuelo = MarsAssets.Mat("Suelo", Color.white, 0.12f, 0f, "Suelo", Vector2.one, "Suelo", 0.9f);
        var mRoca = MarsAssets.Mat("Roca", new Color(0.95f, 0.85f, 0.8f), 0.15f, 0f, "Roca", Vector2.one * 2f, "Roca", 0.8f);
        var mPanel = MarsAssets.Mat("Panel", Color.white, 0.55f, 0.15f, "Panel", Vector2.one, "Panel", 0.6f);
        var mPanelGris = MarsAssets.Mat("PanelGris", new Color(0.55f, 0.56f, 0.58f), 0.45f, 0.3f, "Panel", Vector2.one * 2f, "Panel", 0.6f);
        var mPeligro = MarsAssets.Mat("Peligro", Color.white, 0.35f, 0f, "Peligro", new Vector2(4f, 1f));
        var mSolar = MarsAssets.Mat("Solar", Color.white, 0.85f, 0.6f, "Solar", Vector2.one);
        var mMetal = MarsAssets.Mat("MetalOscuro", new Color(0.18f, 0.19f, 0.21f), 0.6f, 0.8f);
        var mMetalClaro = MarsAssets.Mat("MetalClaro", new Color(0.72f, 0.74f, 0.77f), 0.7f, 0.9f);
        var mNaranja = MarsAssets.Mat("PinturaNaranja", new Color(0.95f, 0.42f, 0.1f), 0.55f, 0.1f);
        var mGoma = MarsAssets.Mat("Goma", new Color(0.06f, 0.06f, 0.06f), 0.15f);
        var mVisor = MarsAssets.Mat("VisorDorado", new Color(1f, 0.75f, 0.3f), 0.95f, 1f);
        var mCaja = MarsAssets.Mat("Caja", new Color(0.9f, 0.85f, 0.75f), 0.3f, 0.1f, "Panel", Vector2.one * 0.5f, "Panel", 0.8f);
        var mBaliza = MarsAssets.Emissive("Baliza", new Color(1f, 0.45f, 0.08f), 4f);
        var mLuzBlanca = MarsAssets.Emissive("LuzBlanca", new Color(1f, 0.92f, 0.8f), 5f);
        var mOjoCian = MarsAssets.Emissive("OjoCian", new Color(0.2f, 0.85f, 1f), 4f);
        var mMeta = MarsAssets.Emissive("MetaVerde", new Color(0.15f, 1f, 0.45f), 2.5f, new Color(0.1f, 0.3f, 0.15f));
        var mHolo = MarsAssets.Emissive("BordeHolo", new Color(0.2f, 0.8f, 1f), 3f);
        var mPantalla = MarsAssets.Mat("Pantalla", new Color(0.02f, 0.04f, 0.06f), 0.9f);

        var physObjects = new PhysicsMaterial("Objetos") { dynamicFriction = 0.6f, staticFriction = 0.7f, bounciness = 0.05f };
        var physRobot = new PhysicsMaterial("Robot") { dynamicFriction = 0f, staticFriction = 0f, frictionCombine = PhysicsMaterialCombine.Minimum };
        CreateOrReplace(physObjects, MarsAssets.Root + "/FisicaObjetos.asset");
        CreateOrReplace(physRobot, MarsAssets.Root + "/FisicaRobot.asset");

        // ---------- Cielo, sol, niebla
        var sun = new GameObject("Sol").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.83f, 0.66f);
        sun.intensity = 1.45f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.85f;
        sun.transform.rotation = Quaternion.Euler(26f, -38f, 0f);
        var sky = MarsAssets.Skybox();
        sky.SetVector("_SunDir", -sun.transform.forward);
        RenderSettings.skybox = sky;
        RenderSettings.sun = sun;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.ambientIntensity = 1.1f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.86f, 0.6f, 0.44f);
        RenderSettings.fogDensity = 0.013f;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

        // ---------- Terreno
        var env = new GameObject("Entorno").transform;
        var terrain = new GameObject("TerrenoMarciano");
        terrain.transform.SetParent(env);
        terrain.AddComponent<MeshFilter>().sharedMesh = MarsAssets.Terrain(170f, 1.7f, Half + 0.6f);
        terrain.AddComponent<MeshRenderer>().sharedMaterial = mSuelo;
        var terrainCol = terrain.AddComponent<MeshCollider>();   // solo para ubicar la decoración; se quita al final
        Physics.SyncTransforms();

        // Piso físico de la arena (plano exacto y estable para el rover)
        var floor = new GameObject("PisoFisico");
        floor.transform.SetParent(env);
        floor.transform.position = new Vector3(0, -0.1f, 0);
        floor.AddComponent<BoxCollider>().size = new Vector3(2 * Half + 2, 0.2f, 2 * Half + 2);

        // ---------- Límites físicos: barreras de seguridad + campo invisible
        var walls = new GameObject("LimitesFisicos").transform;
        walls.SetParent(env);
        float t = 0.3f;
        foreach (var (n, p, s) in new[] {
            ("Norte", new Vector3(0, WallH / 2, Half + t / 2), new Vector3(2 * Half + 2 * t, WallH, t)),
            ("Sur", new Vector3(0, WallH / 2, -Half - t / 2), new Vector3(2 * Half + 2 * t, WallH, t)),
            ("Este", new Vector3(Half + t / 2, WallH / 2, 0), new Vector3(t, WallH, 2 * Half)),
            ("Oeste", new Vector3(-Half - t / 2, WallH / 2, 0), new Vector3(t, WallH, 2 * Half)) })
        {
            var w = new GameObject("Limite_" + n);
            w.transform.SetParent(walls);
            w.transform.position = p;
            w.AddComponent<BoxCollider>().size = s;
            Surface(w, HapticPattern.ParedLimite);

            var hi = new GameObject("BarreraInvisible_" + n);
            hi.transform.SetParent(walls);
            hi.transform.position = p + Vector3.up * (WallH / 2 + 1.3f);
            hi.AddComponent<BoxCollider>().size = new Vector3(s.x, 2.6f, s.z);
            Surface(hi, HapticPattern.ParedLimite);
        }
        // Barreras visibles (tipo "new jersey") con franja de peligro y balizas
        var barrierVis = new GameObject("BarrerasVisuales").transform;
        barrierVis.SetParent(walls);
        int beacon = 0;
        for (float a = -Half; a < Half - 0.01f; a += 2f)
        {
            foreach (var (pos, rotY) in new[] {
                (new Vector3(a + 1f, 0, Half + 0.15f), 0f), (new Vector3(a + 1f, 0, -Half - 0.15f), 0f),
                (new Vector3(Half + 0.15f, 0, a + 1f), 90f), (new Vector3(-Half - 0.15f, 0, a + 1f), 90f) })
            {
                var seg = new GameObject("Barrera");
                seg.transform.SetParent(barrierVis);
                seg.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, rotY, 0));
                Box("Base", seg.transform, new Vector3(0, 0.22f, 0), new Vector3(1.96f, 0.44f, 0.42f), mPanelGris, false, true);
                Box("Franja", seg.transform, new Vector3(0, 0.52f, 0), new Vector3(1.96f, 0.16f, 0.3f), mPeligro, false, true);
            }
        }
        for (float a = -Half; a <= Half + 0.01f; a += 4f)
            foreach (var pos in new[] { new Vector3(a, 0, Half + 0.15f), new Vector3(a, 0, -Half - 0.15f), new Vector3(Half + 0.15f, 0, a), new Vector3(-Half - 0.15f, 0, a) })
            {
                var post = new GameObject("Poste");
                post.transform.SetParent(barrierVis);
                post.transform.position = pos;
                Cylinder("Mastil", post.transform, new Vector3(0, 0.6f, 0), 0.08f, 1.2f, mMetal, false, true);
                var lamp = Sphere("Baliza", post.transform, new Vector3(0, 1.25f, 0), 0.14f, mBaliza, false, true);
                var pulse = lamp.AddComponent<PulseLight>();
                pulse.emissive = lamp.GetComponent<Renderer>();
                pulse.emissionColor = new Color(1f, 0.45f, 0.08f);
                pulse.minIntensity = 0.6f;
                pulse.maxIntensity = 5f;
                pulse.speed = 0.8f;
                pulse.phase = (beacon++ % 4) * 0.25f;
            }

        // ---------- Obstáculos (todos con collider sólido + HapticSurface "Obstaculo")
        var obst = new GameObject("Obstaculos").transform;
        obst.SetParent(env);

        // Base habitacional: domo principal, domo pequeño y túnel
        var hab = Group("ModuloHabitacional", obst, new Vector3(-5.2f, 0, -2.6f));
        Sphere("DomoPrincipal", hab, new Vector3(0, 0, 0), 2.6f, mPanel, true);
        Sphere("DomoPequeno", hab, new Vector3(1.6f, 0, -1.4f), 1.4f, mPanel, true);
        var tunnel = Cylinder("Tunel", hab, new Vector3(0.9f, 0.32f, -0.8f), 0.6f, 1.2f, mPanelGris, true);
        tunnel.transform.localRotation = Quaternion.Euler(90, 0, 0) * Quaternion.Euler(0, 0, 0);
        tunnel.transform.rotation = Quaternion.LookRotation(new Vector3(1.6f, 0, -1.4f).normalized) * Quaternion.Euler(90, 0, 0);
        Box("Compuerta", hab, new Vector3(0.2f, 0.45f, 1.25f), new Vector3(0.7f, 0.9f, 0.3f), mNaranja, true);
        Box("LuzCompuerta", hab, new Vector3(0.2f, 0.95f, 1.36f), new Vector3(0.4f, 0.06f, 0.06f), mLuzBlanca, false, true);
        AddLight(hab, new Vector3(0.2f, 1.1f, 1.8f), new Color(1f, 0.85f, 0.6f), 2.5f, 4f);
        Surface(hab.gameObject, HapticPattern.Obstaculo);

        // Pila de cajas de suministros
        var crates = Group("CajasSuministros", obst, new Vector3(3f, 0, 1.5f));
        Box("Caja_A", crates, new Vector3(0, 0.4f, 0), new Vector3(0.8f, 0.8f, 0.8f), mCaja, true);
        Box("Caja_B", crates, new Vector3(0.85f, 0.35f, -0.2f), new Vector3(0.7f, 0.7f, 0.7f), mCaja, true).transform.localRotation = Quaternion.Euler(0, 18, 0);
        Box("Caja_C", crates, new Vector3(0.15f, 1.05f, 0.05f), new Vector3(0.5f, 0.5f, 0.5f), mNaranja, true).transform.localRotation = Quaternion.Euler(0, -25, 0);
        Box("Franja_A", crates, new Vector3(0, 0.4f, 0.405f), new Vector3(0.8f, 0.12f, 0.01f), mPeligro, false, true);
        Surface(crates.gameObject, HapticPattern.Obstaculo);

        // Campo de paneles solares sobre una bancada (la bancada es lo que choca con el rover)
        var solar = Group("PanelesSolares", obst, new Vector3(-1.5f, 0, 4.5f));
        Box("Bancada", solar, new Vector3(0, 0.25f, 0), new Vector3(5f, 0.5f, 0.6f), mPanelGris, true);
        for (int i = 0; i < 4; i++)
        {
            float x = -1.875f + i * 1.25f;
            Cylinder("Soporte" + i, solar, new Vector3(x, 0.75f, 0), 0.07f, 0.5f, mMetal, true);
            Box("Panel" + i, solar, new Vector3(x, 1.05f, 0), new Vector3(1.18f, 0.03f, 0.85f), mSolar, true).transform.localRotation = Quaternion.Euler(-28, 0, 0);
        }
        Surface(solar.gameObject, HapticPattern.Obstaculo);

        // Tanques de combustible horizontales
        var tanks = Group("TanquesCombustible", obst, new Vector3(6f, 0, -3.5f));
        foreach (float z in new[] { 1f, -1f })
        {
            var tk = Cylinder("Tanque", tanks, new Vector3(0, 0.55f, z), 0.9f, 1.8f, mPanel, true);
            tk.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Cylinder("Banda", tanks, new Vector3(0, 0.55f, z), 0.94f, 0.12f, mNaranja, false, true).transform.localRotation = Quaternion.Euler(90, 0, 0);
            foreach (float dz in new[] { -0.55f, 0.55f })
                Box("Soporte", tanks, new Vector3(0, 0.12f, z + dz), new Vector3(0.8f, 0.24f, 0.12f), mMetal, true);
        }
        Surface(tanks.gameObject, HapticPattern.Obstaculo);

        // Rocas grandes low-poly (collider convexo con la forma de la roca)
        var rocks = new GameObject("Rocas").transform;
        rocks.SetParent(obst);
        foreach (var (pos, scale, seed) in new[] {
            (new Vector3(-6.2f, 0, 5.2f), 1.1f, 1), (new Vector3(1.5f, 0, -5.6f), 0.8f, 2), (new Vector3(-7.4f, 0, -6.6f), 0.95f, 3),
            (new Vector3(7.6f, 0, 0.6f), 0.65f, 4), (new Vector3(-3.4f, 0, 8.1f), 0.6f, 5), (new Vector3(3.6f, 0, -1.6f), 0.45f, 6),
            (new Vector3(-8.3f, 0, 1.0f), 0.5f, 7), (new Vector3(5.2f, 0, 7.6f), 0.5f, 8) })
        {
            var r = Rock("Roca_" + seed, rocks, pos, scale, seed, mRoca, true);
            Surface(r, HapticPattern.Obstaculo);
        }

        // ---------- Decoración fuera de la arena (sin interacción)
        var deco = new GameObject("DecoracionExterior").transform;
        deco.SetParent(env);
        var rng = new System.Random(42);
        for (int i = 0; i < 70; i++)
        {
            float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
            float dist = 13f + (float)rng.NextDouble() * 45f;
            var p = new Vector3(Mathf.Cos(ang) * dist, 60f, Mathf.Sin(ang) * dist);
            if (!Physics.Raycast(p, Vector3.down, out var hit, 200f)) continue;
            float s = 0.4f + (float)rng.NextDouble() * (dist > 30 ? 3.5f : 1.4f);
            var r = Rock("RocaLejana", deco, hit.point + Vector3.down * 0.1f * s, s, 10 + i % 12, mRoca, false);
            r.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
        }
        // Torre de comunicaciones y módulo de aterrizaje lejano
        var tower = Group("TorreComunicaciones", deco, new Vector3(-13.5f, 0, 12.5f));
        Cylinder("Torre", tower, new Vector3(0, 3f, 0), 0.25f, 6f, mMetalClaro, false, true);
        var dish = Sphere("Antena", tower, new Vector3(0.5f, 5.2f, 0), 1.6f, mPanel, false, true);
        dish.transform.localScale = new Vector3(1.6f, 0.35f, 1.6f);
        dish.transform.localRotation = Quaternion.Euler(0, 0, 55);
        var towerLamp = Sphere("Luz", tower, new Vector3(0, 6.15f, 0), 0.25f, mBaliza, false, true);
        var tp = towerLamp.AddComponent<PulseLight>();
        tp.emissive = towerLamp.GetComponent<Renderer>();
        tp.minIntensity = 0.2f;
        tp.maxIntensity = 8f;
        tp.speed = 0.5f;
        var lander = Group("ModuloAterrizaje", deco, new Vector3(16f, 0, -14f));
        if (Physics.Raycast(new Vector3(16f, 60f, -14f), Vector3.down, out var lh, 200f)) lander.position = lh.point;
        Cylinder("Cuerpo", lander, new Vector3(0, 2.2f, 0), 3.2f, 2.2f, mPanel, false, true);
        Sphere("Cupula", lander, new Vector3(0, 3.3f, 0), 3f, mPanelGris, false, true).transform.localScale = new Vector3(3f, 1.4f, 3f);
        for (int i = 0; i < 4; i++)
        {
            var leg = Cylinder("Pata", lander, Quaternion.Euler(0, i * 90 + 45, 0) * new Vector3(0, 0.6f, 1.9f), 0.12f, 2.4f, mMetal, false, true);
            leg.transform.localRotation = Quaternion.Euler(0, i * 90 + 45, 0) * Quaternion.Euler(-30, 0, 0);
        }
        UnityEngine.Object.DestroyImmediate(terrainCol);

        // ---------- Objetos manipulables: cristales de muestra luminosos
        var objs = new GameObject("MuestrasManipulables").transform;
        objs.SetParent(env);
        var objData = new[]
        {
            ("Cristal_Cian", new Vector3(-2f, 0.11f, -1f), new Color(0.15f, 0.9f, 1f), false),
            ("Cristal_Verde", new Vector3(4.5f, 0.11f, 4f), new Color(0.3f, 1f, 0.35f), false),
            ("Orbe_Ambar", new Vector3(-6f, 0.11f, 1.5f), new Color(1f, 0.65f, 0.1f), true),
            ("Cristal_Magenta", new Vector3(0.5f, 0.11f, 7.5f), new Color(1f, 0.25f, 0.85f), false),
        };
        foreach (var (name, pos, col, sphere) in objData)
        {
            var o = GameObject.CreatePrimitive(sphere ? PrimitiveType.Sphere : PrimitiveType.Cube);
            o.transform.localScale = Vector3.one * (sphere ? 0.22f : 0.2f);
            o.name = name;
            o.transform.SetParent(objs);
            o.transform.position = pos;
            o.transform.rotation = Quaternion.Euler(0, UnityEngine.Random.Range(0f, 90f), 0);
            o.GetComponent<Renderer>().sharedMaterial = MarsAssets.Emissive(name, col, 4.5f, col * 0.5f);
            o.GetComponent<Collider>().sharedMaterial = physObjects;
            var rb = o.AddComponent<Rigidbody>();
            rb.mass = 0.4f;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            Surface(o, HapticPattern.ObjetoManipulable);
            var l = AddLight(o.transform, Vector3.zero, col, 1.2f, 1.6f);
            l.transform.localPosition = Vector3.zero;
        }

        // ---------- Plataforma de entrega
        var goalGo = new GameObject("ZonaMeta");
        goalGo.transform.SetParent(env);
        goalGo.transform.position = new Vector3(7.5f, 0f, 7.5f);
        var goalCol = goalGo.AddComponent<BoxCollider>();
        goalCol.isTrigger = true;
        goalCol.center = new Vector3(0, 0.5f, 0);
        goalCol.size = new Vector3(2.5f, 1f, 2.5f);
        var goal = goalGo.AddComponent<GoalZone>();
        Cylinder("AnilloLuz", goalGo.transform, new Vector3(0, 0.006f, 0), 2.7f, 0.012f, mMeta, false, true);
        Cylinder("Plataforma", goalGo.transform, new Vector3(0, 0.012f, 0), 2.45f, 0.024f, mPanelGris, false, true);
        Cylinder("Centro", goalGo.transform, new Vector3(0, 0.026f, 0), 0.9f, 0.006f, mMeta, false, true);
        for (int i = 0; i < 4; i++)
        {
            var pp = Quaternion.Euler(0, 45 + i * 90, 0) * new Vector3(0, 0, 1.55f);
            Cylinder("PosteLuz", goalGo.transform, pp + Vector3.up * 0.3f, 0.06f, 0.6f, mMetal, false, true);
            var gl = Sphere("LuzMeta", goalGo.transform, pp + Vector3.up * 0.65f, 0.12f, mMeta, false, true);
            var gpulse = gl.AddComponent<PulseLight>();
            gpulse.emissive = gl.GetComponent<Renderer>();
            gpulse.emissionColor = new Color(0.15f, 1f, 0.45f);
            gpulse.minIntensity = 0.8f;
            gpulse.maxIntensity = 5f;
            gpulse.phase = i * 0.25f;
        }
        AddLight(goalGo.transform, new Vector3(0, 1.2f, 0), new Color(0.3f, 1f, 0.5f), 2f, 4f);
        Sign("Cartel", goalGo.transform, new Vector3(0, 1.6f, -1.3f), "ZONA DE ENTREGA", new Color(0.4f, 1f, 0.6f), 0.06f);

        // ---------- Rover
        var robotGo = new GameObject("RobotMovil");
        robotGo.transform.position = new Vector3(0f, 0.02f, -3f);
        var rbRobot = robotGo.AddComponent<Rigidbody>();
        rbRobot.mass = 8f;
        var chassisCol = robotGo.AddComponent<BoxCollider>();
        chassisCol.center = new Vector3(0, 0.17f, -0.02f);
        chassisCol.size = new Vector3(0.6f, 0.3f, 0.7f);
        chassisCol.sharedMaterial = physRobot;
        var robot = robotGo.AddComponent<RobotController>();
        var rt = robotGo.transform;

        Box("Chasis", rt, new Vector3(0, 0.21f, -0.02f), new Vector3(0.42f, 0.12f, 0.6f), mPanel, false, true);
        Box("Franja", rt, new Vector3(0, 0.21f, 0.285f), new Vector3(0.43f, 0.05f, 0.02f), mNaranja, false, true);
        Box("PanelSolar", rt, new Vector3(0, 0.285f, -0.07f), new Vector3(0.48f, 0.02f, 0.5f), mSolar, false, true);
        Cylinder("Mastil", rt, new Vector3(0.13f, 0.42f, -0.2f), 0.035f, 0.26f, mMetalClaro, false, true);
        Box("CabezaCamara", rt, new Vector3(0.13f, 0.58f, -0.17f), new Vector3(0.14f, 0.07f, 0.09f), mPanel, false, true);
        Sphere("OjoIzq", rt, new Vector3(0.1f, 0.585f, -0.12f), 0.03f, mOjoCian, false, true);
        Sphere("OjoDer", rt, new Vector3(0.16f, 0.585f, -0.12f), 0.03f, mOjoCian, false, true);
        var rdish = Sphere("Antena", rt, new Vector3(-0.14f, 0.34f, -0.22f), 0.15f, mMetalClaro, false, true);
        rdish.transform.localScale = new Vector3(0.15f, 0.035f, 0.15f);
        rdish.transform.localRotation = Quaternion.Euler(-35, 0, 0);
        Cylinder("Varilla", rt, new Vector3(-0.19f, 0.45f, -0.27f), 0.01f, 0.32f, mMetal, false, true);
        foreach (float x in new[] { -0.27f, 0.27f })
            Box("Balancin", rt, new Vector3(x, 0.17f, 0), new Vector3(0.03f, 0.03f, 0.5f), mMetal, false, true);

        var left = new List<Transform>();
        var right = new List<Transform>();
        foreach (float z in new[] { 0.22f, 0f, -0.22f })
            foreach (float x in new[] { -0.27f, 0.27f })
            {
                var w = Cylinder(x < 0 ? "Rueda_Izq" : "Rueda_Der", rt, new Vector3(x, 0.1f, z), 0.2f, 0.07f, mGoma, false, true);
                w.transform.localRotation = Quaternion.Euler(0, 0, 90);
                var hub = Cylinder("Rin", w.transform, Vector3.zero, 0.5f, 1.1f, mMetalClaro, false, true);
                hub.transform.localScale = new Vector3(0.5f, 0.55f, 0.5f);
                (x < 0 ? left : right).Add(w.transform);
            }
        robot.leftWheels = left.ToArray();
        robot.rightWheels = right.ToArray();
        robot.wheelRadius = 0.1f;
        robot.trackWidth = 0.54f;

        // Faros
        var lights = new List<Light>();
        foreach (float x in new[] { -0.13f, 0.13f })
        {
            Box("Faro", rt, new Vector3(x, 0.235f, 0.295f), new Vector3(0.07f, 0.03f, 0.015f), mLuzBlanca, false, true);
            var spot = new GameObject("LuzFaro").AddComponent<Light>();
            spot.transform.SetParent(rt, false);
            spot.transform.localPosition = new Vector3(x, 0.24f, 0.31f);
            spot.transform.localRotation = Quaternion.Euler(10, 0, 0);
            spot.type = LightType.Spot;
            spot.spotAngle = 55f;
            spot.range = 9f;
            spot.intensity = 2.2f;
            spot.color = new Color(1f, 0.95f, 0.85f);
            lights.Add(spot);
        }

        // Garra
        var gripperGo = new GameObject("Garra");
        gripperGo.transform.SetParent(rt, false);
        gripperGo.transform.localPosition = new Vector3(0, 0.12f, 0.46f);
        var gt = gripperGo.transform;
        Box("Brazo", gt, new Vector3(0, 0.04f, -0.12f), new Vector3(0.08f, 0.05f, 0.1f), mMetal, false, true);
        Box("BasePinza", gt, new Vector3(0, 0.03f, -0.08f), new Vector3(0.46f, 0.04f, 0.04f), mNaranja, false, true);
        var lf = Box("Dedo_Izq", gt, new Vector3(-0.2f, 0f, 0f), new Vector3(0.03f, 0.12f, 0.2f), mMetalClaro, false, true);
        var rf = Box("Dedo_Der", gt, new Vector3(0.2f, 0f, 0f), new Vector3(0.03f, 0.12f, 0.2f), mMetalClaro, false, true);
        var zone = gripperGo.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.center = new Vector3(0, 0.02f, 0.02f);
        zone.size = new Vector3(0.42f, 0.3f, 0.36f);
        var gripper = gripperGo.AddComponent<RobotGripper>();
        gripper.robotBody = rbRobot;
        gripper.leftFinger = lf.transform;
        gripper.rightFinger = rf.transform;

        // Efectos: polvo en las ruedas traseras y chispas al chocar
        var fx = robotGo.AddComponent<RoverFX>();
        var dustMat = MarsAssets.Particles("Polvo", false);
        var sparkMat = MarsAssets.Particles("Chispas", true);
        fx.wheelDust = new[]
        {
            WheelDust(rt, new Vector3(-0.27f, 0.04f, -0.3f), dustMat),
            WheelDust(rt, new Vector3(0.27f, 0.04f, -0.3f), dustMat)
        };
        fx.impactSparks = Sparks(rt, sparkMat);
        fx.headlights = lights.ToArray();

        // ---------- Avatar astronauta (vista en primera persona con la tecla V)
        var avatar = new GameObject("Avatar");
        avatar.transform.position = new Vector3(-1.5f, 0.05f, -7.5f);
        var cc = avatar.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.3f;
        cc.center = new Vector3(0, 0.9f, 0);
        var headGo = new GameObject("CabezaCamara");
        headGo.tag = "Untagged";
        headGo.transform.SetParent(avatar.transform, false);
        headGo.transform.localPosition = new Vector3(0, 1.65f, 0);
        var cam = headGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;
        cam.fieldOfView = 70f;
        headGo.AddComponent<AudioListener>();
        headGo.AddComponent<XRHeadTracker>();
        MarsAssets.AddPostLayer(cam);
        var avatarCtl = avatar.AddComponent<AvatarController>();
        avatarCtl.head = headGo.transform;
        var at = avatar.transform;
        Capsule("Traje", at, new Vector3(0, 1.05f, 0), new Vector3(0.55f, 0.45f, 0.45f), mPanel);
        foreach (float x in new[] { -0.13f, 0.13f })
        {
            Capsule("Pierna", at, new Vector3(x, 0.45f, 0), new Vector3(0.2f, 0.32f, 0.2f), mPanel);
            Box("Bota", at, new Vector3(x, 0.07f, 0.04f), new Vector3(0.18f, 0.14f, 0.28f), mPanelGris, false, true);
            Capsule("Brazo", at, new Vector3(x * 2.4f, 1.0f, 0), new Vector3(0.15f, 0.3f, 0.15f), mPanel);
        }
        Sphere("Casco", at, new Vector3(0, 1.5f, 0), 0.36f, mPanel, false, true);
        Sphere("Visor", at, new Vector3(0, 1.52f, 0.09f), 0.26f, mVisor, false, true);
        Box("Mochila", at, new Vector3(0, 1.05f, -0.28f), new Vector3(0.42f, 0.55f, 0.18f), mPanelGris, false, true);
        Box("Insignia", at, new Vector3(0.14f, 1.15f, 0.22f), new Vector3(0.1f, 0.06f, 0.01f), mNaranja, false, true);

        // ---------- Cámara que sigue al rover (cámara principal)
        var chaseGo = new GameObject("CamaraSeguimiento");
        chaseGo.tag = "MainCamera";
        chaseGo.transform.position = robotGo.transform.position + new Vector3(0, 1.2f, -2.2f);
        chaseGo.transform.LookAt(robotGo.transform.position + Vector3.up * 0.3f);
        var chaseCam = chaseGo.AddComponent<Camera>();
        chaseCam.nearClipPlane = 0.05f;
        chaseCam.farClipPlane = 300f;
        chaseCam.fieldOfView = 65f;
        chaseGo.AddComponent<AudioListener>();
        chaseGo.AddComponent<ChaseCamera>().target = robotGo.transform;
        MarsAssets.AddPostLayer(chaseCam);

        // Post-procesado global
        var volGo = new GameObject("PostProcesadoGlobal");
        var vol = volGo.AddComponent<PostProcessVolume>();
        vol.isGlobal = true;
        vol.sharedProfile = MarsAssets.PostProfile();

        // Polvo flotando en el ambiente
        AmbientDust(dustMat);

        // ---------- Sistema (comunicación + métricas + gafas VR en el celular)
        var sys = new GameObject("Sistema");
        sys.AddComponent<HapticLink>();
        var metrics = sys.AddComponent<TeleopMetrics>();
        metrics.robot = robot;
        metrics.goal = goal;
        metrics.objectsToDeliver = objData.Length;
        var streamer = sys.AddComponent<PhoneStreamer>();
        var eyeGo = new GameObject("CamaraOjoCelular");
        eyeGo.transform.SetParent(sys.transform, false);
        var eyeCam = eyeGo.AddComponent<Camera>();
        eyeCam.enabled = false;
        MarsAssets.AddPostLayer(eyeCam);
        streamer.eyeCamera = eyeCam;
        var distortion = AssetDatabase.LoadAssetAtPath<Material>(MarsAssets.Root + "/Materials/DistorsionCardboard.mat");
        if (distortion == null)
        {
            distortion = new Material(Shader.Find("Hidden/Teleop/CardboardDistortion"));
            AssetDatabase.CreateAsset(distortion, MarsAssets.Root + "/Materials/DistorsionCardboard.mat");
        }
        streamer.distortionMaterial = distortion;

        // Panel holográfico de información (visible desde la cámara y en las gafas)
        var panelGo = new GameObject("PanelInformacion");
        panelGo.transform.position = new Vector3(0f, 2.2f, Half + 0.4f);
        Box("Pantalla", panelGo.transform, new Vector3(0, 0, 0.06f), new Vector3(5.4f, 1.8f, 0.06f), mPantalla, false);
        Box("BordeSup", panelGo.transform, new Vector3(0, 0.92f, 0.02f), new Vector3(5.5f, 0.04f, 0.04f), mHolo, false);
        Box("BordeInf", panelGo.transform, new Vector3(0, -0.92f, 0.02f), new Vector3(5.5f, 0.04f, 0.04f), mHolo, false);
        foreach (float x in new[] { -2.4f, 2.4f })
            Cylinder("Soporte", panelGo.transform, new Vector3(x, -1.25f, 0.1f), 0.08f, 1.1f, mMetal, false);
        metrics.worldPanel = Sign("Texto", panelGo.transform, Vector3.zero, "Base de exploración — Rover teleoperado", new Color(0.55f, 0.92f, 1f), 0.045f);

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[SceneBuilder] Escena creada en " + ScenePath);
    }

    // ------------------------------------------------------------------ utilidades

    static void CreateOrReplace(UnityEngine.Object obj, string path)
    {
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(obj, path);
    }

    static Transform Group(string name, Transform parent, Vector3 pos)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent);
        g.transform.position = pos;
        return g.transform;
    }

    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material m, bool collider, bool local = true)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        if (local) g.transform.localPosition = localPos; else g.transform.position = localPos;
        g.transform.localScale = scale;
        g.GetComponent<Renderer>().sharedMaterial = m;
        if (!collider) UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m, bool collider = true, bool local = true)
        => Prim(PrimitiveType.Cube, name, parent, pos, size, m, collider, local);

    static GameObject Cylinder(string name, Transform parent, Vector3 pos, float diameter, float height, Material m, bool collider = true, bool local = true)
        => Prim(PrimitiveType.Cylinder, name, parent, pos, new Vector3(diameter, height / 2f, diameter), m, collider, local);

    static GameObject Sphere(string name, Transform parent, Vector3 pos, float diameter, Material m, bool collider = true, bool local = true)
        => Prim(PrimitiveType.Sphere, name, parent, pos, Vector3.one * diameter, m, collider, local);

    static GameObject Capsule(string name, Transform parent, Vector3 pos, Vector3 scale, Material m)
        => Prim(PrimitiveType.Capsule, name, parent, pos, scale, m, false);

    static GameObject Rock(string name, Transform parent, Vector3 pos, float scale, int seed, Material m, bool collider)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.position = pos + Vector3.up * (0.18f * scale);
        g.transform.localScale = Vector3.one * scale;
        g.transform.rotation = Quaternion.Euler(0, seed * 47f, 0);
        var mesh = MarsAssets.Rock(seed, 1);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = m;
        if (collider)
        {
            var mc = g.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = true;
        }
        return g;
    }

    static Light AddLight(Transform parent, Vector3 localPos, Color color, float intensity, float range)
    {
        var l = new GameObject("Luz").AddComponent<Light>();
        l.transform.SetParent(parent, false);
        l.transform.localPosition = localPos;
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        return l;
    }

    static TextMesh Sign(string name, Transform parent, Vector3 localPos, string text, Color color, float size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        var tm = go.AddComponent<TextMesh>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.font = font;
        go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        tm.fontSize = 48;
        tm.characterSize = size;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        tm.text = text;
        return tm;
    }

    static GameObject Surface(GameObject g, HapticPattern p)
    {
        var s = g.GetComponent<HapticSurface>();
        if (s == null) s = g.AddComponent<HapticSurface>();
        s.pattern = p;
        return g;
    }

    static ParticleSystem WheelDust(Transform parent, Vector3 localPos, Material mat)
    {
        var go = new GameObject("PolvoRueda");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(-160, 0, 0);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.startColor = new Color(0.75f, 0.48f, 0.32f, 0.45f);
        main.gravityModifier = -0.02f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 30f;
        shape.radius = 0.05f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.2f));
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0.8f, 0), new GradientAlphaKey(0f, 1) });
        col.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
        return ps;
    }

    static ParticleSystem Sparks(Transform parent, Material mat)
    {
        var go = new GameObject("Chispas");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.45f, 0.1f));
        main.gravityModifier = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;
        var em = ps.emission;
        em.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 40f;
        shape.radius = 0.02f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.05f;
        r.lengthScale = 1f;
        return ps;
    }

    static void AmbientDust(Material mat)
    {
        var go = new GameObject("PolvoAmbiental");
        go.transform.position = new Vector3(0, 2f, 0);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.startLifetime = 14f;
        main.startSpeed = 0.05f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startColor = new Color(1f, 0.8f, 0.6f, 0.35f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 600;
        var em = ps.emission;
        em.rateOverTime = 35f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(26f, 4f, 26f);
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);
        vel.z = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                  new[] { new GradientAlphaKey(0f, 0), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1) });
        col.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
    }
}
