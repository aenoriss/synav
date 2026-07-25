using UnityEngine;

namespace NavianChallenge
{
    // Each row shows and hides one structure and carries its colour, dimming when off, so the panel doubles
    // as a legend. Structures live in the volume as voxel labels, so a row drives that label's visibility;
    // the voxels themselves stay in place, which is what lets the trajectory check keep measuring against a
    // vessel the surgeon has hidden from view.
    public class StructureToggles : MonoBehaviour
    {
        [System.Serializable]
        public class Entry
        {
            [Tooltip("Which voxel label this row controls, matching an id in the StructureVoxelizer.")]
            public int labelId = 1;
            public ButtonSignal button;
            public Renderer face;
            public TextMesh label;
            public Color colour = Color.white;

            [System.NonSerialized] public bool shown = true;
        }

        public StructureVoxelizer voxelizer;
        public Entry[] entries;
        public Color offTint = new Color(0.18f, 0.18f, 0.18f);

        void Start()
        {
            foreach (Entry entry in entries)
            {
                if (entry.button == null)
                    continue;

                Entry captured = entry;
                captured.shown = true;
                captured.button.Pressed += () =>
                {
                    captured.shown = !captured.shown;
                    if (voxelizer != null)
                        voxelizer.SetVisible(captured.labelId, captured.shown);
                    Tint(captured);
                };
                Tint(entry);
            }
        }

        void Tint(Entry entry)
        {
            if (entry.face != null)
                entry.face.material.color = entry.shown ? entry.colour : offTint;
        }
    }
}
