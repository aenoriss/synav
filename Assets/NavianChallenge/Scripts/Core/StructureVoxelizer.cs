using System.Collections.Generic;
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

        [Tooltip("Print one line when the bake lands, with the voxel count.")]
        public bool logBuild = true;

        public VolumeDataset LabelDataset { get; private set; }
        public bool Built { get; private set; }

        readonly Dictionary<int, bool> shown = new Dictionary<int, bool>();
        VolumeRenderedObject volume;

        void Update()
        {
            if (!Built && Application.isPlaying)
                Built = TryBuild();
        }

        [ContextMenu("Build Now")]
        public void BuildNow() => Built = TryBuild();

        // The volume is created asynchronously and parented a moment later, so this waits for it to be in
        // place; rasterizing against the half-placed transform would put every vertex outside the grid.
        bool TryBuild()
        {
            var vol = FindFirstObjectByType<VolumeRenderedObject>();
            if (vol == null || vol.dataset == null || vol.meshRenderer == null || vol.transform.parent == null)
                return false;

            volume = vol;
            return Build();
        }

        bool Build()
        {
            VolumeDataset ds = volume.dataset;
            int dimX = ds.dimX, dimY = ds.dimY, dimZ = ds.dimZ;
            Matrix4x4 toUVW = new VolumeSampler(ds, volume.meshRenderer.transform).WorldToUVW;

            float[] data = new float[dimX * dimY * dimZ];
            int marked = 0;
            foreach (Structure s in structures)
                if (s.mesh != null)
                    marked += Rasterize(s.mesh, s.id, data, toUVW, dimX, dimY, dimZ);

            if (marked == 0)
                return false;   // nothing landed in the grid, so the volume is not placed yet

            LabelDataset = ScriptableObject.CreateInstance<VolumeDataset>();
            LabelDataset.data = data;
            LabelDataset.dimX = dimX; LabelDataset.dimY = dimY; LabelDataset.dimZ = dimZ;
            LabelDataset.scale = ds.scale; LabelDataset.rotation = ds.rotation;
            LabelDataset.datasetName = "StructureLabels";
            LabelDataset.RecalculateBounds();

            foreach (Structure s in structures)
                shown[s.id] = true;

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

            return true;
        }

        // Show or hide one structure by rebuilding the labels with that id's alpha zeroed. The voxel data is
        // untouched, so a hidden structure is still there for the trajectory check.
        public void SetVisible(int id, bool visible)
        {
            if (!Built || volume == null)
                return;

            shown[id] = visible;
            volume.SetSegmentationLabels(Labels());
        }

        public bool IsShown(int id) => shown.TryGetValue(id, out bool on) && on;

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
        // surface is missed. Returns how many voxels landed inside the grid, which is how the caller tells a
        // real bake from one that ran before the volume was placed.
        int Rasterize(Transform meshTransform, int id, float[] data, Matrix4x4 toUVW, int dimX, int dimY, int dimZ)
        {
            var filter = meshTransform.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                return 0;

            Vector3[] verts = filter.sharedMesh.vertices;
            int[] tris = filter.sharedMesh.triangles;
            Matrix4x4 toWorld = meshTransform.localToWorldMatrix;
            int marked = 0;

            for (int t = 0; t < tris.Length; t += 3)
            {
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

            return marked;
        }

        static float VoxelSpan(Vector3 ua, Vector3 ub, int dimX, int dimY, int dimZ)
        {
            return new Vector3((ub.x - ua.x) * dimX, (ub.y - ua.y) * dimY, (ub.z - ua.z) * dimZ).magnitude;
        }
    }
}
