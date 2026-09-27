using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using System.Runtime.InteropServices;

namespace MatchZy;

public static class GrenadeFunctions
{
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>?
        CSmokeGrenadeProjectile_CreateFunc { get; private set; } = CreateSmokeFactory();

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>?
        CHEGrenadeProjectile_CreateFunc { get; private set; } = CreateHeFactory();

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>?
        CMolotovProjectile_CreateFunc { get; private set; } = CreateMolotovFactory();

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>?
        CDecoyProjectile_CreateFunc { get; private set; } = CreateDecoyFactory();

    public static void DisableNativeFactory(string grenadeType, string reason)
    {
        bool wasEnabled;
        switch (grenadeType)
        {
            case "smoke":
                wasEnabled = CSmokeGrenadeProjectile_CreateFunc != null;
                CSmokeGrenadeProjectile_CreateFunc = null;
                break;
            case "hegrenade":
                wasEnabled = CHEGrenadeProjectile_CreateFunc != null;
                CHEGrenadeProjectile_CreateFunc = null;
                break;
            case "molotov":
                wasEnabled = CMolotovProjectile_CreateFunc != null;
                CMolotovProjectile_CreateFunc = null;
                break;
            case "decoy":
                wasEnabled = CDecoyProjectile_CreateFunc != null;
                CDecoyProjectile_CreateFunc = null;
                break;
            default:
                wasEnabled = false;
                break;
        }

        if (wasEnabled)
        {
            Console.WriteLine($"[MatchZy] Disabled the native {grenadeType} projectile factory: {reason}. Falling back to CreateEntityByName.");
        }
    }

    private static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>?
        CreateSmokeFactory()
    {
        try
        {
            return new(
                IsLinux
                    ? "55 4C 89 C1 48 89 E5 41 57 49 89 FF 41 56 45 89 CE"
                    : "48 8B C4 48 89 58 ? 48 89 68 ? 48 89 70 ? 57 41 56 41 57 48 81 EC ? ? ? ? 48 8B B4 24 ? ? ? ?"
            );
        }
        catch (Exception exception)
        {
            LogFactoryInitializationFailure("smoke", exception);
            return null;
        }
    }

    private static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>?
        CreateHeFactory()
    {
        try
        {
            return new(
                IsLinux
                    ? "55 4C 89 C1 48 89 E5 41 57 49 89 FF 41 56 49 89 D6"
                    : "48 89 5C 24 ? 48 89 6C 24 ? 48 89 74 24 ? 57 48 83 EC ? 48 8B AC 24 ? ? ? ? 49 8B F8"
            );
        }
        catch (Exception exception)
        {
            LogFactoryInitializationFailure("hegrenade", exception);
            return null;
        }
    }

    private static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>?
        CreateMolotovFactory()
    {
        try
        {
            return new(
                IsLinux
                    ? "55 48 8D 05 ? ? ? ? 48 89 E5 41 57 41 56 41 55 41 54 49 89 FC 53 48 81 EC ? ? ? ? 4C 8D 35 ? ? ? ?"
                    : "48 8B C4 48 89 58 ? 48 89 70 ? 48 89 78 ? 4C 89 40 ? 55 41 54 41 55 41 56 41 57 48 8D 6C 24 ?"
            );
        }
        catch (Exception exception)
        {
            LogFactoryInitializationFailure("molotov", exception);
            return null;
        }
    }

    private static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>?
        CreateDecoyFactory()
    {
        try
        {
            return new(
                IsLinux
                    ? "55 4C 89 C1 48 89 E5 41 57 45 89 CF 41 56 49 89 FE 41 55 49 89 D5 48 89 F2 48 89 FE 41 54 48 8D 3D ? ? ? ? 4D 89 C4 53 48 83 EC ? E8 ? ? ? ? 45 31 C0"
                    : "48 8B C4 55 56 48 81 EC ? ? ? ? 48 89 58 ? 48 8B D9"
            );
        }
        catch (Exception exception)
        {
            LogFactoryInitializationFailure("decoy", exception);
            return null;
        }
    }

    private static void LogFactoryInitializationFailure(string grenadeType, Exception exception)
    {
        Console.WriteLine(
            $"[MatchZy] Could not initialize the native {grenadeType} projectile factory: " +
            $"{exception.GetType().Name}: {exception.Message}. Falling back to CreateEntityByName."
        );
    }
}
