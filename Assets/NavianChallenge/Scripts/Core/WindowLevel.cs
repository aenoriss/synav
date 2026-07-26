using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // Window and level over the volume, the control a radiologist reaches for first. Level is the intensity
    // the window is centred on and width is how much range it spans, so narrowing the window raises contrast
    // across whatever is left inside it, and sliding the level walks that band up and down the tissue types.
    //
    // The floor is not part of the window. Air is the darkest thing in the scan and there is nothing in it to
    // see, so it stays excluded whatever the sliders are set to. That also happens to be most of the volume,
    // and the raymarcher drops a sample below the floor after one texture read instead of four.
    public class WindowLevel : MonoBehaviour
    {
        [Tooltip("Intensities below this are never drawn, whatever the sliders say. Air reads near zero and "
               + "tissue starts around 0.12, so this sits just above the noise.")]
        [Range(0f, 0.2f)] public float airFloor = 0.01f;

        [Tooltip("Centre of the window.")]
        [Range(0f, 1f)] public float level = 0.5f;
        [Tooltip("How much of the intensity range the window spans. Full width shows every tissue at once.")]
        [Range(0f, 1f)] public float width = 1f;

        public float VisibleMin { get; private set; }
        public float VisibleMax { get; private set; }

        VolumeRenderedObject volume;
        bool applied;

        void Update()
        {
            if (!applied)
                applied = Apply();
        }

        public void SetLevel(float value)
        {
            level = Mathf.Clamp01(value);
            Apply();
        }

        public void SetWidth(float value)
        {
            width = Mathf.Clamp01(value);
            Apply();
        }

        bool Apply()
        {
            if (volume == null)
                volume = FindFirstObjectByType<VolumeRenderedObject>();
            if (volume == null)
                return false;

            float half = width * 0.5f;
            VisibleMin = Mathf.Max(airFloor, level - half);
            VisibleMax = Mathf.Max(VisibleMin + 0.01f, Mathf.Min(1f, level + half));

            volume.SetVisibilityWindow(VisibleMin, VisibleMax);
            return true;
        }
    }
}
