using UnityEngine;

namespace Hawks.Hoxton
{
    internal static class MaskHitMesh
    {
        internal static bool Intercepts(Vector3[] vertices, int[] triangles, Bounds bounds, Vector3 hitPoint, Vector3 direction)
        {
            Vector3 rayDirection = direction.normalized;
            if (rayDirection.z >= -.05f) return false;
            Vector3 origin = hitPoint - rayDirection * .40f;
            if (!bounds.IntersectRay(new Ray(origin, rayDirection), out float near) || near > .415f) return false;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 ab = vertices[triangles[i + 1]] - a;
                Vector3 ac = vertices[triangles[i + 2]] - a;
                Vector3 p = Vector3.Cross(rayDirection, ac);
                float det = Vector3.Dot(ab, p);
                if (Mathf.Abs(det) < 1e-9f) continue;
                Vector3 delta = origin - a;
                float u = Vector3.Dot(delta, p) / det;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(delta, ab);
                float v = Vector3.Dot(rayDirection, q) / det;
                if (v < 0f || u + v > 1f) continue;
                float t = Vector3.Dot(ac, q) / det;
                if (t >= 0f && t <= .415f) return true;
            }
            return false;
        }
    }
}
