using UnityEngine;
using UnityVolumeRendering;

namespace NavianChallenge
{
    // All world to voxel maths lives here, so the slice views, the cut and the trajectory ruler cannot
    // drift apart.
    public class VolumeSampler
    {
        readonly VolumeDataset dataset;
        readonly Transform cube;

        // Physical size of the scan in millimetres, dim * voxel spacing straight from the NIfTI header
        // (240 x 240 x 180 for IXI025). Distances are measured against this, not the Unity transform, so a
        // rescaled volume in the scene still reports true patient millimetres.
        readonly Vector3 sizeMm;

        // Cached so intensity can be reported on a fixed 0..1 scale, letting callers pick thresholds (the
        // air-to-tissue boundary, say) as fractions that hold across datasets rather than raw scanner units.
        readonly float minValue;
        readonly float range;

        public VolumeSampler(VolumeDataset dataset, Transform cube)
        {
            this.dataset = dataset;
            this.cube = cube;
            sizeMm = dataset.scale;
            minValue = dataset.GetMinDataValue();
            range = Mathf.Max(1e-5f, dataset.GetMaxDataValue() - minValue);
        }

        // The UVR volume is a unit cube spanning [-0.5, 0.5] in its local space, so shifting the
        // inverse-transformed point by half a unit lands us in normalised [0,1] texture coords.
        public bool TryWorldToUVW(Vector3 world, out Vector3 uvw)
        {
            Vector3 local = cube.InverseTransformPoint(world);
            uvw = local + Vector3.one * 0.5f;
            return uvw.x >= 0f && uvw.x <= 1f
                && uvw.y >= 0f && uvw.y <= 1f
                && uvw.z >= 0f && uvw.z <= 1f;
        }

        // World -> normalised volume coords as a matrix. Views that sweep a whole grid multiply once and
        // then step by constant vectors instead of paying a transform per sample.
        public Matrix4x4 WorldToUVW => Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0.5f)) * cube.worldToLocalMatrix;

        public float SampleUVW(Vector3 uvw)
        {
            return dataset.GetData(
                Mathf.Clamp((int)(uvw.x * dataset.dimX), 0, dataset.dimX - 1),
                Mathf.Clamp((int)(uvw.y * dataset.dimY), 0, dataset.dimY - 1),
                Mathf.Clamp((int)(uvw.z * dataset.dimZ), 0, dataset.dimZ - 1));
        }

        // An optional second volume on the same grid, holding a structure id per voxel. Sampling it through
        // the same world-to-voxel maths as the intensity is the point: a label lookup and an intensity lookup
        // at one world position are guaranteed to describe the same voxel.
        VolumeDataset labels;

        public void UseLabels(VolumeDataset labelDataset)
        {
            labels = labelDataset != null
                  && labelDataset.dimX == dataset.dimX
                  && labelDataset.dimY == dataset.dimY
                  && labelDataset.dimZ == dataset.dimZ
                   ? labelDataset : null;
        }

        public bool HasLabels => labels != null;

        // The structure id at a world point, or 0 for unlabelled. False when there is no label volume or the
        // point is outside the scan.
        public bool TryLabelAtWorld(Vector3 world, out int id)
        {
            id = 0;
            return labels != null && TryWorldToUVW(world, out Vector3 uvw) && TryLabelUVW(uvw, out id);
        }

        // For sweeps that already work in normalised coords and would only be converting back and forth.
        public bool TryLabelUVW(Vector3 uvw, out int id)
        {
            id = 0;
            if (labels == null)
                return false;

            id = Mathf.RoundToInt(labels.GetData(
                Mathf.Clamp((int)(uvw.x * dataset.dimX), 0, dataset.dimX - 1),
                Mathf.Clamp((int)(uvw.y * dataset.dimY), 0, dataset.dimY - 1),
                Mathf.Clamp((int)(uvw.z * dataset.dimZ), 0, dataset.dimZ - 1)));
            return true;
        }

        // The world point at the centre of the volume box -- a point guaranteed to be inside the head, the
        // interior end of a ray cast out to find the scalp.
        public Vector3 VolumeCenterWorld => cube.TransformPoint(Vector3.zero);

        // Half the box's world-space diagonal: a distance from the centre that always clears the head, so a
        // scalp march can start out in the air and step inward.
        public float WorldRadius => 0.5f * cube.TransformVector(Vector3.one).magnitude;

        // Intensity on a 0..1 scale at a world point; false when the point lies outside the volume, which
        // reads as air. Lets a caller march for the air-to-tissue boundary without knowing the raw units.
        public bool TrySampleWorld01(Vector3 world, out float value01)
        {
            if (!TryWorldToUVW(world, out Vector3 uvw))
            {
                value01 = 0f;
                return false;
            }
            value01 = Mathf.Clamp01((SampleUVW(uvw) - minValue) / range);
            return true;
        }

        // Clamped, for views that keep showing a slice while the crosshair sits outside the volume.
        public Vector3Int WorldToClampedVoxel(Vector3 world)
        {
            TryWorldToUVW(world, out Vector3 uvw);
            return new Vector3Int(
                Mathf.Clamp((int)(uvw.x * dataset.dimX), 0, dataset.dimX - 1),
                Mathf.Clamp((int)(uvw.y * dataset.dimY), 0, dataset.dimY - 1),
                Mathf.Clamp((int)(uvw.z * dataset.dimZ), 0, dataset.dimZ - 1));
        }

        // A world point as physical millimetres in patient space. Unclamped, so a point on or just outside
        // the scalp still reads correctly.
        public Vector3 WorldToPatientMm(Vector3 world)
        {
            Vector3 uvw = cube.InverseTransformPoint(world) + Vector3.one * 0.5f;
            return Vector3.Scale(uvw, sizeMm);
        }

        public float PatientDistanceMm(Vector3 worldA, Vector3 worldB)
        {
            return (WorldToPatientMm(worldB) - WorldToPatientMm(worldA)).magnitude;
        }
    }
}
