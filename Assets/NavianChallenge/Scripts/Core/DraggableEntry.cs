using UnityEngine;

namespace NavianChallenge
{
    // Keeps the entry marker on the scalp. It is grabbable like the panels, but a grab moves it freely in
    // space and an entry only means anything on the surface, so wherever it lands is projected back along
    // the ray from the head's centre, turning a drag into a slide over the skull.
    public class DraggableEntry : MonoBehaviour
    {
        public TrajectoryPlanner planner;
        [Tooltip("Re-plan once the entry has slid at least this far, in patient millimetres.")]
        public float replanStepMm = 0.3f;

        Vector3 lastLocal = Vector3.positiveInfinity;
        Vector3 lastEntryMm = Vector3.positiveInfinity;

        void LateUpdate()
        {
            if (planner == null)
                return;

            // Local position, not world: the marker is parented to the volume, so carrying the head around
            // moves it in world while it is still sitting exactly where it was on the scalp. Only a grab that
            // moves it against the anatomy needs projecting back.
            if (transform.localPosition == lastLocal)
                return;

            transform.position = planner.ProjectToScalp(transform.position);
            lastLocal = transform.localPosition;

            if (planner.TryPatientMm(transform.position, out Vector3 mm)
                && (mm - lastEntryMm).sqrMagnitude > replanStepMm * replanStepMm)
            {
                lastEntryMm = mm;
                planner.RefreshPlan();
            }
        }
    }
}
