using UnityEngine;

namespace NavianChallenge
{
    // One of the two window controls. The readout names the band being shown rather than the raw slider
    // value, since what matters is which intensities survive, not where the handle sits.
    public class WindowLevelSlider : DragSlider
    {
        public enum Control { Level, Width }

        public WindowLevel window;
        public Control control = Control.Level;

        protected override void OnValue(float normalised)
        {
            if (window == null)
                return;

            if (control == Control.Level) window.SetLevel(normalised);
            else                          window.SetWidth(normalised);

            if (valueLabel != null)
                valueLabel.text = control == Control.Level
                    ? $"level {normalised * 100f:F0}%"
                    : $"width {normalised * 100f:F0}%   shows {window.VisibleMin * 100f:F0}-{window.VisibleMax * 100f:F0}%";
        }
    }
}
