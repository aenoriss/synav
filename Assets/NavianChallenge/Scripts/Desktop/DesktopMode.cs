using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;

namespace NavianChallenge
{
    // Picks the rig at startup so one scene ships as both builds. Cheap because neither rig talks to the
    // tools: both drive a RayInteractor, and nothing downstream can tell a pinch from a mouse click.
    //
    // Detecting the headset is the fiddly part, and it is a timing problem. XR Plug-in Management selects
    // its loader before the scene loads, so XRGeneralSettings...activeLoader is set by the time this runs,
    // but the OpenXR display session only confirms a frame or more later (later still over Link) -- so
    // XRSettings.isDeviceActive still reads false at Awake even with a headset up. Deciding on that at Awake
    // wrongly falls back to desktop.
    //
    // So: if no loader was selected at all, there is no XR and this is a plain desktop launch -- commit to
    // desktop immediately (that path is the graded deliverable and must start clean). If a loader is up,
    // start on the headset rig right away (no desktop flash for a real headset) and confirm the display is
    // actually presenting within a short window; if it never does, the loader was up without a headset, so
    // fall back to desktop.
    public class DesktopMode : MonoBehaviour
    {
        public enum Rig { Auto, Desktop, Headset }

        [Tooltip("Auto picks by whether an XR device came up. The explicit settings are for trying one rig without unplugging the other.")]
        public Rig rig = Rig.Auto;

        [Tooltip("Switched off when there is no headset: the OVR rig, passthrough, the hand mesh, the wrist menu.")]
        public GameObject[] headsetOnly;
        [Tooltip("Switched on when there is no headset: the desktop camera and its mouse pointer.")]
        public GameObject[] desktopOnly;
        [Tooltip("Components that would fight the desktop controls, but share an object with something that has "
               + "to stay alive. The base scene's orbit camera is the one that matters.")]
        public Behaviour[] disableOnDesktop;
        [Tooltip("Tools the wrist menu would summon. There is no wrist on desktop, so these start open.")]
        public GameObject[] openOnDesktop;

        [Tooltip("How long to wait for the XR display to start presenting before giving up on the headset, "
               + "in seconds. Covers a slow Link handshake; only a headless machine with an idle XR runtime "
               + "ever waits the whole time.")]
        public float headsetGracePeriod = 4f;

        public static bool OnDesktop { get; private set; }

        // Readable from a diagnostic without referencing any XR type; records how the rig was chosen.
        public static string DecisionTrace = "(not decided)";

        void Awake()
        {
            if (rig != Rig.Auto)
            {
                Apply(rig == Rig.Desktop);
                DecisionTrace = "override: " + rig;
                return;
            }

            bool loader = LoaderSelected();
            bool presenting = HmdPresenting();
            DecisionTrace = $"awake loader={loader} presenting={presenting}";

            if (presenting)                     // headset already confirmed
            {
                Apply(false);
                DecisionTrace += " -> headset";
                return;
            }
            if (!loader)                        // no XR at all
            {
                Apply(true);
                DecisionTrace += " -> desktop (no loader)";
                return;
            }

            Apply(false);                       // loader up, session not confirmed yet: start on headset
            DecisionTrace += " -> provisional headset";
            StartCoroutine(ConfirmHeadset());
        }

        IEnumerator ConfirmHeadset()
        {
            float deadline = Time.realtimeSinceStartup + headsetGracePeriod;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (HmdPresenting())
                {
                    DecisionTrace += " | confirmed";
                    yield break;
                }
                yield return null;
            }
            Apply(true);
            DecisionTrace += " | timed out -> desktop";
        }

        // A loader is selected before the scene loads, so this is reliable at Awake; it means XR is going to
        // run, but not yet that a headset is actually presenting.
        static bool LoaderSelected()
        {
            var settings = XRGeneralSettings.Instance;
            return settings != null && settings.Manager != null && settings.Manager.activeLoader != null;
        }

        // The display is actually rendering to a headset. False until the session confirms, which lags Awake.
        static bool HmdPresenting()
        {
            if (XRSettings.isDeviceActive)
                return true;

            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (XRDisplaySubsystem display in displays)
                if (display.running)
                    return true;
            return false;
        }

        void Apply(bool onDesktop)
        {
            OnDesktop = onDesktop;

            foreach (GameObject go in headsetOnly)
                if (go != null)
                    go.SetActive(!onDesktop);

            foreach (GameObject go in desktopOnly)
                if (go != null)
                    go.SetActive(onDesktop);

            if (!onDesktop)
                return;

            foreach (Behaviour b in disableOnDesktop)
                if (b != null)
                    b.enabled = false;

            foreach (GameObject go in openOnDesktop)
                if (go != null)
                    go.SetActive(true);
        }
    }
}
