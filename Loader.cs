using BepInEx;
using BepInEx.Logging;
using BepInEx.NET.Common;
using FrooxEngine;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;

namespace BepInEx_RML_Loader;


[BepInPlugin(GUID, Name, Version)]
public class BepInEx_RML_Loader : BasePlugin
{
    public const string GUID = "me.art0007i.bepinex_rml_loader";
    public const string Name = "BepInEx RML Loader";
    public const string Version = "0.3.0";

    static ManualLogSource Logger = null!;
    public override void Load()
    {
        Logger = Log;
        var harmony = new Harmony(GUID);
        harmony.PatchAll();
    }

    [HarmonyPatch(typeof(GraphicalClient.GraphicalClientRunner), MethodType.StaticConstructor)]
    public class AppPathFixer
    {
        public static void Postfix(ref string ___AssemblyDirectory)
        {
            ___AssemblyDirectory = Paths.GameRootPath;
        }
    }

    [HarmonyPatch]
    public class LocationFixer
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.FirstConstructor(typeof(AssemblyTypeRegistry), (x) => x.GetParameters().Length > 3);
        }
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {
            int killCount = 0;
            var cachePath = AccessTools.PropertyGetter(typeof(GlobalTypeRegistry), nameof(GlobalTypeRegistry.MetadataCachePath));
            var asmLoc = AccessTools.PropertyGetter(typeof(Assembly), nameof(Assembly.Location));

            foreach (var code in codes)
            {
                if (killCount > 0)
                {
                    killCount--;
                    continue;
                }
                if (code.Calls(asmLoc))
                {
                    Logger.LogDebug("Patched AsmLoc");

                    yield return new(OpCodes.Call, AccessTools.Method(typeof(LocationFixer), nameof(ProcessCacheTime)));
                    killCount = 2; // skip next code
                }
                else
                {
                    yield return code;
                }
                if (code.Calls(cachePath))
                {
                    Logger.LogDebug("Patched CachePath");

                    yield return new(OpCodes.Ldarg_1); // assembly
                    yield return new(OpCodes.Call, AccessTools.Method(typeof(LocationFixer), nameof(ProcessCachePath)));
                }
            }
        }

        public static string? ProcessCachePath(string cachePath, Assembly asm)
        {
            if (string.IsNullOrWhiteSpace(asm.Location)) return null;
            return cachePath;
        }

        public static DateTime ProcessCacheTime(Assembly asm)
        {
            if (string.IsNullOrWhiteSpace(asm.Location)) return DateTime.UtcNow;
            return new FileInfo(asm.Location).LastWriteTimeUtc;
        }
    }

    [HarmonyPatch(typeof(EngineInitializer), nameof(EngineInitializer.InitializeFrooxEngine), MethodType.Async)]
    public class AssemblyLoadFixer
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
        {
            var loadFrom = AccessTools.Method(typeof(Assembly), nameof(Assembly.LoadFrom), [typeof(string)]);
            foreach (var code in codes)
            {
                if (code.Calls(loadFrom))
                {
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(AssemblyLoadFixer), nameof(LoadFrom)));
                }
                else
                {
                    yield return code;
                }
            }
        }

        public static Assembly? LoadFrom(string path)
        {
            Logger.LogInfo("Bypassing LoadFrom: " + path);
            return null;
        }
    }
}