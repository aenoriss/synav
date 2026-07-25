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

        // Physical size of the scan from the NIfTI header. Distances measure against this rather than the
        // Unity transform, so a rescaled volume still reports true patient millimetres.
        readonly Vector3 sizeMm;

        // Lets intensity be reported 0..1, so thresholds are fractions rather than raw scanner units.
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

        // The volume is a unit cube spanning [-0.5, 0.5], so the half-unit shift lands in [0,1].
        public bool TryWorldToUVW(Vector3 world, out Vector3 uvw)
        {
            Vector3 local = cube.InverseTransformPoint(world);
            uvw = local + Vector3.one * 0.5f;
            return uvw.x >= 0f && uvw.x <= 1f
                && uvw.y >= 0f && uvw.y <= 1f
                && uvw.z >= 0f && uvw.z <= 1f;
        }

        // As a matrix, so a view sweeping a grid multiplies once and then steps by constant vectors.
        public Matrix4x4 WorldToUVW => Matrix4x4.Translate(new Vector3(0.5f, 0.5f, 0.5f)) * cube.worldToLocalMatrix;

        public float SampleUVW(Vector3 uvw)
        {
            return dataset.GetData(
                Mathf.Clamp((int)(uvw.x * dataset.dimX), 0, dataset.dimX - 1),
                Mathf.Clamp((int)(uvw.y * dataset.dimY), 0, dataset.dimY - 1),
                Mathf.Clamp((int)(uvw.z * dataset.dimZ), 0, dataset.dimZ - 1));
        }

        // A structure id per voxel, on the same grid. Going through the same maths as the intensity is what
        // guarantees a label lookup and an intensity lookup describe the same voxel.
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

        // The structure id at a world point, or 0 for unlabelled.
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

        // Centre of the volume box, so a point that is certainly inside the head.
        public Vector3 VolumeCenterWorld => cube.TransformPoint(Vector3.zero);

        // Half the box diagonal, so a distance from the centre that always clears the head.
        public float WorldRadius => 0.5f * cube.TransformVector(Vector3.one).magnitude;

        // Intensity 0..1 at a world point; false outside the volume, which reads as air.
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

        // Patient millimetres. Unclamped, so a point just outside the scalp still reads correctly.
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
