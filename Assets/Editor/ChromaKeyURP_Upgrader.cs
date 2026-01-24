// Assets/Editor/ChromaKeyURP_Upgrader.cs
// Bulk-upgrades legacy uChromaKey (Built-in pipeline) materials to URP equivalents.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class ChromaKeyURP_Upgrader
{
    // Legacy shader names found in your uploaded shader files:
    // - ChromaKey/Unlit/Transparent
    // - ChromaKey/Unlit/Cutout
    // - ChromaKey/Standard/Transparent
    // - ChromaKey/Standard/Cutout
    private static readonly HashSet<string> LegacyShaderNames = new()
    {
        "ChromaKey/Unlit/Transparent",
        "ChromaKey/Unlit/Cutout",
        "ChromaKey/Standard/Transparent",
        "ChromaKey/Standard/Cutout",
    };

    // TODO: These must match the shader names YOU will create for URP.
    // Example suggested naming (you can change it, but keep consistent):
    // - ChromaKeyURP/Unlit/Transparent
    // - ChromaKeyURP/Unlit/Cutout
    // - ChromaKeyURP/Lit/Transparent
    // - ChromaKeyURP/Lit/Cutout
    private static readonly Dictionary<string, string> LegacyToUrpShaderMap = new()
    {
        { "ChromaKey/Unlit/Transparent",   "ChromaKeyURP/Unlit/Transparent" },
        { "ChromaKey/Unlit/Cutout",        "ChromaKeyURP/Unlit/Cutout" },
        { "ChromaKey/Standard/Transparent","ChromaKeyURP/Lit/Transparent" },
        { "ChromaKey/Standard/Cutout",     "ChromaKeyURP/Lit/Cutout" },
    };

    // Common property names from your legacy uChromaKey shaders
    private static readonly int Prop_Color = Shader.PropertyToID("_Color");
    private static readonly int Prop_MainTex = Shader.PropertyToID("_MainTex");
    private static readonly int Prop_Cull = Shader.PropertyToID("_Cull");

    private static readonly int Prop_KeyColor = Shader.PropertyToID("_ChromaKeyColor");
    private static readonly int Prop_HueRange = Shader.PropertyToID("_ChromaKeyHueRange");
    private static readonly int Prop_SatRange = Shader.PropertyToID("_ChromaKeySaturationRange");
    private static readonly int Prop_BriRange = Shader.PropertyToID("_ChromaKeyBrightnessRange");

    // Optional legacy "Standard" props
    private static readonly int Prop_Metallic = Shader.PropertyToID("_Metallic");
    private static readonly int Prop_Glossiness = Shader.PropertyToID("_Glossiness");

    [MenuItem("Tools/ChromaKey/Upgrade Legacy uChromaKey Materials -> URP")]
    public static void UpgradeAllMaterials()
    {
        var matGuids = AssetDatabase.FindAssets("t:Material");
        int changed = 0;
        int skipped = 0;

        try
        {
            AssetDatabase.StartAssetEditing();

            foreach (var guid in matGuids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null)
                {
                    skipped++;
                    continue;
                }

                var legacyName = mat.shader.name;
                if (!LegacyShaderNames.Contains(legacyName))
                {
                    // Not a legacy uChromaKey material
                    continue;
                }

                if (!LegacyToUrpShaderMap.TryGetValue(legacyName, out var urpShaderName))
                {
                    Debug.LogWarning($"[ChromaKeyURP_Upgrader] No URP mapping for shader '{legacyName}' on material: {path}");
                    skipped++;
                    continue;
                }

                var urpShader = Shader.Find(urpShaderName);
                if (urpShader == null)
                {
                    Debug.LogError(
                        $"[ChromaKeyURP_Upgrader] URP shader not found: '{urpShaderName}'. " +
                        $"Create/import that shader first, then re-run. Material: {path}"
                    );
                    skipped++;
                    continue;
                }

                // Record undo for safety
                Undo.RecordObject(mat, "Upgrade ChromaKey material to URP");

                // Cache legacy values
                var cached = CacheLegacyValues(mat);

                // Swap shader
                mat.shader = urpShader;

                // Restore values (only if the URP shader exposes matching properties)
                RestoreValues(mat, cached);

                // Make sure Transparent vs Cutout behaves
                ApplyQueueHints(mat, legacyName);

                EditorUtility.SetDirty(mat);
                changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        Debug.Log($"[ChromaKeyURP_Upgrader] Done. Upgraded: {changed}, Skipped: {skipped}");
    }

    private struct LegacyCache
    {
        public Color color;
        public Texture mainTex;
        public Vector2 mainTexScale;
        public Vector2 mainTexOffset;

        public float cull;

        public Color keyColor;
        public float hue;
        public float sat;
        public float bri;

        public float metallic;
        public float glossiness;

        public int renderQueue;
    }

    private static LegacyCache CacheLegacyValues(Material mat)
    {
        var c = new LegacyCache
        {
            renderQueue = mat.renderQueue
        };

        if (mat.HasProperty(Prop_Color)) c.color = mat.GetColor(Prop_Color);

        if (mat.HasProperty(Prop_MainTex))
        {
            c.mainTex = mat.GetTexture(Prop_MainTex);
            c.mainTexScale = mat.GetTextureScale(Prop_MainTex);
            c.mainTexOffset = mat.GetTextureOffset(Prop_MainTex);
        }

        if (mat.HasProperty(Prop_Cull)) c.cull = mat.GetFloat(Prop_Cull);

        if (mat.HasProperty(Prop_KeyColor)) c.keyColor = mat.GetColor(Prop_KeyColor);
        if (mat.HasProperty(Prop_HueRange)) c.hue = mat.GetFloat(Prop_HueRange);
        if (mat.HasProperty(Prop_SatRange)) c.sat = mat.GetFloat(Prop_SatRange);
        if (mat.HasProperty(Prop_BriRange)) c.bri = mat.GetFloat(Prop_BriRange);

        if (mat.HasProperty(Prop_Metallic)) c.metallic = mat.GetFloat(Prop_Metallic);
        if (mat.HasProperty(Prop_Glossiness)) c.glossiness = mat.GetFloat(Prop_Glossiness);

        return c;
    }

    private static void RestoreValues(Material mat, LegacyCache c)
    {
        if (mat.HasProperty(Prop_Color)) mat.SetColor(Prop_Color, c.color);

        if (mat.HasProperty(Prop_MainTex))
        {
            mat.SetTexture(Prop_MainTex, c.mainTex);
            mat.SetTextureScale(Prop_MainTex, c.mainTexScale);
            mat.SetTextureOffset(Prop_MainTex, c.mainTexOffset);
        }

        if (mat.HasProperty(Prop_Cull)) mat.SetFloat(Prop_Cull, c.cull);

        if (mat.HasProperty(Prop_KeyColor)) mat.SetColor(Prop_KeyColor, c.keyColor);
        if (mat.HasProperty(Prop_HueRange)) mat.SetFloat(Prop_HueRange, c.hue);
        if (mat.HasProperty(Prop_SatRange)) mat.SetFloat(Prop_SatRange, c.sat);
        if (mat.HasProperty(Prop_BriRange)) mat.SetFloat(Prop_BriRange, c.bri);

        if (mat.HasProperty(Prop_Metallic)) mat.SetFloat(Prop_Metallic, c.metallic);
        if (mat.HasProperty(Prop_Glossiness)) mat.SetFloat(Prop_Glossiness, c.glossiness);

        // If the URP shader doesn't control queue, keep legacy as fallback
        if (mat.renderQueue == -1 && c.renderQueue != -1)
            mat.renderQueue = c.renderQueue;
    }

    private static void ApplyQueueHints(Material mat, string legacyShaderName)
    {
        // These are just safe defaults; your URP shader should implement proper blending/alpha testing.
        bool wasTransparent = legacyShaderName.EndsWith("/Transparent");
        bool wasCutout = legacyShaderName.EndsWith("/Cutout");

        if (wasTransparent)
        {
            // Transparent queue
            mat.renderQueue = 3000;
        }
        else if (wasCutout)
        {
            // AlphaTest queue
            mat.renderQueue = 2450;
        }
    }
}
#endif
