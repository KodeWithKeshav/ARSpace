using UnityEngine;
using UnityEngine.Rendering;

namespace ARSpace.Placement
{
    /// <summary>
    /// Soft blob shadow drawn on the floor underneath a placed object. Real-time shadows do not
    /// land on the (invisible) AR floor, so without this furniture looks like it hovers.
    /// The material lives in Resources so it is always included in player builds.
    /// </summary>
    [DisallowMultipleComponent]
    public class ContactShadow : MonoBehaviour
    {
        const string MaterialResource = "M_ContactShadow";
        const float FootprintPadding = 1.35f;
        const float LiftAboveBase = 0.004f;

        static Material s_Material;
        static Mesh s_Quad;

        void Start()
        {
            Build();
        }

        void Build()
        {
            if (s_Material == null)
                s_Material = Resources.Load<Material>(MaterialResource);

            if (s_Material == null)
                return;

            Vector3 center = new Vector3(0f, 0.4f, 0f);
            Vector3 size = new Vector3(0.8f, 0.8f, 0.8f);
            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                center = box.center;
                size = box.size;
            }

            var go = new GameObject("ContactShadow");
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(center.x, center.y - size.y * 0.5f + LiftAboveBase, center.z);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(size.x * FootprintPadding, 1f, size.z * FootprintPadding);

            go.AddComponent<MeshFilter>().sharedMesh = GetQuad();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = s_Material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        static Mesh GetQuad()
        {
            if (s_Quad != null)
                return s_Quad;

            s_Quad = new Mesh { name = "ContactShadowQuad" };
            s_Quad.vertices = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                new Vector3(0.5f, 0f, 0.5f), new Vector3(-0.5f, 0f, 0.5f)
            };
            s_Quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            s_Quad.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            s_Quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            s_Quad.RecalculateBounds();
            return s_Quad;
        }
    }
}
