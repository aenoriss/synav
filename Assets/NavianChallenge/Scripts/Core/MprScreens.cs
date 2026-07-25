using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // Axial is fixed z, coronal fixed y, sagittal fixed x. The fourth panel shares the section plane's
    // oblique texture rather than sampling the volume again.
    //
    // The panels stay on: gating them on the tissue under the crosshair read a single voxel, so they
    // flipped with sub-millimetre movement at every boundary.
    public class MprScreens : MonoBehaviour
    {
        [Header("Authored panels")]
        public Renderer axialPanel;
        public Renderer coronalPanel;
        public Renderer sagittalPanel;
        public Renderer sectionPanel;

        [Header("Source")]
        [Tooltip("World point to reslice at. Defaults to the section plane's centre.")]
        public Transform crosshair;

        [Header("Slice ID labels")]
        [Tooltip("Which slice of the stack the view is on: the voxel index along the axis it cuts across.")]
        public TextMesh axialLabel;
        public TextMesh coronalLabel;
        public TextMesh sagittalLabel;

        [Header("Structure labels")]
        [Tooltip("Tints pixels inside a structure. Leave empty for plain greyscale reslices.")]
        public StructureVoxelizer structures;
        [Tooltip("How strongly a labelled pixel takes the structure's colour.")]
        [Range(0f, 1f)] public float labelTint = 0.55f;

        VolumeDataset dataset;
        VolumeSampler sampler;
        SectionPlane plane;
        Slice axial, coronal, sagittal;
        Vector3Int last = -Vector3Int.one;
        float min, greyScale;

        float[] labelData;
        Color32[] labelColours;
        int labelVersion = -1;

        class Slice
        {
            public Texture2D texture;
            public Color32[] pixels;
            public Renderer surface;
        }

        void Update()
        {
            if (dataset == null && !Setup())
                return;
            if (crosshair == null)
                return;

            Vector3Int v = sampler.WorldToClampedVoxel(crosshair.position);

            // Hiding a structure repaints the slices too.
            bool labelsChanged = TrackLabels();
            if (labelsChanged) last = -Vector3Int.one;

            if (v.z != last.z) { FillAxial(v.z); Label(axialLabel, v.z, dataset.dimZ); }
            if (v.y != last.y) { FillCoronal(v.y); Label(coronalLabel, v.y, dataset.dimY); }
            if (v.x != last.x) { FillSagittal(v.x); Label(sagittalLabel, v.x, dataset.dimX); }
            last = v;

            AttachSectionTexture();
        }

        // The plane only has a texture once the volume has loaded, so do not assume an ordering.
        void AttachSectionTexture()
        {
            if (plane == null || sectionPanel == null)
                return;

            Texture live = plane.SliceTexture;
            if (live != null && sectionPanel.material.mainTexture != live)
                sectionPanel.material.mainTexture = live;
        }

        bool Setup()
        {
            var volume = FindFirstObjectByType<VolumeRenderedObject>();
            if (volume == null || volume.dataset == null || volume.meshRenderer == null)
                return false;

            dataset = volume.dataset;
            sampler = new VolumeSampler(dataset, volume.meshRenderer.transform);
            min = dataset.GetMinDataValue();
            greyScale = 255f / Mathf.Max(1e-5f, dataset.GetMaxDataValue() - min);

            plane = FindFirstObjectByType<SectionPlane>();
            if (crosshair == null && plane != null)
                crosshair = plane.transform;

            axial = Bind(axialPanel, dataset.dimX, dataset.dimY);
            coronal = Bind(coronalPanel, dataset.dimX, dataset.dimZ);
            sagittal = Bind(sagittalPanel, dataset.dimY, dataset.dimZ);
            return true;
        }

        Slice Bind(Renderer surface, int w, int h)
        {
            if (surface == null)
                return null;

            var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            surface.material.mainTexture = texture;
            return new Slice { texture = texture, pixels = new Color32[w * h], surface = surface };
        }

        void FillAxial(int z)
        {
            if (axial == null) return;
            int w = dataset.dimX, h = dataset.dimY;
            int slab = z * w * h;

            for (int y = 0; y < h; y++)
            {
                int src = slab + y * w;
                int dst = y * w;
                for (int x = 0; x < w; x++)
                    axial.pixels[dst + x] = Shade(src + x);
            }
            Push(axial);
        }

        void FillCoronal(int y)
        {
            if (coronal == null) return;
            int w = dataset.dimX, h = dataset.dimZ;
            int slab = dataset.dimX * dataset.dimY;

            for (int z = 0; z < h; z++)
            {
                int src = y * w + z * slab;
                int dst = z * w;
                for (int x = 0; x < w; x++)
                    coronal.pixels[dst + x] = Shade(src + x);
            }
            Push(coronal);
        }

        void FillSagittal(int x)
        {
            if (sagittal == null) return;
            int w = dataset.dimY, h = dataset.dimZ;
            int stride = dataset.dimX;
            int slab = dataset.dimX * dataset.dimY;

            for (int z = 0; z < h; z++)
            {
                int src = x + z * slab;
                int dst = z * w;
                for (int y = 0; y < w; y++)
                    sagittal.pixels[dst + y] = Shade(src + y * stride);
            }
            Push(sagittal);
        }

        void Push(Slice slice)
        {
            slice.texture.SetPixels32(slice.pixels);
            slice.texture.Apply(false);
        }

        // One-based, to read like a viewer's image number.
        static void Label(TextMesh label, int index, int total)
        {
            if (label != null)
                label.text = (index + 1) + " / " + total;
        }

        // True when the colours changed, so the slices know to repaint.
        bool TrackLabels()
        {
            if (structures == null || !structures.Built || structures.LabelDataset == null)
                return false;

            float[] data = structures.LabelDataset.data;
            if (data == null || data.Length != dataset.data.Length || structures.LabelColours == null)
                return false;

            if (structures.LabelVersion == labelVersion)
                return false;

            labelData = data;
            labelColours = structures.LabelColours;
            labelVersion = structures.LabelVersion;
            return true;
        }

        // Greyscale, tinted toward a structure's colour where one is labelled. Tinting rather than
        // replacing keeps the intensities readable, which is the point of a reslice.
        Color32 Shade(int index)
        {
            float g = Mathf.Clamp((dataset.data[index] - min) * greyScale, 0f, 255f);

            if (labelData != null)
            {
                int id = Mathf.RoundToInt(labelData[index]);
                if (id > 0 && id < labelColours.Length && labelColours[id].a > 0)
                {
                    Color32 c = labelColours[id];
                    return new Color32(
                        (byte)Mathf.Lerp(g, c.r, labelTint),
                        (byte)Mathf.Lerp(g, c.g, labelTint),
                        (byte)Mathf.Lerp(g, c.b, labelTint), 255);
                }
            }

            byte grey = (byte)g;
            return new Color32(grey, grey, grey, 255);
        }
    }
}
