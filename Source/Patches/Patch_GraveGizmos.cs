using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;
using RimSynapse.Psychology.Comps;
using RimSynapse.Psychology.UI;

namespace RimSynapse.Psychology.Patches
{
    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.GetGizmos))]
    public static class Patch_Building_Grave_GetGizmos
    {
        public static void Postfix(ThingWithComps __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance is Building_Grave grave)
            {
                var list = new List<Gizmo>(__result);
                if (grave.Corpse != null && grave.Corpse.InnerPawn != null)
                {
                    AddViewFuneralGizmo(grave.Corpse.InnerPawn, list);
                }

                var storeSettings = grave.GetStoreSettings();
                if (storeSettings != null && storeSettings.filter != null)
                {
                    // Allow Colonists Toggle Gizmo
                    var colonistDef = DefDatabase<SpecialThingFilterDef>.GetNamed("AllowCorpsesColonist", false);
                    if (colonistDef != null)
                    {
                        list.Add(new Command_Toggle
                        {
                            defaultLabel = "Allow Colonists",
                            defaultDesc = "Toggle whether this grave allows colonist corpses.",
                            icon = TexCommand.GatherSpotActive,
                            isActive = () => storeSettings.filter.Allows(colonistDef),
                            toggleAction = () =>
                            {
                                bool current = storeSettings.filter.Allows(colonistDef);
                                storeSettings.filter.SetAllow(colonistDef, !current);
                            }
                        });
                    }

                    // Allow Strangers Toggle Gizmo
                    var strangerDef = DefDatabase<SpecialThingFilterDef>.GetNamed("AllowCorpsesStranger", false);
                    if (strangerDef != null)
                    {
                        list.Add(new Command_Toggle
                        {
                            defaultLabel = "Allow Strangers",
                            defaultDesc = "Toggle whether this grave allows stranger and enemy corpses.",
                            icon = TexCommand.GatherSpotActive,
                            isActive = () => storeSettings.filter.Allows(strangerDef),
                            toggleAction = () =>
                            {
                                bool current = storeSettings.filter.Allows(strangerDef);
                                storeSettings.filter.SetAllow(strangerDef, !current);
                            }
                        });
                    }
                }

                __result = list;
            }
            else if (__instance.def?.defName != null && __instance.def.defName.IndexOf("Stele", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var list = new List<Gizmo>(__result);
                AddSteleGizmos(__instance, list);
                __result = list;
            }
        }

        private static void AddSteleGizmos(ThingWithComps stele, List<Gizmo> list)
        {
            if (Find.World == null) return;

            var worldComp = Find.World.GetComponent<RimSynapse.SynapseCoreWorldComponent>();
            if (worldComp == null) return;

            string steleId = stele.GetUniqueLoadID();
            if (worldComp.steleEventLinks.TryGetValue(steleId, out string eventId))
            {
                var record = worldComp.pawnEventRecords.FirstOrDefault(r => r.id == eventId);
                if (record != null)
                {
                    list.Add(new Command_Action
                    {
                        defaultLabel = "View Commemorated Event",
                        defaultDesc = $"Open the record of the commemorated ceremony: {record.eventName} for {record.targetPawnName}.",
                        icon = TexCommand.GatherSpotActive,
                        action = () =>
                        {
                            Find.WindowStack.Add(new Dialog_CommemoratedEvent(record));
                        }
                    });

                    list.Add(new Command_Action
                    {
                        defaultLabel = "Unlink Ceremony Log",
                        defaultDesc = "Clear the commemoration link from this stele.",
                        icon = TexCommand.ClearPrioritizedWork,
                        action = () =>
                        {
                            worldComp.steleEventLinks.Remove(steleId);
                            Messages.Message("Unlinked ceremony log from this stele.", MessageTypeDefOf.TaskCompletion, false);
                        }
                    });
                }
            }
            else
            {
                list.Add(new Command_Action
                {
                    defaultLabel = "Commemorate Event",
                    defaultDesc = "Assign a historical ceremony or event log to be commemorated by this stele.",
                    icon = TexCommand.GatherSpotActive,
                    action = () =>
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        foreach (var record in worldComp.pawnEventRecords)
                        {
                            options.Add(new FloatMenuOption(
                                $"{record.eventName} - {record.targetPawnName} ({record.dateString})",
                                () =>
                                {
                                    worldComp.steleEventLinks[steleId] = record.id;
                                    Messages.Message($"Commemorated {record.eventName} for {record.targetPawnName} on this stele.", MessageTypeDefOf.TaskCompletion, false);
                                }
                            ));
                        }

                        if (options.Count == 0)
                        {
                            options.Add(new FloatMenuOption("No ceremony logs recorded yet.", null));
                        }

                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                });
            }
        }

        public static void AddViewFuneralGizmo(Pawn deceased, List<Gizmo> gizmos)
        {
            if (deceased == null || Find.World == null) return;
            
            var worldComp = Find.World.GetComponent<SynapsePsychologyWorldComponent>();
            if (worldComp == null) return;
            
            string pawnId = deceased.GetUniqueLoadID();
            if (worldComp.funeralRecords.TryGetValue(pawnId, out string record))
            {
                gizmos.Add(new Command_Action
                {
                    defaultLabel = "View Deceased Profile",
                    defaultDesc = "Open the deceased's profile containing their funeral record, lifetime memories, and social network.",
                    icon = TexCommand.GatherSpotActive,
                    action = () =>
                    {
                        Find.WindowStack.Add(new Dialog_PawnPsychology(deceased));
                    }
                });
            }
            else if (Prefs.DevMode)
            {
                // If in DevMode and no record exists, provide a button to force generate it retroactively
                gizmos.Add(new Command_Action
                {
                    defaultLabel = "DEV: Force Gen Funeral Log",
                    defaultDesc = "Generate a retroactive funeral record for this pawn using the new LLM two-pass system.",
                    icon = TexCommand.GatherSpotActive,
                    action = () =>
                    {
                        var map = deceased.MapHeld ?? Find.CurrentMap;
                        var startState = new Patch_Funeral_Apply.FuneralStartState
                        {
                            weather = map?.weatherManager?.curWeather?.label ?? "clear",
                            timeOfDay = "afternoon",
                            averageMood = 0.5f,
                            moodReason = "grief",
                            averageOpinion = 0f
                        };
                        
                        var attendees = new HashSet<Pawn>();
                        if (map != null)
                        {
                            foreach (var p in map.mapPawns.AllPawns)
                            {
                                if (p != null && p.RaceProps.Humanlike && !p.Dead && p.Spawned)
                                {
                                    attendees.Add(p);
                                }
                            }
                        }
                        
                        Patch_Funeral_Apply.GenerateFuneralRecordAndMemories(deceased, "good", startState, attendees, 0.4f, 0.02f);
                        Messages.Message("Retroactive funeral record generation queued for " + deceased.Name.ToStringShort, MessageTypeDefOf.TaskCompletion, false);
                    }
                });
            }
        }
    }

    [HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.SpawnSetup))]
    public static class Patch_Building_Grave_SpawnSetup
    {
        public static void Postfix(ThingWithComps __instance, Map map, bool respawningAfterLoad)
        {
            if (respawningAfterLoad) return;
            if (!(__instance is Building_Grave grave)) return;

            var strangerDef = DefDatabase<SpecialThingFilterDef>.GetNamed("AllowCorpsesStranger", false);
            if (strangerDef != null)
            {
                var storeSettings = grave.GetStoreSettings();
                if (storeSettings != null && storeSettings.filter != null)
                {
                    storeSettings.filter.SetAllow(strangerDef, false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Corpse), "GetGizmos")]
    public static class Patch_Corpse_GetGizmos
    {
        public static void Postfix(Corpse __instance, ref IEnumerable<Gizmo> __result)
        {
            var list = new List<Gizmo>(__result);
            if (__instance.InnerPawn != null)
            {
                Patch_Building_Grave_GetGizmos.AddViewFuneralGizmo(__instance.InnerPawn, list);
            }
            __result = list;
        }
    }
}
