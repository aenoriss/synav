using UnityEngine;

namespace NavianChallenge
{
    // Desktop-only: angles the section plane with the arrow keys, so setting an oblique cut does not mean
    // grabbing the plane and drag-rotating it. Turns about the plane's own in-plane axes, pivoting on the
    // crosshair at its centre, so the point of interest stays put while the cut tilts around it.
    //
    // Only two axes, on purpose: the plane's normal is its local forward, and a plane is unchanged by
    // spinning about its own normal -- so pitch (about local right) and yaw (about local up) are the whole
    // of what can re-orient a cut. There is no third rotation worth binding.
    public class PlaneRotateInput : MonoBehaviour
    {
        [Tooltip("The section plane to angle. Its forward is the cut normal; right and up lie in the slice.")]
        public Transform plane;
        [Tooltip("Degrees per second while an arrow is held.")]
        public float turnSpeed = 60f;
        [Tooltip("Multiplier while shift is held, matching the faster-move control.")]
        public float sprint = 3f;

        void Update()
        {
            if (plane == null)
                return;

            float pitch = 0f, yaw = 0f;
            if (Input.GetKey(KeyCode.UpArrow)) pitch -= 1f;
            if (Input.GetKey(KeyCode.DownArrow)) pitch += 1f;
            if (Input.GetKey(KeyCode.LeftArrow)) yaw -= 1f;
            if (Input.GetKey(KeyCode.RightArrow)) yaw += 1f;

            if (pitch == 0f && yaw == 0f)
                return;

            bool fast = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            float step = turnSpeed * (fast ? sprint : 1f) * Time.deltaTime;

            // World space about the plane's own axes: the axis argument already carries its orientation, so
            // this reads as "tip the normal up/down, swing it left/right" regardless of where the plane sits.
            plane.Rotate(plane.right, pitch * step, Space.World);
            plane.Rotate(plane.up, yaw * step, Space.World);
        }
    }
}
