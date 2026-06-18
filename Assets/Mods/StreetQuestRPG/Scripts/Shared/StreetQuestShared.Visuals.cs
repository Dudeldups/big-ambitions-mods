using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BAModAPI;
using BigAmbitions.Characters;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem.Legacy;
using Buildings;
using Dialogs;
using Entities;
using Helpers;
using Localizor;
using Player.HUD.ItemInfoOverlays;
using UI.Notification;
using UnityEngine;
using UnityEngine.Rendering;

namespace StreetQuestRPG
{
    internal static partial class StreetQuestShared
    {
        private static void BuildQuestGiverVisual(Transform parent)
        {
            var countertop = CreateVisualBlock(
                parent,
                "Countertop",
                new Vector3(0f, 0.9f, 0f),
                new Vector3(1.7f, 0.16f, 0.7f),
                new Color(0.33f, 0.24f, 0.16f));
            AddOutlineAccent(countertop.transform, new Vector3(0f, -0.09f, 0f), new Vector3(1.78f, 0.03f, 0.78f));

            CreateVisualBlock(
                parent,
                "CrateBase",
                new Vector3(0f, 0.38f, 0f),
                new Vector3(1.55f, 0.72f, 0.62f),
                new Color(0.18f, 0.16f, 0.14f));

            CreateVisualBlock(
                parent,
                "SignPostLeft",
                new Vector3(-0.58f, 1.4f, -0.18f),
                new Vector3(0.08f, 1f, 0.08f),
                new Color(0.22f, 0.18f, 0.12f));
            CreateVisualBlock(
                parent,
                "SignPostRight",
                new Vector3(0.58f, 1.4f, -0.18f),
                new Vector3(0.08f, 1f, 0.08f),
                new Color(0.22f, 0.18f, 0.12f));
            CreateVisualBlock(
                parent,
                "SignBoard",
                new Vector3(0f, 1.75f, -0.18f),
                new Vector3(1.28f, 0.5f, 0.08f),
                new Color(0.75f, 0.69f, 0.52f));

            var label = new GameObject("QuestGiverLabel");
            label.transform.SetParent(parent, false);
            label.transform.localPosition = new Vector3(0f, 1.75f, -0.24f);
            var textMesh = label.AddComponent<TextMesh>();
            textMesh.text = "MACK";
            textMesh.fontSize = 72;
            textMesh.characterSize = 0.06f;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.color = new Color(0.1f, 0.08f, 0.05f);
        }


        private static bool TryAttachQuestGiverVisual(Transform parent, out GameObject visualRoot)
        {
            visualRoot = null;
            try
            {
                foreach (var prefabName in QuestGiverVisualPrefabNames)
                {
                    visualRoot = PrefabHelper.CreatePrefab(prefabName, parent);
                    if (visualRoot != null)
                        break;
                }

                if (visualRoot == null)
                    return false;

                visualRoot.name = "MackVisual";
                visualRoot.transform.SetParent(parent, false);
                visualRoot.transform.localPosition = QuestGiverVisualLocalPosition;
                visualRoot.transform.localRotation = Quaternion.Euler(QuestGiverVisualLocalEulerAngles);
                visualRoot.transform.localScale = Vector3.one;

                foreach (var collider in visualRoot.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;

                foreach (var rigidbody in visualRoot.GetComponentsInChildren<Rigidbody>(true))
                    rigidbody.isKinematic = true;

                InitializeQuestGiverVisual(visualRoot);

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to attach quest giver visual prefab. {exception}");
                visualRoot = null;
                return false;
            }
        }


        private static void InitializeQuestGiverVisual(GameObject root)
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
                    ApplyFixedQuestGiverAppearance(appearanceSetter);

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
                Debug.LogWarning($"StreetQuestRPG: Failed to initialize quest giver visual. {exception}");
            }
        }


        private static void ApplyFixedQuestGiverAppearance(Component appearanceSetter)
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
                    setRandomAppearanceMethod.Invoke(
                        appearanceSetter,
                        new object[] { QuestGiverVisualGender, QuestGiverVisualAgeInDays, QuestGiverVisualSeed });
                    return;
                }

                InvokeParameterlessMethod(appearanceSetter, "SetAppearance");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"StreetQuestRPG: Failed to apply fixed quest giver appearance. {exception}");
                InvokeParameterlessMethod(appearanceSetter, "SetAppearance");
            }
        }


        private static void RemoveUnexpectedQuestGiverChildren(Transform root, ISet<Transform> allowedChildren)
        {
            if (root == null || allowedChildren == null)
                return;

            foreach (Transform child in root)
            {
                if (child == null || allowedChildren.Contains(child))
                    continue;

                UnityEngine.Object.Destroy(child.gameObject);
            }
        }


        private static void AddOutlineAccent(Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            CreateVisualBlock(
                parent,
                "CounterAccent",
                localPosition,
                localScale,
                new Color(0.88f, 0.76f, 0.34f));
        }


        private static Renderer CreateInteractionRendererProxy(Transform parent)
        {
            var proxy = GameObject.CreatePrimitive(PrimitiveType.Cube);
            proxy.name = "InteractionRendererProxy";
            proxy.transform.SetParent(parent, false);
            proxy.transform.localPosition = InteractionRendererLocalPosition;
            proxy.transform.localScale = InteractionRendererLocalScale;

            var collider = proxy.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.Destroy(collider);

            var renderer = proxy.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

                var material = CreateRuntimeMaterial(new Color(0f, 0f, 0f, 0f));
                if (material != null)
                {
                    material.SetFloat("_Mode", 3f);
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.DisableKeyword("_ALPHATEST_ON");
                    material.EnableKeyword("_ALPHABLEND_ON");
                    material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    material.renderQueue = 3000;
                    renderer.sharedMaterial = material;
                }
            }

            return renderer;
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
    }
}
