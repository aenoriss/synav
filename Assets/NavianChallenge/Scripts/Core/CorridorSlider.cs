using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

namespace NavianChallenge
{
    // A drag slider that sets the trajectory's safety-corridor diameter. It rides on the same interactables
    // as the buttons (a RayInteractable and a PokeInteractable on the track), and reads the pointer's world
    // position straight off their pointer events, so mouse ray, hand ray and poke all drive it with no new
    // input path. The pointer is projected onto the track segment to give a 0..1 value.
    public class CorridorSlider : MonoBehaviour
    {
        [Tooltip("Ends of the value axis. The handle slides between these and the pointer is projected onto them.")]
        public Transform trackStart;
        public Transform trackEnd;
        [Tooltip("Visual knob, moved along the track. Purely cosmetic.")]
        public Transform handle;
        public TextMesh valueLabel;
        public TrajectoryPlanner planner;

        public float minMillimetres = 0f;
        public float maxMillimetres = 12f;
        [Range(0f, 1f)] public float initial = 0.25f;

        readonly List<IPointable> pointables = new();
        bool dragging;

        void Start() => SetNormalised(initial);

        void OnEnable()
        {
            foreach (IPointable p in GetComponents<IPointable>())
            {
                p.WhenPointerEventRaised += OnPointer;
                pointables.Add(p);
            }
        }

        void OnDisable()
        {
            foreach (IPointable p in pointables)
                p.WhenPointerEventRaised -= OnPointer;
            pointables.Clear();
        }

        // Move events fire continuously while pointing, so only track them once a select has begun, and stop
        // on release. That turns a point-and-drag into a slider without a separate grab.
        void OnPointer(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select: dragging = true; Apply(evt.Pose.position); break;
                case PointerEventType.Move: if (dragging) Apply(evt.Pose.position); break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel: dragging = false; break;
            }
        }

        void Apply(Vector3 worldPoint)
        {
            Vector3 a = trackStart.position, ab = trackEnd.position - trackStart.position;
            float len2 = ab.sqrMagnitude;
            SetNormalised(len2 > 1e-8f ? Mathf.Clamp01(Vector3.Dot(worldPoint - a, ab) / len2) : 0f);
        }

        public void SetNormalised(float t)
        {
            if (handle != null && trackStart != null && trackEnd != null)
                handle.position = Vector3.Lerp(trackStart.position, trackEnd.position, t);

            float mm = Mathf.Lerp(minMillimetres, maxMillimetres, t);
            if (valueLabel != null)
                valueLabel.text = mm < 0.05f ? "centre line" : $"corridor Ø {mm:F1} mm";
            if (planner != null)
                planner.SetCorridor(mm);
        }
    }
}
