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

        public StorytellerCompProperties_OnceCatchUp()
        {
            compClass = typeof(StorytellerComp_OnceCatchUp);
        }
    }

    // Vanilla SingleOnceFixed, except the time check is "at or after" instead of "exactly at", so an incident that was
    // blocked when its moment came (LEO's tech gate) is offered again every storyteller interval until it fires.
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
            if (ticksSinceStart < Props.fireAfterDaysPassed * GenDate.TicksPerDay)
            {
                yield break;
            }

            IncidentDef incident = Props.incident;
            if (incident.TargetAllowed(target))
            {
                // CanFireNow (and with it LEO's gate) runs in Storyteller.TryFire; a refusal just means "try next interval".
                yield return new FiringIncident(incident, this, GenerateParms(incident.category, target));
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
