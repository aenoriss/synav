using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

namespace NavianChallenge
{
    // Tints the plate fill and not the accent band, because the accent already means "this tool is on".
    // Two different states sharing one channel would fight.
    public class ButtonVisual : MonoBehaviour
    {
        [Tooltip("Plate to tint. Defaults to the first renderer beneath this button.")]
        public Renderer plate;
        [Tooltip("Not the accent channel, which carries on/off state.")]
        public string property = "_Fill";

        public Color normal = new Color(1f, 1f, 1f, 0.035f);
        public Color hover = new Color(1f, 1f, 1f, 0.13f);
        public Color pressed = new Color(1f, 1f, 1f, 0.26f);
        [Tooltip("Instant swaps read as flicker in a headset.")]
        public float fade = 14f;

        readonly List<IInteractableView> views = new();
        Color current;
        Color target;
        int propertyId;

        void Awake()
        {
            if (plate == null)
                plate = GetComponentInChildren<Renderer>();

            propertyId = Shader.PropertyToID(property);
            current = target = normal;
            Apply();
        }

        void OnEnable()
        {
            foreach (IInteractableView view in GetComponents<IInteractableView>())
            {
                view.WhenStateChanged += OnStateChanged;
                views.Add(view);
            }
            Evaluate();
        }

        void OnDisable()
        {
            foreach (IInteractableView view in views)
                view.WhenStateChanged -= OnStateChanged;
            views.Clear();
        }

        void OnStateChanged(InteractableStateChangeArgs args) => Evaluate();

        // Strongest state wins, so a fingertip press is not undone by a ray that is only hovering.
        void Evaluate()
        {
            bool hovering = false, selecting = false;
            foreach (IInteractableView view in views)
            {
                if (view.State == InteractableState.Select) selecting = true;
                else if (view.State == InteractableState.Hover) hovering = true;
            }

            target = selecting ? pressed : hovering ? hover : normal;
        }

        void Update()
        {
            if (current == target)
                return;

            current = Color.Lerp(current, target, 1f - Mathf.Exp(-fade * Time.deltaTime));
            Apply();
        }

        void Apply()
        {
            if (plate != null)
                plate.material.SetColor(propertyId, current);
        }
    }
}
