﻿using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using Verse.AI;

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
                harmony.Patch(original, null, new HarmonyMethod(patch));
            }*/
        }
    }

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
                    yield return new CodeInstruction(OpCodes.Ldloc_S, Convert.ToByte(40));
                    yield return new CodeInstruction(OpCodes.Ldloc_S, tmpIndex);
                    yield return new CodeInstruction(OpCodes.Ldloc_S, Convert.ToByte(35));
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return CodeInstruction.LoadField(typeof(PathFinder), "pathingContext");
                    yield return CodeInstruction.LoadField(typeof(PathingContext), "map");
                    yield return CodeInstruction.Call(typeof(Patch_PathFinder_FindPath), "AllowsDiagonalMovement");
                    yield return new CodeInstruction(OpCodes.Brtrue, label);
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
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
            if (Patch_PathFinder_FindPath.defNames.Contains(building.def.defName))
            {
                var graphic_Linked = building.def.graphicData.Graphic as Graphic_Linked;
                return (graphic_Linked.ShouldLinkWith(building.Position + (building.Position - intVec), building) ||
                    graphic_Linked.ShouldLinkWith(building.Position - (intVec + directions[i % 4] - building.Position), building)) &&
                    !graphic_Linked.ShouldLinkWith(intVec + directions[i % 4], building) &&
                    !graphic_Linked.ShouldLinkWith(intVec, building); ;
            }
            return false;
        }

        public static readonly string[] defNames = new string[]
        {
            "rgh_DiagonalWall",
            "rgh_ConnectorWallR",
            "rgh_ConnectorWallL",
            "rgh_DiagonalDoorwayA",
            "rgh_DiagonalDoorwayB",
            "rgh_DiagonalEmbrasure",
            "rgh_ConnectorEmbrasureR",
            "rgh_ConnectorEmbrasureL"
        };

        public static readonly IntVec3[] directions = new IntVec3[]
        {
            new IntVec3(1, 0, -1),
            new IntVec3(1, 0, 1),
            new IntVec3(-1, 0, 1),
            new IntVec3(-1, 0, -1)
        };
    }

    //[HarmonyPatch(typeof(RegionCostCalculator), "PathableNeighborIndices")]
    //public static class Patch_RegionCostCalculator_PathableNeighborIndices
    //{
    //    public static void Postfix(int index, Map ___map, PathingContext ___pathingContext, List<int> __result)
    //    {
    //        var cellIndices = ___map.cellIndices;
    //        var edificeGrid = ___map.edificeGrid;
    //        var intVec = cellIndices.IndexToCell(index);
    //        var directions = Patch_PathFinder_FindPath.directions;
    //        for (var i = 0; i < 4; i++)
    //        {
    //            var cardinal1 = new IntVec3(directions[i].x, 0, 0);
    //            var cardinal2 = new IntVec3(0, 0, directions[i].z);
    //            var adja1 = intVec + cardinal1;
    //            var adja2 = intVec + cardinal2;
    //            if (!adja1.InBounds(___map) || !adja2.InBounds(___map)) continue;
    //            var num1 = cellIndices.CellToIndex(adja1);
    //            var num2 = cellIndices.CellToIndex(adja2);
    //            if ((edificeGrid[num1] == null || Patch_PathFinder_FindPath.AllowsDiagonalMovement(i, num1, intVec, ___map)) &&
    //                (edificeGrid[num2] == null || Patch_PathFinder_FindPath.AllowsDiagonalMovement(i, num2, intVec, ___map)))
    //            {
    //                __result.Add(cellIndices.CellToIndex(intVec + directions[i]));
    //            }
    //        }
    //    }
    //}

    [HarmonyPatch(typeof(RegionMaker), "TryGenerateRegionFrom")]
    public static class Patch_RegionMaker_TryGenerateRegionFrom
    {
        public static void Postfix(Region __result, Map ___map)
        {
            if (__result == null) return;
            var cellIndices = ___map.cellIndices;
            var edificeGrid = ___map.edificeGrid;
            var directions = Patch_PathFinder_FindPath.directions;
            var defNames = Patch_PathFinder_FindPath.defNames;
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

    [HarmonyPatch(typeof(Designator_PaintFloor), nameof(Designator_PaintFloor.CanDesignateCell))]
    public static class Patch_Designator_PaintFloor_CanDesignateCell
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = instructions.ToList();

            var f_def_passability = AccessTools.Field(typeof(BuildableDef), nameof(BuildableDef.passability));
            var pos = codes.FindIndex(c => c.opcode == OpCodes.Ldfld && c.OperandIs(f_def_passability));
            pos = codes.FindIndex(pos, c => c.opcode == OpCodes.Bne_Un_S);
            var label = codes[pos].operand;
            pos = codes.FindIndex(pos, c => c.opcode == OpCodes.Ldc_I4_0);

            codes.InsertRange(pos, new[]
            {
                CodeInstruction.LoadField(typeof(PassingBetweenDiagonalWalls), nameof(PassingBetweenDiagonalWalls.diagonalWallDefNames)),
                new CodeInstruction(OpCodes.Ldloc_0),
                CodeInstruction.LoadField(typeof(Thing), nameof(Thing.def)),
                CodeInstruction.LoadField(typeof(Def), nameof(Def.defName)),
                new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(List<string>), nameof(List<string>.Contains))),
                new CodeInstruction(OpCodes.Brtrue_S, label),
            });
            return codes;
        }
    }

    [HarmonyPatch(typeof(Designator_RemoveFloor), nameof(Designator_RemoveFloor.CanDesignateCell))]
    public static class Patch_Designator_RemoveFloor_CanDesignateCell
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Patch_Designator_PaintFloor_CanDesignateCell.Transpiler(instructions);
    }

    [HarmonyPatch(typeof(SmoothSurfaceDesignatorUtility), nameof(SmoothSurfaceDesignatorUtility.CanSmoothFloorUnder))]
    public static class Patch_SmoothSurfaceDesignatorUtility_CanSmoothFloorUnder
    {
        public static void Postfix(Building b, ref bool __result)
        {
            __result = __result || PassingBetweenDiagonalWalls.diagonalWallDefNames.Contains(b.def.defName);
        }
    }
}