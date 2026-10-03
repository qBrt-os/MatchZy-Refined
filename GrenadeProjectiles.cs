using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory.DynamicFunctions;
using System.Runtime.InteropServices;

namespace MatchZy;

// Native grenade projectile factories used by practice rethrows.
// The byte signatures live in gamedata/matchzy.json (installed to addons/counterstrikesharp/gamedata/), so after a CS2 update
// only that file needs updating, not the plugin. Each factory is resolved on first use; a missing key or a signature that no
// longer matches gives null, and GrenadeThrownData.Throw then creates the projectile through the entity API instead.
public static class GrenadeFunctions
{
    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    private static readonly HashSet<string> warnedKeys = new();
    private static readonly HashSet<string> disabledTypes = new(StringComparer.OrdinalIgnoreCase);

    private static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>? smokeCreate;
    private static bool smokeResolved;

    private static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>? heCreate;
    private static bool heResolved;

    private static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>? molotovCreate;
    private static bool molotovResolved;

    private static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>? decoyCreate;
    private static bool decoyResolved;

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>? CSmokeGrenadeProjectile_CreateFunc
    {
        get
        {
            if (disabledTypes.Contains("smoke")) return null;
            if (!smokeResolved)
            {
                smokeResolved = true;
                smokeCreate = Resolve(
                    "CSmokeGrenadeProjectile_Create",
                    "55 4C 89 C1 48 89 E5 41 57 49 89 FF 41 56 45 89 CE",
                    "48 8B C4 48 89 58 ? 48 89 68 ? 48 89 70 ? 57 41 56 41 57 48 81 EC ? ? ? ? 48 8B B4 24 ? ? ? ?",
                    sig => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, int, CSmokeGrenadeProjectile>(sig)
                );
            }
            return smokeCreate;
        }
    }

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>? CHEGrenadeProjectile_CreateFunc
    {
        get
        {
            if (disabledTypes.Contains("hegrenade")) return null;
            if (!heResolved)
            {
                heResolved = true;
                heCreate = Resolve(
                    "CHEGrenadeProjectile_Create",
                    "55 4C 89 C1 48 89 E5 41 57 49 89 FF 41 56 49 89 D6",
                    "48 89 5C 24 ? 48 89 6C 24 ? 48 89 74 24 ? 57 48 83 EC ? 48 8B AC 24 ? ? ? ? 49 8B F8",
                    sig => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CHEGrenadeProjectile>(sig)
                );
            }
            return heCreate;
        }
    }

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>? CMolotovProjectile_CreateFunc
    {
        get
        {
            if (disabledTypes.Contains("molotov")) return null;
            if (!molotovResolved)
            {
                molotovResolved = true;
                molotovCreate = Resolve(
                    "CMolotovProjectile_Create",
                    "55 48 8D 05 ? ? ? ? 48 89 E5 41 57 41 56 41 55 41 54 49 89 FC 53 48 81 EC ? ? ? ? 4C 8D 35 ? ? ? ?",
                    "48 8B C4 48 89 58 ? 48 89 70 ? 48 89 78 ? 4C 89 40 ? 55 41 54 41 55 41 56 41 57 48 8D 6C 24 ?",
                    sig => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CMolotovProjectile>(sig)
                );
            }
            return molotovCreate;
        }
    }

    public static MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>? CDecoyProjectile_CreateFunc
    {
        get
        {
            if (disabledTypes.Contains("decoy")) return null;
            if (!decoyResolved)
            {
                decoyResolved = true;
                decoyCreate = Resolve(
                    "CDecoyProjectile_Create",
                    "55 4C 89 C1 48 89 E5 41 57 45 89 CF 41 56 49 89 FE 41 55 49 89 D5 48 89 F2 48 89 FE 41 54 48 8D 3D ? ? ? ? 4D 89 C4 53 48 83 EC ? E8 ? ? ? ? 45 31 C0",
                    "48 8B C4 55 56 48 81 EC ? ? ? ? 48 89 58 ? 48 8B D9",
                    sig => new MemoryFunctionWithReturn<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, int, CDecoyProjectile>(sig)
                );
            }
            return decoyCreate;
        }
    }

    public static void DisableNativeFactory(string grenadeType, string reason)
    {
        if (disabledTypes.Add(grenadeType))
        {
            Console.WriteLine($"[MatchZy] Disabled the native {grenadeType} projectile factory: {reason}. Falling back to CreateEntityByName.");
        }
    }

    private static T? Resolve<T>(string gameDataKey, string fallbackLinux, string fallbackWindows, Func<string, T> create) where T : BaseMemoryFunction
    {
        string? signature = null;
        try
        {
            signature = GameData.GetSignature(gameDataKey);
        }
        catch
        {
            // Gamedata lookup failed; proceed to fallback
        }

        if (string.IsNullOrWhiteSpace(signature))
        {
            signature = IsLinux ? fallbackLinux : fallbackWindows;
        }

        try
        {
            T function = create(signature);
            if (function.Handle != IntPtr.Zero) return function;
            Warn(gameDataKey, "signature not found in this CS2 build");
        }
        catch (Exception e)
        {
            Warn(gameDataKey, e.Message);
        }
        return null;
    }

    private static void Warn(string gameDataKey, string reason)
    {
        if (!warnedKeys.Add(gameDataKey)) return;
        Console.WriteLine($"[MatchZy] Gamedata key {gameDataKey} could not be resolved ({reason}). Rethrows of this grenade use the entity API instead. Make sure addons/counterstrikesharp/gamedata/matchzy.json is installed and up to date.");
    }
}
