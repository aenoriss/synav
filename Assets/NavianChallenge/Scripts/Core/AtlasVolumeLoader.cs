using System.Threading.Tasks;
using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    /// <summary>
    /// Loads the atlas MRI as a UnityVolumeRendering volume and places it under
    /// <see cref="atlasRoot"/>, co-registered with the segmentation meshes.
    ///
    /// [ExecuteAlways]: the volume is generated both in the editor (so you see the MRI
    /// without pressing Play) and at runtime. The volume's 3D texture is rebuilt on the
    /// GPU each time, so it is NOT serialized into the scene file (hideFlags = DontSave):
    /// this keeps the scene/repo light and always correct. Only the meshes are stored in
    /// the scene; the MRI volume is (re)created on load.
    /// </summary>
    [ExecuteAlways]
    public class AtlasVolumeLoader : MonoBehaviour
    {
        [Tooltip("VolumeDataset asset (auto-imported from the .nii.gz by UnityVolumeRendering).")]
        public VolumeDataset dataset;

        [Tooltip("Parent for the created volume. Should be AtlasRoot so it lines up with MeshesRoot.")]
        public Transform atlasRoot;

        const string VolumeName = "MRI_Volume (UnityVolumeRendering)";

        [System.NonSerialized] VolumeRenderedObject volume;

        Transform Root => atlasRoot != null ? atlasRoot : transform.parent;

        void OnEnable()
        {
            if (Application.isPlaying)
                _ = LoadAsync();     // runtime: async, don't block the first frames
            else
                BuildPreview();      // editor: synchronous preview so the MRI is visible without Play
        }

        void OnDisable()
        {
            // Remove the editor preview when the component is disabled / on domain reload.
            if (!Application.isPlaying)
                DestroyVolume();
        }

        VolumeRenderedObject Existing()
        {
            Transform r = Root;
            return r != null ? r.GetComponentInChildren<VolumeRenderedObject>(true) : volume;
        }

        public async Task LoadAsync()
        {
            if (dataset == null || Existing() != null) return;
            Attach(await VolumeObjectFactory.CreateObjectAsync(dataset));
        }

        public void BuildPreview()
        {
            if (dataset == null || Existing() != null) return;
            Attach(VolumeObjectFactory.CreateObject(dataset));
        }

        void Attach(VolumeRenderedObject vol)
        {
            if (vol == null)
            {
                Debug.LogError("[AtlasVolumeLoader] Failed to create the MRI volume (dataset null or import failed).");
                return;
            }
            Transform t = vol.transform;
            if (Root != null) t.SetParent(Root, false);
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            vol.gameObject.name = VolumeName;

            // Editor preview must never be written into the scene file.
            if (!Application.isPlaying)
                vol.gameObject.hideFlags = HideFlags.DontSave;

            volume = vol;
        }

        void DestroyVolume()
        {
            VolumeRenderedObject v = Existing();
            if (v == null) return;
            if (Application.isPlaying) Destroy(v.gameObject);
            else DestroyImmediate(v.gameObject);
            volume = null;
        }

        /// <summary>Force a fresh rebuild (right-click the component → Rebuild Volume).</summary>
        [ContextMenu("Rebuild Volume")]
        public void RebuildVolume()
        {
            DestroyVolume();
            if (Application.isPlaying) _ = LoadAsync();
            else BuildPreview();
        }
    }
}
