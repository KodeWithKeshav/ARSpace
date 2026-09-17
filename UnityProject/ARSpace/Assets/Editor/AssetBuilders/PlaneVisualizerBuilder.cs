using UnityEngine;
using UnityEditor;
using System.IO;
using ARSpace.AR;

namespace ARSpace.Editor.AssetBuilders
{
    /// <summary>
    /// Editor builder for AR plane grid assets and prefab configuration.
    /// Menu item: ARSpace → Setup Plane Grid Assets
    ///
    /// Requirements:
    /// - Creates or updates M_PlaneGrid.mat with ARSpace/PlaneGrid shader
    /// - Creates or updates M_PlaneOutline.mat with ARSpace/UnlitLine shader
    /// - Configures Plane.prefab with PlaneGridVisualizer, shared materials, and LineRenderer
    /// - Fully idempotent: safe to run repeatedly without producing duplicates
    /// </summary>
    public static class PlaneVisualizerBuilder
    {
        const string MaterialsDirectory = "Assets/Art/Materials";
        const string GridMaterialPath = "Assets/Art/Materials/M_PlaneGrid.mat";
        const string OutlineMaterialPath = "Assets/Art/Materials/M_PlaneOutline.mat";
        const string PlanePrefabPath = "Assets/Prefabs/Plane.prefab";

        public static readonly Color BrandOrange = new Color(1.0f, 0.42f, 0.0f, 1.0f);
        public static readonly Color GridNeutral = new Color(0.85f, 0.88f, 0.92f, 0.18f);
        public static readonly Color AccentGrid = new Color(1.0f, 0.42f, 0.0f, 0.45f);

        [MenuItem("ARSpace/Setup Plane Grid Assets", priority = 20)]
        public static void SetupPlaneGridAssets()
        {
            Debug.Log("[PlaneVisualizerBuilder] Setting up plane grid materials and prefab...");

            EnsureDirectoryExists(MaterialsDirectory);

            Material gridMat = SetupGridMaterial();
            Material outlineMat = SetupOutlineMaterial();

            SetupPlanePrefab(gridMat, outlineMat);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[PlaneVisualizerBuilder] ✓ Plane grid assets and prefab successfully configured.");
        }

        public static Material SetupGridMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(GridMaterialPath);
            Shader shader = Shader.Find("ARSpace/PlaneGrid");

            if (shader == null)
            {
                Debug.LogError("[PlaneVisualizerBuilder] Shader 'ARSpace/PlaneGrid' not found!");
                return null;
            }

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, GridMaterialPath);
                Debug.Log($"[PlaneVisualizerBuilder] Created new grid material at {GridMaterialPath}");
            }
            else
            {
                mat.shader = shader;
            }

            // Configure material properties
            mat.SetColor("_GridColor", GridNeutral);
            mat.SetColor("_AccentColor", AccentGrid);
            mat.SetFloat("_PrimarySpacing", 0.5f);
            mat.SetFloat("_SecondarySpacing", 1.0f);
            mat.SetFloat("_PrimaryLineWidth", 1.2f);
            mat.SetFloat("_SecondaryLineWidth", 1.8f);
            mat.SetFloat("_FadeStartDistance", 7.0f);
            mat.SetFloat("_FadeEndDistance", 12.0f);
            mat.SetFloat("_ScanSweepWidth", 0.35f);
            mat.SetFloat("_PlaneAlpha", 1.0f);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static Material SetupOutlineMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            Shader shader = Shader.Find("ARSpace/UnlitLine");

            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            if (shader == null)
            {
                Debug.LogError("[PlaneVisualizerBuilder] Shader 'ARSpace/UnlitLine' not found!");
                return null;
            }

            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, OutlineMaterialPath);
                Debug.Log($"[PlaneVisualizerBuilder] Created new outline material at {OutlineMaterialPath}");
            }
            else
            {
                mat.shader = shader;
            }

            // Set base color to ARSpace brand orange
            Color outlineColor = new Color(BrandOrange.r, BrandOrange.g, BrandOrange.b, 0.9f);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", outlineColor);
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", outlineColor);

            EditorUtility.SetDirty(mat);
            return mat;
        }

        public static void SetupPlanePrefab(Material gridMaterial, Material outlineMaterial)
        {
            if (!File.Exists(PlanePrefabPath))
            {
                Debug.LogError($"[PlaneVisualizerBuilder] Plane prefab not found at {PlanePrefabPath}");
                return;
            }

            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PlanePrefabPath);
            try
            {
                // Ensure PlaneGridVisualizer component exists
                var visualizer = prefabRoot.GetComponent<PlaneGridVisualizer>();
                if (visualizer == null)
                {
                    visualizer = prefabRoot.AddComponent<PlaneGridVisualizer>();
                    Debug.Log("[PlaneVisualizerBuilder] Added PlaneGridVisualizer to Plane.prefab");
                }

                // Configure visualizer serialized fields via SerializedObject
                var serializedObj = new SerializedObject(visualizer);
                serializedObj.Update();

                var sharedGridMatProp = serializedObj.FindProperty("m_SharedGridMaterial");
                if (sharedGridMatProp != null)
                    sharedGridMatProp.objectReferenceValue = gridMaterial;

                var outlineMatProp = serializedObj.FindProperty("m_OutlineMaterial");
                if (outlineMatProp != null)
                    outlineMatProp.objectReferenceValue = outlineMaterial;

                var featherWidthProp = serializedObj.FindProperty("m_FeatheringWidth");
                if (featherWidthProp != null)
                    featherWidthProp.floatValue = 0.25f;

                var outlineWidthProp = serializedObj.FindProperty("m_OutlineWidth");
                if (outlineWidthProp != null)
                    outlineWidthProp.floatValue = 0.006f;

                var fadeDurationProp = serializedObj.FindProperty("m_FadeDuration");
                if (fadeDurationProp != null)
                    fadeDurationProp.floatValue = 0.35f;

                serializedObj.ApplyModifiedProperties();

                // Configure MeshRenderer
                var meshRenderer = prefabRoot.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.sharedMaterial = gridMaterial;
                    meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    meshRenderer.receiveShadows = false;
                }

                // Configure LineRenderer
                var lineRenderer = prefabRoot.GetComponent<LineRenderer>();
                if (lineRenderer == null)
                {
                    lineRenderer = prefabRoot.AddComponent<LineRenderer>();
                }

                lineRenderer.sharedMaterial = outlineMaterial;
                lineRenderer.loop = true;
                lineRenderer.useWorldSpace = false;
                lineRenderer.alignment = LineAlignment.View;
                lineRenderer.startWidth = 0.006f;
                lineRenderer.endWidth = 0.006f;
                lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lineRenderer.receiveShadows = false;

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlanePrefabPath);
                Debug.Log("[PlaneVisualizerBuilder] Successfully updated Plane.prefab with PlaneGridVisualizer and materials.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        static void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }
    }
}
