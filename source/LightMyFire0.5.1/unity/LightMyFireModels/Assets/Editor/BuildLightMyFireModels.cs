using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class BuildLightMyFireModels
{
    private const string ArtRoot = "Assets/LightMyFire/Art";
    private const string GeneratedRoot = "Assets/LightMyFire/Generated";
    private const string MaterialRoot = GeneratedRoot + "/Materials";
    private const string PrefabRoot = GeneratedRoot + "/Prefabs";
    private const string CoalPrefabPath = PrefabRoot + "/LightMyFire_CoalBarrelVisual.prefab";
    private const string ResinPrefabPath = PrefabRoot + "/LightMyFire_ResinBarrelVisual.prefab";
    private const string BundleName = "lightmyfire_assets";

    private const string LidModel = ArtRoot + "/Models/ForgedLid.obj";
    private const string BoardModel = ArtRoot + "/Models/PlaqueBoard.obj";
    private const string FrameModel = ArtRoot + "/Models/PlaqueFrame.obj";
    private const string PreviewBarrelModel = ArtRoot + "/Models/PreviewBarrel.obj";

    // Authored dimensions, in metres. Runtime fitting uses the real Valheim barrel bounds and these
    // dimensions, so the accessories do not depend on the proxy barrel used by the preview renderer.
    private const float AuthoredLidDiameter = 0.95f;
    private const float AuthoredPlaqueWidth = 0.56f;

    public static void BuildWindows()
    {
        string output = ReadArgument("-lightMyFireOutput");
        string preview = ReadArgument("-lightMyFirePreview");
        if (string.IsNullOrWhiteSpace(output))
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "assets"));

        GenerateAssets();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        string staging = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "LightMyFireBundles"));
        Directory.CreateDirectory(staging);
        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[] { CoalPrefabPath, ResinPrefabPath }
        };

        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(
            staging,
            new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);

        string stagedBundle = Path.Combine(staging, BundleName);
        if (manifest == null || !File.Exists(stagedBundle) || new FileInfo(stagedBundle).Length == 0)
            throw new InvalidOperationException("Unity did not produce the LightMyFire AssetBundle.");

        ValidateBundle(stagedBundle);
        Directory.CreateDirectory(output);
        string finalBundle = Path.Combine(output, BundleName);
        File.Copy(stagedBundle, finalBundle, true);
        File.Copy(stagedBundle + ".manifest", finalBundle + ".manifest", true);
        if (!string.IsNullOrWhiteSpace(preview)) RenderPreview(preview);
        Debug.Log("LIGHTMYFIRE_ASSETS_BUILT: " + finalBundle);
    }

    private static void GenerateAssets()
    {
        ConfigureModel(LidModel);
        ConfigureModel(BoardModel);
        ConfigureModel(FrameModel);
        ConfigureModel(PreviewBarrelModel);

        ConfigureTexture(ArtRoot + "/Textures/ForgedIron_Normal.png", true, true);
        ConfigureTexture(ArtRoot + "/Textures/ForgedIron_MetallicSmooth.png", false, true);
        ConfigureTexture(ArtRoot + "/Textures/ForgedIron_AO.png", false, true);
        ConfigureTexture(ArtRoot + "/Textures/PlaqueCoal_Normal.png", true, true);
        ConfigureTexture(ArtRoot + "/Textures/PlaqueCoal_MetallicSmooth.png", false, true);
        ConfigureTexture(ArtRoot + "/Textures/PlaqueCoal_AO.png", false, true);
        ConfigureTexture(ArtRoot + "/Textures/PlaqueResin_Normal.png", true, true);
        ConfigureTexture(ArtRoot + "/Textures/PlaqueResin_MetallicSmooth.png", false, true);
        ConfigureTexture(ArtRoot + "/Textures/PlaqueResin_AO.png", false, true);
        ConfigureTexture(ArtRoot + "/Textures/PreviewBarrelWood_Normal.png", true, true);
        ConfigureTexture(ArtRoot + "/Textures/PreviewBarrelWood_MetallicSmooth.png", false, true);
        ConfigureTexture(ArtRoot + "/Textures/PreviewBarrelWood_AO.png", false, true);

        if (AssetDatabase.IsValidFolder(GeneratedRoot)) AssetDatabase.DeleteAsset(GeneratedRoot);
        EnsureFolder("Assets/LightMyFire", "Generated");
        EnsureFolder(GeneratedRoot, "Materials");
        EnsureFolder(GeneratedRoot, "Prefabs");
        AssetDatabase.Refresh();

        Material iron = MakeMaterial("ForgedIron", "ForgedIron", 1.10f);
        Material coalWood = MakeMaterial("CoalPlaqueWood", "PlaqueCoal", 0.82f);
        Material resinWood = MakeMaterial("ResinPlaqueWood", "PlaqueResin", 0.82f);
        MakeMaterial("PreviewBarrelWood", "PreviewBarrelWood", 0.90f);

        CreatePrefab(false, CoalPrefabPath, coalWood, iron);
        CreatePrefab(true, ResinPrefabPath, resinWood, iron);
    }

    private static void ConfigureModel(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Could not import model: " + path);
        importer.globalScale = 1f;
        importer.importCameras = false;
        importer.importLights = false;
        importer.importNormals = ModelImporterNormals.Calculate;
        importer.importTangents = ModelImporterTangents.CalculateMikk;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.SaveAndReimport();
    }

    private static void ConfigureTexture(string path, bool normal, bool linear)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Could not import texture: " + path);
        importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        importer.sRGBTexture = !linear && !normal;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Trilinear;
        importer.anisoLevel = 8;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SaveAndReimport();
    }

    private static Material MakeMaterial(string name, string stem, float normalStrength)
    {
        Shader shader = Shader.Find("Standard");
        if (shader == null) throw new InvalidOperationException("Unity Standard shader is unavailable.");

        Material mat = new Material(shader) { name = name, color = Color.white };
        mat.SetTexture("_MainTex", LoadTexture(stem + "_Albedo.png"));
        mat.SetTexture("_BumpMap", LoadTexture(stem + "_Normal.png"));
        mat.SetFloat("_BumpScale", normalStrength);
        mat.EnableKeyword("_NORMALMAP");
        mat.SetTexture("_MetallicGlossMap", LoadTexture(stem + "_MetallicSmooth.png"));
        mat.SetFloat("_GlossMapScale", 1f);
        mat.EnableKeyword("_METALLICGLOSSMAP");
        mat.SetTexture("_OcclusionMap", LoadTexture(stem + "_AO.png"));
        mat.SetFloat("_OcclusionStrength", 1f);
        AssetDatabase.CreateAsset(mat, MaterialRoot + "/" + name + ".mat");
        return mat;
    }

    private static Texture2D LoadTexture(string file)
    {
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + "/Textures/" + file);
        if (tex == null) throw new InvalidOperationException("Missing texture: " + file);
        return tex;
    }

    private static GameObject LoadModel(string path)
    {
        GameObject obj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (obj == null) throw new InvalidOperationException("Missing model: " + path);
        return obj;
    }

    private static GameObject InstantiateModel(GameObject model, Transform parent, string name, Material material, Vector3 position)
    {
        GameObject go = PrefabUtility.InstantiatePrefab(model) as GameObject;
        if (go == null) throw new InvalidOperationException("Could not instantiate model: " + name);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = material;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
        return go;
    }

    private static void CreatePrefab(bool resin, string path, Material boardMaterial, Material iron)
    {
        GameObject root = new GameObject(resin ? "LightMyFire_ResinBarrelVisual" : "LightMyFire_CoalBarrelVisual");
        try
        {
            // These remain at origin in the asset. Runtime fitting positions them against the actual
            // piece_chestbarrel bounds, which is much safer than baking proxy-barrel coordinates.
            InstantiateModel(LoadModel(LidModel), root.transform, "MetalLid", iron, Vector3.zero);

            GameObject plaque = new GameObject("FrontPlaque");
            plaque.transform.SetParent(root.transform, false);
            plaque.transform.localPosition = Vector3.zero;
            InstantiateModel(LoadModel(BoardModel), plaque.transform, "WoodPlate", boardMaterial, Vector3.zero);
            InstantiateModel(LoadModel(FrameModel), plaque.transform, "ForgedFrame", iron, Vector3.zero);

            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static void ValidateBundle(string path)
    {
        AssetBundle bundle = AssetBundle.LoadFromFile(path);
        if (bundle == null) throw new InvalidOperationException("The built LightMyFire AssetBundle cannot be loaded.");
        try
        {
            ValidatePrefab(bundle.LoadAsset<GameObject>(CoalPrefabPath), "Coal");
            ValidatePrefab(bundle.LoadAsset<GameObject>(ResinPrefabPath), "Resin");
            Debug.Log("LIGHTMYFIRE_BUNDLE_VALIDATED: compact authored lid + plaque with matte PBR materials");
        }
        finally
        {
            bundle.Unload(true);
        }
    }

    private static void ValidatePrefab(GameObject prefab, string type)
    {
        if (prefab == null) throw new InvalidOperationException(type + " decoration prefab is missing.");
        if (prefab.transform.Find("FrontPlaque") == null || prefab.transform.Find("MetalLid") == null)
            throw new InvalidOperationException(type + " decoration hierarchy is incomplete.");

        Renderer[] rs = prefab.GetComponentsInChildren<Renderer>(true);
        if (rs.Length < 3) throw new InvalidOperationException(type + " decoration renderers are incomplete.");
        if (prefab.GetComponentsInChildren<Collider>(true).Length != 0)
            throw new InvalidOperationException(type + " decoration must not contain colliders.");
        if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
            throw new InvalidOperationException(type + " decoration must not contain scripts.");

        foreach (Renderer r in rs)
        {
            Material m = r.sharedMaterial;
            if (m == null || m.GetTexture("_MainTex") == null || m.GetTexture("_BumpMap") == null ||
                m.GetTexture("_MetallicGlossMap") == null || m.GetTexture("_OcclusionMap") == null)
                throw new InvalidOperationException(type + " renderer is missing PBR maps: " + r.name);
        }
    }

    private static void RenderPreview(string output)
    {
        GameObject coalPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CoalPrefabPath);
        GameObject resinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ResinPrefabPath);
        Material previewWood = AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + "/PreviewBarrelWood.mat");
        Material iron = AssetDatabase.LoadAssetAtPath<Material>(MaterialRoot + "/ForgedIron.mat");

        GameObject coal = null, resin = null, camObj = null, keyObj = null, fillObj = null, rimObj = null, floor = null;
        RenderTexture rt = null;
        Texture2D pixels = null;
        Material floorMat = null;

        try
        {
            coal = CreatePreviewBarrel(new Vector3(-0.60f, 0f, 0f), previewWood, iron, coalPrefab);
            resin = CreatePreviewBarrel(new Vector3(0.60f, 0f, 0f), previewWood, iron, resinPrefab);

            floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.position = new Vector3(0f, -0.43f, 0f);
            floor.transform.localScale = new Vector3(.34f, 1f, .24f);
            floorMat = new Material(Shader.Find("Standard"));
            floorMat.color = new Color(.045f, .040f, .034f);
            floorMat.SetFloat("_Glossiness", .05f);
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;

            keyObj = new GameObject("Key");
            Light key = keyObj.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.25f;
            key.color = new Color(1f, .76f, .56f);
            keyObj.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

            fillObj = new GameObject("Fill");
            Light fill = fillObj.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = .42f;
            fill.color = new Color(.48f, .56f, .70f);
            fillObj.transform.rotation = Quaternion.Euler(24f, 150f, 0f);

            rimObj = new GameObject("Rim");
            Light rim = rimObj.AddComponent<Light>();
            rim.type = LightType.Directional;
            rim.intensity = .32f;
            rim.color = new Color(.95f, .55f, .30f);
            rimObj.transform.rotation = Quaternion.Euler(12f, 195f, 0f);

            camObj = new GameObject("Camera");
            Camera cam = camObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.012f, .011f, .010f);
            cam.fieldOfView = 29f;
            cam.transform.position = new Vector3(0f, .66f, -3.05f);
            cam.transform.LookAt(new Vector3(0f, .02f, 0f));

            rt = new RenderTexture(1400, 800, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            pixels = new Texture2D(1400, 800, TextureFormat.RGBA32, false);
            pixels.ReadPixels(new Rect(0, 0, 1400, 800), 0, 0);
            pixels.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllBytes(output, pixels.EncodeToPNG());
            Debug.Log("LIGHTMYFIRE_PREVIEW_RENDERED: " + output + " (proxy body is preview-only; runtime body is Valheim's native barrel)");
        }
        finally
        {
            RenderTexture.active = null;
            foreach (GameObject g in new[] { coal, resin, camObj, keyObj, fillObj, rimObj, floor })
                if (g != null) UnityEngine.Object.DestroyImmediate(g);
            if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
            if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
            if (floorMat != null) UnityEngine.Object.DestroyImmediate(floorMat);
        }
    }

    private static GameObject CreatePreviewBarrel(Vector3 pos, Material wood, Material iron, GameObject visualPrefab)
    {
        GameObject root = new GameObject("PreviewBarrel");
        root.transform.position = pos;

        GameObject body = InstantiateModel(LoadModel(PreviewBarrelModel), root.transform, "Body", wood, Vector3.zero);

        // Simple hoop geometry just for judging how the custom accessories sit against a Valheim-like
        // barrel silhouette. These hoops are not shipped in the AssetBundle.
        float[] hoopY = { -.33f, 0f, .33f };
        float[] hoopDiameter = { .85f, .925f, .85f };
        for (int i = 0; i < hoopY.Length; i++)
        {
            GameObject band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            band.name = "PreviewHoop";
            band.transform.SetParent(root.transform, false);
            band.transform.localPosition = new Vector3(0f, hoopY[i], 0f);
            band.transform.localScale = new Vector3(hoopDiameter[i], .018f, hoopDiameter[i]);
            band.GetComponent<Renderer>().sharedMaterial = iron;
            UnityEngine.Object.DestroyImmediate(band.GetComponent<Collider>());
        }

        Renderer[] barrelRenderers = root.GetComponentsInChildren<Renderer>(true);
        Bounds localBounds = GetCombinedLocalBounds(root.transform, barrelRenderers);

        GameObject visual = UnityEngine.Object.Instantiate(visualPrefab, root.transform, false);
        visual.name = "LightMyFireDecoration";
        FitDecorationToBounds(visual.transform, localBounds);
        return root;
    }

    private static void FitDecorationToBounds(Transform visual, Bounds barrelBounds)
    {
        float diameter = Mathf.Min(barrelBounds.size.x, barrelBounds.size.z);
        if (diameter <= 0f) diameter = .9f;

        // Keep this proxy preview mathematically aligned with the runtime fitting code. The preview
        // body is not shipped, but the lid/plaque scale and offsets should match what the player sees.
        visual.localPosition = Vector3.zero;
        visual.localRotation = Quaternion.identity;
        visual.localScale = Vector3.one;

        Transform lid = visual.Find("MetalLid");
        if (lid != null)
        {
            float lidScale = (diameter * .90f) / AuthoredLidDiameter;
            lid.localScale = Vector3.one * lidScale;
            lid.localPosition = new Vector3(
                barrelBounds.center.x,
                barrelBounds.max.y + Mathf.Max(.006f, diameter * .008f),
                barrelBounds.center.z);
        }

        Transform plaque = visual.Find("FrontPlaque");
        if (plaque != null)
        {
            float plaqueScale = (diameter * .42f) / AuthoredPlaqueWidth;
            plaque.localScale = Vector3.one * plaqueScale;
            plaque.localPosition = new Vector3(
                barrelBounds.center.x,
                barrelBounds.center.y + barrelBounds.size.y * .06f,
                barrelBounds.min.z - Mathf.Max(.008f, diameter * .012f));
        }
    }

    private static Bounds GetCombinedLocalBounds(Transform root, Renderer[] renderers)
    {
        bool initialized = false;
        Bounds result = new Bounds(Vector3.zero, Vector3.zero);
        foreach (Renderer renderer in renderers)
        {
            Bounds world = renderer.bounds;
            Vector3 min = world.min;
            Vector3 max = world.max;
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                Vector3 corner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z);
                Vector3 local = root.InverseTransformPoint(corner);
                if (!initialized)
                {
                    result = new Bounds(local, Vector3.zero);
                    initialized = true;
                }
                else result.Encapsulate(local);
            }
        }
        return result;
    }

    private static void EnsureFolder(string parent, string name)
    {
        string path = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
    }

    private static string ReadArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
