using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using HarmonyLib;
using System.Reflection;
using Verse.AI;
using System.Reflection.Emit;
using System.Collections;
using RimWorld;

namespace PassingBetweenDiagonalWalls
{
    [StaticConstructorOnStartup]
    class HarmonyPatches
    {
        static HarmonyPatches()
        {
            var harmony = new Harmony("com.harmony.rimworld.passingbetweendiagonalwalls");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    [HarmonyPatch(typeof(PathFinder), "FindPath", typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms), typeof(PathEndMode), typeof(PathFinderCostTuning))]
    public static class Patch_PathFinder_FindPath
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var codes = instructions.ToList();
            var method = AccessTools.Method(typeof(PathFinder), "BlocksDiagonalMovement", new Type[] { typeof(int) });
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
                    yield return CodeInstruction.StoreField(typeof(Patch_PathFinder_FindPath), "tmpIndex");
                    yield return CodeInstruction.LoadLocal(38);
                    yield return CodeInstruction.LoadField(typeof(Patch_PathFinder_FindPath), "tmpIndex");
                    yield return CodeInstruction.LoadLocal(33);
                    yield return CodeInstruction.LoadArgument(0);
                    yield return CodeInstruction.LoadField(typeof(PathFinder), "pathingContext");
                    yield return CodeInstruction.LoadField(typeof(PathingContext), "map");
                    yield return CodeInstruction.Call(typeof(Patch_PathFinder_FindPath), "AllowsDiagonalMovement");
                    yield return new CodeInstruction(OpCodes.Brtrue, label);
                    yield return CodeInstruction.LoadArgument(0);
                    yield return CodeInstruction.LoadField(typeof(Patch_PathFinder_FindPath), "tmpIndex");
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
                    graphic_Linked.ShouldLinkWith(building.Position - (intVec + directions[i - 4] - building.Position), building)) &&
                    !graphic_Linked.ShouldLinkWith(intVec + directions[i - 4], building) &&
                    !graphic_Linked.ShouldLinkWith(intVec, building); ;
            }
            return false;
        }

        public static int tmpIndex;

        public static readonly IntVec3[] directions = new IntVec3[]
        {
            new IntVec3(1, 0, -1),
            new IntVec3(1, 0, 1),
            new IntVec3(-1, 0, 1),
            new IntVec3(-1, 0, -1)
        };
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
                            Patch_PathFinder_FindPath.AllowsDiagonalMovement(i + 4, num1, cell, ___map) && Patch_PathFinder_FindPath.AllowsDiagonalMovement(i + 4, num2, cell, ___map))
                        {
                            var regionLink = new RegionLink();
                            regionLink.Register(__result);
                            regionLink.Register(another);
                            another.type = __result.type;
                            __result.links.Add(regionLink);
                            another.links.Add(regionLink);
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
}
