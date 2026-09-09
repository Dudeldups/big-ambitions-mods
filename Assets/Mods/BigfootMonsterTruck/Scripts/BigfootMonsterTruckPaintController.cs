#nullable enable
using System;
using System.Collections.Generic;
using BAModAPI;
using UnityEngine;

internal sealed class BigfootMonsterTruckPaintController : MonoBehaviour
{
    private const string BodyRendererName = "Object_5";
    private const string StructureRendererName = "Object_4";
    private static readonly int TintId =
        Shader.PropertyToID("Color_3d0f0cdbe6b74be28a1a5be5bab71dea");
    private readonly MaterialPropertyBlock propertyBlock = new();
    private VehicleController? vehicle;
    private MeshRenderer? bodyRenderer;
    private Material? originalMaterial;
    private Material? paintMaterial;
    private Material? originalWindowMaterial;
    private Material? paintWindowMaterial;
    private Texture2D? sourceTexture;
    private Color32[]? sourcePixels;
    private Texture2D? paintedTexture;
    private MeshRenderer? structureRenderer;
    private MeshFilter? structureFilter;
    private Mesh? originalStructureMesh;
    private Mesh? paintedStructureMesh;
    private Material[]? originalStructureMaterials;
    private Material? pillarMaterial;
    private ModContext? context;
    private Color lastTint;
    private bool configured;
    private bool failed;
    private bool loggedReady;
    private int attempts;
    private float nextAttempt;
    private float nextColorCheck;

    public void Initialize(VehicleController controller, ModContext? modContext)
    {
        vehicle = controller;
        context = modContext;
    }

    private void Update()
    {
        if (failed || vehicle == null)
            return;
        if (!configured)
        {
            if (attempts >= 20 || Time.unscaledTime < nextAttempt)
                return;
            attempts++;
            nextAttempt = Time.unscaledTime + 0.25f;
            if (!TryConfigure() && attempts == 20)
            {
                failed = true;
                Warn("paint adapter could not prepare the main body texture.");
            }
            return;
        }
        if (Time.unscaledTime < nextColorCheck)
            return;
        nextColorCheck = Time.unscaledTime + 0.1f;
        ApplySelectedColor();
    }

    private bool TryConfigure()
    {
        foreach (var renderer in vehicle!.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!string.Equals(renderer.name, BodyRendererName, StringComparison.Ordinal))
                continue;
            bodyRenderer = renderer;
            break;
        }
        if (bodyRenderer == null)
            return false;

        var materials = bodyRenderer.sharedMaterials;
        if (materials.Length == 0 || materials[0] == null)
            return false;
        originalMaterial = materials[0];
        sourceTexture = GetBaseTexture(originalMaterial) as Texture2D;
        if (sourceTexture == null)
            return false;

        try
        {
            sourcePixels = ReadSourcePixels(sourceTexture);
        }
        catch (Exception exception)
        {
            failed = true;
            Warn($"body texture readback failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }

        paintMaterial = new Material(originalMaterial)
        {
            name = "Bigfoot Repaintable Body"
        };
        materials[0] = paintMaterial;
        if (materials.Length > 1 && materials[1] != null)
        {
            originalWindowMaterial = materials[1];
            paintWindowMaterial = new Material(originalWindowMaterial)
            {
                name = "Bigfoot Repaintable Windshield Trim"
            };
            materials[1] = paintWindowMaterial;
        }
        bodyRenderer.sharedMaterials = materials;
        var pillarTriangleCount = ConfigurePaintedPillars();
        configured = true;
        ApplySelectedColor(true, pillarTriangleCount);
        return true;
    }

    private void ApplySelectedColor(bool force = false, int pillarTriangleCount = -1)
    {
        if (bodyRenderer == null || paintMaterial == null || sourceTexture == null ||
            sourcePixels == null)
            return;
        bodyRenderer.GetPropertyBlock(propertyBlock);
        var tint = propertyBlock.GetColor(TintId);
        tint.r = Mathf.Clamp01(tint.r);
        tint.g = Mathf.Clamp01(tint.g);
        tint.b = Mathf.Clamp01(tint.b);
        tint.a = 1f;
        if (!force && ColorsMatch(tint, lastTint))
            return;

        Texture2D? replacement = null;
        try
        {
            replacement = CreatePaintedTexture(
                sourceTexture,
                sourcePixels,
                tint,
                out var paintedPixelCount,
                out var flamePixelCount,
                out var flameColor);
            SetBaseTexture(paintMaterial, replacement);
            if (paintWindowMaterial != null)
                SetBaseTexture(paintWindowMaterial, replacement);
            SetPillarColor(tint);
            if (paintedTexture != null)
                Destroy(paintedTexture);
            paintedTexture = replacement;
            replacement = null;
            lastTint = tint;
            if (!loggedReady)
            {
                loggedReady = true;
                context?.Logger.Info(
                    $"BigfootMonsterTruck paint ready vehicle={vehicle?.GetInstanceID()} " +
                    $"texture={sourceTexture.width}x{sourceTexture.height} " +
                    $"maskedPixels={paintedPixelCount}/{sourcePixels.Length} " +
                    $"flamePixels={flamePixelCount}/{sourcePixels.Length} " +
                    $"pillarTriangles={Mathf.Max(0, pillarTriangleCount)} " +
                    $"tint={tint} contrast={flameColor}.");
            }
            else
            {
                context?.Logger.Info(
                    $"BigfootMonsterTruck paint changed vehicle={vehicle?.GetInstanceID()} " +
                    $"tint={tint} contrast={flameColor}.");
            }
        }
        catch (Exception exception)
        {
            if (replacement != null)
                Destroy(replacement);
            failed = true;
            Warn($"paint update failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static Color32[] ReadSourcePixels(Texture2D source)
    {
        if (source.isReadable)
            return source.GetPixels32();

        var previous = RenderTexture.active;
        var temporary = RenderTexture.GetTemporary(
            source.width,
            source.height,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.sRGB);
        Texture2D? readable = null;
        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            readable = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                false,
                false);
            readable.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0, false);
            readable.Apply(false, false);
            return readable.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            if (readable != null)
                Destroy(readable);
        }
    }

    private static Texture2D CreatePaintedTexture(
        Texture2D source,
        Color32[] originalPixels,
        Color tint,
        out int paintedPixelCount,
        out int flamePixelCount,
        out Color flameColor)
    {
        var pixels = (Color32[])originalPixels.Clone();
        paintedPixelCount = 0;
        flamePixelCount = 0;
        flameColor = GetContrastingFlameColor(tint);
        for (var index = 0; index < pixels.Length; index++)
        {
            var sourceColor = pixels[index];
            var red = sourceColor.r / 255f;
            var green = sourceColor.g / 255f;
            var blue = sourceColor.b / 255f;
            var maximum = Mathf.Max(red, Mathf.Max(green, blue));
            var minimum = Mathf.Min(red, Mathf.Min(green, blue));
            var chroma = maximum - minimum;
            // The atlas uses near-black for the base body and strongly colored or
            // bright pixels for flames, sponsor decals, lamps, grille and bumper.
            // Keep the transition deliberately narrow so those authored graphics
            // cannot inherit the selected vehicle color.
            var darkMask = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.075f, 0.17f, maximum));
            var neutralMask = 1f - Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.028f, 0.085f, chroma));
            var paintMask = darkMask * neutralMask;
            // Blue is the atlas' complete secondary paint layer: both the light
            // flames and their darker edging. Recolor it from the selected base
            // color instead of relying on the finite vanilla paint palette.
            var blueDominance = blue - Mathf.Max(red, green);
            var flameMask = Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(0.07f, 0.20f, blueDominance)) *
                Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(0.16f, 0.42f, maximum));
            var u = ((index % source.width) + 0.5f) / source.width;
            var v = ((index / source.width) + 0.5f) / source.height;
            if (IsProtectedDetailUv(u, v))
            {
                paintMask = 0f;
                flameMask = 0f;
            }
            if (paintMask <= 0f && flameMask <= 0f)
                continue;

            var original = new Color(red, green, blue, 1f);
            var painted = original;
            if (paintMask > 0f)
            {
                paintedPixelCount++;
                var shade = Mathf.Lerp(0.58f, 1f, Mathf.Clamp01(maximum / 0.24f));
                var target = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
                painted = Color.Lerp(painted, target, paintMask);
            }
            if (flameMask > 0f)
            {
                flamePixelCount++;
                var flameShade = Mathf.Lerp(0.62f, 1f, Mathf.InverseLerp(0.25f, 0.98f, maximum));
                var target = new Color(
                    flameColor.r * flameShade,
                    flameColor.g * flameShade,
                    flameColor.b * flameShade,
                    1f);
                painted = Color.Lerp(painted, target, flameMask);
            }
            pixels[index] = new Color32(
                (byte)Mathf.RoundToInt(painted.r * 255f),
                (byte)Mathf.RoundToInt(painted.g * 255f),
                (byte)Mathf.RoundToInt(painted.b * 255f),
                sourceColor.a);
        }

        var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true, false)
        {
            name = $"Bigfoot body paint {tint.r:F2},{tint.g:F2},{tint.b:F2}",
            filterMode = source.filterMode,
            wrapMode = source.wrapMode,
            anisoLevel = source.anisoLevel,
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private static bool IsProtectedDetailUv(float u, float v)
    {
        // These atlas islands contain the complete painted headlamp assemblies
        // and the small Bigfoot grille badge. Their dark and blue pixels are
        // product artwork, not either layer of the two-tone body livery.
        var headlamp = v >= 0.535f && v <= 0.655f &&
                       ((u >= 0.045f && u <= 0.165f) ||
                        (u >= 0.385f && u <= 0.525f));
        var grilleBadge = u >= 0.018f && u <= 0.155f &&
                          v >= 0.795f && v <= 0.895f;
        return headlamp || grilleBadge;
    }

    private static Color GetContrastingFlameColor(Color tint)
    {
        Color.RGBToHSV(tint, out var hue, out var saturation, out var value);
        var luminance = 0.2126f * tint.r + 0.7152f * tint.g + 0.0722f * tint.b;

        // Preserve the familiar blue-on-black stock livery. Neutral colors need
        // an intentional accent because hue rotation is undefined for greys.
        if (saturation < 0.16f)
        {
            if (luminance < 0.20f)
                return new Color(0.02f, 0.41f, 0.97f, 1f);
            if (luminance > 0.68f)
                return new Color(0.04f, 0.22f, 0.68f, 1f);
            return new Color(1f, 0.38f, 0.03f, 1f);
        }

        var contrastHue = Mathf.Repeat(hue + 0.5f, 1f);
        var contrastSaturation = Mathf.Max(0.78f, saturation);
        var contrastValue = luminance < 0.42f
            ? 1f
            : luminance > 0.72f
                ? 0.58f
                : Mathf.Clamp(1.08f - value * 0.22f, 0.76f, 0.96f);
        return Color.HSVToRGB(contrastHue, contrastSaturation, contrastValue);
    }

    private int ConfigurePaintedPillars()
    {
        foreach (var renderer in vehicle!.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (!string.Equals(renderer.name, StructureRendererName, StringComparison.Ordinal))
                continue;
            structureRenderer = renderer;
            structureFilter = renderer.GetComponent<MeshFilter>();
            break;
        }
        var sourceMesh = structureFilter?.sharedMesh;
        if (structureRenderer == null || sourceMesh == null || sourceMesh.subMeshCount != 1)
            return 0;

        var sourceTriangles = sourceMesh.GetTriangles(0);
        var vertices = sourceMesh.vertices;
        var regularTriangles = new List<int>(sourceTriangles.Length);
        var pillarTriangles = new List<int>();
        for (var index = 0; index < sourceTriangles.Length; index += 3)
        {
            var a = sourceTriangles[index];
            var b = sourceTriangles[index + 1];
            var c = sourceTriangles[index + 2];
            var center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            var absX = Mathf.Abs(center.x);
            var isCabinPillar =
                absX >= 0.82f && absX <= 1.45f &&
                center.y >= 0.65f && center.y <= 2.15f &&
                center.z >= -0.05f && center.z <= 1.10f;
            var destination = isCabinPillar ? pillarTriangles : regularTriangles;
            destination.Add(a);
            destination.Add(b);
            destination.Add(c);
        }
        if (pillarTriangles.Count == 0)
            return 0;

        originalStructureMesh = sourceMesh;
        originalStructureMaterials = structureRenderer.sharedMaterials;
        paintedStructureMesh = Instantiate(sourceMesh);
        paintedStructureMesh.name = "Bigfoot structure with repaintable A-pillars";
        paintedStructureMesh.subMeshCount = 2;
        paintedStructureMesh.SetTriangles(regularTriangles, 0);
        paintedStructureMesh.SetTriangles(pillarTriangles, 1);
        structureFilter!.sharedMesh = paintedStructureMesh;

        var sourceMaterial = originalStructureMaterials.Length > 0
            ? originalStructureMaterials[0]
            : originalMaterial;
        if (sourceMaterial == null)
            return 0;
        pillarMaterial = new Material(sourceMaterial)
        {
            name = "Bigfoot Repaintable A-Pillars"
        };
        SetBaseTexture(pillarMaterial, Texture2D.whiteTexture);
        SetFloat(pillarMaterial, "_Metallic", 0.18f);
        SetFloat(pillarMaterial, "_Smoothness", 0.48f);
        structureRenderer.sharedMaterials = new[] { sourceMaterial, pillarMaterial };
        return pillarTriangles.Count / 3;
    }

    private void SetPillarColor(Color tint)
    {
        if (pillarMaterial == null)
            return;
        var color = new Color(
            Mathf.Max(0.035f, tint.r * 0.82f),
            Mathf.Max(0.035f, tint.g * 0.82f),
            Mathf.Max(0.035f, tint.b * 0.82f),
            1f);
        SetColor(pillarMaterial, "_BaseColor", color);
        SetColor(pillarMaterial, "_Color", color);
        SetColor(pillarMaterial, "baseColorFactor", color);
    }

    private static Texture? GetBaseTexture(Material material)
    {
        foreach (var property in new[] { "_BaseColorMap", "_MainTex", "baseColorTexture" })
            if (material.HasProperty(property) && material.GetTexture(property) != null)
                return material.GetTexture(property);
        return null;
    }

    private static void SetBaseTexture(Material material, Texture texture)
    {
        foreach (var property in new[] { "_BaseColorMap", "_MainTex", "baseColorTexture" })
            if (material.HasProperty(property))
                material.SetTexture(property, texture);
    }

    private static void SetColor(Material material, string property, Color color)
    {
        if (material.HasProperty(property))
            material.SetColor(property, color);
    }

    private static void SetFloat(Material material, string property, float value)
    {
        if (material.HasProperty(property))
            material.SetFloat(property, value);
    }

    private static bool ColorsMatch(Color left, Color right) =>
        Mathf.Abs(left.r - right.r) < 0.002f &&
        Mathf.Abs(left.g - right.g) < 0.002f &&
        Mathf.Abs(left.b - right.b) < 0.002f;

    private void Warn(string message) => context?.Logger.Warn(
        $"BigfootMonsterTruck paint vehicle={vehicle?.GetInstanceID()}: {message}");

    private void OnDestroy()
    {
        if (bodyRenderer != null && originalMaterial != null && paintMaterial != null)
        {
            var materials = bodyRenderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                if (materials[index] == paintMaterial)
                    materials[index] = originalMaterial;
                else if (paintWindowMaterial != null && originalWindowMaterial != null &&
                         materials[index] == paintWindowMaterial)
                    materials[index] = originalWindowMaterial;
            }
            bodyRenderer.sharedMaterials = materials;
        }
        if (paintedTexture != null)
            Destroy(paintedTexture);
        if (paintMaterial != null)
            Destroy(paintMaterial);
        if (paintWindowMaterial != null)
            Destroy(paintWindowMaterial);
        if (structureFilter != null && originalStructureMesh != null)
            structureFilter.sharedMesh = originalStructureMesh;
        if (structureRenderer != null && originalStructureMaterials != null)
            structureRenderer.sharedMaterials = originalStructureMaterials;
        if (paintedStructureMesh != null)
            Destroy(paintedStructureMesh);
        if (pillarMaterial != null)
            Destroy(pillarMaterial);
        paintedTexture = null;
        paintMaterial = null;
        originalWindowMaterial = null;
        paintWindowMaterial = null;
        sourceTexture = null;
        sourcePixels = null;
        bodyRenderer = null;
        structureRenderer = null;
        structureFilter = null;
        originalStructureMesh = null;
        paintedStructureMesh = null;
        originalStructureMaterials = null;
        pillarMaterial = null;
    }
}
