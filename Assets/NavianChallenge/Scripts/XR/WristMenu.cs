using UnityEngine;
using Oculus.Interaction;

namespace NavianChallenge
{
    // Worn on the wrist rather than left in the room, because a panel you put down in MR usually ends up
    // somewhere behind you. WristAnchor owns the pose and the reveal gesture.
    public class WristMenu : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            [Tooltip("The tool this row shows and hides.")]
            public GameObject target;
            public ButtonSignal row;
            [Tooltip("The row plate. Tinted on its accent channel, so only the leading band changes.")]
            public Renderer accent;
            public TextMesh label;
        }

        [Tooltip("Supplies the wrist pose and the palm-towards-you test.")]
        public WristAnchor anchor;
        [Tooltip("Child holding the menu's visuals. Hidden until the palm turns towards you.")]
        public Transform container;
        public Entry[] entries;

        [Header("Colours")]
        [Tooltip("Material colour driven on each row. The rows light only their leading band.")]
        public string tintProperty = "_AccentColor";
        public Color activeAccent = new Color(0.16f, 0.42f, 0.72f, 1f);
        public Color idleAccent = new Color(0.34f, 0.38f, 0.44f, 0.45f);
        public Color labelOn = Color.white;
        public Color labelOff = new Color(0.58f, 0.58f, 0.63f);

        bool shown = true;

        void Start()
        {
            foreach (Entry entry in entries)
            {
                if (entry.target == null || entry.row == null)
                    continue;

                Entry captured = entry;
                entry.row.Pressed += () => Summon(captured);
                Paint(entry);
            }

            Show(false);
        }

        void Update()
        {
            Show(anchor != null && anchor.Tracked && anchor.PalmFacingViewer);
        }

        void Show(bool visible)
        {
            if (visible == shown || container == null)
                return;

            shown = visible;
            container.gameObject.SetActive(visible);
        }

        // Visibility only, so a tool comes back exactly where you left it.
        void Summon(Entry entry)
        {
            entry.target.SetActive(!entry.target.activeSelf);
            Paint(entry);
        }

        void Paint(Entry entry)
        {
            bool on = entry.target != null && entry.target.activeSelf;
            if (entry.accent != null) entry.accent.material.SetColor(tintProperty, on ? activeAccent : idleAccent);
            if (entry.label != null) entry.label.color = on ? labelOn : labelOff;
        }
    }
}
