using System.Collections.Generic;
using AncientAmorphousThreat;
using RimWorld;
using Verse;

namespace AATDelayedStartFix
{
    public class StorytellerCompProperties_OnceCatchUp : StorytellerCompProperties
    {
        public IncidentDef incident;
        public float fireAfterDaysPassed;
        public int minColonistCount;
        // How often a blocked incident is retried once its time has come. Vanilla checks storyteller comps every
        // 1000 ticks; retrying that often spams other mods' TryFire hooks (Chester Choose's diagnostic log) for no gain.
        public float retryIntervalDays = 1f;

        public StorytellerCompProperties_OnceCatchUp()
        {
            compClass = typeof(StorytellerComp_OnceCatchUp);
        }
    }

    // Vanilla SingleOnceFixed, except the time check is "at or after" instead of "exactly at", so an incident that was
    // blocked when its moment came (LEO's tech gate) is offered again (once per retryIntervalDays) until it fires.
    //
    // One-time guarantee: Storyteller.TryFire records a successful fire in the target's StoryState.lastFireTicks
    // before the next comp/target is evaluated, so AlreadyHappened() sees it straight away and latches the saved flag.
    // After that every call is a single bool check.
    public class StorytellerComp_OnceCatchUp : StorytellerComp
    {
        private StorytellerCompProperties_OnceCatchUp Props => (StorytellerCompProperties_OnceCatchUp)props;

        public override IEnumerable<FiringIncident> MakeIntervalIncidents(IIncidentTarget target)
        {
            GameComponent_OnceCatchUp tracker = Current.Game.GetComponent<GameComponent_OnceCatchUp>();
            if (tracker == null || tracker.fired)
            {
                yield break;
            }
            if (AlreadyHappened())
            {
                tracker.fired = true;
                yield break;
            }

            int ticksSinceStart = Find.TickManager.TicksGame;
            if (Props.minColonistCount > 0)
            {
                if (PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Count < Props.minColonistCount)
                {
                    yield break;
                }
                ticksSinceStart -= target.StoryState.GetTicksFromColonistCount(Props.minColonistCount);
            }
            int ticksOverdue = ticksSinceStart - (int)(Props.fireAfterDaysPassed * GenDate.TicksPerDay);
            if (ticksOverdue < 0)
            {
                yield break;
            }

            // Stateless throttle: the storyteller asks once per 1000 ticks, so exactly one ask lands in each 1000-tick
            // window. The first is at the due time, then one per retry period. No saved tick, so Chester's simulated
            // look-ahead can't push the next real try back.
            int retryTicks = (int)(Props.retryIntervalDays * GenDate.TicksPerDay);
            if (retryTicks > 1000 && ticksOverdue % retryTicks >= 1000)
            {
                yield break;
            }

            IncidentDef incident = Props.incident;
            if (!incident.TargetAllowed(target))
            {
                yield break;
            }
            // Ask CanFireNow (where LEO's gate sits) before handing the incident to the storyteller, so a gated attempt
            // never reaches Storyteller.TryFire or anything hooked onto it. TryFire asks again; vanilla caches it per tick.
            IncidentParms parms = GenerateParms(incident.category, target);
            if (incident.Worker.CanFireNow(parms))
            {
                yield return new FiringIncident(incident, this, parms);
            }
        }

        // Covers saves where the debris already came the normal way, through Randy's random pick, or by dev mode
        // (forced incidents are not written to lastFireTicks, hence the debris and boss checks).
        private bool AlreadyHappened()
        {
            IncidentDef incident = Props.incident;
            if (Find.World.StoryState.lastFireTicks.ContainsKey(incident))
            {
                return true;
            }

            GameComponent_AAT aat = Current.Game.GetComponent<GameComponent_AAT>();
            if (aat != null && (aat.IncidentFired || aat.Pawn != null || aat.StageIIPawn != null))
            {
                return true;
            }

            List<SpawnViaSkyfalleData> debris = incident.GetModExtension<ModExtension_AnorphousChunkDrop>()?.spawnedThings;
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map.StoryState.lastFireTicks.ContainsKey(incident))
                {
                    return true;
                }
                if (debris == null)
                {
                    continue;
                }
                for (int j = 0; j < debris.Count; j++)
                {
                    ThingDef def = debris[j].thingDef;
                    if (def != null && map.listerThings.ThingsOfDef(def).Count > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }

    // Saved latch, so the incident stays done even after the map it fired on is abandoned.
    public class GameComponent_OnceCatchUp : GameComponent
    {
        public bool fired;

        public GameComponent_OnceCatchUp(Game game)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref fired, "fired", false);
        }
    }
}
