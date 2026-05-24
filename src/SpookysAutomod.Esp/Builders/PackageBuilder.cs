using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Noggog;

namespace SpookysAutomod.Esp.Builders;

/// <summary>
/// Fluent builder for creating Package records (AI behavior packages for NPCs).
/// Now with full Mutagen API support for proper package structure including
/// Data dictionary, ProcedureTree, and complete package configuration.
/// </summary>
public class PackageBuilder
{
    private readonly SkyrimMod _mod;
    private readonly Package _package;
    private byte _nextDataIndex = 0;

    public PackageBuilder(SkyrimMod mod, string editorId)
    {
        _mod = mod;
        _package = mod.Packages.AddNew();
        _package.EditorID = editorId;

        // Initialize with sensible defaults
        _package.Flags = Package.Flag.OffersServices;
        _package.InterruptOverride = Package.Interrupt.None;
        _package.PreferredSpeed = Package.Speed.Walk;

        // Mutagen leaves Package.Type at 0 by default, but the engine's PACK
        // record format only recognizes Type=18 (a regular Package) or Type=19
        // (a PackageTemplate). Type=0 is treated as malformed and the engine
        // silently refuses to evaluate the package - no errors, no movement,
        // just stays on whatever package was already active. Every builder
        // path through this class produces a regular Package, so set 18.
        // Discovered 2026-05-20 while debugging M2 travel.
        _package.Type = Package.Types.Package;
    }

    /// <summary>
    /// Configure package as Sandbox type for general wandering/idling at the
    /// actor's current location.
    ///
    /// Emits a Type=18 Sandbox package modeled byte-for-byte on vanilla
    /// `DefaultSandboxCurrentLocation1024` (Skyrim.esm 0x000BFB6B). Slot 0's
    /// PLDT uses LocationType.NearSelf (the engine's "sandbox where the
    /// actor stands" anchor) - this works for any actor including the
    /// player, no per-actor reference plumbing needed. The data input
    /// values (12 sparse slots) are also lifted from that vanilla package.
    ///
    /// Earlier revisions of this method emitted PLDT type=0 (NearReference)
    /// with a null FormID - a shape no vanilla package uses; the engine
    /// accepts it silently but produces no visible movement. The 6
    /// undocumented bits 10-15 in InterruptFlags (mask 0xFC00) are also
    /// required; without them sandboxing NPCs visibly "stay put" because
    /// the engine treats partial flag sets as "don't initiate interrupts."
    /// See https://en.uesp.net/wiki/Skyrim_Mod:Mod_File_Format/PACK
    /// for partial documentation; bits 10-15 are extracted-from-vanilla.
    /// </summary>
    /// <param name="radius">Radius in units around the actor (default 1024,
    /// matching DefaultSandboxCurrentLocation1024).</param>
    public PackageBuilder AsSandbox(uint radius = 1024)
    {
        // Slot 0: Location data - PLDT type=12 (NearSelf), no FormID, given
        // radius. Mutagen routes type=12 through LocationFallback (anything
        // not in {NearReference, InCell, ObjectID, ObjectType,
        // LinkedReference} goes through the fallback writer).
        _package.Data[(sbyte)0] = new PackageDataLocation
        {
            Name = "Location",
            Location = new LocationTargetRadius
            {
                Target = new LocationFallback
                {
                    Type = LocationTargetRadius.LocationType.NearSelf,
                    Data = 0
                },
                Radius = radius
            }
        };

        // Slots 1, 3-7, 14, 25, 27, 31: ten Bool inputs. Values lifted from
        // DefaultSandboxCurrentLocation1024. The Sandbox template's
        // procedure tree reads these as specific behavior toggles:
        //   slot 1 = Allow Eating
        //   slot 3 = Allow Sleeping
        //   slot 4 = Allow Conversation
        //   slot 5 = Allow Idle Markers
        //   slot 6 = Allow Sitting
        //   slot 7 = Allow Wandering   <- the toggle that gates visible movement
        //   slot 14 = Unlock On Arrival
        //   slots 25/27/31 = Preferred Path On / Ride Horse / Allow Special Furniture
        _package.Data[(sbyte)1]  = new PackageDataBool { Name = "Bool",  Data = true  };
        _package.Data[(sbyte)3]  = new PackageDataBool { Name = "Bool",  Data = true  };
        _package.Data[(sbyte)4]  = new PackageDataBool { Name = "Bool",  Data = true  };
        _package.Data[(sbyte)5]  = new PackageDataBool { Name = "Bool",  Data = true  };
        _package.Data[(sbyte)6]  = new PackageDataBool { Name = "Bool",  Data = true  };
        _package.Data[(sbyte)7]  = new PackageDataBool { Name = "Bool",  Data = true  };
        _package.Data[(sbyte)14] = new PackageDataBool { Name = "Bool",  Data = false };
        _package.Data[(sbyte)25] = new PackageDataBool { Name = "Bool",  Data = false };
        _package.Data[(sbyte)27] = new PackageDataBool { Name = "Bool",  Data = false };
        _package.Data[(sbyte)31] = new PackageDataBool { Name = "Bool",  Data = true  };

        // Slot 29: Float (Energy in the template's BNAM labels). Vanilla value.
        _package.Data[(sbyte)29] = new PackageDataFloat { Name = "Float", Data = 50.0f };
        _nextDataIndex = 32;

        // Procedure tree stub. The Sandbox template provides the real tree;
        // the engine ignores ours when a PackageTemplate is set. We still
        // emit a placeholder branch so the binary layout matches what
        // Mutagen-built packages produce in other working cases (our Travel
        // fix kept a similar placeholder and the engine accepted it).
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Sandbox"
        };
        branch.DataInputIndices.Add(0);
        _package.ProcedureTree.Add(branch);

        // Vanilla template-using Sandboxes emit Flags=0. Drop the
        // constructor's OffersServices (merchant) default that's wrong for
        // every package type except Vendor.
        _package.Flags = 0;

        // PreferredSpeed = Run (PKDT byte 6 = 2). Matches the vanilla gold
        // standard. The constructor's Walk default makes sandboxing actors
        // dawdle visibly.
        _package.PreferredSpeed = Package.Speed.Run;

        // InterruptFlags = 0x0000FEFF. Mutagen names bits 0-9 (skipping
        // bit 8 which is unused); bits 10-15 (mask 0xFC00) aren't in the
        // enum but vanilla sandboxing packages set them and the engine
        // requires them for visible interrupt response. Cast adds the
        // unnamed bits.
        _package.InteruptFlags =
              Package.InterruptFlag.HellosToPlayer
            | Package.InterruptFlag.RandomConversations
            | Package.InterruptFlag.ObserveCombatBehavior
            | Package.InterruptFlag.GreetCorpseBehavior
            | Package.InterruptFlag.ReactionToPlayerActions
            | Package.InterruptFlag.FriendlyFireComments
            | Package.InterruptFlag.AggroRadiusBehavior
            | Package.InterruptFlag.AllowIdleChatter
            | Package.InterruptFlag.WorldInteractions
            | (Package.InterruptFlag)0xFC00;

        // Schedule = any time (-1s). Mutagen's all-zero default reads as
        // "midnight, 0 duration" which the engine treats as "never run".
        _package.ScheduleMonth     = -1;
        _package.ScheduleDayOfWeek = (Package.DayOfWeek)(-1);
        _package.ScheduleHour      = -1;
        _package.ScheduleMinute    = -1;

        // PKCU DataInputVersion = 10 (the Sandbox template's expected hash).
        _package.DataInputVersion = 10;

        // Reference vanilla Skyrim.esm "Sandbox" template at 0x0001C254.
        // Without the template, the engine has nowhere to look up the
        // Sandbox procedure logic and the package never runs.
        var sandboxTemplate = new FormKey(
            ModKey.FromFileName("Skyrim.esm"),
            0x0001C254u);
        _package.PackageTemplate.SetTo(sandboxTemplate);

        return this;
    }

    /// <summary>
    /// Configure package as Travel type for moving to a destination.
    /// </summary>
    /// <param name="destinationRef">FormKey of destination marker/reference</param>
    public PackageBuilder AsTravel(FormKey destinationRef)
    {
        // Emits a Type=18 Travel package matching vanilla Braith's
        // (0x0010DE9F) structure byte-for-byte where it matters:
        //   - References Skyrim.esm Travel template (0x00016FAA)
        //   - 3 data inputs at sequential indices: Location, Bool, Bool
        //     (template expects exactly these in this order)
        //   - Schedule = "any time" (Month/DayOfWeek/Hour/Minute = -1)
        //   - DataInputVersion = 3 (matches template's input count, the
        //     hash-like trailing int in PKCU)
        //   - Flags = MustComplete | IgnoreCombat (vanilla travel default)

        // Data input slot indices match vanilla Braith's UNAM markers (0, 2,
        // 4 - sparse, not 0/1/2 sequential). The "Travel" template's
        // procedure references these specific slot IDs to bind inputs;
        // putting Bools at 1/3 would leave the template's expected 2/4
        // slots empty.

        // Slot 0: Location pointing at the destination REFR.
        _package.Data[(sbyte)0] = new PackageDataLocation
        {
            Name = "Place to Travel",
            Location = new LocationTargetRadius
            {
                Target = new LocationTarget
                {
                    Link = destinationRef.ToLink<IPlacedGetter>()
                },
                Radius = 0
            }
        };

        // Slot 2: "Ride Horse if possible?" Bool.
        _package.Data[(sbyte)2] = new PackageDataBool
        {
            Name = "Ride Horse if possible?",
            Data = false
        };

        // Slot 4: "Prefer Preferred Path?" Bool.
        _package.Data[(sbyte)4] = new PackageDataBool
        {
            Name = "Prefer Preferred Path?",
            Data = false
        };
        _nextDataIndex = 5;

        // Procedure tree: a single "Procedure"-type branch with ProcedureType
        // "Travel" referencing data input slot 0.
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Travel"
        };
        branch.DataInputIndices.Add(0);
        _package.ProcedureTree.Add(branch);

        // Travel-appropriate top-level fields. OffersServices (constructor
        // default) is a merchant flag, irrelevant.
        _package.Flags = Package.Flag.MustComplete | Package.Flag.IgnoreCombat;

        // Schedule = always available. Vanilla emits Month=-1, DayOfWeek=-1,
        // Hour=-1, Minute=-1 (Date stays 0; it's unsigned). All-zeros (the
        // Mutagen default) reads as "midnight, 0 duration" - effectively
        // "never run."
        _package.ScheduleMonth     = -1;
        _package.ScheduleDayOfWeek = (Package.DayOfWeek)(-1);
        _package.ScheduleHour      = -1;
        _package.ScheduleMinute    = -1;

        // PKCU last int: the template's "input count" expectation - vanilla
        // shows 3 for Travel-template-based packages. Mutagen exposes this
        // as DataInputVersion.
        _package.DataInputVersion = 3;

        // Template reference: vanilla "Travel" at Skyrim.esm:0x00016FAA. The
        // engine reads the procedure logic from the template; our package
        // overrides only the data inputs.
        var travelTemplate = new FormKey(
            ModKey.FromFileName("Skyrim.esm"),
            0x00016FAAu);
        _package.PackageTemplate.SetTo(travelTemplate);

        return this;
    }

    /// <summary>
    /// Configure package as Travel type targeting a quest alias. The package's
    /// target resolves at runtime to whatever REFR fills the alias.
    ///
    /// IMPORTANT: alias-targeted packages MUST have their OwnerQuest set
    /// (via WithOwnerQuest) so the engine can resolve the alias index
    /// against that quest's Aliases list. Call WithOwnerQuest BEFORE running
    /// the package in-game; the order of fluent calls doesn't matter, but
    /// both must end up set.
    ///
    /// Sets Flags = MustComplete | IgnoreCombat to override the constructor's
    /// OffersServices default (which is a merchant flag, irrelevant to travel).
    /// Skyrim Package records have no Priority field; alias-attached package
    /// precedence is determined by ordering within alias.PackageData.
    /// </summary>
    /// <param name="aliasIndex">ID of the alias in the owner quest's Aliases list</param>
    public PackageBuilder AsTravelToAlias(int aliasIndex)
    {
        var targetData = new PackageDataTarget
        {
            Name = "TravelDestination",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetAlias { Alias = aliasIndex }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Travel"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        // OffersServices (the constructor's default) is a merchant flag and
        // wrong for travel. Use MustComplete + IgnoreCombat - the standard
        // vanilla travel pair. Skyrim Package records have no Priority field;
        // call-site is expected to use ActorUtil.AddPackageOverride to push
        // this onto a specific actor with explicit priority.
        _package.Flags = Package.Flag.MustComplete | Package.Flag.IgnoreCombat;

        // Reference vanilla Skyrim.esm "Travel" template (FormID 0x00016FAA).
        // Vanilla Type=18 packages get their procedure logic (the actual
        // "navmesh to destination" behavior) from this template. Without
        // it, the engine treats our embedded ProcedureTree as advisory and
        // the package never actually executes. Discovered by byte-diffing
        // our PACK record against vanilla WhiterunKidFightSceneBraithTravel
        // (0x0010DE9F) which references 0x00016FAA as its template.
        var travelTemplate = new FormKey(
            ModKey.FromFileName("Skyrim.esm"),
            0x00016FAAu);
        _package.PackageTemplate.SetTo(travelTemplate);

        return this;
    }

    /// <summary>
    /// Set the package's owning Quest. Required when the package's target
    /// uses an alias index - the engine resolves alias indices against this
    /// quest's Aliases list. For non-alias packages this is optional.
    /// </summary>
    /// <param name="questFormKey">FormKey of the owning Quest record.</param>
    public PackageBuilder WithOwnerQuest(FormKey questFormKey)
    {
        _package.OwnerQuest.SetTo(questFormKey);
        return this;
    }

    /// <summary>
    /// Configure package as Sleep type for sleeping in a bed.
    /// </summary>
    /// <param name="bedRef">FormKey of bed furniture reference</param>
    /// <param name="startHour">Hour to start sleeping (0-23, default: 22 for 10 PM)</param>
    /// <param name="duration">Hours to sleep (default: 8)</param>
    public PackageBuilder AsSleep(FormKey bedRef, byte startHour = 22, byte duration = 8)
    {
        if (startHour > 23)
            throw new ArgumentException("Start hour must be 0-23", nameof(startHour));

        if (duration == 0 || duration > 24)
            throw new ArgumentException("Duration must be 1-24 hours", nameof(duration));

        // Set schedule
        _package.ScheduleHour = (sbyte)startHour;
        _package.ScheduleDurationInMinutes = duration * 60;

        // Add special sleep flag
        _package.Flags |= Package.Flag.WearSleepOutfit;

        // Add target data for bed
        var targetData = new PackageDataTarget
        {
            Name = "SleepFurniture",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = bedRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch for sleep
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Sleep"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Eat type for eating at furniture.
    /// </summary>
    /// <param name="furnitureRef">FormKey of chair/table furniture reference</param>
    /// <param name="startHour">Hour to start eating (0-23, default: 12 for noon)</param>
    /// <param name="duration">Hours to eat (default: 2)</param>
    public PackageBuilder AsEat(FormKey furnitureRef, byte startHour = 12, byte duration = 2)
    {
        if (startHour > 23)
            throw new ArgumentException("Start hour must be 0-23", nameof(startHour));

        if (duration == 0 || duration > 24)
            throw new ArgumentException("Duration must be 1-24 hours", nameof(duration));

        // Set schedule
        _package.ScheduleHour = (sbyte)startHour;
        _package.ScheduleDurationInMinutes = duration * 60;

        // Add target data for furniture
        var targetData = new PackageDataTarget
        {
            Name = "EatFurniture",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = furnitureRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch for eat
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Eat"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Follow type for following an actor referenced by a
    /// FormKey (static REFR). The package references vanilla Skyrim.esm "Follow"
    /// template (FormID 0x00019B2C) which provides the actual procedure logic;
    /// our derived PACK supplies the data dictionary that overrides the
    /// template's defaults. See AsFollowToAlias for the alias-target variant
    /// (the one the _GaiusMissions M5.6 framework consumes).
    /// </summary>
    /// <param name="targetRef">FormKey of actor to follow.</param>
    /// <param name="minRadius">Closest distance follower maintains. Below this
    /// the follower stops walking. Vanilla template default 128.</param>
    /// <param name="maxRadius">Furthest distance before the follower runs.
    /// Matches fFollowStartSprintDistance for the "don't constantly sprint"
    /// sweet spot. Vanilla template default 256.</param>
    /// <param name="goToLeadersGoal">"Accompany" in template's data labeling -
    /// simulates walking WITH the leader (caravan-style) rather than following
    /// behind. Vanilla template default true.</param>
    /// <param name="needLOS">If true, follower only follows when it has
    /// line-of-sight to target. Vanilla template default false.</param>
    /// <param name="rideHorse">If true, follower will mount a horse when
    /// possible. Vanilla template default false.</param>
    public PackageBuilder AsFollow(
        FormKey targetRef,
        float minRadius = 128.0f,
        float maxRadius = 256.0f,
        bool  goToLeadersGoal = true,
        bool  needLOS = false,
        bool  rideHorse = false)
    {
        var targetData = new PackageDataTarget
        {
            Name = "Target to Follow",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = targetRef.ToLink<IPlacedGetter>()
            }
        };
        PopulateFollowDataFromTemplate(
            targetData, minRadius, maxRadius, goToLeadersGoal, needLOS, rideHorse);
        return this;
    }

    /// <summary>
    /// Configure package as Follow type targeting a ReferenceAlias on the
    /// package's owning quest. Mirrors AsTravelToAlias for alias-bound targets;
    /// the alias index is interpreted by the engine against the owning quest's
    /// Aliases list. Call WithOwnerQuest separately to set OwnerQuest, then
    /// fill the alias at runtime with the actual leader actor.
    ///
    /// Slot layout extracted from Skyrim.esm:0x00019B2C ("Follow" vanilla
    /// template) via tools/inspect_pack:
    ///   Slot 0: PackageDataTarget "Target to Follow"
    ///   Slot 1: PackageDataFloat  "Min Radius:"
    ///   Slot 2: PackageDataFloat  "Max Radius:"
    ///   Slot 4: PackageDataBool   "Accompany?"   (wiki's GoToLeadersGoal)
    ///   Slot 6: PackageDataBool   "Ride Horse?"
    ///   Slot 8: PackageDataBool   "Need LOS?"
    /// The template's ProcedureTree runs Follow procedure with these slots
    /// as inputs; our derived package just supplies the data values that
    /// override the template's defaults.
    /// </summary>
    /// <param name="aliasIndex">ID of the alias on the owner quest's Aliases
    /// list that will hold the actor to follow.</param>
    /// <param name="minRadius">Closest distance follower maintains.</param>
    /// <param name="maxRadius">Furthest distance before the follower runs.</param>
    /// <param name="goToLeadersGoal">If true, walks WITH the leader (caravan-
    /// style) rather than behind.</param>
    /// <param name="needLOS">If true, follower only follows when LOS to target.</param>
    /// <param name="rideHorse">If true, follower mounts a horse when possible.</param>
    public PackageBuilder AsFollowToAlias(
        int   aliasIndex,
        float minRadius = 128.0f,
        float maxRadius = 256.0f,
        bool  goToLeadersGoal = true,
        bool  needLOS = false,
        bool  rideHorse = false)
    {
        var targetData = new PackageDataTarget
        {
            Name = "Target to Follow",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetAlias { Alias = aliasIndex }
        };
        PopulateFollowDataFromTemplate(
            targetData, minRadius, maxRadius, goToLeadersGoal, needLOS, rideHorse);
        return this;
    }

    // Shared Follow-package configuration: slots 0/1/2/4/6/8 match vanilla
    // Skyrim.esm:0x00019B2C ("Follow") slot layout. Template provides the real
    // ProcedureTree at runtime; we emit a placeholder branch only so the
    // binary format matches what Mutagen produces for working Travel packs.
    private void PopulateFollowDataFromTemplate(
        PackageDataTarget targetData,
        float             minRadius,
        float             maxRadius,
        bool              goToLeadersGoal,
        bool              needLOS,
        bool              rideHorse)
    {
        _package.Data[(sbyte)0] = targetData;
        _package.Data[(sbyte)1] = new PackageDataFloat { Name = "Min Radius:", Data = minRadius };
        _package.Data[(sbyte)2] = new PackageDataFloat { Name = "Max Radius:", Data = maxRadius };
        _package.Data[(sbyte)4] = new PackageDataBool  { Name = "Accompany?",  Data = goToLeadersGoal };
        _package.Data[(sbyte)6] = new PackageDataBool  { Name = "Ride Horse?", Data = rideHorse };
        _package.Data[(sbyte)8] = new PackageDataBool  { Name = "Need LOS?",   Data = needLOS };
        _nextDataIndex = 9;

        // Procedure tree placeholder - template provides the real tree.
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Follow"
        };
        branch.DataInputIndices.Add(0);
        _package.ProcedureTree.Add(branch);

        // Flags from the vanilla Follow template (extracted via inspect_pack).
        _package.Flags = Package.Flag.AllowSwimming;

        // PreferredSpeed = Run (matches template).
        _package.PreferredSpeed = Package.Speed.Run;

        // InterruptFlags 0xFEFF (= 65279) extracted from template. Mutagen's
        // enum names bits 0-9 (skipping bit 8 which is unused per Bethesda's
        // data); bits 10-15 (mask 0xFC00) aren't named in the enum but vanilla
        // sandboxing/follow packages set them and the engine requires them.
        // Construction matches the AsSandbox helper so the bit pattern is
        // identical to the template's 0xFEFF.
        _package.InteruptFlags =
              Package.InterruptFlag.HellosToPlayer
            | Package.InterruptFlag.RandomConversations
            | Package.InterruptFlag.ObserveCombatBehavior
            | Package.InterruptFlag.GreetCorpseBehavior
            | Package.InterruptFlag.ReactionToPlayerActions
            | Package.InterruptFlag.FriendlyFireComments
            | Package.InterruptFlag.AggroRadiusBehavior
            | Package.InterruptFlag.AllowIdleChatter
            | Package.InterruptFlag.WorldInteractions
            | (Package.InterruptFlag)0xFC00;

        // Schedule = any time (-1s). Mutagen's all-zero default reads as
        // "midnight, 0 duration" = "never run".
        _package.ScheduleMonth     = -1;
        _package.ScheduleDayOfWeek = (Package.DayOfWeek)(-1);
        _package.ScheduleHour      = -1;
        _package.ScheduleMinute    = -1;

        // PKCU DataInputVersion = 4 extracted from template.
        _package.DataInputVersion = 4;

        // Reference vanilla Skyrim.esm "Follow" template at 0x00019B2C.
        // Without the template, the engine has no procedure tree to bind
        // our data slots to and the package never executes.
        var followTemplate = new FormKey(
            ModKey.FromFileName("Skyrim.esm"),
            0x00019B2Cu);
        _package.PackageTemplate.SetTo(followTemplate);
    }

    /// <summary>
    /// Configure package as Guard type for guarding a location.
    /// </summary>
    /// <param name="markerRef">FormKey of guard position marker/reference</param>
    public PackageBuilder AsGuard(FormKey markerRef)
    {
        // Add target data for guard position
        var targetData = new PackageDataTarget
        {
            Name = "GuardPosition",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = markerRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch for guard
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Guard"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        // Guards should always have weapons drawn
        _package.Flags |= Package.Flag.WeaponDrawn;

        return this;
    }

    /// <summary>
    /// Configure package as Patrol type for patrolling to a marker.
    /// Note: Full multi-point patrol requires more complex configuration.
    /// </summary>
    /// <param name="patrolMarker">FormKey of patrol point marker</param>
    public PackageBuilder AsPatrol(FormKey patrolMarker)
    {
        // Add target data for patrol point
        var targetData = new PackageDataTarget
        {
            Name = "PatrolPoint",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = patrolMarker.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch for patrol
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Patrol"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as UseItemAt type for using/activating objects.
    /// NPCs will use crafting stations, cook at pots, work at forges, etc.
    /// </summary>
    /// <param name="itemRef">FormKey of object to use/activate (forge, cooking pot, etc.)</param>
    public PackageBuilder AsUseItemAt(FormKey itemRef)
    {
        // Add target data for item to use
        var targetData = new PackageDataTarget
        {
            Name = "UseItem",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = itemRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "UseItemAt"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Sit type for sitting at furniture.
    /// NPCs will sit at chairs, benches, etc.
    /// </summary>
    /// <param name="furnitureRef">FormKey of chair/bench to sit at</param>
    public PackageBuilder AsSit(FormKey furnitureRef)
    {
        // Add target data for furniture
        var targetData = new PackageDataTarget
        {
            Name = "SitFurniture",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = furnitureRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Sit"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as UseIdleMarker type for ambient activities.
    /// NPCs will sweep, lean, hammer, chop wood, etc. at idle markers.
    /// </summary>
    /// <param name="idleMarkerRef">FormKey of idle marker</param>
    public PackageBuilder AsUseIdleMarker(FormKey idleMarkerRef)
    {
        // Add target data for idle marker
        var targetData = new PackageDataTarget
        {
            Name = "IdleMarker",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = idleMarkerRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "UseIdleMarker"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Flee type for fleeing from danger.
    /// </summary>
    /// <param name="fleeFromRef">FormKey of what to flee from (optional, can flee from combat)</param>
    /// <param name="fleeDistance">Distance to flee (default: 1000 units)</param>
    public PackageBuilder AsFlee(FormKey? fleeFromRef = null, ushort fleeDistance = 1000)
    {
        if (fleeFromRef.HasValue)
        {
            // Add target data for what to flee from
            var targetData = new PackageDataTarget
            {
                Name = "FleeFrom",
                Type = PackageDataTarget.Types.SingleRef,
                Target = new PackageTargetSpecificReference
                {
                    Reference = fleeFromRef.Value.ToLink<IPlacedGetter>()
                }
            };
            var targetIndex = _nextDataIndex++;
            _package.Data[(sbyte)targetIndex] = targetData;

            var branch = new PackageBranch
            {
                BranchType = "Procedure",
                ProcedureType = "Flee"
            };
            branch.DataInputIndices.Add(targetIndex);
            _package.ProcedureTree.Add(branch);
        }
        else
        {
            // Flee from combat (no specific target)
            var branch = new PackageBranch
            {
                BranchType = "Procedure",
                ProcedureType = "Flee"
            };
            _package.ProcedureTree.Add(branch);
        }

        // Remove combat flags so NPC will flee
        _package.Flags &= ~Package.Flag.IgnoreCombat;

        return this;
    }

    /// <summary>
    /// Configure package as Accompany type for accompanying an actor to a destination.
    /// Similar to Follow but stops at destination.
    /// </summary>
    /// <param name="targetRef">FormKey of actor to accompany</param>
    /// <param name="destinationRef">FormKey of destination marker</param>
    public PackageBuilder AsAccompany(FormKey targetRef, FormKey destinationRef)
    {
        // Add target data
        var targetData = new PackageDataTarget
        {
            Name = "AccompanyTarget",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = targetRef.ToLink<IPlacedGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add destination data
        var destData = new PackageDataTarget
        {
            Name = "Destination",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = destinationRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var destIndex = _nextDataIndex++;
        _package.Data[(sbyte)destIndex] = destData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Accompany"
        };
        branch.DataInputIndices.Add(targetIndex);
        branch.DataInputIndices.Add(destIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as CastMagic type for casting spells.
    /// </summary>
    /// <param name="targetRef">FormKey of target location/actor</param>
    public PackageBuilder AsCastMagic(FormKey targetRef)
    {
        // Add target data
        var targetData = new PackageDataTarget
        {
            Name = "CastTarget",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = targetRef.ToLink<IPlacedGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "CastMagic"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Dialogue type for engaging in dialogue.
    /// </summary>
    /// <param name="targetRef">FormKey of dialogue target</param>
    public PackageBuilder AsDialogue(FormKey targetRef)
    {
        // Add target data
        var targetData = new PackageDataTarget
        {
            Name = "DialogueTarget",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = targetRef.ToLink<IPlacedGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Dialogue"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Find type for searching for something.
    /// </summary>
    /// <param name="targetRef">FormKey of what to find</param>
    public PackageBuilder AsFind(FormKey targetRef)
    {
        // Add target data
        var targetData = new PackageDataTarget
        {
            Name = "FindTarget",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = targetRef.ToLink<IPlacedGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Find"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Ambush type for waiting in ambush.
    /// </summary>
    /// <param name="ambushMarkerRef">FormKey of ambush position marker</param>
    public PackageBuilder AsAmbush(FormKey ambushMarkerRef)
    {
        // Add target data for ambush position
        var targetData = new PackageDataTarget
        {
            Name = "AmbushPosition",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = ambushMarkerRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Ambush"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        // Set stealth flag
        _package.Flags |= Package.Flag.AlwaysSneak;

        return this;
    }

    /// <summary>
    /// Configure package as Wander type for wandering within a radius.
    /// </summary>
    /// <param name="wanderMarkerRef">FormKey of wander center marker</param>
    /// <param name="radius">Wander radius in units (default: 1000)</param>
    public PackageBuilder AsWander(FormKey wanderMarkerRef, ushort radius = 1000)
    {
        // Add location data for wander center
        var locationData = new PackageDataLocation
        {
            Name = "WanderLocation",
            Location = new LocationTargetRadius
            {
                Target = new LocationTarget
                {
                    Link = wanderMarkerRef.ToLink<IPlacedGetter>()
                },
                Radius = radius
            }
        };
        var locationIndex = _nextDataIndex++;
        _package.Data[(sbyte)locationIndex] = locationData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Wander"
        };
        branch.DataInputIndices.Add(locationIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Wait type for waiting at a location.
    /// </summary>
    /// <param name="waitMarkerRef">FormKey of wait position marker</param>
    public PackageBuilder AsWait(FormKey waitMarkerRef)
    {
        // Add target data for wait position
        var targetData = new PackageDataTarget
        {
            Name = "WaitPosition",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = waitMarkerRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Wait"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Activate type for activating a specific object.
    /// </summary>
    /// <param name="activateRef">FormKey of object to activate</param>
    public PackageBuilder AsActivate(FormKey activateRef)
    {
        // Add target data for object to activate
        var targetData = new PackageDataTarget
        {
            Name = "ActivateTarget",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = activateRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Activate"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Relax type for relaxing at a location.
    /// </summary>
    /// <param name="relaxMarkerRef">FormKey of relax location marker</param>
    public PackageBuilder AsRelax(FormKey relaxMarkerRef)
    {
        // Add target data for relax position
        var targetData = new PackageDataTarget
        {
            Name = "RelaxPosition",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = relaxMarkerRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Relax"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as ForceGreet type for force greeting an actor.
    /// </summary>
    /// <param name="targetRef">FormKey of actor to force greet</param>
    public PackageBuilder AsForceGreet(FormKey targetRef)
    {
        // Add target data for greet target
        var targetData = new PackageDataTarget
        {
            Name = "GreetTarget",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = targetRef.ToLink<IPlacedGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "ForceGreet"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Greet type for standard greeting behavior.
    /// </summary>
    /// <param name="targetRef">Optional FormKey of actor to greet</param>
    public PackageBuilder AsGreet(FormKey? targetRef = null)
    {
        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Greet"
        };

        // If target specified, add target data
        if (targetRef.HasValue)
        {
            var targetData = new PackageDataTarget
            {
                Name = "GreetTarget",
                Type = PackageDataTarget.Types.SingleRef,
                Target = new PackageTargetSpecificReference
                {
                    Reference = targetRef.Value.ToLink<IPlacedGetter>()
                }
            };
            var targetIndex = _nextDataIndex++;
            _package.Data[(sbyte)targetIndex] = targetData;
            branch.DataInputIndices.Add(targetIndex);
        }

        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as UseWeapon type for using a specific weapon.
    /// </summary>
    /// <param name="weaponRef">FormKey of weapon to use</param>
    /// <param name="targetRef">Optional FormKey of target to attack</param>
    public PackageBuilder AsUseWeapon(FormKey weaponRef, FormKey? targetRef = null)
    {
        // Add weapon data
        var weaponData = new PackageDataTarget
        {
            Name = "Weapon",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = weaponRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var weaponIndex = _nextDataIndex++;
        _package.Data[(sbyte)weaponIndex] = weaponData;

        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "UseWeapon"
        };
        branch.DataInputIndices.Add(weaponIndex);

        // Add target if specified
        if (targetRef.HasValue)
        {
            var targetData = new PackageDataTarget
            {
                Name = "Target",
                Type = PackageDataTarget.Types.SingleRef,
                Target = new PackageTargetSpecificReference
                {
                    Reference = targetRef.Value.ToLink<IPlacedGetter>()
                }
            };
            var targetIndex = _nextDataIndex++;
            _package.Data[(sbyte)targetIndex] = targetData;
            branch.DataInputIndices.Add(targetIndex);
        }

        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as UseMagic type for casting a spell.
    /// </summary>
    /// <param name="spellRef">FormKey of spell to cast</param>
    /// <param name="targetRef">Optional FormKey of target</param>
    public PackageBuilder AsUseMagic(FormKey spellRef, FormKey? targetRef = null)
    {
        // Add spell data
        var spellData = new PackageDataTarget
        {
            Name = "Spell",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = spellRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var spellIndex = _nextDataIndex++;
        _package.Data[(sbyte)spellIndex] = spellData;

        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "UseMagic"
        };
        branch.DataInputIndices.Add(spellIndex);

        // Add target if specified
        if (targetRef.HasValue)
        {
            var targetData = new PackageDataTarget
            {
                Name = "Target",
                Type = PackageDataTarget.Types.SingleRef,
                Target = new PackageTargetSpecificReference
                {
                    Reference = targetRef.Value.ToLink<IPlacedGetter>()
                }
            };
            var targetIndex = _nextDataIndex++;
            _package.Data[(sbyte)targetIndex] = targetData;
            branch.DataInputIndices.Add(targetIndex);
        }

        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as LockDoors type for locking doors on schedule.
    /// </summary>
    /// <param name="doorRef">Optional FormKey of specific door to lock</param>
    public PackageBuilder AsLockDoors(FormKey? doorRef = null)
    {
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "LockDoors"
        };

        // If door specified, add door data
        if (doorRef.HasValue)
        {
            var doorData = new PackageDataTarget
            {
                Name = "Door",
                Type = PackageDataTarget.Types.SingleRef,
                Target = new PackageTargetSpecificReference
                {
                    Reference = doorRef.Value.ToLink<IPlacedObjectGetter>()
                }
            };
            var doorIndex = _nextDataIndex++;
            _package.Data[(sbyte)doorIndex] = doorData;
            branch.DataInputIndices.Add(doorIndex);
        }

        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as UnlockDoors type for unlocking doors on schedule.
    /// </summary>
    /// <param name="doorRef">Optional FormKey of specific door to unlock</param>
    public PackageBuilder AsUnlockDoors(FormKey? doorRef = null)
    {
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "UnlockDoors"
        };

        // If door specified, add door data
        if (doorRef.HasValue)
        {
            var doorData = new PackageDataTarget
            {
                Name = "Door",
                Type = PackageDataTarget.Types.SingleRef,
                Target = new PackageTargetSpecificReference
                {
                    Reference = doorRef.Value.ToLink<IPlacedObjectGetter>()
                }
            };
            var doorIndex = _nextDataIndex++;
            _package.Data[(sbyte)doorIndex] = doorData;
            branch.DataInputIndices.Add(doorIndex);
        }

        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Dismount type for dismounting from a horse.
    /// </summary>
    public PackageBuilder AsDismount()
    {
        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Dismount"
        };
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Acquire type for picking up/acquiring objects.
    /// </summary>
    /// <param name="objectRef">FormKey of object to acquire</param>
    public PackageBuilder AsAcquire(FormKey objectRef)
    {
        // Add target data for object to acquire
        var targetData = new PackageDataTarget
        {
            Name = "AcquireTarget",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = objectRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Acquire"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Escort type for escorting actors to a destination.
    /// </summary>
    /// <param name="escortRef">FormKey of actor to escort</param>
    /// <param name="destinationRef">FormKey of destination</param>
    public PackageBuilder AsEscort(FormKey escortRef, FormKey destinationRef)
    {
        // Add escort target data
        var escortData = new PackageDataTarget
        {
            Name = "EscortTarget",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = escortRef.ToLink<IPlacedGetter>()
            }
        };
        var escortIndex = _nextDataIndex++;
        _package.Data[(sbyte)escortIndex] = escortData;

        // Add destination data
        var destData = new PackageDataTarget
        {
            Name = "Destination",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = destinationRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var destIndex = _nextDataIndex++;
        _package.Data[(sbyte)destIndex] = destData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Escort"
        };
        branch.DataInputIndices.Add(escortIndex);
        branch.DataInputIndices.Add(destIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Say type for speaking dialogue at a location.
    /// </summary>
    /// <param name="topicRef">FormKey of dialogue topic</param>
    /// <param name="locationRef">Optional FormKey of location where to say dialogue</param>
    public PackageBuilder AsSay(FormKey topicRef, FormKey? locationRef = null)
    {
        // Add topic data
        var topicData = new PackageDataTarget
        {
            Name = "Topic",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = topicRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var topicIndex = _nextDataIndex++;
        _package.Data[(sbyte)topicIndex] = topicData;

        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Say"
        };
        branch.DataInputIndices.Add(topicIndex);

        // Add location if specified
        if (locationRef.HasValue)
        {
            var locData = new PackageDataTarget
            {
                Name = "Location",
                Type = PackageDataTarget.Types.SingleRef,
                Target = new PackageTargetSpecificReference
                {
                    Reference = locationRef.Value.ToLink<IPlacedObjectGetter>()
                }
            };
            var locIndex = _nextDataIndex++;
            _package.Data[(sbyte)locIndex] = locData;
            branch.DataInputIndices.Add(locIndex);
        }

        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Shout type for casting a shout.
    /// </summary>
    /// <param name="shoutRef">FormKey of shout to cast</param>
    /// <param name="targetRef">Optional FormKey of target</param>
    public PackageBuilder AsShout(FormKey shoutRef, FormKey? targetRef = null)
    {
        // Add shout data
        var shoutData = new PackageDataTarget
        {
            Name = "Shout",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = shoutRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var shoutIndex = _nextDataIndex++;
        _package.Data[(sbyte)shoutIndex] = shoutData;

        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Shout"
        };
        branch.DataInputIndices.Add(shoutIndex);

        // Add target if specified
        if (targetRef.HasValue)
        {
            var targetData = new PackageDataTarget
            {
                Name = "Target",
                Type = PackageDataTarget.Types.SingleRef,
                Target = new PackageTargetSpecificReference
                {
                    Reference = targetRef.Value.ToLink<IPlacedGetter>()
                }
            };
            var targetIndex = _nextDataIndex++;
            _package.Data[(sbyte)targetIndex] = targetData;
            branch.DataInputIndices.Add(targetIndex);
        }

        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as FollowTo type for following an actor to a destination.
    /// </summary>
    /// <param name="followRef">FormKey of actor to follow</param>
    /// <param name="destinationRef">FormKey of destination</param>
    public PackageBuilder AsFollowTo(FormKey followRef, FormKey destinationRef)
    {
        // Add follow target data
        var followData = new PackageDataTarget
        {
            Name = "FollowTarget",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = followRef.ToLink<IPlacedGetter>()
            }
        };
        var followIndex = _nextDataIndex++;
        _package.Data[(sbyte)followIndex] = followData;

        // Add destination data
        var destData = new PackageDataTarget
        {
            Name = "Destination",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = destinationRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var destIndex = _nextDataIndex++;
        _package.Data[(sbyte)destIndex] = destData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "FollowTo"
        };
        branch.DataInputIndices.Add(followIndex);
        branch.DataInputIndices.Add(destIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as HoldPosition type for holding a specific position.
    /// </summary>
    /// <param name="positionRef">FormKey of position marker</param>
    public PackageBuilder AsHoldPosition(FormKey positionRef)
    {
        // Add position data
        var positionData = new PackageDataTarget
        {
            Name = "Position",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = positionRef.ToLink<IPlacedObjectGetter>()
            }
        };
        var positionIndex = _nextDataIndex++;
        _package.Data[(sbyte)positionIndex] = positionData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "HoldPosition"
        };
        branch.DataInputIndices.Add(positionIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as KeepAnEyeOn type for observing/watching a target.
    /// </summary>
    /// <param name="targetRef">FormKey of target to watch</param>
    public PackageBuilder AsKeepAnEyeOn(FormKey targetRef)
    {
        // Add target data
        var targetData = new PackageDataTarget
        {
            Name = "WatchTarget",
            Type = PackageDataTarget.Types.SingleRef,
            Target = new PackageTargetSpecificReference
            {
                Reference = targetRef.ToLink<IPlacedGetter>()
            }
        };
        var targetIndex = _nextDataIndex++;
        _package.Data[(sbyte)targetIndex] = targetData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "KeepAnEyeOn"
        };
        branch.DataInputIndices.Add(targetIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Hover type for flying creatures to hover/travel in air.
    /// </summary>
    /// <param name="hoverMarkerRef">FormKey of hover location marker</param>
    /// <param name="radius">Hover area radius (default: 1000)</param>
    public PackageBuilder AsHover(FormKey hoverMarkerRef, ushort radius = 1000)
    {
        // Add location data for hover area
        var locationData = new PackageDataLocation
        {
            Name = "HoverLocation",
            Location = new LocationTargetRadius
            {
                Target = new LocationTarget
                {
                    Link = hoverMarkerRef.ToLink<IPlacedGetter>()
                },
                Radius = radius
            }
        };
        var locationIndex = _nextDataIndex++;
        _package.Data[(sbyte)locationIndex] = locationData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Hover"
        };
        branch.DataInputIndices.Add(locationIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Configure package as Orbit type for orbiting around a location or target.
    /// </summary>
    /// <param name="orbitRef">FormKey of location or target to orbit</param>
    /// <param name="radius">Orbit radius (default: 500)</param>
    public PackageBuilder AsOrbit(FormKey orbitRef, ushort radius = 500)
    {
        // Add location data for orbit center
        var locationData = new PackageDataLocation
        {
            Name = "OrbitCenter",
            Location = new LocationTargetRadius
            {
                Target = new LocationTarget
                {
                    Link = orbitRef.ToLink<IPlacedGetter>()
                },
                Radius = radius
            }
        };
        var locationIndex = _nextDataIndex++;
        _package.Data[(sbyte)locationIndex] = locationData;

        // Add procedure branch
        var branch = new PackageBranch
        {
            BranchType = "Procedure",
            ProcedureType = "Orbit"
        };
        branch.DataInputIndices.Add(locationIndex);
        _package.ProcedureTree.Add(branch);

        return this;
    }

    /// <summary>
    /// Set the location where this package operates (for location-based packages).
    /// </summary>
    /// <param name="locationRef">FormKey of location reference</param>
    /// <param name="radius">Radius around location (default: 500)</param>
    public PackageBuilder WithLocation(FormKey locationRef, uint radius = 500)
    {
        // Add or update location data
        var locationData = new PackageDataLocation
        {
            Name = "PackageLocation",
            Location = new LocationTargetRadius
            {
                Target = new LocationTarget
                {
                    Link = locationRef.ToLink<IPlacedGetter>()
                },
                Radius = radius
            }
        };

        // Add to data if not already present
        if (!_package.Data.ContainsKey(0))
        {
            _package.Data[0] = locationData;
        }

        return this;
    }

    /// <summary>
    /// Set the time schedule for when this package runs.
    /// </summary>
    /// <param name="startHour">Hour to start (0-23)</param>
    /// <param name="duration">Duration in hours</param>
    public PackageBuilder WithSchedule(byte startHour, byte duration)
    {
        if (startHour > 23)
            throw new ArgumentException("Start hour must be 0-23", nameof(startHour));

        if (duration == 0 || duration > 24)
            throw new ArgumentException("Duration must be 1-24 hours", nameof(duration));

        _package.ScheduleHour = (sbyte)startHour;
        _package.ScheduleDurationInMinutes = duration * 60;

        return this;
    }

    /// <summary>
    /// Add conditions that must be met for this package to run.
    /// </summary>
    public PackageBuilder WithConditions(Action<List<Condition>> configure)
    {
        var conditions = new List<Condition>();
        configure(conditions);

        foreach (var condition in conditions)
        {
            _package.Conditions.Add(condition);
        }

        return this;
    }

    /// <summary>
    /// Add a single condition to the package.
    /// </summary>
    public PackageBuilder AddCondition(Condition condition)
    {
        _package.Conditions.Add(condition);
        return this;
    }

    /// <summary>
    /// Set package flags directly.
    /// </summary>
    public PackageBuilder WithFlags(Package.Flag flags)
    {
        _package.Flags = flags;
        return this;
    }

    /// <summary>
    /// Add a flag to existing package flags.
    /// </summary>
    public PackageBuilder AddFlag(Package.Flag flag)
    {
        _package.Flags |= flag;
        return this;
    }

    /// <summary>
    /// Mark this package as interruptible by combat.
    /// </summary>
    public PackageBuilder WithCombatInterrupt()
    {
        _package.InterruptOverride = Package.Interrupt.Combat;
        return this;
    }

    /// <summary>
    /// Mark this package as interruptible by spectator events.
    /// </summary>
    public PackageBuilder WithSpectatorInterrupt()
    {
        _package.InterruptOverride = Package.Interrupt.Spectator;
        return this;
    }

    /// <summary>
    /// Set the preferred speed for this package.
    /// </summary>
    public PackageBuilder WithSpeed(Package.Speed speed)
    {
        _package.PreferredSpeed = speed;
        return this;
    }

    /// <summary>
    /// Configure package to allow NPCs to walk.
    /// </summary>
    public PackageBuilder WithWalkSpeed()
    {
        return WithSpeed(Package.Speed.Walk);
    }

    /// <summary>
    /// Configure package to allow NPCs to run.
    /// </summary>
    public PackageBuilder WithRunSpeed()
    {
        return WithSpeed(Package.Speed.Run);
    }

    /// <summary>
    /// Configure package to allow NPCs to fast walk.
    /// </summary>
    public PackageBuilder WithFastWalkSpeed()
    {
        return WithSpeed(Package.Speed.FastWalk);
    }

    /// <summary>
    /// Build and return the completed package record.
    /// </summary>
    public Package Build() => _package;

    /// <summary>
    /// Build and return the package's FormKey.
    /// </summary>
    public FormKey BuildFormKey() => _package.FormKey;
}
