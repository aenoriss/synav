using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // The plane that cuts the volume, with the crosshair at its centre. A box cutout rather than a cross
    // section plane, because a plane clips an infinite half space and takes the far side of the head too.
    public class SectionPlane : MonoBehaviour
    {
        [Tooltip("Authored child carrying UnityVolumeRendering's box cutout, sized to the plane's rectangle.")]
        public CutoutBox cutter;
        [Tooltip("Side of the sampled square, in local units. Should match the authored Surface quad.")]
        public float planeSize = 0.3f;
        [Tooltip("Pixels per side of the resliced image sent to the panel display.")]
        public int sliceResolution = 192;

        // Shared with the MPR board, so showing the cut twice costs nothing extra.
        public Texture SliceTexture => sliceTexture;

        [Header("Structure labels")]
        [Tooltip("Tints the cut where it passes through a structure, matching the MPR panels and the 3D "
               + "overlay. Leave empty for a plain greyscale cut.")]
        public StructureVoxelizer structures;
        [Range(0f, 1f)] public float labelTint = 0.55f;

        VolumeSampler sampler;
        Texture2D sliceTexture;
        Color32[] slicePixels;
        float min, range;
        Matrix4x4 lastToUVW;
        int labelVersion = -1;

        void Awake()
        {
            sliceTexture = new Texture2D(sliceResolution, sliceResolution, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            slicePixels = new Color32[sliceResolution * sliceResolution];
        }

        void Update()
        {
            if (sampler == null && !Bind())
                return;

            KeepCutFacingViewer();

            if (structures != null && structures.Built && !sampler.HasLabels)
                sampler.UseLabels(structures.LabelDataset);

            // Keyed on the plane composed with the volume, not the plane alone: the volume is grabbable too,
            // so moving either one changes the cut.
            Matrix4x4 toUVW = sampler.WorldToUVW * transform.localToWorldMatrix;
            int version = structures != null ? structures.LabelVersion : -1;
            if (toUVW == lastToUVW && version == labelVersion)
                return;

            lastToUVW = toUVW;
            labelVersion = version;
            RenderSlice(toUVW);
        }

        // Move the cutout to whichever side of the plane the viewer is on, so turning the plane around
        // never buries the open face.
        void KeepCutFacingViewer()
        {
            if (cutter == null)
                return;

            Camera viewer = Camera.main;
            if (viewer == null)
                return;

            float depth = cutter.transform.localScale.z;
            float side = Vector3.Dot(transform.forward, viewer.transform.position - transform.position);
            float offset = (side >= 0f ? 0.5f : -0.5f) * depth;

            Vector3 local = cutter.transform.localPosition;
            if (!Mathf.Approximately(local.z, offset))
                cutter.transform.localPosition = new Vector3(local.x, local.y, offset);
        }

        bool Bind()
        {
            var volume = FindFirstObjectByType<VolumeRenderedObject>();
            if (volume == null || volume.dataset == null || volume.meshRenderer == null || volume.volumeContainerObject == null)
                return false;

            if (cutter != null)
                cutter.SetTargetObject(volume);

            sampler = new VolumeSampler(volume.dataset, volume.meshRenderer.transform);
            min = volume.dataset.GetMinDataValue();
            range = Mathf.Max(1e-5f, volume.dataset.GetMaxDataValue() - min);
            return true;
        }

        // Takes the already-composed world-to-UVW matrix, so the sweep below steps by constant vectors
        // instead of transforming every pixel.
        void RenderSlice(Matrix4x4 toUVW)
        {
            int n = sliceResolution;
            float step = planeSize / (n - 1);

            Vector3 rowStart = toUVW.MultiplyPoint3x4(new Vector3(-planeSize * 0.5f, -planeSize * 0.5f, 0f));
            Vector3 acrossStep = toUVW.MultiplyVector(new Vector3(step, 0f, 0f));
            Vector3 downStep = toUVW.MultiplyVector(new Vector3(0f, step, 0f));

            for (int y = 0; y < n; y++)
            {
                Vector3 uvw = rowStart;
                int row = y * n;
                for (int x = 0; x < n; x++)
                {
                    slicePixels[row + x] = Shade(uvw);
                    uvw += acrossStep;
                }
                rowStart += downStep;
            }

            sliceTexture.SetPixels32(slicePixels);
            sliceTexture.Apply(false);
        }

        Color32 Shade(Vector3 uvw)
        {
            if (uvw.x < 0f || uvw.x > 1f || uvw.y < 0f || uvw.y > 1f || uvw.z < 0f || uvw.z > 1f)
                return new Color32(0, 0, 0, 0);

            float g = Mathf.Clamp01((sampler.SampleUVW(uvw) - min) / range) * 255f;

            // Tinted where the cut passes through a structure, so an oblique slice carries the same reading
            // as the ortho panels; the greyscale underneath stays visible.
            if (sampler.HasLabels && sampler.TryLabelUVW(uvw, out int id))
            {
                Color32[] colours = structures.LabelColours;
                if (id > 0 && colours != null && id < colours.Length && colours[id].a > 0)
                {
                    Color32 c = colours[id];
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
