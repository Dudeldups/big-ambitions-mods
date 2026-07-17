#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
#if GUN_STORE_HELP_UI_DEBUG
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
#endif
using BAModAPI;
using Localizor;
using UnityEngine;
#if GUN_STORE_HELP_UI_DEBUG
using UnityEngine.EventSystems;
#endif
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(10000)]
internal sealed class GunStoreHelpDebugRuntime : MonoBehaviour
{
    private ModContext? context;
    private bool shuttingDown;
    private Coroutine? pendingNavigationPatch;

    public static GunStoreHelpDebugRuntime Initialize(ModContext context)
    {
        var existing = FindObjectOfType<GunStoreHelpDebugRuntime>();
        if (existing == null)
        {
            var runtimeObject = new GameObject(nameof(GunStoreHelpDebugRuntime));
            DontDestroyOnLoad(runtimeObject);
            existing = runtimeObject.AddComponent<GunStoreHelpDebugRuntime>();
        }

        existing.context = context;
        existing.shuttingDown = false;
        LocalizorManager.OnLanguageChanged -= existing.HandleLanguageChanged;
        LocalizorManager.OnLanguageChanged += existing.HandleLanguageChanged;
        SceneManager.sceneLoaded -= existing.HandleSceneLoaded;
        SceneManager.sceneLoaded += existing.HandleSceneLoaded;
        existing.ScheduleNavigationPatch();
        return existing;
    }

    public void Shutdown()
    {
        if (shuttingDown)
            return;

        shuttingDown = true;
        LocalizorManager.OnLanguageChanged -= HandleLanguageChanged;
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (pendingNavigationPatch != null)
            StopCoroutine(pendingNavigationPatch);
        Destroy(gameObject);
    }

    private void HandleLanguageChanged()
    {
        ScheduleNavigationPatch();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ScheduleNavigationPatch();
    }

    private void ScheduleNavigationPatch()
    {
        if (pendingNavigationPatch != null)
            StopCoroutine(pendingNavigationPatch);

        pendingNavigationPatch = StartCoroutine(PatchNavigationAfterUiRefresh());
    }

    private IEnumerator PatchNavigationAfterUiRefresh()
    {
        // Let the native Help and localization callbacks finish rebuilding their UI first.
        yield return null;
        pendingNavigationPatch = null;

        try
        {
            var result = GunStoreHelpNavigationPatch.TryApply(this);
            if (result == GunStoreHelpNavigationPatchResult.Applied)
            {
                context?.Logger.Info(
                    "Updated Gun Store links in the native Help System navigation.");
            }
        }
        catch (Exception exception)
        {
            context?.Logger.Error(exception);
        }
    }
}

internal enum GunStoreHelpNavigationPatchResult
{
    HelpSystemNotReady,
    Applied,
    AlreadyPresent
}

internal static class GunStoreHelpNavigationPatch
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly IReadOnlyDictionary<string, string[]> NavigationPagesByCategory =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["common_business_types"] = new[]
            {
                "businesstypes-gunstore"
            },
            ["common_sellable_products"] = new[]
            {
                "products-gunstore-businesstype:itemname_ak47",
                "products-gunstore-businesstype:itemname_ammosmall",
                "products-gunstore-businesstype:itemname_wincheatersxp",
                "products-gunstore-businesstype:itemname_berettam9",
                "products-gunstore-businesstype:itemname_ammolarge",
                "products-gunstore-businesstype:itemname_rpg"
            },
            ["common_factory_ingredients"] = new[]
            {
                "products-gunstore-businesstype:itemname_gunpartscheap",
                "products-gunstore-businesstype:itemname_gunpartsexpensive"
            }
        };

    public static GunStoreHelpNavigationPatchResult TryApply(MonoBehaviour coroutineHost)
    {
        var foundTargetCategory = false;
        var applied = false;

        foreach (var helpSystem in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (helpSystem == null || !helpSystem.gameObject.scene.IsValid())
                continue;

            var helpSystemType = helpSystem.GetType();
            if (!string.Equals(helpSystemType.FullName, "UnityEngine.UI.Extensions.HelpSystem.HelpSystem",
                    StringComparison.Ordinal) &&
                !string.Equals(helpSystemType.Name, "HelpSystem", StringComparison.Ordinal))
            {
                continue;
            }

            var categoriesField = GetField(helpSystemType, "_categories");
            if (categoriesField?.GetValue(helpSystem) is not IEnumerable categories)
                continue;

            var helpSystemChanged = false;
            foreach (var category in categories)
            {
                if (category == null)
                    continue;

                var categoryType = category.GetType();
                var categoryKey = GetField(categoryType, "CategoryLocalizorKey")?.GetValue(category) as string;
                if (categoryKey == null ||
                    !NavigationPagesByCategory.TryGetValue(categoryKey, out var desiredSlugs))
                {
                    continue;
                }

                var pagesField = GetField(categoryType, "Pages");
                if (pagesField?.GetValue(category) is not IList pages)
                    continue;

                foundTargetCategory = true;
                var pageType = GetListElementType(pages.GetType()) ??
                               pages.Cast<object?>().FirstOrDefault(page => page != null)?.GetType();
                if (pageType == null)
                    continue;

                var slugField = GetField(pageType, "Slug");
                var pagePrefixField = GetField(pageType, "PageLocalizorKeyPrefix");
                if (slugField == null || pagePrefixField == null)
                    continue;

                foreach (var desiredSlug in desiredSlugs)
                {
                    var existingPage = pages.Cast<object?>()
                        .FirstOrDefault(page =>
                            page != null &&
                            string.Equals(
                                GetField(page.GetType(), "Slug")?.GetValue(page) as string,
                                desiredSlug,
                                StringComparison.Ordinal));
                    if (existingPage != null)
                    {
                        var prefixField = GetField(existingPage.GetType(), "PageLocalizorKeyPrefix");
                        var currentPrefix = prefixField?.GetValue(existingPage) as string;
                        if (!string.Equals(currentPrefix, desiredSlug, StringComparison.Ordinal))
                        {
                            prefixField?.SetValue(existingPage, desiredSlug);
                            helpSystemChanged = true;
                        }

                        continue;
                    }

                    var newPage = Activator.CreateInstance(pageType);
                    if (newPage == null)
                        continue;

                    slugField.SetValue(newPage, desiredSlug);
                    pagePrefixField.SetValue(newPage, desiredSlug);
                    pages.Add(newPage);
                    helpSystemChanged = true;
                }
            }

            if (helpSystemChanged)
            {
                RefreshGeneratedNavigation(helpSystem, helpSystemType, coroutineHost);
                applied = true;
            }
        }

        if (applied)
            return GunStoreHelpNavigationPatchResult.Applied;

        return foundTargetCategory
            ? GunStoreHelpNavigationPatchResult.AlreadyPresent
            : GunStoreHelpNavigationPatchResult.HelpSystemNotReady;
    }

    private static void RefreshGeneratedNavigation(
        MonoBehaviour helpSystem,
        Type helpSystemType,
        MonoBehaviour coroutineHost)
    {
        var generatedField = GetField(helpSystemType, "_generatedHelpCategories");
        if (generatedField?.GetValue(helpSystem) is not IList generated || generated.Count == 0)
            return;

        var loadCategories = helpSystemType.GetMethods(InstanceFlags)
            .FirstOrDefault(method =>
                string.Equals(method.Name, "LoadCategories", StringComparison.Ordinal) &&
                method.GetParameters().Length == 0);
        if (loadCategories == null)
            return;

        foreach (var generatedEntry in generated.Cast<object?>().ToArray())
        {
            if (generatedEntry is Component component)
                UnityEngine.Object.Destroy(component.gameObject);
            else if (generatedEntry is UnityEngine.Object unityObject)
                UnityEngine.Object.Destroy(unityObject);
        }

        generated.Clear();

        var returnValue = loadCategories.Invoke(helpSystem, null);
        if (returnValue is IEnumerator enumerator)
            coroutineHost.StartCoroutine(enumerator);
    }

    private static Type? GetListElementType(Type listType)
    {
        if (listType.IsGenericType)
            return listType.GetGenericArguments().FirstOrDefault();

        return listType.GetInterfaces()
            .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>))
            ?.GetGenericArguments()
            .FirstOrDefault();
    }

    private static FieldInfo? GetField(Type type, string name)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            var field = current.GetField(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }

        return null;
    }
}

#if GUN_STORE_HELP_UI_DEBUG
internal static class GunStoreHelpDebugLogger
{
    private const string PreferredLogDirectory =
        @"E:\Coding\Big Ambitions\mods\BigAmbitionsModdingSDK\Logs\Mods";

    private static readonly object Sync = new object();
    private static readonly string ResolvedLogDirectory = ResolveLogDirectory();

    public static string LogPath => Path.Combine(ResolvedLogDirectory, "GunStore-help-ui-debug.log");

    public static void StartSession()
    {
        Append($"===== Gun Store Help UI debug session started {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} =====");
        Append("Open the native Help System and press F9 to capture its hierarchy and navigation data.");
    }

    public static void AppendSnapshot(StringBuilder snapshot)
    {
        if (snapshot == null)
            throw new ArgumentNullException(nameof(snapshot));

        Append(snapshot.ToString());
    }

    public static void Error(string message, Exception exception)
    {
        Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] ERROR {message}{Environment.NewLine}{exception}");
    }

    private static void Append(string message)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath) ?? ResolvedLogDirectory);
            File.AppendAllText(LogPath, message + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static string ResolveLogDirectory()
    {
        try
        {
            Directory.CreateDirectory(PreferredLogDirectory);
            return PreferredLogDirectory;
        }
        catch
        {
            var fallback = Path.Combine(Path.GetTempPath(), "GunStore", "Logs");
            Directory.CreateDirectory(fallback);
            return fallback;
        }
    }
}

internal readonly struct GunStoreHelpDebugSnapshotResult
{
    public GunStoreHelpDebugSnapshotResult(string logPath, int helpComponentCount, int rootCount, int elementCount)
    {
        LogPath = logPath;
        HelpComponentCount = helpComponentCount;
        RootCount = rootCount;
        ElementCount = elementCount;
    }

    public string LogPath { get; }
    public int HelpComponentCount { get; }
    public int RootCount { get; }
    public int ElementCount { get; }
}

internal static class GunStoreHelpDebugSnapshotWriter
{
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private const int MaximumHierarchyElements = 2000;
    private const int MaximumCollectionEntries = 150;
    private const int MaximumObjectDepth = 6;

    public static GunStoreHelpDebugSnapshotResult WriteSnapshot()
    {
        var builder = new StringBuilder(128 * 1024);
        var helpComponents = FindHelpComponents();
        var roots = FindHelpUiRoots(helpComponents);
        var elementCount = 0;

        builder.AppendLine("============================================================");
        builder.AppendLine($"Gun Store Help UI snapshot: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        builder.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");
        builder.AppendLine($"Screen: {Screen.width}x{Screen.height}");
        AppendEventSystem(builder);
        builder.AppendLine($"Help-like components: {helpComponents.Count}");

        foreach (var component in helpComponents)
        {
            builder.AppendLine();
            builder.AppendLine(
                $"HELP COMPONENT type={component.GetType().AssemblyQualifiedName} " +
                $"path={GetHierarchyPath(component.transform)} active={component.gameObject.activeInHierarchy} " +
                $"enabled={component.enabled}");
            AppendRootObjectMembers(builder, component);
        }

        builder.AppendLine();
        AppendHelpRuntimeTypeSummary(builder);

        builder.AppendLine();
        builder.AppendLine($"HELP UI ROOTS: {roots.Count}");
        foreach (var root in roots)
        {
            builder.AppendLine();
            builder.AppendLine($"ROOT {GetHierarchyPath(root)}");
            AppendHierarchy(root, builder, 0, ref elementCount);
        }

        builder.AppendLine();
        builder.AppendLine(
            $"END snapshot: helpComponents={helpComponents.Count}, roots={roots.Count}, elements={elementCount}");
        GunStoreHelpDebugLogger.AppendSnapshot(builder);
        return new GunStoreHelpDebugSnapshotResult(
            GunStoreHelpDebugLogger.LogPath,
            helpComponents.Count,
            roots.Count,
            elementCount);
    }

    private static List<MonoBehaviour> FindHelpComponents()
    {
        return Resources.FindObjectsOfTypeAll<MonoBehaviour>()
            .Where(component => component != null && component.gameObject.scene.IsValid())
            .Where(component =>
            {
                var typeName = component.GetType().FullName ?? component.GetType().Name;
                return typeName.IndexOf("HelpSystem", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       typeName.IndexOf("HelpStructure", StringComparison.OrdinalIgnoreCase) >= 0;
            })
            .OrderBy(component => GetHierarchyPath(component.transform), StringComparer.Ordinal)
            .ToList();
    }

    private static List<Transform> FindHelpUiRoots(IReadOnlyList<MonoBehaviour> helpComponents)
    {
        var roots = new Dictionary<int, Transform>();

        foreach (var component in helpComponents)
            AddUiRoot(roots, component.transform);

        foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
        {
            if (transform == null || !transform.gameObject.scene.IsValid())
                continue;

            if (transform.name.IndexOf("Help", StringComparison.OrdinalIgnoreCase) >= 0)
                AddUiRoot(roots, transform);
        }

        foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
        {
            if (behaviour == null || !behaviour.gameObject.scene.IsValid())
                continue;

            var text = TryReadText(behaviour);
            if (text == null)
                continue;

            if (text.IndexOf("Help System", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("Gun Store", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                AddUiRoot(roots, behaviour.transform);
            }
        }

        return roots.Values
            .OrderBy(GetHierarchyPath, StringComparer.Ordinal)
            .ToList();
    }

    private static void AddUiRoot(IDictionary<int, Transform> roots, Transform candidate)
    {
        var canvas = candidate.GetComponentInParent<Canvas>(true);
        var root = canvas != null ? canvas.rootCanvas.transform : candidate.root;
        roots[root.GetInstanceID()] = root;
    }

    private static void AppendEventSystem(StringBuilder builder)
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            builder.AppendLine("EventSystem: <none>");
            return;
        }

        var selected = eventSystem.currentSelectedGameObject;
        builder.AppendLine(
            $"EventSystem: {GetHierarchyPath(eventSystem.transform)}, " +
            $"selected={(selected == null ? "<none>" : GetHierarchyPath(selected.transform))}");
    }

    private static void AppendRootObjectMembers(StringBuilder builder, object root)
    {
        var visited = new HashSet<object>(ReferenceComparer.Instance);
        AppendObjectMembers(builder, root, 1, visited);
    }

    private static void AppendObjectMembers(
        StringBuilder builder,
        object value,
        int depth,
        ISet<object> visited)
    {
        if (depth > MaximumObjectDepth || !visited.Add(value))
            return;

        var type = value.GetType();
        foreach (var field in GetAllFields(type).OrderBy(field => field.Name, StringComparer.Ordinal))
        {
            object? fieldValue;
            try
            {
                fieldValue = field.GetValue(value);
            }
            catch (Exception exception)
            {
                AppendIndent(builder, depth);
                builder.AppendLine($"FIELD {field.Name}: <error {exception.GetType().Name}: {exception.Message}>");
                continue;
            }

            AppendValue(builder, $"FIELD {field.DeclaringType?.Name}.{field.Name}", fieldValue, depth, visited);
        }

        foreach (var property in type.GetProperties(InstanceFlags)
                     .Where(property => property.GetIndexParameters().Length == 0)
                     .Where(property => IsSimpleType(property.PropertyType))
                     .OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            try
            {
                AppendValue(builder, $"PROPERTY {property.Name}", property.GetValue(value), depth, visited);
            }
            catch (Exception exception)
            {
                AppendIndent(builder, depth);
                builder.AppendLine($"PROPERTY {property.Name}: <error {exception.GetType().Name}: {exception.Message}>");
            }
        }
    }

    private static void AppendValue(
        StringBuilder builder,
        string label,
        object? value,
        int depth,
        ISet<object> visited)
    {
        AppendIndent(builder, depth);
        if (value == null)
        {
            builder.AppendLine($"{label}: <null>");
            return;
        }

        var type = value.GetType();
        if (IsSimpleType(type))
        {
            builder.AppendLine($"{label}: {FormatSimpleValue(value)}");
            return;
        }

        if (value is UnityEngine.Object unityObject)
        {
            builder.AppendLine(
                $"{label}: unityObject type={type.FullName} name={unityObject.name} id={unityObject.GetInstanceID()}");
            return;
        }

        if (value is IDictionary dictionary)
        {
            builder.AppendLine($"{label}: dictionary type={type.FullName} count={dictionary.Count}");
            var index = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (index >= MaximumCollectionEntries)
                {
                    AppendIndent(builder, depth + 1);
                    builder.AppendLine("<collection truncated>");
                    break;
                }

                AppendValue(builder, $"KEY[{index}]", entry.Key, depth + 1, visited);
                AppendValue(builder, $"VALUE[{index}]", entry.Value, depth + 1, visited);
                index++;
            }

            return;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            builder.AppendLine($"{label}: collection type={type.FullName}");
            var index = 0;
            foreach (var entry in enumerable)
            {
                if (index >= MaximumCollectionEntries)
                {
                    AppendIndent(builder, depth + 1);
                    builder.AppendLine("<collection truncated>");
                    break;
                }

                AppendValue(builder, $"[{index}]", entry, depth + 1, visited);
                index++;
            }

            if (index == 0)
            {
                AppendIndent(builder, depth + 1);
                builder.AppendLine("<empty>");
            }

            return;
        }

        builder.AppendLine($"{label}: object type={type.AssemblyQualifiedName}");
        if (depth < MaximumObjectDepth && ShouldExpand(type))
            AppendObjectMembers(builder, value, depth + 1, visited);
    }

    private static void AppendHelpRuntimeTypeSummary(StringBuilder builder)
    {
        builder.AppendLine("HELP RUNTIME TYPE SUMMARY");
        foreach (var type in GetLoadableTypes()
                     .Where(type =>
                     {
                         var name = type.FullName ?? type.Name;
                         return name.Equals("HelpSystem", StringComparison.Ordinal) ||
                                name.IndexOf("HelpStructure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("HelpCategoryEntry", StringComparison.OrdinalIgnoreCase) >= 0;
                     })
                     .OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            builder.AppendLine($"TYPE {type.AssemblyQualifiedName}");
            foreach (var field in type.GetFields(StaticFlags | InstanceFlags)
                         .OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                builder.AppendLine(
                    $"  {(field.IsStatic ? "STATIC" : "INSTANCE")} FIELD " +
                    $"{field.FieldType.FullName} {field.Name}");
                if (!field.IsStatic)
                    continue;

                try
                {
                    var value = field.GetValue(null);
                    builder.AppendLine($"    value={FormatSummaryValue(value)}");
                }
                catch (Exception exception)
                {
                    builder.AppendLine($"    value=<error {exception.GetType().Name}: {exception.Message}>");
                }
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type != null).Cast<Type>().ToArray();
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
                yield return type;
        }
    }

    private static IEnumerable<FieldInfo> GetAllFields(Type type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetFields(InstanceFlags | BindingFlags.DeclaredOnly))
                yield return field;
        }
    }

    private static void AppendHierarchy(Transform transform, StringBuilder builder, int depth, ref int elementCount)
    {
        if (elementCount >= MaximumHierarchyElements)
            return;

        elementCount++;
        AppendIndent(builder, depth);
        builder.Append("- ");
        builder.Append(transform.name);
        builder.Append($" activeSelf={transform.gameObject.activeSelf} activeHierarchy={transform.gameObject.activeInHierarchy}");
        builder.Append(" components=[");
        builder.Append(string.Join(", ", transform.gameObject.GetComponents<Component>()
            .Where(component => component != null)
            .Select(component => component.GetType().FullName ?? component.GetType().Name)
            .ToArray()));
        builder.Append(']');

        if (transform is RectTransform rectTransform)
        {
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            var canvas = rectTransform.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var minimum = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            var maximum = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            builder.Append($" rect=({minimum.x:0.#},{minimum.y:0.#})-({maximum.x:0.#},{maximum.y:0.#})");
        }

        var text = TryGetText(transform.gameObject);
        if (!string.IsNullOrWhiteSpace(text))
            builder.Append($" text=\"{Escape(text!)}\"");

        builder.AppendLine();
        for (var index = 0; index < transform.childCount; index++)
            AppendHierarchy(transform.GetChild(index), builder, depth + 1, ref elementCount);
    }

    private static string? TryGetText(GameObject gameObject)
    {
        foreach (var component in gameObject.GetComponents<Component>())
        {
            var text = TryReadText(component);
            if (!string.IsNullOrWhiteSpace(text))
                return text!.Trim();
        }

        return null;
    }

    private static string? TryReadText(Component component)
    {
        var type = component.GetType();
        if (type.Name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) < 0 &&
            type.Name.IndexOf("InputField", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return null;
        }

        try
        {
            return type.GetProperty("text", InstanceFlags)?.GetValue(component) as string;
        }
        catch
        {
            return null;
        }
    }

    private static bool ShouldExpand(Type type)
    {
        var name = type.FullName ?? type.Name;
        return name.IndexOf("Help", StringComparison.OrdinalIgnoreCase) >= 0 ||
               (type.Namespace != null &&
                !type.Namespace.StartsWith("System", StringComparison.Ordinal) &&
                !type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal));
    }

    private static bool IsSimpleType(Type type)
    {
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) ||
               type == typeof(DateTime) || type == typeof(Guid) || type == typeof(Type);
    }

    private static string FormatSimpleValue(object value)
    {
        return value is string text ? $"\"{Escape(text)}\"" : value.ToString() ?? "<null>";
    }

    private static string FormatSummaryValue(object? value)
    {
        if (value == null)
            return "<null>";
        if (IsSimpleType(value.GetType()))
            return FormatSimpleValue(value);
        if (value is ICollection collection)
            return $"{value.GetType().FullName} count={collection.Count}";
        return value.GetType().FullName ?? value.GetType().Name;
    }

    private static void AppendIndent(StringBuilder builder, int depth)
    {
        builder.Append(' ', depth * 2);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var names = new List<string>();
        var current = transform;
        while (current != null)
        {
            names.Add(current.name);
            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names.ToArray());
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\"", "\\\"");
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceComparer Instance = new ReferenceComparer();

        public new bool Equals(object? left, object? right)
        {
            return ReferenceEquals(left, right);
        }

        public int GetHashCode(object value)
        {
            return RuntimeHelpers.GetHashCode(value);
        }
    }
}
#endif
