using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // A straight entry-to-target trajectory, the atomic unit of a surgical plan. Two points captured from
    // the crosshair, a line between them, the depth in patient millimetres, and a warning when the track
    // comes within the chosen safety corridor of a vein.
    //
    // The two markers are parented to the volume in the scene, so a trajectory planned on the head stays
    // on the head when it's grabbed and turned -- Unity's transform hierarchy tracks them, no code needed.
    // The line and the readout live off the volume (a LineRenderer under the volume's 0.001 scale would be
    // invisibly thin), so they're re-glued to the markers each frame; that's two position reads, nothing.
    //
    // Depth is measured through VolumeSampler in scan millimetres rather than Unity metres, because the
    // instrument's real travel is a patient dimension, not a scene one -- and because it is a patient
    // dimension, it does not change when the head is moved or scaled. Neither does the vein-proximity
    // result (the veins move with the volume too). So the expensive vein scan runs only when the
    // trajectory or the corridor changes, never per frame.
    //
    // The vein check measures the shortest distance from the track to the vein geometry and flags it when
    // that falls within the corridor radius. It walks the mesh vertices rather than sweeping a physics
    // sphere, because a concave MeshCollider only answers raycasts, not sweeps, so a SphereCast silently
    // misses. The distance is measured in patient millimetres: IXI025's voxels are 0.9375 x 0.9375 x 1.2 mm,
    // so a single world-to-mm ratio taken along the track's own direction and then applied as an isotropic
    // radius would be wrong by close to that anisotropy whenever the nearest vein sits off-axis.
    public class TrajectoryPlanner : MonoBehaviour
    {
        [Tooltip("The point a Set captures. The section plane centre, which is the crosshair.")]
        public Transform crosshair;

        [Header("Buttons")]
        public ButtonSignal setEntryButton;
        public ButtonSignal setTargetButton;
        public ButtonSignal clearButton;

        [Header("Visuals")]
        public LineRenderer line;
        public TextMesh readout;
        [Tooltip("Parented to the volume in the scene, so it tracks the anatomy when the head is grabbed.")]
        public Transform entryMarker;
        public Transform targetMarker;
        [Tooltip("Translucent tube drawn around the track, its diameter the safety corridor.")]
        public Transform corridorTube;
        [Tooltip("The corridor tube's colour while clear; it turns the hit colour when it breaches a vein.")]
        public Color corridorColour = new Color(0.369f, 0.541f, 0.847f);

        [Header("Entry snap")]
        [Tooltip("The entry always lands on the scalp -- the outer air-to-tissue boundary in the volume -- "
               + "found by marching intensity, so it holds even with the surface mesh hidden. This is the "
               + "normalised intensity that counts as tissue: 0 is darkest (air), 1 is brightest.")]
        public float scalpThreshold = 0.12f;
        [Tooltip("How many samples the scalp march takes from outside the head to its centre. Counted rather "
               + "than a fixed step so it holds whatever world units the scene uses; the binary refine then "
               + "tightens the hit far below one sample. 512 is well under a voxel across this volume.")]
        public int scalpSamples = 512;

        [Header("Vein check")]
        [Tooltip("The vein mesh. The corridor is measured against its geometry.")]
        public Transform veins;
        [Tooltip("Safety corridor diameter in millimetres. Driven by the slider.")]
        public float corridorMm = 3f;

        public Color safeColour = new Color(0.45f, 0.85f, 0.55f);
        public Color hitColour = new Color(0.95f, 0.30f, 0.32f);

        [Header("Ray gradient")]
        [Tooltip("The ray fades from this at the entry end to the target colour at the far end, matching the "
               + "two markers. Overridden by a solid hit colour when the track crosses a vein.")]
        public Color rayEntryColour = new Color(0.369f, 0.541f, 0.847f);
        public Color rayTargetColour = new Color(0.13f, 0.38f, 0.70f);

        VolumeSampler sampler;
        Vector3[] veinVerticesMm;
        bool hasEntry, hasTarget;

        // The corridor tube's last fit, so it is re-shaped only when the markers or the width move.
        Vector3 lastTubeEntry, lastTubeTarget;
        float lastTubeMm = -1f;

        void Start()
        {
            if (line != null) line.positionCount = 2;
            if (setEntryButton != null) setEntryButton.Pressed += SetEntry;
            if (setTargetButton != null) setTargetButton.Pressed += SetTarget;
            if (clearButton != null) clearButton.Pressed += Clear;
            UpdateVisibility();
        }

        public void SetEntry()
        {
            if (crosshair == null || entryMarker == null) return;
            // The scalp, not wherever the crosshair happens to sit: an instrument enters through the surface.
            entryMarker.position = SnapToScalp(crosshair.position);   // child of the volume, so this sticks
            hasEntry = true;
            UpdateVisibility();
            Recompute();
        }

        // Projects a point onto the scalp along the ray from the head's centre through it. Marches intensity
        // from outside the head inward; the first crossing of the tissue threshold is the outer surface on
        // that side. Outside-in rather than centre-out so a sinus or other internal air pocket can't be read
        // as the outside. Falls back to the point itself if the ray never meets tissue.
        Vector3 SnapToScalp(Vector3 near)
        {
            if (!EnsureSampler()) return near;

            Vector3 center = sampler.VolumeCenterWorld;
            Vector3 dir = near - center;
            if (dir.sqrMagnitude < 1e-8f) return near;   // sitting on the centre: no direction to project along
            dir.Normalize();

            float radius = sampler.WorldRadius;
            int steps = Mathf.Max(1, scalpSamples);
            Vector3 stepVec = -dir * (radius / steps);

            Vector3 p = center + dir * radius;           // start out in the air, aimed back at the centre
            for (int i = 0; i <= steps; i++)
            {
                if (sampler.TrySampleWorld01(p, out float v) && v >= scalpThreshold)
                    return Refine(p - stepVec, p);       // crossing sits between this tissue sample and the last air one
                p += stepVec;
            }
            return near;
        }

        // Binary search between a known air point and a known tissue point for a surface hit finer than the
        // march step. Eight halvings take the step down by 256x, well under a voxel.
        Vector3 Refine(Vector3 air, Vector3 tissue)
        {
            for (int i = 0; i < 8; i++)
            {
                Vector3 mid = (air + tissue) * 0.5f;
                if (sampler.TrySampleWorld01(mid, out float v) && v >= scalpThreshold) tissue = mid;
                else air = mid;
            }
            return tissue;
        }

        public void SetTarget()
        {
            if (crosshair == null || targetMarker == null) return;
            targetMarker.position = crosshair.position;
            hasTarget = true;
            UpdateVisibility();
            Recompute();
        }

        // For the draggable entry: hold its marker on the scalp, and re-run the plan as it slides so the vein
        // warning tracks the trajectory instead of only updating on a button press.
        public Vector3 ProjectToScalp(Vector3 world) => SnapToScalp(world);
        public void RefreshPlan() => Recompute();
        // A world point in patient millimetres, for telling a real move of the entry from the head being
        // carried around it (the latter leaves the patient-space coordinate unchanged).
        public bool TryPatientMm(Vector3 world, out Vector3 mm)
        {
            if (!EnsureSampler()) { mm = Vector3.zero; return false; }
            mm = sampler.WorldToPatientMm(world);
            return true;
        }

        public void Clear()
        {
            hasEntry = hasTarget = false;
            UpdateVisibility();
        }

        public void SetCorridor(float millimetres)
        {
            corridorMm = Mathf.Max(0f, millimetres);
            Recompute();
        }

        void UpdateVisibility()
        {
            if (entryMarker != null) entryMarker.gameObject.SetActive(hasEntry);
            if (targetMarker != null) targetMarker.gameObject.SetActive(hasTarget);

            bool complete = hasEntry && hasTarget;
            if (line != null) line.enabled = complete;
            if (readout != null) readout.gameObject.SetActive(complete);
            if (corridorTube != null) corridorTube.gameObject.SetActive(complete);
        }

        // Depth and vein status are patient-space quantities, unchanged by any move of the head, so they
        // are recomputed only when the trajectory or the corridor actually changes.
        void Recompute()
        {
            if (!hasEntry || !hasTarget || !EnsureSampler()) return;

            Vector3 entry = entryMarker.position;
            Vector3 target = targetMarker.position;
            float depthMm = sampler.PatientDistanceMm(entry, target);

            bool crossesVein = false;
            float veinDepthMm = 0f;
            if ((target - entry).sqrMagnitude > 1e-10f && EnsureVeins())
            {
                Vector3 entryMm = sampler.WorldToPatientMm(entry);
                Vector3 targetMm = sampler.WorldToPatientMm(target);

                float nearestMm = float.MaxValue;
                float nearestT = 0f;
                for (int i = 0; i < veinVerticesMm.Length; i++)
                {
                    float d = DistanceToSegment(veinVerticesMm[i], entryMm, targetMm, out float t);
                    if (d < nearestMm) { nearestMm = d; nearestT = t; }
                }
                if (nearestMm <= corridorMm * 0.5f)
                {
                    crossesVein = true;
                    veinDepthMm = depthMm * nearestT;
                }
            }

            // The ray fades entry-to-target when clear, and turns solid red as an unmissable warning when it
            // crosses a vein. The LineRenderer bakes this into per-vertex colour for the ray shader to read.
            if (line != null)
                line.colorGradient = crossesVein
                    ? Flat(hitColour)
                    : EntryToTarget(rayEntryColour, rayTargetColour);

            if (readout != null)
            {
                readout.color = crossesVein ? hitColour : safeColour;
                readout.text = crossesVein
                    ? $"{depthMm:F0} mm\ncrosses vein at {veinDepthMm:F0} mm"
                    : $"{depthMm:F0} mm";
            }

            // Clearly visible but still see-through, and denser when breached so the warning reads stronger.
            if (corridorTube != null)
            {
                var r = corridorTube.GetComponent<Renderer>();
                if (r != null)
                {
                    Color c = crossesVein ? hitColour : corridorColour;
                    c.a = crossesVein ? 0.6f : 0.42f;
                    r.material.color = c;
                }
            }
        }

        // The markers track the volume for free; this keeps the line and the label glued to them, and turns
        // the label to face the viewer. Cheap enough to run unconditionally rather than guess when to skip.
        void LateUpdate()
        {
            if (!hasEntry || !hasTarget) return;

            if (line != null)
            {
                line.SetPosition(0, entryMarker.position);
                line.SetPosition(1, targetMarker.position);
            }
            if (readout != null)
            {
                readout.transform.position = Vector3.Lerp(entryMarker.position, targetMarker.position, 0.5f);
                if (Camera.main != null)
                    readout.transform.rotation = Quaternion.LookRotation(readout.transform.position - Camera.main.transform.position);
            }

            SizeCorridor();
        }

        // The tube lives off the volume, like the line, so it is re-fitted whenever the markers or the width
        // move. Its radius is the corridor in patient millimetres converted by the track's own
        // world-per-millimetre ratio -- the same scale the vein check measures against, so the tube on screen
        // is the tube being tested.
        void SizeCorridor()
        {
            if (corridorTube == null || !EnsureSampler())
                return;

            Vector3 entry = entryMarker.position, target = targetMarker.position;
            if (entry == lastTubeEntry && target == lastTubeTarget && corridorMm == lastTubeMm)
                return;
            lastTubeEntry = entry; lastTubeTarget = target; lastTubeMm = corridorMm;

            Vector3 axis = target - entry;
            float lenWorld = axis.magnitude;
            float lenMm = sampler.PatientDistanceMm(entry, target);
            if (lenWorld < 1e-6f || lenMm < 1e-5f)
                return;

            float worldPerMm = lenWorld / lenMm;
            float radiusWorld = corridorMm * 0.5f * worldPerMm;

            // Unity's cylinder is 2 units tall along Y and 1 unit across, so half-length on Y and diameter on X/Z.
            corridorTube.SetPositionAndRotation(
                (entry + target) * 0.5f,
                Quaternion.FromToRotation(Vector3.up, axis / lenWorld));
            corridorTube.localScale = new Vector3(radiusWorld * 2f, lenWorld * 0.5f, radiusWorld * 2f);
        }

        bool EnsureSampler()
        {
            if (sampler != null) return true;
            var volume = FindFirstObjectByType<VolumeRenderedObject>();
            if (volume == null || volume.dataset == null || volume.meshRenderer == null) return false;
            sampler = new VolumeSampler(volume.dataset, volume.meshRenderer.transform);
            return true;
        }

        // Cached once in patient millimetres and never recomputed: the veins share the volume's parent, so
        // wherever the volume is dragged the two move together and their relative geometry -- all this cache
        // records -- never changes.
        bool EnsureVeins()
        {
            if (veinVerticesMm != null) return true;
            if (veins == null || !EnsureSampler()) return false;
            var filter = veins.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return false;

            Vector3[] local = filter.sharedMesh.vertices;
            Matrix4x4 toWorld = veins.localToWorldMatrix;
            veinVerticesMm = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++)
                veinVerticesMm[i] = sampler.WorldToPatientMm(toWorld.MultiplyPoint3x4(local[i]));
            return true;
        }

        // Index 0 of the line is the entry (see LateUpdate), so gradient stop 0 is the entry colour.
        static Gradient EntryToTarget(Color entry, Color target)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(entry, 0f), new GradientColorKey(target, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static Gradient Flat(Color c)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(c, 0f), new GradientColorKey(c, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b, out float t)
        {
            Vector3 ab = b - a;
            t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }
    }
}
