using System;
using System.Reflection;
using BigAmbitions.Characters;
using Helpers;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetQuestRPG
{
    internal static class StreetQuestCharacterCreator
    {
        private static readonly BindingFlags ReflectionFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static GameObject CreateHost(StreetQuestCharacterDefinition definition, Transform parent = null)
        {
            if (definition == null)
                return null;

            definition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(definition);
            if (definition == null)
                return null;

            var root = new GameObject(string.IsNullOrWhiteSpace(definition.gameObjectName)
                ? $"StreetQuestRPG.Character.{definition.id}"
                : definition.gameObjectName);

            if (parent != null)
                root.transform.SetParent(parent, false);

            root.transform.position = definition.PositionOr(Vector3.zero);
            var forward = FlattenDirection(definition.ForwardOr(Vector3.forward));
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            root.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);
            return root;
        }

        public static bool TryAttachPrefabVisual(
            Transform parent,
            StreetQuestCharacterDefinition definition,
            out GameObject visualRoot)
        {
            visualRoot = null;
            if (parent == null)
                return false;

            if (definition == null)
                return false;

            definition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(definition);
            if (definition == null || !definition.HasPrefabName)
                return false;

            try
            {
                visualRoot = PrefabHelper.CreatePrefab(definition.prefabName, parent);

                if (visualRoot == null)
                    return false;

                visualRoot.name = string.IsNullOrWhiteSpace(definition.visualObjectName)
                    ? $"{definition.displayName ?? definition.id}Visual"
                    : definition.visualObjectName;
                visualRoot.transform.SetParent(parent, false);
                visualRoot.SetActive(true);
                visualRoot.transform.localPosition = definition.LocalPositionOr(Vector3.zero);
                visualRoot.transform.localRotation = Quaternion.Euler(definition.LocalEulerAnglesOr(Vector3.zero));
                visualRoot.transform.localScale = definition.LocalScaleOr(Vector3.one);

                DisablePhysics(visualRoot);
                EnableVisualRenderers(visualRoot);
                InitializeHumanoidVisual(visualRoot, definition);
                StripHiddenChildObjects(visualRoot, definition);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to attach character visual '{definition.id}'. {exception}");
                visualRoot = null;
                return false;
            }
        }

        public static Renderer CreateInvisibleInteractionRendererProxy(
            Transform parent,
            StreetQuestCharacterDefinition definition)
        {
            if (parent == null)
                return null;

            if (definition == null)
                return null;

            definition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(definition);
            if (definition == null)
                return null;

            var proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            proxy.name = "InteractionRendererProxy";
            proxy.transform.SetParent(parent, false);
            proxy.transform.localPosition = definition.InteractionRendererLocalPositionOr(new Vector3(0f, 0.9f, 0f));
            proxy.transform.localScale = definition.InteractionRendererLocalScaleOr(new Vector3(0.08f, 0.08f, 0.08f));

            var collider = proxy.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);

            var renderer = proxy.GetComponent<Renderer>();
            if (renderer == null)
                return null;

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            var material = CreateRuntimeMaterial(new Color(0f, 0f, 0f, 0f));
            if (material == null)
                return renderer;

            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
            renderer.sharedMaterial = material;
            return renderer;
        }

        public static void BuildFallbackStandVisual(Transform parent, StreetQuestCharacterDefinition definition)
        {
            if (parent == null)
                return;

            if (definition == null)
                return;

            definition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(definition);
            if (definition == null)
                return;

            var countertop = CreateVisualBlock(
                parent,
                "Countertop",
                new Vector3(0f, 0.9f, 0f),
                new Vector3(1.7f, 0.16f, 0.7f),
                new Color(0.33f, 0.24f, 0.16f));
            CreateVisualBlock(countertop.transform, "CounterAccent", new Vector3(0f, -0.09f, 0f), new Vector3(1.78f, 0.03f, 0.78f), new Color(0.88f, 0.76f, 0.34f));

            CreateVisualBlock(parent, "CrateBase", new Vector3(0f, 0.38f, 0f), new Vector3(1.55f, 0.72f, 0.62f), new Color(0.18f, 0.16f, 0.14f));
            CreateVisualBlock(parent, "SignPostLeft", new Vector3(-0.58f, 1.4f, -0.18f), new Vector3(0.08f, 1f, 0.08f), new Color(0.22f, 0.18f, 0.12f));
            CreateVisualBlock(parent, "SignPostRight", new Vector3(0.58f, 1.4f, -0.18f), new Vector3(0.08f, 1f, 0.08f), new Color(0.22f, 0.18f, 0.12f));
            CreateVisualBlock(parent, "SignBoard", new Vector3(0f, 1.75f, -0.18f), new Vector3(1.28f, 0.5f, 0.08f), new Color(0.75f, 0.69f, 0.52f));

            var label = new GameObject("QuestGiverLabel");
            label.transform.SetParent(parent, false);
            label.transform.localPosition = new Vector3(0f, 1.75f, -0.24f);
            var textMesh = label.AddComponent<TextMesh>();
            textMesh.text = (definition.displayName ?? definition.id ?? "NPC").ToUpperInvariant();
            textMesh.fontSize = 72;
            textMesh.characterSize = 0.06f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.1f, 0.08f, 0.05f);
        }

        public static BoxCollider AddInteractionCollider(
            GameObject root,
            StreetQuestCharacterDefinition definition,
            bool visualPrefabAttached)
        {
            if (root == null)
                return null;

            if (definition == null)
                return null;

            definition = StreetQuestCharacterRuntimeResolver.ResolveRuntimeDefinition(definition);
            if (definition == null)
                return null;

            var collider = root.AddComponent<BoxCollider>();
            collider.center = visualPrefabAttached
                ? definition.ColliderCenterWithPrefabOr(new Vector3(0f, 1.05f, -0.05f))
                : definition.ColliderCenterFallbackOr(new Vector3(0f, 0.95f, 0f));
            collider.size = visualPrefabAttached
                ? definition.ColliderSizeWithPrefabOr(new Vector3(1.3f, 2.1f, 0.55f))
                : definition.ColliderSizeFallbackOr(new Vector3(1.8f, 1.9f, 1.2f));
            return collider;
        }

        private static void DisablePhysics(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;

            foreach (var rigidbody in root.GetComponentsInChildren<Rigidbody>(true))
                rigidbody.isKinematic = true;
        }

        private static void EnableVisualRenderers(GameObject root)
        {
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;

                renderer.enabled = true;
                var rendererGameObject = renderer.gameObject;
                if (rendererGameObject != null && !rendererGameObject.activeSelf)
                    rendererGameObject.SetActive(true);
            }
        }

        private static void InitializeHumanoidVisual(GameObject root, StreetQuestCharacterDefinition definition)
        {
            if (root == null)
                return;

            try
            {
                var homelessType = FindType("Entities.Homeless") ?? FindType("Homeless");
                var homeless = homelessType != null ? root.GetComponent(homelessType) : null;
                if (homeless != null)
                {
                    InvokeParameterlessMethod(homeless, "Init");
                    InvokeParameterlessMethod(homeless, "Enable");
                }

                var appearanceSetterType = FindType("AppearanceSetter");
                var appearanceSetter = appearanceSetterType != null
                    ? root.GetComponent(appearanceSetterType)
                    : null;
                if (appearanceSetter != null)
                    ApplyConfiguredAppearance(appearanceSetter, definition);

                var baseHumanType = FindType("BaseHuman");
                var baseHuman = baseHumanType != null ? root.GetComponent(baseHumanType) : null;
                if (baseHuman != null)
                    InvokeParameterlessMethod(baseHuman, "ResetAnimator");

                foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                {
                    if (animator == null)
                        continue;

                    animator.enabled = true;
                    animator.Rebind();
                    animator.Update(0f);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to initialize character visual '{definition?.id}'. {exception}");
            }
        }

        private static void StripHiddenChildObjects(GameObject root, StreetQuestCharacterDefinition definition)
        {
            if (root == null || definition?.hiddenChildObjectNames == null || definition.hiddenChildObjectNames.Length == 0)
                return;

            var transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (var hiddenName in definition.hiddenChildObjectNames)
            {
                if (string.IsNullOrWhiteSpace(hiddenName))
                    continue;

                foreach (var transform in transforms)
                {
                    if (transform == null || transform == root.transform)
                        continue;

                    if (!string.Equals(transform.name, hiddenName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    UnityEngine.Object.Destroy(transform.gameObject);
                }
            }
        }

        private static void ApplyConfiguredAppearance(Component appearanceSetter, StreetQuestCharacterDefinition definition)
        {
            if (appearanceSetter == null)
                return;

            try
            {
                var setterType = appearanceSetter.GetType();
                var setRandomAppearanceMethod = setterType.GetMethod(
                    "SetRandomAppearance",
                    ReflectionFlags,
                    null,
                    new[]
                    {
                        typeof(Gender),
                        typeof(int),
                        typeof(int)
                    },
                    null);

                if (setRandomAppearanceMethod != null)
                {
                    var gender = ParseGender(definition != null ? definition.gender : null);
                    var ageInDays = definition != null && definition.ageInDays > 0
                        ? definition.ageInDays
                        : 42 * 365;
                    var appearanceSeed = definition != null && definition.appearanceSeed != 0
                        ? definition.appearanceSeed
                        : 104729;

                    setRandomAppearanceMethod.Invoke(
                        appearanceSetter,
                        new object[]
                        {
                            gender,
                            ageInDays,
                            appearanceSeed
                        });
                    return;
                }

                InvokeParameterlessMethod(appearanceSetter, "SetAppearance");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to apply configured character appearance. {exception}");
                InvokeParameterlessMethod(appearanceSetter, "SetAppearance");
            }
        }

        private static Gender ParseGender(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, true, out Gender gender))
                return gender;

            return Gender.Male;
        }

        private static GameObject CreateVisualBlock(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.transform.SetParent(parent, false);
            block.transform.localPosition = localPosition;
            block.transform.localScale = localScale;

            var collider = block.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);

            var renderer = block.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = CreateRuntimeMaterial(color);
                if (material != null)
                    renderer.sharedMaterial = material;
            }

            return block;
        }

        private static Material CreateRuntimeMaterial(Color color)
        {
            var shader = Shader.Find("Standard")
                         ?? Shader.Find("HDRP/Lit")
                         ?? Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null)
                return null;

            var material = new Material(shader);
            if (material.HasProperty("_Color"))
                material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);

            return material;
        }

        private static Vector3 FlattenDirection(Vector3 direction)
        {
            direction.y = 0f;
            return direction.normalized;
        }

        private static Type FindType(string typeName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var exactType = assembly.GetType(typeName, throwOnError: false);
                if (exactType != null)
                    return exactType;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException exception)
                {
                    types = exception.Types;
                }

                if (types == null)
                    continue;

                foreach (var type in types)
                {
                    if (type == null)
                        continue;

                    if (string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                        string.Equals(type.Name, typeName, StringComparison.Ordinal) ||
                        (type.FullName?.EndsWith("." + typeName, StringComparison.Ordinal) ?? false))
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private static void InvokeParameterlessMethod(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrEmpty(methodName))
                return;

            for (var instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                var method = instanceType.GetMethod(methodName, ReflectionFlags, null, Type.EmptyTypes, null);
                if (method == null)
                    continue;

                method.Invoke(instance, null);
                return;
            }
        }
    }
}
