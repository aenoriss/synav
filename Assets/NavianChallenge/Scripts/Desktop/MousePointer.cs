using System;
using UnityEngine;
using Oculus.Interaction;

namespace NavianChallenge
{
    // A RayInteractor takes its ray from this transform and its clicks from this ISelector, which is the
    // same pair a hand ray gives it. So the mouse lands on the same RayInteractables and nothing downstream
    // needs a desktop branch.
    public class MousePointer : MonoBehaviour, ISelector
    {
        public event Action WhenSelected;
        public event Action WhenUnselected;

        [Tooltip("Camera the screen ray is cast from. Defaults to the one this pointer hangs off.")]
        public Camera viewer;

        void Awake()
        {
            if (viewer == null)
                viewer = GetComponentInParent<Camera>();
        }

        void Update()
        {
            if (viewer == null)
                return;

            Ray ray = viewer.ScreenPointToRay(Input.mousePosition);
            transform.SetPositionAndRotation(ray.origin, Quaternion.LookRotation(ray.direction));

            if (Input.GetMouseButtonDown(0))
                WhenSelected?.Invoke();
            else if (Input.GetMouseButtonUp(0))
                WhenUnselected?.Invoke();
        }
    }
}
