using System;
using System.Collections.Generic;
using UnityEngine;
using Oculus.Interaction;

namespace NavianChallenge
{
    // One press event per button, whichever interactable fired it. A button carries a PokeInteractable for
    // touching it and a RayInteractable for pointing at it, and the menus should not have to care which.
    public class ButtonSignal : MonoBehaviour
    {
        public event Action Pressed;

        [Tooltip("Presses closer together than this count as one.")]
        public float cooldown = 0.35f;

        readonly List<IInteractableView> views = new();
        float lastPress = float.NegativeInfinity;

        void OnEnable()
        {
            foreach (IInteractableView view in GetComponents<IInteractableView>())
            {
                view.WhenStateChanged += OnStateChanged;
                views.Add(view);
            }
        }

        void OnDisable()
        {
            foreach (IInteractableView view in views)
                view.WhenStateChanged -= OnStateChanged;
            views.Clear();
        }

        void OnStateChanged(InteractableStateChangeArgs args)
        {
            if (args.NewState != InteractableState.Select)
                return;

            // Reaching out to poke leaves the ray on the button too, so both select a few frames apart and
            // the toggle flips on with the touch then back off as the hand withdraws. A frame guard was
            // not wide enough to catch it.
            if (Time.time - lastPress < cooldown)
                return;

            lastPress = Time.time;
            Pressed?.Invoke();
        }
    }
}
