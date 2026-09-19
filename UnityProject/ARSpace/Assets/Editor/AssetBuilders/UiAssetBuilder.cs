using System.IO;
using UnityEngine;
using UnityEditor;

namespace ARSpace.Editor.AssetBuilders
{
    /// <summary>
    /// Generates the small set of art assets the runtime UI needs:
    /// - a crisp, anti-aliased 9-sliced rounded-rectangle sprite (replaces Unity's low-resolution built-in UISprite)
    /// - the soft contact-shadow material used under placed furniture (lives in Resources so builds always include it)
    /// Idempotent; called automatically by the scene builder.
    /// </summary>
    public static class UiAssetBuilder
    {
        public const string RoundedSpritePath = "Assets/Art/UI/UIRounded.png";
        public const string ContactShadowMaterialPath = "Assets/Resources/M_ContactShadow.mat";

        const int TextureSize = 128;
        const int CornerRadius = 48;

        public static Sprite EnsureRoundedSprite()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
            if (existing != null)
                return existing;

            Directory.CreateDirectory(Path.GetDirectoryName(RoundedSpritePath));

            var tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[TextureSize * TextureSize];
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    // Signed distance to a rounded rectangle, with ~1px anti-aliasing.
                    float px = x + 0.5f - TextureSize * 0.5f;
                    float py = y + 0.5f - TextureSize * 0.5f;
                    float half = TextureSize * 0.5f - CornerRadius;
                    float dx = Mathf.Max(Mathf.Abs(px) - half, 0f);
                    float dy = Mathf.Max(Mathf.Abs(py) - half, 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy) - CornerRadius;
                    float alpha = Mathf.Clamp01(0.5f - dist);
                    pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(RoundedSpritePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(RoundedSpritePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(RoundedSpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(CornerRadius, CornerRadius, CornerRadius, CornerRadius);
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
        }

        public const string ReticleLineMaterialPath = "Assets/Art/Materials/M_ReticleLine.mat";

        /// <summary>Line material whose colour comes entirely from each LineRenderer's own colours (white base).</summary>
        public static Material EnsureReticleLineMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ReticleLineMaterialPath);
            Shader shader = Shader.Find("ARSpace/UnlitLine");
            if (shader == null)
                return mat;

            if (mat == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReticleLineMaterialPath));
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, ReticleLineMaterialPath);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        public static Material EnsureContactShadowMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ContactShadowMaterialPath);
            Shader shader = Shader.Find("ARSpace/ContactShadow");
            if (shader == null)
            {
                Debug.LogError("[UiAssetBuilder] Shader 'ARSpace/ContactShadow' not found — placed furniture will have no floor shadow.");
                return mat;
            }

            if (mat == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ContactShadowMaterialPath));
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, ContactShadowMaterialPath);
            }
            else
            {
                mat.shader = shader;
            }

            mat.SetColor("_ShadowColor", new Color(0f, 0f, 0f, 1f));
            mat.SetFloat("_Intensity", 0.55f);
            mat.SetFloat("_Softness", 1.6f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
