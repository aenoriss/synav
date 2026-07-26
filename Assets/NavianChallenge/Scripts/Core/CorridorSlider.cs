using UnityEngine;

namespace NavianChallenge
{
    // Sets the trajectory's safety-corridor diameter.
    public class CorridorSlider : DragSlider
    {
        public TrajectoryPlanner planner;

        public float minMillimetres = 0f;
        public float maxMillimetres = 12f;

        protected override void OnValue(float normalised)
        {
            float mm = Mathf.Lerp(minMillimetres, maxMillimetres, normalised);
            if (valueLabel != null)
                valueLabel.text = mm < 0.05f ? "centre line" : $"corridor Ø {mm:F1} mm";
            if (planner != null)
                planner.SetCorridor(mm);
        }
    }
}
