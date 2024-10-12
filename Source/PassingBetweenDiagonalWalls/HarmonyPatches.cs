using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Noise;

namespace PassingBetweenDiagonalWalls
{
    [StaticConstructorOnStartup]
    class HarmonyPatches
    {
        static HarmonyPatches()
        {
            var harmony = new Harmony("com.harmony.rimworld.passingbetweendiagonalwalls");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            /*if (ModsConfig.IsActive("owlchemist.cleanpathfinding") || ModsConfig.IsActive("pathfinding.framework"))
            {
                var original = AccessTools.Method(typeof(RegionTypeUtility), nameof(RegionTypeUtility.GetExpectedRegionType));
                var patch = AccessTools.Method(typeof(Patch_RegionTypeUtility_GetExpectedRegionType), nameof(Patch_RegionTypeUtility_GetExpectedRegionType.Postfix));
                harmony.Patch(original, null, patch);
            }*/
        }
    }

    [HarmonyAfter("PerfectPathingPatch")]
    [HarmonyPatch(typeof(PathFinder), "FindPath", typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode), typeof(PathFinderCostTuning))]
    public static class Patch_PathFinder_FindPath
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = instructions.ToList();
            var method = AccessTools.Method(typeof(PathFinder), "BlocksDiagonalMovement", new Type[] { typeof(int) });
            var tmpIndex = generator.DeclareLocal(typeof(int));
            codes = codes.Select((c, i) => {
                if ((c.opcode == OpCodes.Ldarg_0) && (codes[i + 4].operand as MethodInfo == method || codes[i + 5].operand as MethodInfo == method))
                {
                    return new CodeInstruction(OpCodes.Nop).WithLabels(c.labels);
                }
                return c;
            }).ToList();

            foreach (var (code, i) in codes.Select((c, i) => (c, i)))
            {
                if (code.opcode == OpCodes.Call && code.operand.Equals(method))
                {
                    var label = codes[i + 1].operand;
                    yield return new CodeInstruction(OpCodes.Stloc_S, tmpIndex);
                    yield return CodeInstruction.LoadLocal(38);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, tmpIndex);
                    yield return CodeInstruction.LoadLocal(33);
                    yield return CodeInstruction.LoadArgument(0);
                    yield return CodeInstruction.LoadField(typeof(PathFinder), "pathingContext");
                    yield return CodeInstruction.LoadField(typeof(PathingContext), "map");
                    yield return CodeInstruction.Call(typeof(Patch_PathFinder_FindPath), "AllowsDiagonalMovement");
                    yield return new CodeInstruction(OpCodes.Brtrue, label);
                    yield return CodeInstruction.LoadArgument(0);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, tmpIndex);
                }

                yield return code;
            }
        }

        public static bool AllowsDiagonalMovement(int i, int num, IntVec3 intVec, Map map)
        {
            Building building = map.edificeGrid[num];
            if (building == null) return false;

            if (building.def.thingClass.Name == "Building_DiagonalDoor")
            {
                return true;
            }
            if (PassingBetweenDiagonalWalls.diagonalWallDefNames.Contains(building.def.defName))
            {
                var graphic_Linked = building.def.graphicData.Graphic as Graphic_Linked;
                return (graphic_Linked.ShouldLinkWith(building.Position + (building.Position - intVec), building) ||
                    graphic_Linked.ShouldLinkWith(building.Position - (intVec + directions[i % 4] - building.Position), building)) &&
                    !graphic_Linked.ShouldLinkWith(intVec + directions[i % 4], building) &&
                    !graphic_Linked.ShouldLinkWith(intVec, building); ;
            }
            return false;
        }

        public static readonly IntVec3[] directions = new IntVec3[]
        {
            new IntVec3(1, 0, -1),
            new IntVec3(1, 0, 1),
            new IntVec3(-1, 0, 1),
            new IntVec3(-1, 0, -1)
        };
    }

    [HarmonyPatch(typeof(RegionCostCalculator), "PathableNeighborIndices")]
    public static class Patch_RegionCostCalculator_PathableNeighborIndices
    {
        public static void Postfix(int index, Map ___map, PathingContext ___pathingContext, List<int> __result)
        {
            var cellIndices = ___map.cellIndices;
            var edificeGrid = ___map.edificeGrid;
            var intVec = cellIndices.IndexToCell(index);
            var directions = Patch_PathFinder_FindPath.directions;
            for (var i = 0; i < 4; i++)
            {
                var cardinal1 = new IntVec3(directions[i].x, 0, 0);
                var cardinal2 = new IntVec3(0, 0, directions[i].z);
                var num1 = cellIndices.CellToIndex(intVec + cardinal1);
                var num2 = cellIndices.CellToIndex(intVec + cardinal2);
                if ((edificeGrid[num1] == null || Patch_PathFinder_FindPath.AllowsDiagonalMovement(i, num1, intVec, ___map)) &&
                    (edificeGrid[num2] == null || Patch_PathFinder_FindPath.AllowsDiagonalMovement(i, num2, intVec, ___map)))
                {
                    __result.Add(cellIndices.CellToIndex(intVec + directions[i]));
                }
            }
        }
    }

    [HarmonyPatch(typeof(RegionMaker), "TryGenerateRegionFrom")]
    public static class Patch_RegionMaker_TryGenerateRegionFrom
    {
        public static void Postfix(Region __result, Map ___map)
        {
            if (__result == null) return;
            var cellIndices = ___map.cellIndices;
            var edificeGrid = ___map.edificeGrid;
            var directions = Patch_PathFinder_FindPath.directions;
            var defNames = PassingBetweenDiagonalWalls.diagonalWallDefNames;
            foreach (var cell in __result.Cells)
            {
                for (var i = 0; i < 4; i++)
                {
                    var another = (cell + directions[i]).GetRegion(___map);
                    if (another != null && __result != another)
                    {
                        var cardinal1 = new IntVec3(directions[i].x, 0, 0);
                        var cardinal2 = new IntVec3(0, 0, directions[i].z);
                        var num1 = cellIndices.CellToIndex(cell + cardinal1);
                        var num2 = cellIndices.CellToIndex(cell + cardinal2);
                        if (defNames.Contains(edificeGrid[num1]?.def.defName) && defNames.Contains(edificeGrid[num2]?.def.defName) &&
                            Patch_PathFinder_FindPath.AllowsDiagonalMovement(i, num1, cell, ___map) && Patch_PathFinder_FindPath.AllowsDiagonalMovement(i, num2, cell, ___map))
                        {
                            var regionLink = new RegionLink();
                            regionLink.span = new EdgeSpan(__result.AnyCell, SpanDirection.North, 0);
                            regionLink.Register(__result);
                            regionLink.Register(another);
                            another.type = __result.type;
                            __result.links.Add(regionLink);
                            another.links.Add(regionLink);
                            foreach (var oldLink in another.links.Where(l => l.regions.Any(r => r == null)).ToArray())
                            {
                                another.links.Remove(oldLink);
                            }
                        }
                    }
                }
            }
        }

    }

    /*[HarmonyPatch(typeof(RegionCostCalculator), nameof(RegionCostCalculator.GetRegionDistance))]
    public static class Patch_RegionCostCalculator_GetRegionDistance
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = instructions.ToList();
            var m_Region_Allows = AccessTools.Method(typeof(Region), nameof(Region.Allows));
            var pos = codes.FindIndex(c => c.opcode == OpCodes.Callvirt && c.OperandIs(m_Region_Allows)) + 1;
            var label = codes[pos].operand;
            var pos2 = codes.FindLastIndex(pos, c => c.opcode == OpCodes.Ldarg_0);
            var region = generator.DeclareLocal(typeof(Region));

            var labeltest = generator.DefineLabel();

            codes.InsertRange(pos2, new[]{
                new CodeInstruction(OpCodes.Stloc_S, region),
                new CodeInstruction(OpCodes.Ldloc_S, region),
                new CodeInstruction(OpCodes.Brtrue_S, labeltest),
                new CodeInstruction(OpCodes.Ldstr, "got null region"),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Log), nameof(Log.Message), new Type[]{ typeof(string) })),
                new CodeInstruction(OpCodes.Br_S, label),
                new CodeInstruction(OpCodes.Ldloc_S, region).WithLabels(labeltest)
            });
            return codes;
        }
    }

    [HarmonyPatch(typeof(RegionCostCalculator), nameof(RegionCostCalculator.GetRegionBestDistances))]
    public static class Patch_RegionCostCalculator_GetRegionBestDistances
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            return Patch_RegionCostCalculator_GetRegionDistance.Transpiler(instructions, generator);
        }
    }

    [HarmonyPatch(typeof(RegionCostCalculator), nameof(RegionCostCalculator.Init))]
    public static class Patch_RegionCostCalculator_Init
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            return Patch_RegionCostCalculator_GetRegionDistance.Transpiler(instructions, generator);
        }
    }

    [HarmonyPatch(typeof(RegionCostCalculator), "GetPreciseRegionLinkDistances")]
    public static class Patch_RegionCostCalculator_GetPreciseRegionLinkDistances
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            return Patch_RegionCostCalculator_GetRegionDistance.Transpiler(instructions, generator);
        }
    }*/

    [HarmonyPatch(typeof(ThingDef), "CanInteractThroughCorners", MethodType.Getter)]
    public static class Patch_ThingDef_CanInteractThroughCorners
    {
        public static void Postfix(ref bool __result, ThingDef __instance)
        {
            __result = __result || __instance.thingClass.Name == "Building_DiagonalDoor";
        }
    }

    [HarmonyPatch(typeof(RegionTypeUtility), nameof(RegionTypeUtility.GetExpectedRegionType))]
    public static class Patch_RegionTypeUtility_GetExpectedRegionType
    {
        public static void Postfix(IntVec3 c, Map map, ref RegionType __result)
        {
            if (!c.InBounds(map)) return;

            var building = c.GetEdifice(map);
            if (building != null && PassingBetweenDiagonalWalls.diagonalWallDefNames.Contains(building.def.defName))
            {
                __result = (RegionType)17;
            }
        }
    }
}