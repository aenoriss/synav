using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityVolumeRendering;

namespace NavianChallenge.EditorTools
{
    /// <summary>
    /// Builds the base scene (AtlasRoot + MRI volume loader + 4 aligned meshes +
    /// camera + light + controller), materials, and - in batch runs - renders
    /// alignment screenshots with the GPU.
    /// </summary>
    public static class ChallengeSceneBuilder
    {
        const string DataDir   = "Assets/NavianChallenge/Data/Atlas/IXI025";
        const string MeshDir   = DataDir + "/Meshes";
        const string T1Asset   = DataDir + "/MRI/IXI025_t1.nii.gz";
        const string MatDir    = "Assets/NavianChallenge/Materials";
        const string ScenePath = "Assets/NavianChallenge/Scenes/NavianChallenge_Main.unity";

        // Empirical mesh mirror inside the cube (set from screenshot calibration).
        // Applied as MeshesRoot localScale sign. (1,1,1) = no mirror.
        static readonly Vector3 MeshFlip = new Vector3(1f, 1f, 1f);

        // label order -> (obj file, scene name, material, color rgba, transparent)
        struct MeshDef { public string obj, name, mat; public Color col; public bool transp; }
        // All structures use the same default white material (candidate can recolor them).
        static readonly MeshDef[] Meshes =
        {
            new MeshDef{ obj="SustanciaGris",   name="GrayMatter",  mat="GrayMatter",  col=Color.white, transp=false },
            new MeshDef{ obj="SustanciaBlanca", name="WhiteMatter", mat="WhiteMatter", col=Color.white, transp=false },
            new MeshDef{ obj="Venas",           name="Veins",       mat="Veins",       col=Color.white, transp=false },
            new MeshDef{ obj="Piel",            name="Skin",        mat="Skin",        col=Color.white, transp=false },
        };

        [MenuItem("Navian/Build Challenge Scene")]
        public static void BuildMenu()
        {
            Build();
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("[Navian] Scene built: " + ScenePath);
        }

        // Entry point for -executeMethod (batch): build, save, exit.
        public static void BuildBatch()
        {
            try
            {
                Build();
                Debug.Log("###NAVIAN_BUILD_OK###");
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogError("###NAVIAN_BUILD_FAIL### " + e);
                EditorApplication.Exit(1);
            }
        }

        static void Build()
        {
            AssetDatabase.Refresh();

            VolumeDataset dataset = AssetDatabase.LoadAssetAtPath<VolumeDataset>(T1Asset);
            if (dataset == null)
                throw new System.Exception("VolumeDataset not found at " + T1Asset + " (ScriptedImporter did not run?)");
            Debug.Log($"[Navian] dataset dims=({dataset.dimX},{dataset.dimY},{dataset.dimZ}) scale={dataset.scale} rotation={dataset.rotation.eulerAngles}");

            Dictionary<string, Material> mats = BuildMaterials();

            // --- new empty scene ---
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var atlasRoot = new GameObject("AtlasRoot");
            atlasRoot.transform.position = new Vector3(0f, 1.899969f, 0.45f);
            atlasRoot.transform.localRotation = Quaternion.Euler(270.5211f, 0f, 74.3341f);
            atlasRoot.transform.localScale = new Vector3(0.001f, 0.001f, 0.001f);

            // MeshesRoot mirrors the UVR volume container transform so meshes co-register
            // with the MRI texture (dataset.scale == dim*spacing, dataset.rotation == 90x).
            var meshesRoot = new GameObject("MeshesRoot");
            meshesRoot.transform.SetParent(atlasRoot.transform, false);
            meshesRoot.transform.localRotation = dataset.rotation;
            meshesRoot.transform.localScale = Vector3.Scale(dataset.scale, MeshFlip);

            var meshGOs = new List<GameObject>();
            foreach (var d in Meshes)
            {
                string objPath = $"{MeshDir}/{d.obj}.obj";
                Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(objPath).OfType<Mesh>().FirstOrDefault();
                if (mesh == null) { Debug.LogError("Mesh not found: " + objPath); meshGOs.Add(null); continue; }
                Debug.Log($"[Navian] {d.obj}: bounds center={mesh.bounds.center} size={mesh.bounds.size} verts={mesh.vertexCount}");

                var go = new GameObject(d.name);
                go.transform.SetParent(meshesRoot.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mats[d.mat];
                meshGOs.Add(go);
            }

            // --- camera + light ---
            var camGO = new GameObject("Main Camera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.08f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 5000f;

            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // frame camera on the mesh bounds
            Bounds b = ComputeBounds(meshGOs);
            float dist = b.extents.magnitude * 2.4f;
            Vector3 viewDir = new Vector3(0.35f, 0.25f, -1f).normalized;
            cam.transform.position = b.center - viewDir * dist;
            cam.transform.rotation = Quaternion.LookRotation((b.center - cam.transform.position).normalized, Vector3.up);

            // --- rig: loader + controller ---
            var rig = new GameObject("AtlasRig");
            rig.transform.SetParent(atlasRoot.transform, false);

            var loader = rig.AddComponent<NavianChallenge.AtlasVolumeLoader>();
            loader.dataset = dataset;
            loader.atlasRoot = atlasRoot.transform;

            var ctrl = rig.AddComponent<NavianChallenge.AtlasSceneController>();
            ctrl.atlasRoot = atlasRoot.transform;
            ctrl.cam = cam;

            // --- save ---
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            Debug.Log("[Navian] Saved scene " + ScenePath);
        }

        static Dictionary<string, Material> BuildMaterials()
        {
            Directory.CreateDirectory(MatDir);
            Shader std = Shader.Find("Standard");
            var result = new Dictionary<string, Material>();
            foreach (var d in Meshes)
            {
                string path = $"{MatDir}/{d.mat}.mat";
                var m = new Material(std) { name = d.mat };
                m.color = d.col;
                m.SetColor("_Color", d.col);
                m.SetFloat("_Glossiness", 0.12f);
                m.SetFloat("_Metallic", 0f);
                if (d.transp) SetTransparent(m, d.col);
                AssetDatabase.CreateAsset(m, path);
                result[d.mat] = m;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static void SetTransparent(Material m, Color c)
        {
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_Mode", 3f);
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.color = c;
        }

        static Bounds ComputeBounds(List<GameObject> gos)
        {
            bool has = false;
            Bounds b = new Bounds(Vector3.zero, Vector3.one);
            foreach (var go in gos)
            {
                if (go == null) continue;
                var r = go.GetComponent<Renderer>();
                if (r == null) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        static void AddSceneToBuildSettings(string path)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == path))
            {
                scenes.Insert(0, new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
