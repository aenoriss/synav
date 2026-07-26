using UnityEngine;

namespace NavianChallenge
{
    // Fixed foveated rendering. The volume is drawn by a raymarcher, so its cost is the pixels covered times
    // the samples taken behind each one, and it covers most of the view -- fragment work is what the budget
    // goes on. Foveation shades the outer rings of the eye buffer at reduced resolution, which is where
    // someone reading a slice or aiming a trajectory is not looking. Quest 3S has no eye tracking, so the
    // sharp region sits at the centre of the view rather than following the eye.
    //
    // Meta's API rather than Unity's SRP one: the SRP path needs URP or HDRP and this project is Built-in.
    // The level is pinned instead of left adaptive because the load here barely varies -- the head fills the
    // view for the whole session, so there is no light frame for an adaptive level to back off on.
    public class Foveation : MonoBehaviour
    {
        [Tooltip("How much of the periphery drops resolution. High is the strongest even level; HighTop skews "
               + "the detail upwards, which suits a scene with nothing to read low in the view.")]
        public OVRManager.FoveatedRenderingLevel level = OVRManager.FoveatedRenderingLevel.High;

        [Tooltip("How long to keep trying, in seconds. The runtime reports foveation unsupported until the "
               + "session is presenting, which lags the scene load.")]
        public float grace = 5f;

        void Update()
        {
            // Stays false on PC, so a build running over Link just gives up after the grace period.
            if (OVRManager.fixedFoveatedRenderingSupported)
            {
                OVRManager.useDynamicFoveatedRendering = false;
                OVRManager.foveatedRenderingLevel = level;
                enabled = false;
            }
            else if (Time.timeSinceLevelLoad > grace)
            {
                enabled = false;
            }
        }
    }
}
