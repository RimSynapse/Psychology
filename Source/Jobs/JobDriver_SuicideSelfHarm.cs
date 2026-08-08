using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using RimWorld;

namespace RimSynapse.Psychology.Jobs
{
    public class JobDriver_SuicideSelfHarm : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // First toil: dramatic pause/wait (120 ticks = 2 seconds real-time at 1x speed)
            Toil waitToil = Toils_General.Wait(120);
            waitToil.WithProgressBarToilDelay(TargetIndex.A);
            waitToil.FailOnDestroyedOrNull(TargetIndex.A);
            yield return waitToil;

            // Second toil: execute self-harm
            Toil selfHarmToil = new Toil();
            selfHarmToil.initAction = () =>
            {
                Pawn p = this.pawn;
                if (p == null || p.Dead) return;

                ThingWithComps primary = p.equipment?.Primary;
                if (primary == null) return;

                // Resolve damage type and amount based on weapon
                VerbTracker verbTracker = primary.GetComp<CompEquippable>()?.verbTracker;
                Verb primaryVerb = verbTracker?.AllVerbs?.FirstOrDefault(v => v.verbProps.isPrimary);
                ThingDef projectileDef = primaryVerb?.verbProps?.defaultProjectile;

                DamageDef damageDef = DamageDefOf.Bullet;
                float damageAmount = 15f;
                float armorPenetration = 0.2f;

                if (projectileDef != null)
                {
                    damageDef = projectileDef.projectile.damageDef;
                    damageAmount = (float)projectileDef.projectile.GetDamageAmount(primary, null);
                    armorPenetration = projectileDef.projectile.GetArmorPenetration(primary, null);
                }
                else
                {
                    damageDef = DamageDefOf.Stab;
                    damageAmount = 15f;
                    armorPenetration = 0.3f;
                    
                    if (primary.def.tools != null && primary.def.tools.Count > 0)
                    {
                        var tool = primary.def.tools[0];
                        damageAmount = tool.power;
                        armorPenetration = tool.armorPenetration;
                        if (tool.capacities != null && tool.capacities.Count > 0)
                        {
                            var cap = tool.capacities[0];
                            if (cap != null && cap.defName != null)
                            {
                                if (cap.defName.IndexOf("Cut", System.StringComparison.OrdinalIgnoreCase) >= 0)
                                    damageDef = DamageDefOf.Cut;
                                else if (cap.defName.IndexOf("Stab", System.StringComparison.OrdinalIgnoreCase) >= 0)
                                    damageDef = DamageDefOf.Stab;
                                else if (cap.defName.IndexOf("Blunt", System.StringComparison.OrdinalIgnoreCase) >= 0)
                                    damageDef = DamageDefOf.Blunt;
                            }
                        }
                    }
                }

                // Choose a lethal part (Brain, Head, or Heart)
                BodyPartRecord part = p.RaceProps.body.AllParts.FirstOrDefault(bp => bp.def.defName.Equals("Brain", System.StringComparison.OrdinalIgnoreCase))
                                   ?? p.RaceProps.body.AllParts.FirstOrDefault(bp => bp.def.defName.Equals("Head", System.StringComparison.OrdinalIgnoreCase))
                                   ?? p.RaceProps.body.AllParts.FirstOrDefault(bp => bp.def.defName.Equals("Heart", System.StringComparison.OrdinalIgnoreCase));

                if (part != null)
                {
                    // Apply heavy damage multiplier to ensure suicide is lethal (tunable, Stage 3)
                    float suicideMult = RimSynapse.Psychology.RimSynapsePsychologyMod.Settings?.suicideDamageMultiplier ?? 5.0f;
                    var dinfo = new DamageInfo(
                        def: damageDef,
                        amount: damageAmount * suicideMult,
                        armorPenetration: armorPenetration,
                        angle: -1f,
                        instigator: p,
                        hitPart: part,
                        weapon: primary.def
                    );
                    p.TakeDamage(dinfo);
                }
            };
            selfHarmToil.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return selfHarmToil;
        }
    }
}
