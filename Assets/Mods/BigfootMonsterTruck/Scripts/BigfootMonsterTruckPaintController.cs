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
    private Texture2D? paintedWindowTexture;
    private MeshRenderer? structureRenderer;
    private MeshFilter? structureFilter;
    private Mesh? originalStructureMesh;
    private Mesh? paintedStructureMesh;
    private Material[]? originalStructureMaterials;
    private Material? pillarMaterial;
    private GameObject? factoryBadgeObject;
    private Mesh? factoryBadgeMesh;
    private GameObject? grilleBackingObject;
    private Mesh? grilleBackingMesh;
    private Material? grilleBackingMaterial;
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
        var badgeTriangleCount = ConfigureFactoryGrilleBadge();
        var grilleTriangleCount = ConfigurePaintedGrilleBacking();
        var pillarTriangleCount = ConfigurePaintedPillars();
        configured = true;
        ApplySelectedColor(
            true,
            pillarTriangleCount,
            badgeTriangleCount,
            grilleTriangleCount);
        return true;
    }

    private void ApplySelectedColor(
        bool force = false,
        int pillarTriangleCount = -1,
        int badgeTriangleCount = -1,
        int grilleTriangleCount = -1)
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
        Texture2D? windowReplacement = null;
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
            {
                windowReplacement = CreateWindshieldTexture(replacement);
                SetBaseTexture(paintWindowMaterial, windowReplacement);
                SetColor(
                    paintWindowMaterial,
                    "_Color",
                    new Color(0.78f, 0.84f, 0.88f, 0.11f));
            }
            SetPillarColor(tint);
            SetGrilleBackingColor(tint);
            if (paintedTexture != null)
                Destroy(paintedTexture);
            if (paintedWindowTexture != null)
                Destroy(paintedWindowTexture);
            paintedTexture = replacement;
            paintedWindowTexture = windowReplacement;
            replacement = null;
            windowReplacement = null;
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
                    $"badgeTriangles={Mathf.Max(0, badgeTriangleCount)} " +
                    $"grilleTriangles={Mathf.Max(0, grilleTriangleCount)} " +
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
            if (windowReplacement != null)
                Destroy(windowReplacement);
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

    private static Texture2D CreateWindshieldTexture(Texture2D paintedBodyTexture)
    {
        var pixels = paintedBodyTexture.GetPixels32();
        for (var index = 0; index < pixels.Length; index++)
        {
            var color = pixels[index];
            var red = color.r / 255f;
            var green = color.g / 255f;
            var blue = color.b / 255f;
            var maximum = Mathf.Max(red, Mathf.Max(green, blue));
            var minimum = Mathf.Min(red, Mathf.Min(green, blue));

            // The bundled glass shader derives opacity from atlas brightness.
            // Suppress only its neutral light-grey glass field. Bright white
            // lettering and saturated repaint trim retain their authored color
            // and remain opaque.
            if (maximum >= 0.32f && maximum < 0.86f && maximum - minimum < 0.10f)
                pixels[index] = new Color32(32, 38, 42, color.a);
        }

        var texture = new Texture2D(
            paintedBodyTexture.width,
            paintedBodyTexture.height,
            TextureFormat.RGBA32,
            true,
            false)
        {
            name = paintedBodyTexture.name + " windshield",
            filterMode = paintedBodyTexture.filterMode,
            wrapMode = paintedBodyTexture.wrapMode,
            anisoLevel = paintedBodyTexture.anisoLevel,
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        return texture;
    }

    private static bool IsProtectedDetailUv(float u, float v)
    {
        // These atlas islands contain the complete painted headlamp assemblies.
        // Their dark and blue pixels are product artwork, not either layer of
        // the two-tone body livery. The grille badge is protected geometrically
        // because its UVs overlap unrelated body paint elsewhere in the atlas.
        var headlamp = v >= 0.535f && v <= 0.655f &&
                       ((u >= 0.045f && u <= 0.165f) ||
                        (u >= 0.385f && u <= 0.525f));
        return headlamp;
    }

    private int ConfigureFactoryGrilleBadge()
    {
        var sourceFilter = bodyRenderer?.GetComponent<MeshFilter>();
        var sourceMesh = sourceFilter?.sharedMesh;
        if (bodyRenderer == null || sourceMesh == null || originalMaterial == null ||
            sourceMesh.subMeshCount == 0)
            return 0;

        var vertices = sourceMesh.vertices;
        var sourceTriangles = sourceMesh.GetTriangles(0);
        var badgeTriangles = new List<int>();
        for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
        {
            var a = sourceTriangles[index];
            var b = sourceTriangles[index + 1];
            var c = sourceTriangles[index + 2];
            var center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            var isBadge =
                Mathf.Abs(center.x) <= 0.18f &&
                center.y >= 3.68f && center.y <= 3.78f &&
                center.z >= -0.36f && center.z <= -0.20f;
            if (!isBadge)
                continue;
            badgeTriangles.Add(a);
            badgeTriangles.Add(b);
            badgeTriangles.Add(c);
        }
        if (badgeTriangles.Count == 0)
            return 0;

        factoryBadgeMesh = Instantiate(sourceMesh);
        factoryBadgeMesh.name = sourceMesh.name + "_FactoryGrilleBadge";
        factoryBadgeMesh.subMeshCount = 1;
        factoryBadgeMesh.SetTriangles(badgeTriangles, 0, true);
        factoryBadgeMesh.RecalculateBounds();

        factoryBadgeObject = new GameObject("BigfootMonsterTruck_FactoryGrilleBadge");
        factoryBadgeObject.transform.SetParent(bodyRenderer.transform, false);
        // Pull the copied surface a hair toward the front to avoid z-fighting
        // with the recolored copy underneath it.
        factoryBadgeObject.transform.localPosition = new Vector3(0f, 0.0015f, 0f);
        factoryBadgeObject.layer = bodyRenderer.gameObject.layer;
        factoryBadgeObject.AddComponent<MeshFilter>().sharedMesh = factoryBadgeMesh;
        var badgeRenderer = factoryBadgeObject.AddComponent<MeshRenderer>();
        badgeRenderer.sharedMaterial = originalMaterial;
        badgeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        badgeRenderer.receiveShadows = false;
        return badgeTriangles.Count / 3;
    }

    private int ConfigurePaintedGrilleBacking()
    {
        var sourceFilter = bodyRenderer?.GetComponent<MeshFilter>();
        var sourceMesh = sourceFilter?.sharedMesh;
        if (bodyRenderer == null || sourceMesh == null || originalMaterial == null ||
            sourceTexture == null || sourcePixels == null || sourceMesh.subMeshCount == 0)
            return 0;

        var vertices = sourceMesh.vertices;
        var uv = sourceMesh.uv;
        if (uv.Length != vertices.Length)
            return 0;
        var sourceTriangles = sourceMesh.GetTriangles(0);
        var candidates = new List<Vector3Int>();
        var paintableCandidates = new List<bool>();
        for (var index = 0; index + 2 < sourceTriangles.Length; index += 3)
        {
            var a = sourceTriangles[index];
            var b = sourceTriangles[index + 1];
            var c = sourceTriangles[index + 2];
            var center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
            var insideGrille =
                Mathf.Abs(center.x) <= 0.78f &&
                center.y >= 3.50f && center.y <= 3.78f &&
                center.z >= -0.53f && center.z <= -0.04f;
            var insideBadge =
                Mathf.Abs(center.x) <= 0.18f &&
                center.y >= 3.68f && center.y <= 3.78f &&
                center.z >= -0.36f && center.z <= -0.20f;
            if (!insideGrille || insideBadge)
                continue;

            candidates.Add(new Vector3Int(a, b, c));
            var triangleUv = (uv[a] + uv[b] + uv[c]) / 3f;
            var sourceColor = SampleSourceColor(triangleUv);
            var red = sourceColor.r / 255f;
            var green = sourceColor.g / 255f;
            var blue = sourceColor.b / 255f;
            var maximum = Mathf.Max(red, Mathf.Max(green, blue));
            var blueDominance = blue - Mathf.Max(red, green);
            paintableCandidates.Add(maximum <= 0.48f || blueDominance >= 0.06f);
        }
        var grilleTriangles = SelectPaintableConnectedSurfaces(
            candidates,
            paintableCandidates);
        if (grilleTriangles.Count == 0)
            return 0;

        grilleBackingMesh = Instantiate(sourceMesh);
        grilleBackingMesh.name = sourceMesh.name + "_PaintedInnerGrille";
        grilleBackingMesh.subMeshCount = 1;
        grilleBackingMesh.SetTriangles(grilleTriangles, 0, true);
        grilleBackingMesh.RecalculateBounds();

        grilleBackingMaterial = new Material(originalMaterial)
        {
            name = "Bigfoot Repaintable Grille Backing"
        };
        SetBaseTexture(grilleBackingMaterial, Texture2D.whiteTexture);
        ClearTexture(grilleBackingMaterial, "_NormalMap");
        ClearTexture(grilleBackingMaterial, "_BumpMap");
        ClearTexture(grilleBackingMaterial, "normalTexture");
        ClearTexture(grilleBackingMaterial, "_MaskMap");
        ClearTexture(grilleBackingMaterial, "_MetallicGlossMap");
        ClearTexture(grilleBackingMaterial, "_OcclusionMap");
        ClearTexture(grilleBackingMaterial, "_DetailMap");
        SetFloat(grilleBackingMaterial, "_Metallic", 0.15f);
        SetFloat(grilleBackingMaterial, "_Smoothness", 0.40f);

        grilleBackingObject = new GameObject("BigfootMonsterTruck_PaintedGrilleBacking");
        grilleBackingObject.transform.SetParent(bodyRenderer.transform, false);
        grilleBackingObject.transform.localPosition = new Vector3(0f, 0.0015f, 0f);
        grilleBackingObject.layer = bodyRenderer.gameObject.layer;
        grilleBackingObject.AddComponent<MeshFilter>().sharedMesh = grilleBackingMesh;
        var backingRenderer = grilleBackingObject.AddComponent<MeshRenderer>();
        backingRenderer.sharedMaterial = grilleBackingMaterial;
        backingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        backingRenderer.receiveShadows = false;
        return grilleTriangles.Count / 3;
    }

    private static List<int> SelectPaintableConnectedSurfaces(
        List<Vector3Int> triangles,
        List<bool> paintable)
    {
        var parents = new int[triangles.Count];
        var vertexOwner = new Dictionary<int, int>();
        for (var index = 0; index < triangles.Count; index++)
        {
            parents[index] = index;
            ConnectGrilleVertex(triangles[index].x, index, parents, vertexOwner);
            ConnectGrilleVertex(triangles[index].y, index, parents, vertexOwner);
            ConnectGrilleVertex(triangles[index].z, index, parents, vertexOwner);
        }

        var groups = new Dictionary<int, List<int>>();
        for (var index = 0; index < triangles.Count; index++)
        {
            var root = FindGrilleRoot(index, parents);
            if (!groups.TryGetValue(root, out var group))
            {
                group = new List<int>();
                groups[root] = group;
            }
            group.Add(index);
        }

        var selected = new List<int>();
        foreach (var group in groups.Values)
        {
            var paintableCount = 0;
            foreach (var triangleIndex in group)
                if (paintable[triangleIndex])
                    paintableCount++;
            // Work at connected-surface granularity. Sampling individual atlas
            // pixels left isolated unpainted triangles inside otherwise valid
            // grille panels.
            if (paintableCount == 0 || paintableCount < group.Count * 0.45f)
                continue;
            foreach (var triangleIndex in group)
            {
                var triangle = triangles[triangleIndex];
                selected.Add(triangle.x);
                selected.Add(triangle.y);
                selected.Add(triangle.z);
            }
        }
        return selected;
    }

    private static void ConnectGrilleVertex(
        int vertex,
        int triangle,
        int[] parents,
        Dictionary<int, int> vertexOwner)
    {
        if (vertexOwner.TryGetValue(vertex, out var owner))
            UnionGrilleGroups(triangle, owner, parents);
        else
            vertexOwner[vertex] = triangle;
    }

    private static int FindGrilleRoot(int value, int[] parents)
    {
        while (parents[value] != value)
        {
            parents[value] = parents[parents[value]];
            value = parents[value];
        }
        return value;
    }

    private static void UnionGrilleGroups(int left, int right, int[] parents)
    {
        var leftRoot = FindGrilleRoot(left, parents);
        var rightRoot = FindGrilleRoot(right, parents);
        if (leftRoot != rightRoot)
            parents[rightRoot] = leftRoot;
    }

    private Color32 SampleSourceColor(Vector2 uv)
    {
        var x = Mathf.Clamp(
            Mathf.FloorToInt(Mathf.Repeat(uv.x, 1f) * sourceTexture!.width),
            0,
            sourceTexture.width - 1);
        var y = Mathf.Clamp(
            Mathf.FloorToInt(Mathf.Repeat(uv.y, 1f) * sourceTexture.height),
            0,
            sourceTexture.height - 1);
        return sourcePixels![y * sourceTexture.width + x];
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
                return new Color(0.04f, 0.30f, 0.95f, 1f);
            return new Color(1f, 0.38f, 0.03f, 1f);
        }

        var contrastHue = Mathf.Repeat(hue + 0.5f, 1f);
        var contrastSaturation = Mathf.Max(0.78f, saturation);
        var contrastValue = luminance < 0.42f
            ? 1f
            : luminance > 0.72f
                ? 0.86f
                : Mathf.Clamp(1.08f - value * 0.22f, 0.82f, 0.96f);
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

    private void SetGrilleBackingColor(Color tint)
    {
        if (grilleBackingMaterial == null)
            return;
        var color = new Color(
            Mathf.Max(0.025f, tint.r * 0.72f),
            Mathf.Max(0.025f, tint.g * 0.72f),
            Mathf.Max(0.025f, tint.b * 0.72f),
            1f);
        SetColor(grilleBackingMaterial, "_BaseColor", color);
        SetColor(grilleBackingMaterial, "_Color", color);
        SetColor(grilleBackingMaterial, "baseColorFactor", color);
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

    private static void ClearTexture(Material material, string property)
    {
        if (material.HasProperty(property))
            material.SetTexture(property, null);
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
        if (paintedWindowTexture != null)
            Destroy(paintedWindowTexture);
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
        if (factoryBadgeObject != null)
            Destroy(factoryBadgeObject);
        if (factoryBadgeMesh != null)
            Destroy(factoryBadgeMesh);
        if (grilleBackingObject != null)
            Destroy(grilleBackingObject);
        if (grilleBackingMesh != null)
            Destroy(grilleBackingMesh);
        if (grilleBackingMaterial != null)
            Destroy(grilleBackingMaterial);
        paintedTexture = null;
        paintedWindowTexture = null;
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
        factoryBadgeObject = null;
        factoryBadgeMesh = null;
        grilleBackingObject = null;
        grilleBackingMesh = null;
        grilleBackingMaterial = null;
    }
}
