using UnityEngine;

namespace NavianChallenge
{
    // Angles the section plane with the arrow keys, turning about its own in-plane axes so the crosshair
    // at its centre stays put. Two axes only: a plane is unchanged by spinning about its own normal, so
    // pitch and yaw are the whole of what can re-orient a cut.
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

            // World space about the plane's own axes, so this holds wherever the plane has been moved.
            plane.Rotate(plane.right, pitch * step, Space.World);
            plane.Rotate(plane.up, yaw * step, Space.World);
        }
    }
}
