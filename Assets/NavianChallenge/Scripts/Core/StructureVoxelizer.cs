using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // Rasterizes the structure meshes into one integer-label volume on the MRI's voxel grid and installs it
    // as UnityVolumeRendering's secondary volume, so the structures render as voxels inside the scan and the
    // same field can be read per voxel to test a trajectory against vessels.
    //
    // The meshes need Read/Write enabled in their import settings, or the bake comes out empty.
    public class StructureVoxelizer : MonoBehaviour
    {
        [System.Serializable]
        public class Structure
        {
            public Transform mesh;
            public int id = 1;
            public string label = "Structure";
            public Color colour = Color.white;
        }

        [Tooltip("Written in order, so the last entry wins any overlap. Veins belong last.")]
        public Structure[] structures;

        [Tooltip("Cap on samples per triangle edge.")]
        public int maxSamplesPerEdge = 8;

        [Tooltip("The label field stored on disk. Left empty it fills itself in on the first run. Right-click "
               + "the component to force a refresh after changing the meshes.")]
        public TextAsset bakedLabels;

        const string BakedPath = "Assets/NavianChallenge/Data/Atlas/IXI025/Labels/StructureLabels.bytes";

        [Tooltip("Triangles rasterized per frame when there is no baked asset.")]
        public int trianglesPerFrame = 12000;

        public bool logBuild = true;

        public VolumeDataset LabelDataset { get; private set; }
        public bool Built { get; private set; }

        // Colour per id for the slice views. Alpha is only a shown/hidden flag; the alphas above are tuned
        // for accumulation along a ray, which is not what a flat slice needs.
        public Color32[] LabelColours { get; private set; }
        public int LabelVersion { get; private set; }

        readonly Dictionary<int, bool> shown = new Dictionary<int, bool>();
        VolumeRenderedObject volume;
        bool building;
        int marked;

        void Update()
        {
            if (Built || building || !Application.isPlaying)
                return;

            // The volume is parented a moment after it is created, and rasterizing against the half-placed
            // transform puts every vertex outside the grid.
            var vol = FindFirstObjectByType<VolumeRenderedObject>();
            if (vol == null || vol.dataset == null || vol.meshRenderer == null || vol.transform.parent == null)
                return;

            volume = vol;
            building = true;

            float[] stored = LoadBaked(volume.dataset);
            if (stored != null)
            {
                Install(stored, volume.dataset);
                return;
            }

            StartCoroutine(Build());
        }

        IEnumerator Build()
        {
            VolumeDataset ds = volume.dataset;
            int dimX = ds.dimX, dimY = ds.dimY, dimZ = ds.dimZ;
            Matrix4x4 toUVW = new VolumeSampler(ds, volume.meshRenderer.transform).WorldToUVW;

            float[] data = new float[dimX * dimY * dimZ];
            marked = 0;
            foreach (Structure s in structures)
            {
                if (s.mesh == null)
                    continue;

                IEnumerator raster = Rasterize(s.mesh, s.id, data, toUVW, dimX, dimY, dimZ);
                while (raster.MoveNext())
                    yield return null;
            }

            if (marked == 0)
            {
                building = false;   // volume not placed yet, try again next frame
                yield break;
            }

#if UNITY_EDITOR
            if (bakedLabels == null)
                SaveBaked(data, dimX, dimY, dimZ);
#endif
            Install(data, ds);
        }

        void Install(float[] data, VolumeDataset ds)
        {
            int dimX = ds.dimX, dimY = ds.dimY, dimZ = ds.dimZ;

            LabelDataset = ScriptableObject.CreateInstance<VolumeDataset>();
            LabelDataset.data = data;
            LabelDataset.dimX = dimX; LabelDataset.dimY = dimY; LabelDataset.dimZ = dimZ;
            LabelDataset.scale = ds.scale; LabelDataset.rotation = ds.rotation;
            LabelDataset.datasetName = "StructureLabels";
            LabelDataset.RecalculateBounds();

            foreach (Structure s in structures)
                shown[s.id] = true;
            RebuildColourTable();

            volume.AddSegmentation(LabelDataset, Labels());

            // The voxels are the structures now; the objects stay alive because a rebuild reads their meshes.
            foreach (Structure s in structures)
                if (s.mesh != null)
                {
                    var renderer = s.mesh.GetComponent<Renderer>();
                    if (renderer != null) renderer.enabled = false;
                }

            if (logBuild)
                Debug.Log("[StructureVoxelizer] baked " + marked + " voxels from " + structures.Length
                        + " structures (ids up to " + LabelDataset.GetMaxDataValue() + ") on a "
                        + dimX + "x" + dimY + "x" + dimZ + " grid");

            Built = true;
            building = false;
        }

        // Hiding zeroes the label's alpha and leaves the voxels alone, so the trajectory check still sees it.
        public void SetVisible(int id, bool visible)
        {
            if (!Built || volume == null)
                return;

            shown[id] = visible;
            volume.SetSegmentationLabels(Labels());
            RebuildColourTable();
        }

        public bool IsShown(int id) => shown.TryGetValue(id, out bool on) && on;

        void RebuildColourTable()
        {
            int maxId = 0;
            foreach (Structure s in structures)
                maxId = Mathf.Max(maxId, s.id);

            var table = new Color32[maxId + 1];
            foreach (Structure s in structures)
            {
                Color c = s.colour;
                c.a = IsShown(s.id) ? 1f : 0f;
                table[s.id] = c;
            }
            LabelColours = table;
            LabelVersion++;
        }

        // One byte per voxel, gzipped. Most of the grid is empty, so it packs to a few hundred kilobytes.
        static byte[] Encode(float[] data, int dimX, int dimY, int dimZ)
        {
            var raw = new byte[12 + data.Length];
            System.Buffer.BlockCopy(new[] { dimX, dimY, dimZ }, 0, raw, 0, 12);
            for (int i = 0; i < data.Length; i++)
                raw[12 + i] = (byte)Mathf.Clamp(Mathf.RoundToInt(data[i]), 0, 255);

            using (var output = new MemoryStream())
            {
                using (var zip = new GZipStream(output, CompressionMode.Compress))
                    zip.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }

        // Null for a missing, unreadable or differently sized file, which then falls back to rasterizing.
        float[] LoadBaked(VolumeDataset ds)
        {
#if UNITY_EDITOR
            // A fresh clone has the file but no reference to it yet.
            if (bakedLabels == null)
            {
                bakedLabels = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(BakedPath);
                if (bakedLabels != null && !Application.isPlaying)
                    UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
            if (bakedLabels == null || bakedLabels.bytes == null || bakedLabels.bytes.Length == 0)
                return null;

            try
            {
                using (var input = new MemoryStream(bakedLabels.bytes))
                using (var zip = new GZipStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    zip.CopyTo(output);
                    byte[] raw = output.ToArray();
                    if (raw.Length < 12)
                        return null;

                    var dims = new int[3];
                    System.Buffer.BlockCopy(raw, 0, dims, 0, 12);
                    int count = ds.dimX * ds.dimY * ds.dimZ;
                    if (dims[0] != ds.dimX || dims[1] != ds.dimY || dims[2] != ds.dimZ || raw.Length != 12 + count)
                        return null;

                    var data = new float[count];
                    marked = 0;
                    for (int i = 0; i < count; i++)
                    {
                        data[i] = raw[12 + i];
                        if (raw[12 + i] != 0) marked++;
                    }
                    return data;
                }
            }
            catch (IOException)
            {
                return null;
            }
        }

#if UNITY_EDITOR
        // Run after changing the meshes; every play afterwards just unpacks the file.
        [ContextMenu("Bake Labels To Disk")]
        public void BakeToDisk()
        {
            var vol = FindFirstObjectByType<VolumeRenderedObject>();
            if (vol == null || vol.dataset == null || vol.meshRenderer == null || vol.transform.parent == null)
            {
                Debug.LogError("[StructureVoxelizer] No placed volume to bake against. Enter play mode, or let "
                             + "the editor preview build, and try again.");
                return;
            }

            VolumeDataset ds = vol.dataset;
            int dimX = ds.dimX, dimY = ds.dimY, dimZ = ds.dimZ;
            Matrix4x4 toUVW = new VolumeSampler(ds, vol.meshRenderer.transform).WorldToUVW;

            var data = new float[dimX * dimY * dimZ];
            marked = 0;
            foreach (Structure s in structures)
            {
                if (s.mesh == null) continue;
                IEnumerator raster = Rasterize(s.mesh, s.id, data, toUVW, dimX, dimY, dimZ);
                while (raster.MoveNext()) { }   // run it straight through; no frames to protect here
            }

            if (marked == 0)
            {
                Debug.LogError("[StructureVoxelizer] Bake marked no voxels. Check that the meshes have "
                             + "Read/Write enabled and that the volume is placed.");
                return;
            }

            SaveBaked(data, dimX, dimY, dimZ);
        }

        void SaveBaked(float[] data, int dimX, int dimY, int dimZ)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BakedPath));
            File.WriteAllBytes(BakedPath, Encode(data, dimX, dimY, dimZ));
            UnityEditor.AssetDatabase.ImportAsset(BakedPath);

            bakedLabels = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(BakedPath);
            UnityEditor.EditorUtility.SetDirty(this);

            if (logBuild)
                Debug.Log("[StructureVoxelizer] wrote " + BakedPath
                        + " (" + (new FileInfo(BakedPath).Length / 1024) + " KB); later runs unpack it");
        }
#endif

        // Ascending by id, which UnityVolumeRendering needs: it walks this list in order to build the
        // transfer function and its own sort call discards the result. Rasterizing wants the opposite order.
        List<SegmentationLabel> Labels()
        {
            var list = new List<SegmentationLabel>(structures.Length);
            foreach (Structure s in structures)
            {
                Color c = s.colour;
                if (shown.TryGetValue(s.id, out bool on) && !on)
                    c.a = 0f;
                list.Add(new SegmentationLabel { id = s.id, name = s.label, colour = c });
            }
            list.Sort((a, b) => a.id.CompareTo(b.id));
            return list;
        }

        // Samples each triangle on a barycentric grid sized to its voxel extent, so no voxel under the
        // surface is missed. Yields periodically: doing all four meshes in one frame stalls XR badly enough
        // for the compositor to drop the app.
        IEnumerator Rasterize(Transform meshTransform, int id, float[] data, Matrix4x4 toUVW, int dimX, int dimY, int dimZ)
        {
            var filter = meshTransform.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                yield break;

            Vector3[] verts = filter.sharedMesh.vertices;
            int[] tris = filter.sharedMesh.triangles;
            Matrix4x4 toWorld = meshTransform.localToWorldMatrix;
            int budget = Mathf.Max(1, trianglesPerFrame);
            int sinceYield = 0;

            for (int t = 0; t < tris.Length; t += 3)
            {
                if (++sinceYield >= budget)
                {
                    sinceYield = 0;
                    yield return null;
                }

                Vector3 a = toWorld.MultiplyPoint3x4(verts[tris[t]]);
                Vector3 b = toWorld.MultiplyPoint3x4(verts[tris[t + 1]]);
                Vector3 c = toWorld.MultiplyPoint3x4(verts[tris[t + 2]]);
                Vector3 ua = toUVW.MultiplyPoint3x4(a), ub = toUVW.MultiplyPoint3x4(b), uc = toUVW.MultiplyPoint3x4(c);

                int n = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(
                    VoxelSpan(ua, ub, dimX, dimY, dimZ),
                    VoxelSpan(ua, uc, dimX, dimY, dimZ))) + 1, 1, maxSamplesPerEdge);

                for (int i = 0; i <= n; i++)
                    for (int j = 0; j <= n - i; j++)
                    {
                        float wb = (float)i / n, wc = (float)j / n, wa = 1f - wb - wc;
                        Vector3 uvw = toUVW.MultiplyPoint3x4(a * wa + b * wb + c * wc);
                        int x = Mathf.FloorToInt(uvw.x * dimX), y = Mathf.FloorToInt(uvw.y * dimY), z = Mathf.FloorToInt(uvw.z * dimZ);
                        if (x < 0 || x >= dimX || y < 0 || y >= dimY || z < 0 || z >= dimZ)
                            continue;

                        int index = x + y * dimX + z * dimX * dimY;
                        if (data[index] != id) marked++;
                        data[index] = id;
                    }
            }
        }

        static float VoxelSpan(Vector3 ua, Vector3 ub, int dimX, int dimY, int dimZ)
        {
            return new Vector3((ub.x - ua.x) * dimX, (ub.y - ua.y) * dimY, (ub.z - ua.z) * dimZ).magnitude;
        }
    }
}
