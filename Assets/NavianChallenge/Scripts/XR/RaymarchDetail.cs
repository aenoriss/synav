using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // How finely the headset marches the volume. What a pixel of the volume costs is the number of samples
    // taken along its ray, and stereo pays that twice against a shorter frame, so the headset marches more
    // coarsely than the desktop build. The shader already offsets each ray's start by a noise value scaled
    // to the step size, so a coarser march spends its error on noise rather than banding.
    //
    // Thin structures are what a coarse march costs: a vessel narrower than the spacing is only caught by
    // some of the rays that cross it. That is a picture, not a verdict -- the corridor check reads vein
    // labels off the dataset on the CPU, so what counts as a collision does not move with this.
    public class RaymarchDetail : MonoBehaviour
    {
        [Tooltip("Fraction of the shader's 256 steps per ray. 1 matches the desktop build. Below about a "
               + "half there is less than one sample per voxel and vessels start to break up.")]
        [Range(0.25f, 1f)] public float sampling = 0.75f;

        VolumeRenderedObject volume;

        void Update()
        {
            if (volume == null)
                volume = FindFirstObjectByType<VolumeRenderedObject>();
            if (volume == null)
                return;

            volume.SetSamplingRateMultiplier(sampling);
            enabled = false;
        }
    }
}
