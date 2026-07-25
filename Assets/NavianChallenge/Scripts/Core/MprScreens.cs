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

        VolumeDataset dataset;
        VolumeSampler sampler;
        SectionPlane plane;
        Slice axial, coronal, sagittal;
        Vector3Int last = -Vector3Int.one;
        float min, greyScale;

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
            float[] data = dataset.data;
            int slab = z * w * h;

            for (int y = 0; y < h; y++)
            {
                int src = slab + y * w;
                int dst = y * w;
                for (int x = 0; x < w; x++)
                    axial.pixels[dst + x] = Grey(data[src + x]);
            }
            Push(axial);
        }

        void FillCoronal(int y)
        {
            if (coronal == null) return;
            int w = dataset.dimX, h = dataset.dimZ;
            float[] data = dataset.data;
            int slab = dataset.dimX * dataset.dimY;

            for (int z = 0; z < h; z++)
            {
                int src = y * w + z * slab;
                int dst = z * w;
                for (int x = 0; x < w; x++)
                    coronal.pixels[dst + x] = Grey(data[src + x]);
            }
            Push(coronal);
        }

        void FillSagittal(int x)
        {
            if (sagittal == null) return;
            int w = dataset.dimY, h = dataset.dimZ;
            float[] data = dataset.data;
            int stride = dataset.dimX;
            int slab = dataset.dimX * dataset.dimY;

            for (int z = 0; z < h; z++)
            {
                int src = x + z * slab;
                int dst = z * w;
                for (int y = 0; y < w; y++)
                    sagittal.pixels[dst + y] = Grey(data[src + y * stride]);
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

        Color32 Grey(float value)
        {
            byte g = (byte)Mathf.Clamp((value - min) * greyScale, 0f, 255f);
            return new Color32(g, g, g, 255);
        }
    }
}
