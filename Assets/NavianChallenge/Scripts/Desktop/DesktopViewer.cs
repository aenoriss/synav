using UnityEngine;

namespace NavianChallenge
{
    // First-person rather than orbit, because the panels sit in a ring facing a standing user and a camera
    // orbiting from outside would mostly see their backs. Reads keys and mouse position directly, so it
    // does not depend on the Input Manager still carrying Unity's default axes.
    public class DesktopViewer : MonoBehaviour
    {
        [Tooltip("Degrees turned per pixel of drag.")]
        public float lookSensitivity = 0.16f;
        public float walkSpeed = 1.1f;
        [Tooltip("Multiplier while shift is held.")]
        public float sprint = 3f;
        [Tooltip("Metres moved per wheel notch.")]
        public float wheelSpeed = 0.25f;

        [Tooltip("Consulted so the wheel does not double as zoom while a panel is being pushed or pulled.")]
        public MouseGrab grab;
        public bool showHelp = true;

        Vector3 homePosition;
        Quaternion homeRotation;
        Vector3 lastMouse;
        float yaw, pitch;
        GUIStyle helpStyle;

        void Awake()
        {
            homePosition = transform.position;
            homeRotation = transform.rotation;
            lastMouse = Input.mousePosition;
            AdoptCurrentAngles();
        }

        void Update()
        {
            Vector3 mouse = Input.mousePosition;
            Vector3 drag = mouse - lastMouse;
            lastMouse = mouse;

            if (Input.GetMouseButton(1))
            {
                yaw += drag.x * lookSensitivity;
                pitch = Mathf.Clamp(pitch - drag.y * lookSensitivity, -85f, 85f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
            }

            Walk();

            if (Input.GetKeyDown(KeyCode.F))
            {
                transform.SetPositionAndRotation(homePosition, homeRotation);
                AdoptCurrentAngles();
            }

            if (Input.GetKeyDown(KeyCode.H))
                showHelp = !showHelp;
        }

        void Walk()
        {
            // Arrows are left free for PlaneRotateInput to angle the cut; WASD alone drives the walk.
            float forward = Axis(KeyCode.W, KeyCode.S);
            float right = Axis(KeyCode.D, KeyCode.A);
            float up = Axis(KeyCode.E, KeyCode.Q);
            float wheel = grab != null && grab.Holding ? 0f : Input.mouseScrollDelta.y;

            if (forward == 0f && right == 0f && up == 0f && wheel == 0f)
                return;

            // Level, so looking up does not walk you into the ceiling.
            Vector3 level = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            float speed = walkSpeed * (Input.GetKey(KeyCode.LeftShift) ? sprint : 1f) * Time.deltaTime;

            transform.position += (level * forward + transform.right * right + Vector3.up * up) * speed
                                + transform.forward * (wheel * wheelSpeed);
        }

        static float Axis(KeyCode plus, KeyCode minus)
        {
            float v = 0f;
            if (Input.GetKey(plus)) v += 1f;
            if (Input.GetKey(minus)) v -= 1f;
            return v;
        }

        void AdoptCurrentAngles()
        {
            Vector3 euler = transform.rotation.eulerAngles;
            pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            yaw = euler.y;
        }

        void OnGUI()
        {
            if (!showHelp)
                return;

            if (helpStyle == null)
            {
                helpStyle = new GUIStyle(GUI.skin.label);
                helpStyle.normal.textColor = new Color(0.86f, 0.88f, 0.92f);
                helpStyle.fontSize = 13;
            }

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(10f, 10f, 330f, 170f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(22f, 18f, 320f, 158f),
                "Right-drag\tlook around\n" +
                "W A S D\tmove\tQ / E\tdown / up\n" +
                "Arrows\tangle the cut plane\n" +
                "Shift\tfaster\tWheel\tforward / back\n" +
                "\n" +
                "Left-click\tpress a button\n" +
                "Left-drag\tmove a panel or the head\n" +
                "\twheel: reach, shift: rotate\n" +
                "\n" +
                "F\treset view\tH\thide this",
                helpStyle);
        }
    }
}
