#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BAModAPI;
using Vehicles.VehicleTypes;

internal static class BugattiChironAchievementLoadGuard
{
    private static readonly MethodInfo? TargetMethod = typeof(GameManager).GetMethod(
        "ForceUpdateAchievementsOnSteam",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? ReplacementMethod =
        typeof(BugattiChironAchievementLoadGuard).GetMethod(
            nameof(ForceUpdateAchievementsWhenVehicleTypesAreReady),
            BindingFlags.Static | BindingFlags.NonPublic);

    private static BugattiChironMethodDetour? detour;
    private static ModContext? context;
    private static VehicleType? vehicleType;
    private static GameManager? pendingGameManager;
    private static string? installError;
    private static bool deferredLogged;
    private static bool resumedLogged;

    internal static void Install()
    {
        if (detour != null || TargetMethod == null || ReplacementMethod == null)
            return;

        detour = new BugattiChironMethodDetour(TargetMethod, ReplacementMethod);
        if (!detour.Apply(out installError))
            detour = null;
    }

    internal static void Configure(ModContext modContext, VehicleType registeredVehicleType)
    {
        context = modContext;
        vehicleType = registeredVehicleType;
        Install();

        if (!string.IsNullOrEmpty(installError))
        {
            context.Logger.Warn(
                "BugattiChiron: save-load achievement guard could not be installed: " + installError);
            installError = null;
        }

        if (pendingGameManager != null)
            LogDeferredRefresh();
    }

    internal static void ResumeIfReady()
    {
        if (pendingGameManager == null || !IsVehicleTypeReady())
            return;

        var gameManager = pendingGameManager;
        pendingGameManager = null;
        if (!resumedLogged)
        {
            context?.Logger.Info(
                "BugattiChiron: resumed the deferred Steam achievement refresh after vehicle registration.");
            resumedLogged = true;
        }

        InvokeOriginal(gameManager);
    }

    internal static void Shutdown()
    {
        if (detour != null)
            detour.Restore(out _);

        detour = null;
        context = null;
        vehicleType = null;
        pendingGameManager = null;
        installError = null;
        deferredLogged = false;
        resumedLogged = false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ForceUpdateAchievementsWhenVehicleTypesAreReady(GameManager gameManager)
    {
        if (!HasUnresolvedSavedBugatti())
        {
            InvokeOriginal(gameManager);
            return;
        }

        if (vehicleType != null &&
            BugattiChironVehicleTypeRegistration.EnsureRegistered(vehicleType) &&
            IsVehicleTypeReady())
        {
            InvokeOriginal(gameManager);
            return;
        }

        pendingGameManager = gameManager;
        LogDeferredRefresh();
    }

    private static bool HasUnresolvedSavedBugatti()
    {
        if (IsVehicleTypeReady())
            return false;

        var save = SaveGameManager.Current;
        return ContainsBugatti(save?.VehicleInstances) ||
               ContainsBugatti(save?.privateDriverVehicleInstances);
    }

    private static void LogDeferredRefresh()
    {
        if (deferredLogged || context == null)
            return;

        context.Logger.Info(
            "BugattiChiron: deferred the Steam achievement refresh until the saved Bugatti vehicle type is registered.");
        deferredLogged = true;
    }

    private static bool IsVehicleTypeReady()
    {
        return VehicleTypeHelper.GetVehicleType(BugattiChironMod.VehicleTypeName) != null;
    }

    private static bool ContainsBugatti(IEnumerable<VehicleInstance>? vehicles)
    {
        if (vehicles == null)
            return false;

        foreach (var vehicle in vehicles)
        {
            if (vehicle != null && string.Equals(
                    vehicle.vehicleTypeName,
                    BugattiChironMod.VehicleTypeName,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static void InvokeOriginal(GameManager gameManager)
    {
        if (TargetMethod == null || detour == null)
            return;

        if (!detour.Restore(out var restoreError))
        {
            context?.Logger.Warn(
                "BugattiChiron: could not restore the achievement method before resuming it: " +
                restoreError);
            return;
        }

        try
        {
            TargetMethod.Invoke(gameManager, null);
        }
        catch (TargetInvocationException exception)
        {
            context?.Logger.Error(exception.InnerException ?? exception);
        }
        catch (Exception exception)
        {
            context?.Logger.Error(exception);
        }
        finally
        {
            if (!detour.Apply(out var applyError))
            {
                context?.Logger.Warn(
                    "BugattiChiron: could not restore the save-load achievement guard: " + applyError);
            }
        }
    }
}

internal sealed class BugattiChironMethodDetour
{
    private const uint PageExecuteReadWrite = 0x40;
    private const int JumpSize = 14;

    private readonly MethodInfo target;
    private readonly MethodInfo replacement;
    private byte[]? originalBytes;
    private byte[]? jumpBytes;
    private IntPtr targetAddress;

    internal BugattiChironMethodDetour(MethodInfo target, MethodInfo replacement)
    {
        this.target = target;
        this.replacement = replacement;
    }

    internal bool Apply(out string error)
    {
        error = string.Empty;
        if (jumpBytes != null)
            return true;

        if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8)
        {
            error = "the current runtime does not support the Windows x64 method guard";
            return false;
        }

        try
        {
            RuntimeHelpers.PrepareMethod(target.MethodHandle);
            RuntimeHelpers.PrepareMethod(replacement.MethodHandle);
            targetAddress = target.MethodHandle.GetFunctionPointer();
            originalBytes = new byte[JumpSize];
            Marshal.Copy(targetAddress, originalBytes, 0, originalBytes.Length);
            jumpBytes = CreateAbsoluteJump(replacement.MethodHandle.GetFunctionPointer());
            WriteBytes(targetAddress, jumpBytes);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            originalBytes = null;
            jumpBytes = null;
            return false;
        }
    }

    internal bool Restore(out string error)
    {
        error = string.Empty;
        if (originalBytes == null || jumpBytes == null)
            return true;

        try
        {
            var currentBytes = new byte[JumpSize];
            Marshal.Copy(targetAddress, currentBytes, 0, currentBytes.Length);
            if (!BytesEqual(currentBytes, jumpBytes))
            {
                error = "the target method changed after the guard was installed";
                return false;
            }

            WriteBytes(targetAddress, originalBytes);
            jumpBytes = null;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static byte[] CreateAbsoluteJump(IntPtr destination)
    {
        var bytes = new byte[JumpSize];
        bytes[0] = 0xFF;
        bytes[1] = 0x25;
        Buffer.BlockCopy(BitConverter.GetBytes(destination.ToInt64()), 0, bytes, 6, 8);
        return bytes;
    }

    private static void WriteBytes(IntPtr address, byte[] bytes)
    {
        if (!VirtualProtect(
                address,
                new UIntPtr((uint)bytes.Length),
                PageExecuteReadWrite,
                out var previousProtection))
        {
            throw new InvalidOperationException("VirtualProtect failed.");
        }

        try
        {
            Marshal.Copy(bytes, 0, address, bytes.Length);
            FlushInstructionCache(GetCurrentProcess(), address, new UIntPtr((uint)bytes.Length));
        }
        finally
        {
            VirtualProtect(address, new UIntPtr((uint)bytes.Length), previousProtection, out _);
        }
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
            return false;

        for (var index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
                return false;
        }

        return true;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(
        IntPtr address,
        UIntPtr size,
        uint newProtection,
        out uint oldProtection);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(
        IntPtr process,
        IntPtr baseAddress,
        UIntPtr size);
}
