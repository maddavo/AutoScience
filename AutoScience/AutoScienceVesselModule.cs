using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AutoScience {

    /// Represents a particular unique situation in which science can be run
    public class LocationData {
        public ExperimentSituations State;
        public CelestialBody Body;
        public String Biome;
        public bool OnLadder;

        // override Equals and GetHashCode for storing in HashSet

        public override bool Equals(object obj) {
            if (obj == null) return false;
            LocationData other = obj as LocationData;
            if (other != null) return State == other.State && Body == other.Body && Biome == other.Biome && OnLadder == other.OnLadder;
            else return false;
        }

        public override int GetHashCode() {
            return State.GetHashCode() ^ Body.GetHashCode() ^ Biome.GetHashCode() ^ OnLadder.GetHashCode();
        }
    }

    /// The meat of the plugin, runs science automatically on any loaded vessel and queues location data on unloaded vessels
    class AutoScienceVesselModule : VesselModule {

        public LocationData Location;

        // Cache list of runnable experiments so we're not iterating over potentially-massive vessels every time
        public List<ModuleScienceExperiment> Experiments;

        // Cache the first science container we find (usually the root command pod) because same
        public List<ModuleScienceContainer> Containers;

        // Scientist onboard for resettable experiments? (goo etc)
        public bool HasScientist;

        // Queue visited biomes while vessel is unloaded, to run science the next time it's loaded
        // Use HashSet to enforce uniqueness and O(1) insert
        HashSet<LocationData> QueuedBiomes = new HashSet<LocationData>();

        // Avoid flooding the log when a modded experiment cannot produce a stock ScienceSubject.
        HashSet<String> MissingScienceSubjects = new HashSet<String>();

        // Does this vessel have a location and potentially QueuedBiomes? (generate on first update frame)
        public bool initialized = false;

        // Is this vessel loaded (Experiments, Container info is valid)?
        public bool loaded = false;

        // Check for science again after rebuilding (might have added parts when docking, EVA construction)
        public bool needsUpdate;

        // Hack - don't do science immediately after loading (EVA Kerbals are not immediately attached to the ladder)
        public float spawnTime;

        /// <summary>
        /// Check if we've moved locations and need to run science again
        /// Also updates our location so that GetBiome() is only called once (expensive operation? involves texture lookup)
        /// Runs per vessel per frame!!
        /// <summary>
        private bool CheckMove() {
            String NewBiome = GetBiome();

            bool moved = (
                Location.State != ScienceUtil.GetExperimentSituation(Vessel.EVALadderVessel) ||
                Location.Body != Vessel.mainBody ||
                Location.Biome != NewBiome ||
                Location.OnLadder != (Vessel.EVALadderVessel != Vessel));

            if (moved) {
                Location.State = ScienceUtil.GetExperimentSituation(Vessel.EVALadderVessel);
                Location.Body = Vessel.mainBody;
                Location.Biome = NewBiome;
                Location.OnLadder = Vessel.EVALadderVessel != Vessel;
            }

            return moved;
        }
        
        /// <summary>
        /// First run of FixedUpdate(), make sure data is good
        /// </summary>
        public void InitVessel() {
            spawnTime = UnityEngine.Time.time;
            Location = new LocationData();
            CheckMove();
            Rebuild();
            initialized = true;
        }

        /// <summary>
        /// Vessel was modified, is newly loaded, etc - do expensive per-part operations here and stash the results
        /// </summary>
        public void Rebuild() {
            Experiments = Vessel.FindPartModulesImplementing<ModuleScienceExperiment>();
            Containers = Vessel.FindPartModulesImplementing<ModuleScienceContainer>();
            loaded = true;
            needsUpdate = true;
        }

        // All info for this module is regenerated when vessel loads - the only thing that needs to
        // persist across scenes/saves is the queued locations for unloaded vessels.

        /// <summary>
        /// Save any queued biomes when switching scenes/saving game
        /// </summary>
        protected override void OnSave(ConfigNode node) {
            node.ClearNodes();

            if (!AutoScienceAddon.TrackUnloadedVessels) return;

            foreach (LocationData d in QueuedBiomes) {
                ConfigNode newNode = new ConfigNode("StoredLocation");
                newNode.AddValue("State", (int)d.State);
                newNode.AddValue("Body", d.Body.bodyName);
                newNode.AddValue("Biome", d.Biome);
                newNode.AddValue("OnLadder", d.OnLadder.ToString());
                node.AddNode(newNode);
            }
        }

        /// <summary>
        /// Load any queued biomes when switching scenes/loading game
        /// </summary>
        protected override void OnLoad(ConfigNode node) {
            foreach (ConfigNode storedNode in node.GetNodes("StoredLocation")) {
                LocationData newLocation = new LocationData();
                newLocation.State = (ExperimentSituations)Int32.Parse(storedNode.GetValue("State"));
                newLocation.Body = FlightGlobals.Bodies.FirstOrDefault(b => b.bodyName == storedNode.GetValue("Body"));
                newLocation.Biome = storedNode.GetValue("Biome");
                newLocation.OnLadder = bool.Parse(storedNode.GetValue("OnLadder"));
                QueuedBiomes.Add(newLocation);
            }
        }
        
        /// <summary>
        /// Check for new situations to run science every game tic
        /// </summary>
        public void FixedUpdate() {
            if (!AutoScienceAddon.ModActive) return;

            // ensure location data is valid on first run
            if (!initialized) InitVessel();

            // don't run science immediately after vessel init, wait a bit
            if (spawnTime < UnityEngine.Time.time - 0.2f) {

                // only try to run science on loaded vessels that we have full part data for
                if (Vessel.loaded) {

                    // call Rebuild() if it hasn't been
                    if (!loaded) Rebuild();

                    // check for queued data
                    if (AutoScienceAddon.TrackUnloadedVessels && QueuedBiomes.Any()) {
                        foreach (LocationData d in QueuedBiomes) {
                            CheckScience(d.State, d.Body, d.Biome);
                        }
                        QueuedBiomes.Clear();
                    }

                    // I'm doing science and I'm still alive~
                    if ((CheckMove() || needsUpdate) && Containers.Any()) {
                        needsUpdate = false;
                        CheckScience(ScienceUtil.GetExperimentSituation(Vessel), Vessel.mainBody, GetBiome());
                    }
                } else if (AutoScienceAddon.TrackUnloadedVessels) {
                    if (loaded) loaded = false;
                    if (CheckMove()) {
                        QueueLocation();
                    }
                }
            } 
        }
        
        /// <summary>
        /// Vessel is unloaded, queue the current situation to check for science later
        /// </summary>
        private void QueueLocation() {
            LocationData NewLocation = new LocationData();
            NewLocation.State = Location.State;
            NewLocation.Body = Location.Body;
            NewLocation.Biome = Location.Biome;
            NewLocation.OnLadder = Location.OnLadder;

            QueuedBiomes.Add(NewLocation);
        }
        
        /// <summary>
        /// Check for any new science to do!
        /// Must inject all relevant location data rather than using the vessel's current state
        /// (We might be running queued science from a vessel that was unloaded)
        /// </summary>
        public void CheckScience(ExperimentSituations s, CelestialBody b, String Biome) {
            HasScientist = Vessel.GetVesselCrew().Any(k => k.trait == KerbalRoster.scientistTrait);
            foreach (ModuleScienceExperiment e in Experiments) {
                TryScience(e, s, b, Biome);
            }
        }

        /// <summary>
        /// Only do surface samples if they're available
        /// Copied from ForScience! without checking if it's 100% correct
        /// </summary>
        public bool SurfaceSamplesUnlocked() {
            return GameVariables.Instance.UnlockedEVA(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.AstronautComplex))
                && GameVariables.Instance.UnlockedFuelTransfer(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.ResearchAndDevelopment));
        }
        
        /// <summary>
        /// Run a particular experiment in a given situation if possible
        /// </summary>
        public void TryScience(ModuleScienceExperiment e, ExperimentSituations s, CelestialBody b, String Biome) {
            if (!e || !e.vessel || !Containers.Any()) return; // this should never fail but better safe than sorry

            // Some modded science modules can exist without a valid experiment definition.
            if (e.experiment == null) return;

            // Don't cheat the goo/materials bay experiments
            if (!e.rerunnable && !HasScientist) return;

            // Don't run an experiment that isn't available (duh)
            if (!e.experiment.IsAvailableWhile(s, b)) return;

            // Don't run an EVA experiment unless you actually have one
            // (Per-part iteration across vessel, but only if requiresInventoryPart is true; in stock this should only ever be an EVA Kerbal?)
            if (e.requiresInventoryPart) {
                bool found = false;

                // issues with Vessel.protoVessel.GetAllProtoPartsIncludingCargo(), using this instead
                foreach(ModuleInventoryPart i in Vessel.FindPartModulesImplementing<ModuleInventoryPart>()) {
                    if (i.storedParts.Values.Any(p => p.partName == e.requiredInventoryPart)) {
                        found = true;
                        break;
                    }
                }

                if (!found) return;
            }

            if (!SurfaceSamplesUnlocked() && e.experiment.id == "surfaceSample") return;

            // Don't run science while on a ladder when your craft is landed
            // DO run science when splashed down, since it's frustrating to try and get back up on the pod sometimes :)
            if (Vessel.EVALadderVessel != Vessel && s == ExperimentSituations.SrfLanded) return;

            // Generate science data
            ScienceSubject subject = GetScienceSubject(e.experiment, s, b, Biome);
            if (subject == null) {
                String bodyName = b == null ? "<unknown body>" : b.bodyName;
                String warningKey = String.Format("{0}|{1}|{2}|{3}", e.experiment.id, s, bodyName, Biome);

                if (MissingScienceSubjects.Add(warningKey)) {
                    Debug.LogWarning(String.Format(
                        "[AutoScience] KSP could not create a science subject for experiment '{0}' in {1} at {2} ({3}); skipping it.",
                        e.experiment.id,
                        s,
                        bodyName,
                        String.IsNullOrEmpty(Biome) ? "no biome" : Biome));
                }

                return;
            }

            ScienceData data = new ScienceData(e.experiment.baseValue * subject.dataScale, e.xmitDataScalar, 0f, subject.id, subject.title);

            // Don't do zero-value science (TODO: toolbar option)
            if (!AutoScienceAddon.CollectZeroValueScience && ResearchAndDevelopment.GetScienceValue(e.experiment.baseValue * e.experiment.dataScale, subject) < 0.1) return;

            // Finally!
            if (AutoScienceAddon.CollectDuplicateScience) {
                foreach(ModuleScienceContainer c in Containers) {
                    if (!c.HasData(data)) c.AddData(data);
                }
            } else {
                if (!HasData(data)) Containers.First().AddData(data);
            }
        }

        /// <summary>
        /// Checks whether this vessel contains the given data in any of its science containers
        /// </summary>
        public bool HasData(ScienceData data) {
            if (Containers.Any(c => c.HasData(data))) return true;

            // Also check parent vessel if we're a Kerbal on a ladder
            if (Vessel.EVALadderVessel != Vessel) {
                AutoScienceVesselModule ParentModule = Vessel.EVALadderVessel.FindVesselModuleImplementing<AutoScienceVesselModule>();
                return ParentModule.Containers.Any(c => c.HasData(data));
            }

            return false;
        }
        
        /// <summary>
        /// Get current biome, including all the KSC buildings ("LandedAt" if it exists)
        /// </summary>
        public String GetBiome() {
            if (Vessel.mainBody.BiomeMap == null) return null;
            if (!string.IsNullOrEmpty(Vessel.landedAt)) return Vessel.GetLandedAtString(Vessel.landedAt);
            else return ScienceUtil.GetExperimentBiome(Vessel.mainBody, Vessel.latitude, Vessel.longitude);
        }

        private ScienceSubject GetScienceSubject(ScienceExperiment e, ExperimentSituations s, CelestialBody b, String Biome) {
            return ResearchAndDevelopment.GetExperimentSubject(
                    e,
                    s,
                    b,
                    e.BiomeIsRelevantWhile(s) ? Biome : String.Empty,
                    null);
        }
    }
}
