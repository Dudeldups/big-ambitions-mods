#nullable enable
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using UnityEngine;

public static class GunStoreRecipeFactory
{
    public static ScriptableObject? CreateAk47Recipe()
    {
        var recipeType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("BigAmbitions.Factories.Recipes.Recipe", false))
            .FirstOrDefault(type => type != null);
        if (recipeType == null || !typeof(ScriptableObject).IsAssignableFrom(recipeType))
            return null;

        var recipeAsset = ScriptableObject.CreateInstance(recipeType);
        recipeAsset.name = "Ak47Recipe";

        SetFieldValue(recipeType, recipeAsset, "id", "sSoU0AdCKUWnH+qY0k+K+A==");

        var recipeItemType = recipeType.Assembly.GetType("BigAmbitions.Factories.Recipes.RecipeItem");
        if (recipeItemType == null)
            return recipeAsset;

        SetCollectionField(recipeType, recipeAsset, "ingredients", recipeItemType, new[]
        {
            CreateRecipeItem(recipeItemType, "ba:itemname_plastic", 20),
            CreateRecipeItem(recipeItemType, "gunstore-businesstype:itemname_gunpartscheap", 40),
            CreateRecipeItem(recipeItemType, "gunstore-businesstype:itemname_gunpartsexpensive", 20)
        });

        SetFieldValue(recipeType, recipeAsset, "output",
            CreateRecipeItem(recipeItemType, "gunstore-businesstype:itemname_ak47", 20));

        var machineVisualsField = recipeType.GetField("machineVisuals",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var machineVisualType = GetElementType(machineVisualsField?.FieldType);
        if (machineVisualsField != null && machineVisualType != null)
        {
            SetCollectionField(recipeType, recipeAsset, "machineVisuals", machineVisualType, new[]
            {
                CreateMachineVisual(machineVisualType, "ba:itemname_lasercuttingmachine",
                    "gunstore-businesstype:itemname_gunpartscheap",
                    "gunstore-businesstype:itemname_gunpartscheap"),
                CreateMachineVisual(machineVisualType, "ba:itemname_consumergoodsassemblymachine",
                    string.Empty,
                    "gunstore-businesstype:itemname_ak47")
            });
        }

        return recipeAsset;
    }

    private static object CreateRecipeItem(Type recipeItemType, string itemName, int amount)
    {
        var recipeItem = Activator.CreateInstance(recipeItemType);
        if (recipeItem == null)
            throw new InvalidOperationException($"Could not create {recipeItemType.FullName}.");

        SetFieldValue(recipeItemType, recipeItem, "item", itemName);
        SetFieldValue(recipeItemType, recipeItem, "amount", amount);

        return recipeItem;
    }

    private static object CreateMachineVisual(Type machineVisualType, string machineName, string inputItemName,
        string outputItemName)
    {
        var machineVisual = Activator.CreateInstance(machineVisualType);
        if (machineVisual == null)
            throw new InvalidOperationException($"Could not create {machineVisualType.FullName}.");

        SetFieldValue(machineVisualType, machineVisual, "machineName", machineName);
        SetFieldValue(machineVisualType, machineVisual, "inputItemName", inputItemName);
        SetFieldValue(machineVisualType, machineVisual, "outputItemName", outputItemName);
        SetFieldValue(machineVisualType, machineVisual, "shaderColorA", Color.clear);
        SetFieldValue(machineVisualType, machineVisual, "shaderColorB", Color.clear);

        return machineVisual;
    }

    private static void SetCollectionField(Type ownerType, object owner, string fieldName, Type elementType,
        object[] values)
    {
        var field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            return;

        if (field.FieldType.IsArray)
        {
            var array = Array.CreateInstance(elementType, values.Length);
            for (var i = 0; i < values.Length; i++)
                array.SetValue(values[i], i);

            field.SetValue(owner, array);
            return;
        }

        var list = Activator.CreateInstance(field.FieldType) as IList;
        if (list == null)
            return;

        foreach (var value in values)
            list.Add(value);

        field.SetValue(owner, list);
    }

    private static Type? GetElementType(Type? collectionType)
    {
        if (collectionType == null)
            return null;

        if (collectionType.IsArray)
            return collectionType.GetElementType();

        return collectionType.IsGenericType ? collectionType.GetGenericArguments().FirstOrDefault() : null;
    }

    private static void SetFieldValue(Type ownerType, object owner, string fieldName, object? value)
    {
        var field = ownerType.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null)
            field.SetValue(owner, value);
    }
}
