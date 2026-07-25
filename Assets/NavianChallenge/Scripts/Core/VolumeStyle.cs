using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // White, mostly opaque transfer function, so the anatomy reads as tissue rather than a faint cloud.
    // The volume object is generated at runtime, so this waits for it instead of being authored.
    public class VolumeStyle : MonoBehaviour
    {
        [Tooltip("Density below which nothing is drawn. Keeps the background out of the render.")]
        [Range(0f, 1f)] public float tissueStart = 0.12f;
        [Range(0f, 1f)] public float opacity = 0.9f;

        bool applied;

        void Update()
        {
            if (applied)
                return;

            var volume = FindFirstObjectByType<VolumeRenderedObject>();
            if (volume == null || volume.transferFunction == null)
                return;

            var tf = volume.transferFunction;

            tf.colourControlPoints.Clear();
            tf.colourControlPoints.Add(new TFColourControlPoint(0f, Color.white));
            tf.colourControlPoints.Add(new TFColourControlPoint(1f, Color.white));

            tf.alphaControlPoints.Clear();
            tf.alphaControlPoints.Add(new TFAlphaControlPoint(0f, 0f));
            tf.alphaControlPoints.Add(new TFAlphaControlPoint(tissueStart, 0f));
            tf.alphaControlPoints.Add(new TFAlphaControlPoint(1f, opacity));

            volume.SetTransferFunction(tf);
            applied = true;
        }
    }
}
