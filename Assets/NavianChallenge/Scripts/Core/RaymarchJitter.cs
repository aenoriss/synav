using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // How far each ray is allowed to start off the sample grid. Rays that all sample at the same depths turn
    // a soft intensity ramp into visible rings, so the march offsets each ray's start by a per-pixel amount
    // and scatters that error instead.
    //
    // The offset is worth about five steps as it comes, which is more than decorrelating the grid asks for
    // and reads as grain rather than as smoothness. It is worst in the headset: the pattern is keyed to the
    // surface of the volume, so a moving head lands different pixels on different parts of it every frame
    // and the grain crawls. Scaling the pattern down brings the offset back to a single step.
    public class RaymarchJitter : MonoBehaviour
    {
        [Tooltip("Fraction of the march's five-step offset to keep. 0.2 leaves about one step, which is what "
               + "it takes to break the banding up. Towards zero the rays line up again and the rings return.")]
        [Range(0f, 1f)] public float amount = 0.2f;

        [Tooltip("Resolution of the offset pattern, matching the one it replaces.")]
        public int resolution = 512;

        VolumeRenderedObject volume;

        void Update()
        {
            if (volume == null)
                volume = FindFirstObjectByType<VolumeRenderedObject>();
            if (volume == null || volume.meshRenderer == null || volume.meshRenderer.sharedMaterial == null)
                return;

            volume.meshRenderer.sharedMaterial.SetTexture("_NoiseTex", Pattern());
            enabled = false;
        }

        Texture2D Pattern()
        {
            Texture2D pattern = new Texture2D(resolution, resolution, TextureFormat.R8, false);
            Color32[] pixels = new Color32[resolution * resolution];
            for (int i = 0; i < pixels.Length; i++)
            {
                byte v = (byte)(Random.value * amount * 255f);
                pixels[i] = new Color32(v, v, v, 255);
            }
            pattern.SetPixels32(pixels);
            pattern.Apply();
            return pattern;
        }
    }
}
