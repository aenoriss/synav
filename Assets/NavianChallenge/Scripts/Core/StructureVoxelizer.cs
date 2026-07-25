using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // Rasterizes the co-registered structure meshes into one integer-label volume on the MRI's own voxel grid
    // and installs it as UnityVolumeRendering's secondary volume. The structures then render as coloured
    // voxels inside the MRI, and the same field can be read per voxel, so a trajectory is testable against
    // vein voxels directly instead of against mesh geometry sitting beside the scan.
    //
    // The mapping needs no tuning: MeshesRoot carries the same transform as the volume container, so
    // VolumeSampler.WorldToUVW takes a mesh vertex straight to the voxel it falls in. One voxel holds one id,
    // and structures are written in array order with the last one winning an overlap, so veins go last.
    //
    // Requires Read/Write Enabled on each mesh's import settings: without it runtime code cannot read
    // vertices and the bake silently comes out empty.
    public class StructureVoxelizer : MonoBehaviour
    {
        [System.Serializable]
        public class Structure
        {
            [Tooltip("A mesh under MeshesRoot. Read/Write must be enabled on its import settings.")]
            public Transform mesh;
            [Tooltip("Unique label id written into the voxel grid.")]
            public int id = 1;
            public string label = "Structure";
            [Tooltip("Overlay colour. Alpha is how solid the voxels read.")]
            public Color colour = Color.white;
        }

        [Tooltip("Written in order, so the LAST entry wins any overlap. Veins belong last.")]
        public Structure[] structures;

        [Tooltip("Cap on samples per triangle edge. Higher fills large triangles but costs more at load.")]
        public int maxSamplesPerEdge = 8;

        [Tooltip("The label field, rasterized ahead of time and stored on disk. With this set, starting the "
               + "scene just unpacks it. Left empty it fills itself in: the meshes are rasterized once and the "
               + "result written here, so only the very first run pays for it. Right-click the component and "
               + "choose Bake Labels To Disk to force a refresh after changing the meshes.")]
        public TextAsset bakedLabels;

        const string BakedPath = "Assets/NavianChallenge/Data/Atlas/IXI025/Labels/StructureLabels.bytes";

        [Tooltip("Triangles rasterized before giving the frame back, for the fallback path that has no baked "
               + "asset. The four meshes come to a few hundred thousand triangles between them, which in one "
               + "frame stalls long enough for an XR compositor to drop the app.")]
        public int trianglesPerFrame = 12000;

        [Tooltip("Print one line when the bake lands, with the voxel count.")]
        public bool logBuild = true;

        public VolumeDataset LabelDataset { get; private set; }
        public bool Built { get; private set; }

        // Colour per label id for the 2D views, which tint their pixels rather than raymarching. Alpha is
        // only a shown/hidden flag here: the alphas above are tuned for accumulation along a ray, which is
        // not what a flat slice needs.
        public Color32[] LabelColours { get; private set; }

        // Bumped whenever the table changes, so the slice views know to redraw without polling it.
        public int LabelVersion { get; private set; }

        readonly Dictionary<int, bool> shown = new Dictionary<int, bool>();
        VolumeRenderedObject volume;
        bool building;
        int marked;

        void Update()
        {
            if (Built || building || !Application.isPlaying)
                return;

            // The volume is created asynchronously and parented a moment later; rasterizing against that
            // half-placed transform would put every vertex outside the grid, so wait for it to settle.
            var vol = FindFirstObjectByType<VolumeRenderedObject>();
            if (vol == null || vol.dataset == null || vol.meshRenderer == null || vol.transform.parent == null)
                return;

            volume = vol;
            building = true;

            // The bake only depends on the meshes and the grid, both fixed, so a stored result is as good as
            // a fresh one and costs an unpack instead of a few hundred thousand triangles.
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
                building = false;   // nothing landed in the grid, so the volume is not placed yet
                yield break;
            }

#if UNITY_EDITOR
            // Having paid for the rasterization once, keep it, so no later run has to.
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

            // The voxels are the structures now, so the meshes they came from stop drawing. The objects stay
            // alive: their transforms and geometry are what a rebuild reads.
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

        // Show or hide one structure by rebuilding the labels with that id's alpha zeroed. The voxel data is
        // untouched, so a hidden structure is still there for the trajectory check.
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

        // One byte per voxel behind a gzip stream. Ids are small integers and most of the grid is empty, so
        // this packs a 256x256x150 field down to a fraction of a megabyte -- small enough to keep in the repo
        // beside the scan it belongs to.
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

        // Null whenever there is no asset, it cannot be read, or it was baked for a different grid, so a
        // stale or mismatched file falls back to rasterizing rather than loading a wrong field.
        float[] LoadBaked(VolumeDataset ds)
        {
#if UNITY_EDITOR
            // A fresh clone has the file but no reference to it yet, so find it by path and keep it. Outside
            // play the reference is written back into the scene, which is what a build reads.
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
        // Rasterizes now and writes the result next to the scan, then points this component at it. Meant to
        // be run once whenever the meshes or the grid change; after that every play just unpacks the file.
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

        // Sorted by id ascending, which UnityVolumeRendering requires: it builds the segmentation transfer
        // function by walking this list in order, and its own sort call discards the result, so an unsorted
        // list interleaves the control points and the overlay maps to nothing. Rasterizing wants the opposite
        // order, later structures overwriting earlier ones, so the two are kept apart.
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

        // Each triangle is sampled on a barycentric grid sized to its own voxel extent, so no voxel under the
        // surface is missed. Counts into `marked`, which is how the caller tells a real bake from one that
        // ran before the volume was placed, and hands the frame back every so often so the bake never stalls
        // long enough to be noticed.
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
