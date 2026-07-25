using UnityEngine;
using Oculus.Interaction;

namespace NavianChallenge
{
    // Press and drag, the desktop equivalent of reaching out and grabbing a panel. Tries the pointer's ray
    // hit first, which covers the panels, then a physics raycast for grabbables with a collider but no ray
    // surface, which here means the volume.
    public class MouseGrab : MonoBehaviour
    {
        [Tooltip("Interactor whose ray hit is used first. Usually the pointer sitting under the desktop camera.")]
        public RayInteractor pointer;
        public Camera viewer;

        [Tooltip("Metres of reach gained or lost per wheel notch while dragging.")]
        public float reachSpeed = 0.12f;
        [Tooltip("Degrees turned per pixel of drag while shift is held.")]
        public float turnSpeed = 0.35f;
        [Tooltip("How close to the camera a held object may be pulled.")]
        public float minReach = 0.2f;

        public bool Holding => held != null;
        // Which transform is held, so a constraint like the scalp-bound entry can tell it is the one being dragged.
        public Transform Held => held;

        Transform held;
        float reach;
        Vector3 offset;
        Vector3 lastMouse;

        void Awake()
        {
            if (viewer == null)
                viewer = GetComponentInParent<Camera>();
            lastMouse = Input.mousePosition;
        }

        void Update()
        {
            Vector3 mouse = Input.mousePosition;
            Vector3 drag = mouse - lastMouse;
            lastMouse = mouse;

            if (Input.GetMouseButtonDown(0))
                Take(mouse);
            else if (Input.GetMouseButtonUp(0))
                held = null;

            if (held == null || viewer == null)
                return;

            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                // About the camera's axes, so sideways spins and upwards tips back, as it looks on screen.
                held.Rotate(viewer.transform.up, -drag.x * turnSpeed, Space.World);
                held.Rotate(viewer.transform.right, drag.y * turnSpeed, Space.World);
                return;
            }

            reach = Mathf.Max(minReach, reach + Input.mouseScrollDelta.y * reachSpeed);

            Ray ray = viewer.ScreenPointToRay(mouse);
            held.position = ray.origin + ray.direction * reach + offset;
        }

        void Take(Vector3 mouse)
        {
            if (viewer == null)
                return;

            Ray ray = viewer.ScreenPointToRay(mouse);

            if (pointer != null && pointer.HasCandidate && pointer.CollisionInfo.HasValue)
            {
                RayInteractable target = pointer.Candidate;

                // A button is not a handle, and neither is anything marked exempt. Let the press through
                // instead of dragging the panel it sits on.
                if (target.GetComponent<ButtonSignal>() != null || target.GetComponent<MouseGrabExempt>() != null)
                    return;

                if (Hold(target.GetComponentInParent<Grabbable>(), ray, pointer.CollisionInfo.Value.Point))
                    return;
            }

            if (Physics.Raycast(ray, out RaycastHit hit, 20f))
                Hold(hit.collider.GetComponentInParent<Grabbable>(), ray, hit.point);
        }

        bool Hold(Grabbable grabbable, Ray ray, Vector3 hitPoint)
        {
            if (grabbable == null)
                return false;

            held = grabbable.transform;
            // Along the ray, not from the camera: ScreenPointToRay starts at the near plane. The offset is
            // what stops the object jumping to sit under the cursor.
            reach = Vector3.Dot(hitPoint - ray.origin, ray.direction);
            offset = held.position - hitPoint;
            return true;
        }
    }
}
