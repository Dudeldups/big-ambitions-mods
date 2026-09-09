#nullable enable
using System;
using BAModAPI;
using UnityEngine;

internal sealed class BigfootMonsterTruckPaintController : MonoBehaviour
{
    private const string BodyRendererName = "Object_5";
    private static readonly int TintId =
        Shader.PropertyToID("Color_3d0f0cdbe6b74be28a1a5be5bab71dea");
    private readonly MaterialPropertyBlock propertyBlock = new();
    private VehicleController? vehicle;
    private MeshRenderer? bodyRenderer;
    private Material? originalMaterial;
    private Material? paintMaterial;
    private Texture2D? sourceTexture;
    private Color32[]? sourcePixels;
    private Texture2D? paintedTexture;
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
        bodyRenderer.sharedMaterials = materials;
        configured = true;
        ApplySelectedColor(true);
        return true;
    }

    private void ApplySelectedColor(bool force = false)
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
            replacement = CreatePaintedTexture(sourceTexture, sourcePixels, tint);
            SetBaseTexture(paintMaterial, replacement);
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
                    $"texture={sourceTexture.width}x{sourceTexture.height}.");
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
        Color tint)
    {
        var pixels = (Color32[])originalPixels.Clone();
        for (var index = 0; index < pixels.Length; index++)
        {
            var sourceColor = pixels[index];
            var red = sourceColor.r / 255f;
            var green = sourceColor.g / 255f;
            var blue = sourceColor.b / 255f;
            var maximum = Mathf.Max(red, Mathf.Max(green, blue));
            var minimum = Mathf.Min(red, Mathf.Min(green, blue));
            var chroma = maximum - minimum;
            var darkMask = 1f - Mathf.SmoothStep(0.16f, 0.34f, maximum);
            var neutralMask = 1f - Mathf.SmoothStep(0.055f, 0.18f, chroma);
            var paintMask = darkMask * neutralMask;
            if (paintMask <= 0f)
                continue;

            var shade = Mathf.Lerp(0.58f, 1f, Mathf.Clamp01(maximum / 0.24f));
            var target = new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f);
            var original = new Color(red, green, blue, 1f);
            var painted = Color.Lerp(original, target, paintMask);
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
                if (materials[index] == paintMaterial)
                    materials[index] = originalMaterial;
            bodyRenderer.sharedMaterials = materials;
        }
        if (paintedTexture != null)
            Destroy(paintedTexture);
        if (paintMaterial != null)
            Destroy(paintMaterial);
        paintedTexture = null;
        paintMaterial = null;
        sourceTexture = null;
        sourcePixels = null;
        bodyRenderer = null;
    }
}
