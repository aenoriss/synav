using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // Axial is fixed z, coronal fixed y, sagittal fixed x. The fourth panel shares the section plane's
    // oblique texture rather than sampling the volume again.
    //
    // The panels stay on. Gating them on whether the crosshair sat on visible tissue read a single voxel,
    // so it flipped with sub-millimetre movement at every tissue boundary.
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
        [Tooltip("Shows which slice of the stack each view is on, the way a viewer prints the image number. "
               + "The number is the voxel index along the axis the view cuts across: z for axial, y for "
               + "coronal, x for sagittal -- reformatted from one volume, so it is the reslice index, the "
               + "stand-in for a stored image's instance number.")]
        public TextMesh axialLabel;
        public TextMesh coronalLabel;
        public TextMesh sagittalLabel;

        [Header("Structure labels")]
        [Tooltip("Tints slice pixels that fall inside a structure, so the 2D views show the same anatomy as "
               + "the 3D overlay. Leave empty for plain greyscale reslices.")]
        public StructureVoxelizer structures;
        [Tooltip("How strongly a labelled pixel takes the structure's colour. The slices stay readable as "
               + "greyscale underneath, so this is its own setting rather than the overlay's alpha.")]
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

            // Hiding a structure repaints the slices too, so a filtered view is filtered everywhere.
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

        // One-based to read like a radiology viewer's image number ("74 / 150"), where the stored array is
        // zero-based. Only called when the index changes, so no per-frame string churn.
        static void Label(TextMesh label, int index, int total)
        {
            if (label != null)
                label.text = (index + 1) + " / " + total;
        }

        // Picks up the label field once it exists, and reports when its colours changed so the slices repaint.
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

        // The scan in greyscale, tinted toward a structure's colour where one is labelled. Tinting rather
        // than replacing keeps the underlying intensities readable, which is the point of a reslice.
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
