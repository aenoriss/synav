using UnityEngine;
using Oculus.Interaction;

namespace NavianChallenge
{
    // Rail of section tabs down the left, the selected section's contents on the right. Only one section
    // shows at a time, so the panel keeps its size however many sections it grows.
    public class MainMenu : MonoBehaviour
    {
        [System.Serializable]
        public class Section
        {
            public string title;
            public ButtonSignal tab;
            public Renderer tabFace;
            public TextMesh tabLabel;
            public GameObject content;
            [Tooltip("Colour of this tab's leading edge, so each section reads as its own.")]
            public Color accent = new Color(0.369f, 0.541f, 0.847f);
        }

        public Section[] sections;

        [Header("Colours")]
        [Tooltip("Accent-band colour property on the tab face.")]
        public string tintProperty = "_AccentColor";
        [Tooltip("How far the leading edge dims when its section is not the open one.")]
        [Range(0f, 1f)] public float idleAccentAlpha = 0.32f;
        public Color selectedTitle = Color.white;
        public Color idleTitle = new Color(0.55f, 0.60f, 0.68f);

        void Start()
        {
            for (int i = 0; i < sections.Length; i++)
            {
                if (sections[i].tab == null)
                    continue;

                int index = i;
                sections[i].tab.Pressed += () => Select(index);
            }

            Select(0);
        }

        public void Select(int index)
        {
            for (int i = 0; i < sections.Length; i++)
            {
                bool on = i == index;
                Section s = sections[i];

                if (s.content != null) s.content.SetActive(on);
                if (s.tabFace != null)
                {
                    // Each tab keeps its own hue; only the open one shows it at full strength.
                    var m = s.tabFace.material;
                    m.EnableKeyword("_ACCENT");
                    if (m.HasProperty("_AccentOn")) m.SetFloat("_AccentOn", 1f);
                    Color a = s.accent;
                    m.SetColor(tintProperty, on ? a : new Color(a.r, a.g, a.b, idleAccentAlpha));
                }
                if (s.tabLabel != null) s.tabLabel.color = on ? selectedTitle : idleTitle;
            }
        }
    }
}
