using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace v49Jetpacks
{
    [BepInPlugin(PLUGIN_GUID, PLUGIN_NAME, PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        const string PLUGIN_GUID = "butterystancakes.lethalcompany.v49jetpacks", PLUGIN_NAME = "v49 Jetpacks", PLUGIN_VERSION = "1.0.1";
        internal static new ManualLogSource Logger;
        internal static ConfigEntry<bool> configMidairCrashing;
        internal static ConfigEntry<int> configBatteryLife;

        void Awake()
        {
            configMidairCrashing = Config.Bind(
                "Tweaking",
                "Mid-air Crashing",
                true,
                "Jetpacks exploding from \"overheating\" was actually caused by a bug that caused the player to crash into themselves or invisible triggers once the jetpack reached a certain speed, even in open space. This bug was fixed in v50, but you should leave this setting enabled if you want v49 authenticity.");

            configBatteryLife = Config.Bind(
                "Tweaking",
                "Battery Life",
                60,
                new ConfigDescription(
                    "The battery life of the jetpack in seconds. v49 is 60s. v50 final uses 50s. Nerfed jetpacks (v50 beta) used 40s.",
                new AcceptableValueList<int>(60, 50, 40)));

            Logger = base.Logger;

            new Harmony(PLUGIN_GUID).PatchAll();

            Logger.LogInfo($"{PLUGIN_NAME} v{PLUGIN_VERSION} loaded");
        }
    }

    [HarmonyPatch]
    class v49JetpacksPatches
    {
        [HarmonyPatch(typeof(JetpackItem), nameof(JetpackItem.EquipItem))]
        [HarmonyPostfix]
        static void EquipJetpack(JetpackItem __instance)
        {
            __instance.itemProperties.batteryUsage = Plugin.configBatteryLife.Value;
            // deceleration 50/s -> 75/s
            __instance.jetpackDeaccelleration = 75f;
        }

        [HarmonyPatch(typeof(JetpackItem), nameof(JetpackItem.Update))]
        [HarmonyTranspiler]
        static IEnumerable<CodeInstruction> JetpackUpdate(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = instructions.ToList();

            for (int i = 1; i < codes.Count - 1; i++)
            {
                // change lerp speed from 50x to 1x (gives the jetpack more "inertia" in the air, reduces turning and adjustment speeds)
                if (codes[i].opcode == OpCodes.Ldc_R4 && (float)codes[i].operand == 50f && codes[i + 1].opcode == OpCodes.Mul)
                {
                    codes[i].operand = 1f;
                    Plugin.Logger.LogInfo("Reduced force change factor from 50x to 1x");
                }
                // replaces QueryTriggerInteraction.Ignore with QueryTriggerInteraction.Collide if Mid-air Crashing is enabled
                else if (codes[i].opcode == OpCodes.Ldc_I4_1 && Plugin.configMidairCrashing.Value && codes[i - 1].opcode == OpCodes.Ldfld && (FieldInfo)codes[i - 1].operand == typeof(StartOfRound).GetField(nameof(StartOfRound.allPlayersCollideWithMask), BindingFlags.Instance | BindingFlags.Public))
                {
                    codes[i].opcode = OpCodes.Ldc_I4_2;
                    Plugin.Logger.LogInfo("Re-enabled collision with triggers (this will cause \"overheat explosions\" to occur)");
                }
                // removes isGrounded check to allow sliding on terrain again
                else if (codes[i].opcode == OpCodes.Brfalse && codes[i - 1].opcode == OpCodes.Ldfld && (FieldInfo)codes[i - 1].operand == typeof(PlayerControllerB).GetField(nameof(PlayerControllerB.jetpackControls), BindingFlags.Instance | BindingFlags.Public))
                {
                    int jumpTo = -1;
                    for (int j = i + 1; j < codes.Count - 3; j++)
                    {
                        if (codes[j].opcode == OpCodes.Ldarg_0 && codes[j + 2].opcode == OpCodes.Stfld && (FieldInfo)codes[j + 2].operand == typeof(JetpackItem).GetField("forces", BindingFlags.Instance | BindingFlags.NonPublic))
                            jumpTo = j + 3;
                    }
                    if (jumpTo > i)
                    {
                        object jumpToLabel = null;
                        for (int k = i + 1; k < jumpTo - 3; k++)
                        {
                            if (jumpToLabel == null && (codes[k].opcode == OpCodes.Ble_Un || codes[k].opcode == OpCodes.Brfalse))
                                jumpToLabel = codes[k].operand;
                            codes[k].opcode = OpCodes.Nop;
                        }
                        codes[i].opcode = OpCodes.Brtrue;
                        codes[i].operand = jumpToLabel;
                        Plugin.Logger.LogInfo("Patched out grounded check (this will allow sliding along terrain again)");
                    }
                    else
                        Plugin.Logger.LogError("Unable to find isGrounded condition (has JetpackItem been updated?)");
                }
            }

            return codes;
        }
    }
}