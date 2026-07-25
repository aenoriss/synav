using UnityEngine;

namespace NavianChallenge
{
    // Keeps the entry point on the outer surface of the head. The marker is grabbable like the panels --
    // mouse-drag on desktop, hand-grab in XR -- but a grab moves it freely in space, and an entry only means
    // anything on the scalp. So wherever it lands it is projected back onto the surface along the ray from
    // the head's centre, which turns a drag into a slide over the skull.
    //
    // The projection is idempotent, a point already on the surface mapping to itself, so applying it every
    // frame does not fight the grab. The vein corridor is only re-checked once the entry has moved in patient
    // millimetres, so carrying the whole head around does not trigger a re-plan.
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
