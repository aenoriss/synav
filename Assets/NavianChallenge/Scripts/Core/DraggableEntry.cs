using UnityEngine;

namespace NavianChallenge
{
    // Keeps the entry marker on the scalp. It is grabbable like the panels, but a grab moves it freely in
    // space and an entry only means anything on the surface, so wherever it lands is projected back along
    // the ray from the head's centre, turning a drag into a slide over the skull.
    //
    // The projection is idempotent, so applying it every frame does not fight the grab.
    public class DraggableEntry : MonoBehaviour
    {
        public TrajectoryPlanner planner;
        [Tooltip("Re-plan once the entry has slid at least this far, in patient millimetres.")]
        public float replanStepMm = 0.3f;

        Vector3 lastEntryMm = Vector3.positiveInfinity;

        void LateUpdate()
        {
            if (planner == null)
                return;

            transform.position = planner.ProjectToScalp(transform.position);

            if (planner.TryPatientMm(transform.position, out Vector3 mm)
                && (mm - lastEntryMm).sqrMagnitude > replanStepMm * replanStepMm)
            {
                lastEntryMm = mm;
                planner.RefreshPlan();
            }
        }
    }
}
