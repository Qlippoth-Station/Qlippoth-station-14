# Qlippoth Actions: Initiations and Results

Every Qlippoth carries a `QlippothActions` component with a list of actions. One action = one **initiation** (when) + a list of **results** (what), plus optional gates:

```yaml
- !type:QlippothAction
  actionName: parry
  initiation: !type:OnHolderDamagedInitiation
  requireState: { stance: defensive }   # only while state matches
  forbidState: { mood: asleep }         # never while state matches
  cooldown: 6                           # seconds
  chance: 0.5                           # 0..1, rolled after the other gates
  maxFires: 1                           # 0 = unlimited
  continueOnFailure: false              # a result returning false stops the rest of the chain
  results:
    - !type:NegateDamageResult
```

Results run in order. A result returns `false` when it could not do its job (no valid target, welded door, chance missed), which stops the remaining results of that action. Flow results use this as a gate.

## Shared building blocks

### `filter:` (QlippothTargetFilter)
Used by initiations that look at other entities and by range targeting.
`requiredComponent`, `requiredComponents`, `forbiddenComponents`, `whitelist`, `blacklist`, `onlyAlive`, `onlyDead`, `onlyMobs`, `onlyPlayers`, `minSanity`, `maxSanity`, `corrupted`, `minDamage`, `anchored`, `excludeQlippoths`, `excludeOffspring`, `excludeHolder`.

### `targeting:` (QlippothTargeting)
Who a result acts on. Default `Target` = the initiation's target (falls back to the Qlippoth).
Modes: `Target`, `Actor`, `Self`, `Holder`, `InRange`, `AroundTarget`, `RandomInRange`, `NearestInRange`, `PullPartner`, `Offspring`, `QlippothsInRange`. Range modes take `range`, `filter`, `maxTargets`, `includeSelf`.
The same `DamageResult` is a touch (`Target`), an aura (`InRange`) or a self-harm (`Self`) depending on this field.

### `destination:` (QlippothDestination)
Where something goes or spawns. Modes: `Self`, `Target`, `Actor`, `RandomNearSelf`, `RandomNearTarget`, `RandomOnGrid`, `RandomOnStation`, `NearestWithComponent`, `Offset`. Takes `range`, `component`, `offset`.

### State
`QlippothActions.state` is a string dictionary. Actions gate on it (`requireState` / `forbidState`), state results write it, `OnStateChangedInitiation` reacts to it. Counters are strings holding numbers (`IncrementStateResult`, `RequireStateResult min/max`).

### Chains
`ChainResult { key }` fires the action whose initiation is `OnChainInitiation { key }` immediately; `DelayResult { key, delay }` does it later. The original event (target, actor) is forwarded through the chain. This is how "N seconds after X" and multi-step behaviours are built.

---

## Initiations

### External (another player does something to it)
| Initiation | Fires when | Target |
|---|---|---|
| `OnActionInitiation { key }` | a player presses an item action button with that key | the player |
| `OnUsedOnInitiation { requiredComponent, filter }` | the held Qlippoth is clicked on something | the clicked entity |
| `OnActivateInitiation { filter }` | a player clicks the Qlippoth in the world | the player |
| `OnInteractHandInitiation { filter }` | a player touches it with an empty hand | the player |
| `OnUseInHandInitiation` | the holder activates it in hand (Z) | the holder |
| `OnInteractUsingInitiation { usedFilter, userFilter, consumeUsed }` | a player uses another item on it (feeding, tools) | the item used |
| `OnAttackedInitiation { filter }` | a mob melee-attacks it | the attacker |
| `OnThrownInitiation` / `OnLandInitiation` | it is thrown / lands | the thrower |
| `OnThrowHitInitiation { filter }` | the thrown Qlippoth hits something | what it hit |
| `OnEquippedInitiation { slots }` / `OnUnequippedInitiation { slots }` | worn as clothing / taken off | the wearer |
| `OnHeardInitiation { keywords, range, speakerFilter }` | someone speaks nearby (optionally a keyword) | the speaker |
| `OnInsertedIntoContainerInitiation { containerIds }` / `OnRemovedFromContainerInitiation` | put into / taken out of a locker, bag, crate | the container owner |
| `OnEntityInsertedInitiation { containerIds, filter }` / `OnEntityRemovedInitiation` | something is put into / taken out of the Qlippoth's own container | the inserted entity |

### Trigger (its own physical situation)
| Initiation | Fires when | Target |
|---|---|---|
| `OnPickedUpInitiation` / `OnDroppedInitiation` | taken into / out of a hand | the holder |
| `OnPullInitiation` / `OnPullStoppedInitiation` | pulling starts / stops | the puller |
| `OnHolderDamagedInitiation { externalOnly }` | the holder is about to take damage (results may change it) | the holder |
| `OnHolderMobStateInitiation { states }` | the holder falls into crit / dies | the holder |
| `OnMeleeHitInitiation` | the Qlippoth as a weapon lands a hit | what was hit |
| `OnDamagedInitiation { increased, minDelta, damageTypes, totalAbove, totalBelow, externalOnly }` | the Qlippoth itself is damaged / healed | the damage source |
| `OnMobStateInitiation { states }` | the Qlippoth mob's state changes | itself |
| `OnHealthThresholdInitiation { threshold, above }` | its total damage crosses a value (polled) | itself |
| `OnDestroyedInitiation` | destroyed by damage, about to be deleted | itself |
| `OnSpawnInitiation` | spawned into the world | itself |
| `OnCollideInitiation { filter }` | something bumps into it | the other body |
| `OnStepTriggerInitiation { filter }` | something steps on it | the stepper |
| `OnAnchorChangedInitiation { anchored }` | wrenched down / pried loose | itself |
| `OnPowerChangedInitiation { powered }` | machine Qlippoth gains / loses power | itself |
| `OnStateChangedInitiation { key, value }` | one of its own state keys is set | itself |
| `OnOffspringDiedInitiation` | a mob it spawned dies | the offspring |
| `OnEnterRangeInitiation { range, requiredComponent, filter }` | an entity walks into range (polled) | that entity |
| `OnLeaveRangeInitiation { ... }` | an entity leaves range (polled) | that entity |
| `OnCrowdInitiation { range, minCount, maxCount, edgeTriggered }` | N..M entities are in range (polled) | itself |
| `OnGasInitiation { gas, minMoles, maxMoles }` | the gas on its tile is in a range (polled) | itself |
| `OnTemperatureInitiation { min, max }` / `OnPressureInitiation { min, max }` | its tile's air is in a range (polled) | itself |
| `OnHolderSanityInitiation { min, max }` | the holder's sanity is in a range (polled) | the holder |
| `OnLightLevelInitiation { range, dark }` | it sits in darkness / light (polled) | itself |
| `OnCorruptionAppliedInitiation` / `OnCorruptionPulseInitiation` | it corrupts someone / a corruption pulse ticks | the victim |

### Timed (time since spawn; all share `interval, startDelay, variance, chance, maxFires, onlyWhileHeld, onlyWhileContained`)
| Initiation | Fires when |
|---|---|
| `IntervalInitiation` | every `interval` seconds |
| `AfterDelayInitiation { delay }` | once, `delay` seconds after spawn |
| `ProximityInitiation { range, requiredComponent, filter, maxTargets }` | every tick, once per entity in range (auras) |
| `RoundTimeInitiation { atRoundTime }` | once, when the round is that old |
| `WhileStateInitiation { key, value }` | every tick while a state holds a value |
| `WhilePulledInitiation` | every tick while being pulled (target = puller) |
| `WhileInContainerInitiation` | every tick while inside a container |

### Event based (something happens in the round)
| Initiation | Fires when | Target |
|---|---|---|
| `OnGameRuleStartedInitiation { ruleIds }` / `OnGameRuleEndedInitiation` | a station event / game rule starts / ends | - |
| `OnAlertLevelInitiation { levels }` | the alert level changes | - |
| `OnSignalInitiation { signal, senderFilter }` | another Qlippoth sends a `SignalResult` | the sender |
| `OnArrivedInitiation { kind }` | it arrives by gate breach / containment dock / rift dungeon | gate or chamber |
| `OnContainedInitiation` | docked into a containment chamber | the chamber |
| `OnContainmentBreachedInitiation` | its chamber is breached | the chamber |
| `OnEscapedContainmentInitiation` | it has left a breached chamber | the chamber |
| `OnGateClearedInitiation` | its rift's gate is cleared by the crew | the gate |
| `OnAnyGateBreachedInitiation` / `OnGateSpawnedInitiation` | any gate breaches / appears (all Qlippoths) | the gate |
| `OnShuttleCalledInitiation { called }` | the emergency shuttle is called / recalled | - |
| `OnRoundEndInitiation` | the round ends | - |

### Internal (the Qlippoth's controller, or its own chain)
| Initiation | Fires when | Target |
|---|---|---|
| `OnSelfActionInitiation { key }` | a player-controlled Qlippoth presses its own action button | itself |
| `OnChainInitiation { key }` | `ChainResult` / `DelayResult` of the same Qlippoth | forwarded |
| `OnSpokeInitiation { keywords }` | its controller says something | itself |
| `OnEmoteInitiation { emotes, includeHolder }` | its controller (or holder) emotes | the emoter |
| `OnMindAddedInitiation` / `OnMindRemovedInitiation` | a player takes / leaves control | itself |
| `OnSelfMeleeHitInitiation { filter }` | the Qlippoth mob lands its own melee hit | what was hit |

### Interface (a player opens something on it)
| Initiation | Fires when | Target |
|---|---|---|
| `VerbInitiation { key, text, alternative, priority, userFilter, showWhenState }` | a player picks the entry from the right-click menu | the player |
| `OnExaminedInitiation { detailsRangeOnly, examinerFilter }` | a player examines it (pair with `ExamineTextResult`) | the examiner |

---

## Results

### Effect (attack, heal, buff, debuff, manipulate)
`DamageResult`, `HealResult`, `StunResult`, `KnockdownResult`, `StaminaDamageResult`, `ElectrocuteResult`, `FlashResult`, `StatusEffectResult` (any status effect prototype: blindness, drunk, sleep, stutter, slowdown...), `JitterResult`, `IgniteResult`, `ExtinguishResult`, `SpeedModifierResult` (timed), `DamageSanityResult`, `RestoreSanityResult`, `SetSanityDrainMultiplierResult`, `ApplyCorruptionResult`, `RemoveCorruptionResult`, `InjectReagentResult`, `SetMobStateResult`, `GibResult`, `DeleteResult`, `DropHeldItemsResult`, `ForceEmoteResult`, `ForceSayResult`, `RenameResult`, `ExamineTextResult`, `SetLightResult`, `PoweredLightResult` (off/on/toggle/break station lights), `EmpResult`, `ExplosionResult`, `PullResult`, `PopupResult`, `NegateDamageResult`, `ScaleIncomingDamageResult`, `BonusMeleeDamageResult`, `BonusAttackedDamageResult`, `OpenDoorResult`, `HoldDoorsInRangeResult`, `SetDoorBoltsResult`, `SetPinpointerActiveResult`, `PointAtNearestResult`.

### Produce (things come out of it)
`SpawnItemResult` (destination, scatter, anchor), `SpawnEffectResult`, `SpawnOnTilesInRangeResult`, `PlaySoundResult`, `ReleaseGasResult` (single gas or mixture), `SpeakResult` (say / whisper / emote), `WhisperToResult` (private chat line), `ShootProjectileResult`, `ThrowSpawnedResult`.

### Reproduce (new mobs and Qlippoths)
`SpawnMobResult`, `SpawnQlippothResult`, `SpawnCopyResult` (all with ghost role text, offspring tracking, `maxAlive`), `OfferGhostRoleResult`, `CullOffspringResult`.

### External movement
`TeleportResult` (targeting + destination), `SwapPlacesResult`, `ThrowResult` (away / toward / random / fixed), `PushResult`, `SetAnchoredResult`, `FaceResult`.

### Reaction (chemistry and atmos)
`HeatTileResult`, `HotspotResult`, `RemoveGasResult`, `SpillReagentResult`, `SmokeResult` (smoke / foam), `AddReagentResult` (bloodstream or a container solution), `EmptySolutionResult`, `HeatSolutionResult`, `SpillSolutionResult`.

### Conversion
`ReplaceEntityResult` (one prototype or a source->replacement map; keeps position, mind, state), `PolymorphResult`, `RevertPolymorphResult`, `ConvertTilesResult`, `AddComponentsResult` (give anything `QlippothActions` = make it a Qlippoth), `RemoveComponentsResult`, `TransferMindResult` (possess / absorb / swap), `EnthrallResult`.

### Event
`StartGameRuleResult`, `EndGameRuleResult`, `AnnounceResult`, `AlertLevelResult`, `CallShuttleResult`, `SignalResult`, `BreachGatesResult`.

### State
`SetStateResult`, `ToggleStateResult`, `IncrementStateResult`, `ClearStateResult`.

### Flow (control the chain)
`ChanceResult`, `RequireTargetResult`, `RequireStateResult`, `RequireSituationResult`, `RandomResult` (weighted pick), `GroupResult`, `ForEachTargetResult`, `RepeatResult`, `ChainResult`, `DelayResult`, `CancelDelayResult`, `ResetCooldownResult`, `StopResult`, `LogResult`.

See `QlippothShowcaseIdol` in `Resources/Prototypes/Entities/Qlippoths/qlippoths.yml` for an entity that uses most of these together.
