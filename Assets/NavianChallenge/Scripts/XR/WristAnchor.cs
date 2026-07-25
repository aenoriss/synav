using UnityEngine;
using Oculus.Interaction.Input;

namespace NavianChallenge
{
    // Follows the hand rather than being parented to it. Parenting to the wrist bone meant an added-object
    // override on a prefab we do not own, and a menu that disappeared with the hand visual.
    //
    // The frame comes from measured joint positions, not the bone's local axes. Which way a bone's axes
    // point is an SDK detail; three joints give a frame that cannot be wrong.
    public class WristAnchor : MonoBehaviour
    {
        [Tooltip("Hand to ride. OVRHands carries one Hand per side, so this must be the left one specifically.")]
        public Hand hand;

        [Header("Placement, metres from the wrist")]
        [Tooltip("Out along the ulnar (little finger) side, so the panel clears the hand.")]
        public float lateralOffset = 0.055f;
        [Tooltip("Along the hand, towards the fingers.")]
        public float forwardOffset = 0.02f;
        [Tooltip("Off the plane of the palm.")]
        public float liftOffset = 0.01f;
        [Tooltip("Flip if the panel ends up on the thumb side or facing into the hand.")]
        public bool invertPalmNormal;

        [Header("Smoothing")]
        [Tooltip("Higher follows the hand more tightly; lower is calmer but lags.")]
        public float positionSmoothing = 14f;
        public float rotationSmoothing = 14f;

        [Header("Reveal")]
        [Tooltip("How square-on the palm must face you before the menu counts as shown, in degrees.")]
        public float revealAngle = 45f;
        [Tooltip("Extra slack before it hides again, so hovering at the limit cannot strobe it.")]
        public float hideSlack = 12f;

        public bool Tracked { get; private set; }
        public bool PalmFacingViewer { get; private set; }

        bool placed;

        void Update()
        {
            if (!ReadFrame(out Vector3 wrist, out Vector3 alongHand, out Vector3 acrossHand, out Vector3 palmNormal))
            {
                Tracked = false;
                PalmFacingViewer = false;
                placed = false;
                return;
            }

            Tracked = true;

            Vector3 target = wrist
                + acrossHand * lateralOffset
                + alongHand * forwardOffset
                + palmNormal * liftOffset;

            Quaternion facing = Quaternion.LookRotation(-palmNormal, alongHand);

            if (!placed)
            {
                // Snap on first acquisition, or the panel visibly flies in from wherever it was.
                transform.SetPositionAndRotation(target, facing);
                placed = true;
            }
            else
            {
                float p = 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime);
                float r = 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, target, p);
                transform.rotation = Quaternion.Slerp(transform.rotation, facing, r);
            }

            PalmFacingViewer = FacingViewer(wrist, palmNormal);
        }

        // alongHand runs to the fingers, acrossHand runs index to pinky, their cross product is the palm.
        bool ReadFrame(out Vector3 wrist, out Vector3 alongHand, out Vector3 acrossHand, out Vector3 palmNormal)
        {
            wrist = alongHand = acrossHand = palmNormal = Vector3.zero;

            if (hand == null || !hand.IsTrackedDataValid)
                return false;

            if (!hand.GetJointPose(HandJointId.HandWristRoot, out Pose wristPose)
                || !hand.GetJointPose(HandJointId.HandIndex1, out Pose indexPose)
                || !hand.GetJointPose(HandJointId.HandMiddle1, out Pose middlePose)
                || !hand.GetJointPose(HandJointId.HandPinky1, out Pose pinkyPose))
                return false;

            wrist = wristPose.position;

            Vector3 along = middlePose.position - wrist;
            Vector3 across = pinkyPose.position - indexPose.position;
            if (along.sqrMagnitude < 1e-8f || across.sqrMagnitude < 1e-8f)
                return false;

            alongHand = along.normalized;
            // Orthogonalise, so the frame stays square as the fingers move.
            acrossHand = Vector3.ProjectOnPlane(across, alongHand).normalized;
            if (acrossHand.sqrMagnitude < 1e-8f)
                return false;

            palmNormal = Vector3.Cross(alongHand, acrossHand).normalized;
            if (invertPalmNormal)
            {
                palmNormal = -palmNormal;
                acrossHand = -acrossHand;
            }
            return true;
        }

        bool FacingViewer(Vector3 wrist, Vector3 palmNormal)
        {
            Camera viewer = Camera.main;
            if (viewer == null)
                return false;

            Vector3 toViewer = viewer.transform.position - wrist;
            if (toViewer.sqrMagnitude < 1e-6f)
                return false;

            float alignment = Vector3.Dot(palmNormal, toViewer.normalized);
            float limit = Mathf.Cos((PalmFacingViewer ? revealAngle + hideSlack : revealAngle) * Mathf.Deg2Rad);
            return alignment >= limit;
        }

    }
}
