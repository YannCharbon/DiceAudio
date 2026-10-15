/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

global using DAImportItem = DiceAudio.Portability.ImportItem<DiceAudio.Portability.DAAssetKind>;
global using DAImportPlan = DiceAudio.Portability.ImportPlan<DiceAudio.Portability.DAAssetKind>;

namespace DiceAudio.Portability
{
    /// <summary>
    /// Compares a bundle with the library and proposes what to do with each
    /// asset (workplan §1.1, spec §4.1 and §4.2). Reads the library, writes
    /// nothing: the plan is only applied by <see cref="DAImportApplier"/>.
    /// </summary>
    public sealed class ConflictPlanner
    {
        private const string RootFolderName = ".";
        private static readonly StringComparison NameComparison = StringComparison.CurrentCultureIgnoreCase;

        private readonly DALibrarySnapshot _local;

        /// <summary>Who uses what, inside the file: for the warning shown when something used is skipped.</summary>
        private readonly Dictionary<DAImportItem, List<DAImportItem>> _dependents = new();

        /// <summary>A local scene record placed with the scenario item record that plays it (spec §4.1, Targets).</summary>
        private readonly Dictionary<DAImportItem, DAImportItem> _followers = new();

        /// <summary>Items whose new name the user typed: a change of the name affixes leaves them alone.</summary>
        private readonly HashSet<DAImportItem> _namedByHand = new(ReferenceEqualityComparer.Instance);

        public ConflictPlanner(DALibrarySnapshot local)
        {
            _local = local;
        }

        /// <summary>The suffix new names get by default: "Name (2)", as before the affixes existed.</summary>
        public const string DefaultNameSuffix = " (2)";

        /// <summary>Put before the name of every item imported under a new name (the wizard's "Names of copies" bar).</summary>
        public string NamePrefix { get; private set; } = "";

        /// <summary>Put after the name of every item imported under a new name.</summary>
        public string NameSuffix { get; private set; } = DefaultNameSuffix;

        /// <summary>Folder ids already used in the library: an imported folder that reuses one gets a new id.</summary>
        public IReadOnlySet<Guid> LocalFolderIds => _local.Folders.Select(f => f.Id).ToHashSet();

        // ── Choices (spec §4.1) ────────────────────────────────────────────

        /// <summary>A folder whose name matches several local folders but none exactly (spec §4.1).</summary>
        private static readonly IReadOnlyList<ImportChoice> AmbiguousFolderChoices = new[] { ImportChoice.ImportWithNewName, ImportChoice.Skip };

        public static IReadOnlyList<ImportChoice> ChoicesFor(DAAssetKind kind, ImportStatus status) => (kind, status) switch
        {
            (_, ImportStatus.UpToDate) => new[] { ImportChoice.Nothing },
            (DAAssetKind.Tag, ImportStatus.Differs) => new[] { ImportChoice.KeepLocal, ImportChoice.ReplaceLocal },
            (DAAssetKind.Folder, ImportStatus.Differs) => new[] { ImportChoice.ImportAsCopy, ImportChoice.Merge },
            (DAAssetKind.ScenarioGroup, ImportStatus.Differs) => new[] { ImportChoice.ImportAsCopy, ImportChoice.ReplaceLocal, ImportChoice.KeepLocal, ImportChoice.Merge },
            (_, ImportStatus.Differs) => new[] { ImportChoice.ImportAsCopy, ImportChoice.ReplaceLocal, ImportChoice.KeepLocal },
            (_, ImportStatus.NameUsed) => new[] { ImportChoice.ImportWithNewName, ImportChoice.KeepBothSameName, ImportChoice.UseLocal, ImportChoice.Skip },
            _ => new[] { ImportChoice.Import, ImportChoice.Skip },
        };

        public static ImportChoice DefaultChoice(DAAssetKind kind, ImportStatus status) => (kind, status) switch
        {
            (_, ImportStatus.UpToDate) => ImportChoice.Nothing,
            (DAAssetKind.Tag, ImportStatus.Differs) => ImportChoice.KeepLocal,
            (_, ImportStatus.Differs) => ImportChoice.ImportAsCopy,
            (_, ImportStatus.NameUsed) => ImportChoice.ImportWithNewName,
            _ => ImportChoice.Import,
        };

        // ── Analysis ───────────────────────────────────────────────────────

        public DAImportPlan Analyse(DABundle bundle)
        {
            var plan = new DAImportPlan();
            plan.Warnings.AddRange(bundle.Warnings);
            _dependents.Clear();
            _followers.Clear();

            var byIncoming = new Dictionary<object, DAImportItem>(ReferenceEqualityComparer.Instance);
            void Add(DAImportItem item)
            {
                plan.Items.Add(item);
                byIncoming[item.Incoming] = item;
            }

            foreach (var tag in bundle.Tags) Add(AnalyseTag(tag));

            var folderNames = bundle.Folders.ToDictionary(f => f.Id, f => f.Name);
            foreach (var folder in bundle.Folders) Add(AnalyseFolder(bundle, folder));

            foreach (var record in bundle.AudioItems)
            {
                string folderName = record.FolderId is Guid fid && folderNames.TryGetValue(fid, out var fn) ? fn : RootFolderName;
                Add(AnalyseAudioItem(record, folderName));
            }

            foreach (var scene in bundle.Scenes)
                Add(ById(DAAssetKind.Scene, scene, scene.Id, scene.Name, _local.Scenes.FirstOrDefault(s => s.Id == scene.Id),
                         () => _local.Scenes.FirstOrDefault(s => SameName(s.Name, scene.Name)),
                         l => DAFingerprint.Of((DAScene)l) == DAFingerprint.Of(scene)));

            foreach (var preset in bundle.Presets)
                Add(ById(DAAssetKind.Preset, preset, preset.Id, preset.Name, _local.Presets.FirstOrDefault(p => p.Id == preset.Id),
                         () => _local.Presets.FirstOrDefault(p => SameName(p.Name, preset.Name)),
                         l => DAFingerprint.Of((DAPreset)l) == DAFingerprint.Of(preset)));

            foreach (var group in bundle.ScenarioGroups)
                Add(ById(DAAssetKind.ScenarioGroup, group, group.Id, group.Name, _local.ScenarioGroups.FirstOrDefault(g => g.Id == group.Id),
                         () => _local.ScenarioGroups.FirstOrDefault(g => SameName(g.Name, group.Name)),
                         l => DAFingerprint.Of((DAScenarioGroup)l) == DAFingerprint.Of(group)));

            // Records: parts of a group exported without it. They need a target.
            foreach (var record in bundle.LocalScenes)
            {
                var item = NewTargetItem(DAAssetKind.LocalScene, record, record.Scene.Id, record.Scene.Name);
                Add(item);
                SetTargetCore(plan, item, DefaultGroupTarget(plan, record.SourceGroupId, record.SourceGroupName), null);
            }
            foreach (var record in bundle.Scenarios)
            {
                var item = NewTargetItem(DAAssetKind.Scenario, record, record.Scenario.Id, record.Scenario.Name);
                Add(item);
                SetTargetCore(plan, item, DefaultGroupTarget(plan, record.SourceGroupId, record.SourceGroupName), null);
            }
            foreach (var record in bundle.ScenarioItems)
            {
                var item = NewTargetItem(DAAssetKind.ScenarioItem, record, record.Item.Id, record.Item.Name);
                Add(item);
                var scenario = _local.ScenarioGroups.SelectMany(g => g.Scenarios).FirstOrDefault(s => s.Id == record.SourceScenarioId);
                SetTargetCore(plan, item, scenario?.Id ?? Guid.Empty, scenario == null ? record.SourceScenarioName : null);

                // The local scene record this item plays goes with it.
                if (record.Item.LocalSceneId is Guid sceneId)
                {
                    var follower = plan.Items.FirstOrDefault(i => i.Kind == DAAssetKind.LocalScene && i.IncomingId == sceneId);
                    if (follower != null)
                    {
                        _followers[follower] = item;
                        ApplyFollow(plan, item);
                    }
                }
            }

            BuildDependents(bundle, plan, byIncoming);

            // Defaults that rename (Import as a copy, Import with a new name) get
            // their unique name now that every scope is known, in plan order.
            foreach (var item in plan.Items.Where(i => i.NeedsNewName && i.NewName == null))
                item.NewName = DefaultNewName(plan, item);
            return plan;
        }

        private DAImportItem AnalyseTag(DATagRecord tag)
        {
            string? localCategory = _local.TypeTags.Contains(tag.Name) ? "Type"
                : _local.LocationTags.Contains(tag.Name) ? "Location"
                : _local.ExpressionTags.Contains(tag.Name) ? "Expression" : null;

            // A tag the exporter had in no list says nothing about its category,
            // and importing it would add it to no list: nothing to do then.
            var status = localCategory == null ? (tag.Category == "Unknown" ? ImportStatus.UpToDate : ImportStatus.New)
                : localCategory == tag.Category || tag.Category == "Unknown" ? ImportStatus.UpToDate
                : ImportStatus.Differs;
            return NewItem(DAAssetKind.Tag, tag, null, tag.Name, status, localCategory);
        }

        private DAImportItem AnalyseFolder(DABundle bundle, DAFolderRecord folder)
        {
            var exact = _local.Folders.Where(f => f.Name == folder.Name).ToList();
            var loose = exact.Count > 0 ? exact : _local.Folders.Where(f => SameName(f.Name, folder.Name)).ToList();

            if (loose.Count > 1)
            {
                // Ambiguous: several local folders differ from this name only by
                // case. None is the match, and the name must change, so the user
                // sees it: Import with a new name (pre-filled, unique), or Skip.
                return new DAImportItem
                {
                    Kind = DAAssetKind.Folder, Incoming = folder, IncomingId = folder.Id, IncomingName = folder.Name,
                    Status = ImportStatus.NameUsed, AllowedChoices = AmbiguousFolderChoices,
                    Choice = ImportChoice.ImportWithNewName,
                };
            }

            if (loose.Count == 0) return NewItem(DAAssetKind.Folder, folder, folder.Id, folder.Name, ImportStatus.New, null);

            var local = loose[0];
            bool samePath = BundleParentPath(bundle, folder).SequenceEqual(LocalParentPath(local), StringComparer.CurrentCultureIgnoreCase);
            return NewItem(DAAssetKind.Folder, folder, folder.Id, folder.Name, samePath ? ImportStatus.UpToDate : ImportStatus.Differs, local);
        }

        private DAImportItem AnalyseAudioItem(DAAudioItemRecord record, string folderName)
        {
            var byId = _local.AudioItems.FirstOrDefault(i => i.Id == record.Id);
            if (byId != null)
            {
                bool same = DAFingerprint.Of(byId) == DAFingerprint.Of(record, folderName);
                return NewItem(DAAssetKind.AudioItem, record, record.Id, record.Name, same ? ImportStatus.UpToDate : ImportStatus.Differs, byId);
            }

            var byName = _local.AudioItems.FirstOrDefault(i => SameName(i.Name, record.Name) && SameName(LocalFolderName(i), folderName));
            return byName != null
                ? NewItem(DAAssetKind.AudioItem, record, record.Id, record.Name, ImportStatus.NameUsed, byName)
                : NewItem(DAAssetKind.AudioItem, record, record.Id, record.Name, ImportStatus.New, null);
        }

        private static DAImportItem ById(DAAssetKind kind, object incoming, Guid id, string name, object? byId,
                                        Func<object?> byName, Func<object, bool> sameContent)
        {
            if (byId != null)
                return NewItem(kind, incoming, id, name, sameContent(byId) ? ImportStatus.UpToDate : ImportStatus.Differs, byId);
            var named = byName();
            return named != null
                ? NewItem(kind, incoming, id, name, ImportStatus.NameUsed, named)
                : NewItem(kind, incoming, id, name, ImportStatus.New, null);
        }

        private static DAImportItem NewItem(DAAssetKind kind, object incoming, Guid? id, string name, ImportStatus status, object? local,
                                            DAImportItem? parent = null) => new()
        {
            Kind = kind, Incoming = incoming, IncomingId = id, IncomingName = name, Local = local,
            Status = status, AllowedChoices = ChoicesFor(kind, status), Choice = DefaultChoice(kind, status), Parent = parent,
        };

        private static DAImportItem NewTargetItem(DAAssetKind kind, object incoming, Guid id, string name) => new()
        {
            Kind = kind, Incoming = incoming, IncomingId = id, IncomingName = name,
            Status = ImportStatus.New, AllowedChoices = ChoicesFor(kind, ImportStatus.New), Choice = ImportChoice.Import,
            NeedsTarget = true,
        };

        // ── Targets (spec §4.1) ────────────────────────────────────────────

        /// <summary>The local group with this id, else a new group named after the one the record came from.</summary>
        private Guid DefaultGroupTarget(DAImportPlan plan, Guid sourceGroupId, string sourceGroupName) =>
            _local.ScenarioGroups.Any(g => g.Id == sourceGroupId) ? sourceGroupId : Guid.Empty;

        /// <summary>The name a new group made for records of <paramref name="sourceGroupName"/> gets: unique among the groups.</summary>
        public string NewGroupName(DAImportPlan plan, string sourceGroupName)
        {
            string wanted = string.IsNullOrWhiteSpace(sourceGroupName) ? "Imported scenario group" : sourceGroupName.Trim();
            // Other records of the same source share the new group, so their name is not "taken".
            return Unique(wanted, n => _local.ScenarioGroups.Any(g => SameName(g.Name, n))
                                       || plan.AllItems.Any(i => i.Kind == DAAssetKind.ScenarioGroup && i.Inserts && SameName(ResultName(i), n)));
        }

        /// <summary>
        /// Points a record at a group (local scenes, scenarios) or a scenario
        /// (scenario items): <paramref name="targetId"/> = that container, or
        /// <see cref="Guid.Empty"/> for a new one named <paramref name="newName"/>.
        /// The status is worked out again in the new scope.
        /// </summary>
        public void SetTarget(DAImportPlan plan, DAImportItem item, Guid? targetId, string? newName)
        {
            if (_followers.ContainsKey(item)) return;   // placed with the scenario item that plays it
            SetTargetCore(plan, item, targetId, newName);
            ApplyFollow(plan, item);
        }

        /// <summary>The local scene a scenario item record plays follows it into the same group.</summary>
        public bool Follows(DAImportItem item, out DAImportItem? leader) => _followers.TryGetValue(item, out leader);

        private void ApplyFollow(DAImportPlan plan, DAImportItem leader)
        {
            foreach (var (follower, lead) in _followers)
            {
                if (lead != leader) continue;
                var record = (DAScenarioItemRecord)leader.Incoming;
                Guid groupId = Guid.Empty;
                if (leader.TargetContainerId is Guid scenarioId && scenarioId != Guid.Empty)
                    groupId = _local.ScenarioGroups.FirstOrDefault(g => g.Scenarios.Any(s => s.Id == scenarioId))?.Id ?? Guid.Empty;
                SetTargetCore(plan, follower, groupId, groupId == Guid.Empty ? NewGroupName(plan, record.SourceGroupName) : null);
            }
        }

        private void SetTargetCore(DAImportPlan plan, DAImportItem item, Guid? targetId, string? newName)
        {
            item.TargetContainerId = targetId;
            if (targetId == Guid.Empty)
            {
                item.TargetContainerNewName = !string.IsNullOrWhiteSpace(newName) ? newName.Trim()
                    : item.Incoming switch
                    {
                        DALocalSceneRecord r => NewGroupName(plan, r.SourceGroupName),
                        DAScenarioRecord r => NewGroupName(plan, r.SourceGroupName),
                        DAScenarioItemRecord r => string.IsNullOrWhiteSpace(r.SourceScenarioName) ? "Imported scenario" : r.SourceScenarioName,
                        _ => null,
                    };
            }
            else item.TargetContainerNewName = null;

            // Status in the new scope: GUID first, then the name.
            object? local = null;
            var status = ImportStatus.New;
            if (targetId is Guid t && t != Guid.Empty)
            {
                switch (item.Incoming)
                {
                    case DALocalSceneRecord r when _local.ScenarioGroups.FirstOrDefault(g => g.Id == t) is { } g:
                        local = g.LocalScenes.FirstOrDefault(s => s.Id == r.Scene.Id);
                        status = local != null ? (DAFingerprint.Of((DAScene)local) == DAFingerprint.Of(r.Scene) ? ImportStatus.UpToDate : ImportStatus.Differs)
                               : (local = g.LocalScenes.FirstOrDefault(s => SameName(s.Name, r.Scene.Name))) != null ? ImportStatus.NameUsed : ImportStatus.New;
                        break;
                    case DAScenarioRecord r when _local.ScenarioGroups.FirstOrDefault(g => g.Id == t) is { } g:
                        local = g.Scenarios.FirstOrDefault(s => s.Id == r.Scenario.Id);
                        status = local != null ? (DAFingerprint.Of((DAScenario)local) == DAFingerprint.Of(r.Scenario) ? ImportStatus.UpToDate : ImportStatus.Differs)
                               : (local = g.Scenarios.FirstOrDefault(s => SameName(s.Name, r.Scenario.Name))) != null ? ImportStatus.NameUsed : ImportStatus.New;
                        break;
                    case DAScenarioItemRecord r when _local.ScenarioGroups.SelectMany(g => g.Scenarios).FirstOrDefault(s => s.Id == t) is { } s:
                        local = s.Items.FirstOrDefault(i => i.Id == r.Item.Id);
                        status = local != null ? (DAFingerprint.Of((DAScenarioItem)local) == DAFingerprint.Of(r.Item) ? ImportStatus.UpToDate : ImportStatus.Differs)
                               : (local = s.Items.FirstOrDefault(i => SameName(i.Name, r.Item.Name))) != null ? ImportStatus.NameUsed : ImportStatus.New;
                        break;
                }
            }

            item.Local = local;
            item.Status = status;
            item.AllowedChoices = ChoicesFor(item.Kind, status);
            item.Choice = DefaultChoice(item.Kind, status);
            _namedByHand.Remove(item);
            item.NewName = item.NeedsNewName ? DefaultNewName(plan, item) : null;
        }

        // ── Choices and names ──────────────────────────────────────────────

        /// <summary>
        /// Sets a choice, gives it a suggested new name when it needs one (a
        /// valid name typed by hand is kept), and expands or clears a Merge.
        /// </summary>
        public void SetChoice(DAImportPlan plan, DAImportItem item, ImportChoice choice)
        {
            if (!item.AllowedChoices.Contains(choice)) return;
            item.Choice = choice;
            if (item.NeedsNewName && !KeepsTypedName(plan, item)) item.NewName = DefaultNewName(plan, item);
            ExpandMerge(plan, item);
        }

        private bool KeepsTypedName(DAImportPlan plan, DAImportItem item) =>
            _namedByHand.Contains(item) && item.NewName != null && IsNameAvailable(plan, item, item.NewName);

        /// <summary>A new name the user typed: kept as it is when the name affixes change.</summary>
        public void SetNewName(DAImportItem item, string? name)
        {
            item.NewName = name;
            _namedByHand.Add(item);
        }

        public bool IsNamedByHand(DAImportItem item) => _namedByHand.Contains(item);

        /// <summary>
        /// Changes the text put before and after the names of the items
        /// imported under a new name, and gives each of them, except those
        /// named by hand, its new default name, made unique in plan order.
        /// </summary>
        public void SetNameAffixes(DAImportPlan plan, string? prefix, string? suffix)
        {
            NamePrefix = prefix ?? "";
            NameSuffix = suffix ?? "";
            RenameSuggested(plan, plan.AllItems.Where(i => i.NeedsNewName && !_namedByHand.Contains(i)).ToList());
        }

        /// <summary>The name an item imported under a new name gets unless the user types one: the affixes around its trimmed name, made unique.</summary>
        public string DefaultNewName(DAImportPlan plan, DAImportItem item) =>
            MakeUniqueName(plan, item, NamePrefix + item.IncomingName.Trim() + NameSuffix);

        // Suggests the names again, in plan order. The old names are moved out
        // of the way first, so that a name about to change never blocks another.
        private void RenameSuggested(DAImportPlan plan, IReadOnlyList<DAImportItem> items)
        {
            foreach (var item in items) item.NewName = Guid.NewGuid().ToString();
            foreach (var item in items) item.NewName = DefaultNewName(plan, item);
        }

        /// <summary>
        /// Fills the children of a group being merged: its scenarios and local
        /// scenes, each compared within the local group (no Merge at that
        /// level). Clears them for any other choice. Folders merge by their
        /// contents, which are already items of the plan.
        /// </summary>
        public void ExpandMerge(DAImportPlan plan, DAImportItem container)
        {
            container.Children.Clear();
            if (container.Choice != ImportChoice.Merge || container.Kind != DAAssetKind.ScenarioGroup) return;
            if (container.Incoming is not DAScenarioGroup incoming || container.Local is not DAScenarioGroup local) return;

            foreach (var scenario in incoming.Scenarios)
            {
                var byId = local.Scenarios.FirstOrDefault(s => s.Id == scenario.Id);
                var status = byId != null ? (DAFingerprint.Of(byId) == DAFingerprint.Of(scenario) ? ImportStatus.UpToDate : ImportStatus.Differs)
                           : local.Scenarios.Any(s => SameName(s.Name, scenario.Name)) ? ImportStatus.NameUsed : ImportStatus.New;
                object? match = byId ?? local.Scenarios.FirstOrDefault(s => SameName(s.Name, scenario.Name));
                container.Children.Add(NewItem(DAAssetKind.Scenario, scenario, scenario.Id, scenario.Name, status, match, container));
            }
            foreach (var scene in incoming.LocalScenes)
            {
                var byId = local.LocalScenes.FirstOrDefault(s => s.Id == scene.Id);
                var status = byId != null ? (DAFingerprint.Of(byId) == DAFingerprint.Of(scene) ? ImportStatus.UpToDate : ImportStatus.Differs)
                           : local.LocalScenes.Any(s => SameName(s.Name, scene.Name)) ? ImportStatus.NameUsed : ImportStatus.New;
                object? match = byId ?? local.LocalScenes.FirstOrDefault(s => SameName(s.Name, scene.Name));
                container.Children.Add(NewItem(DAAssetKind.LocalScene, scene, scene.Id, scene.Name, status, match, container));
            }
            foreach (var child in container.Children)
                if (child.NeedsNewName) child.NewName = DefaultNewName(plan, child);
        }

        /// <summary>
        /// "Same choice for all…": the same choice on every item of this kind
        /// and status that allows it. Names typed by hand are kept while they
        /// are valid; the others are suggested again.
        /// </summary>
        public void ApplyToAllOfKind(DAImportPlan plan, DAAssetKind kind, ImportStatus status, ImportChoice choice)
        {
            var items = plan.AllItems.Where(i => i.Kind == kind && i.Status == status && i.AllowedChoices.Contains(choice)).ToList();
            foreach (var item in items)
            {
                item.Choice = choice;
                ExpandMerge(plan, item);
            }
            RenameSuggested(plan, items.Where(i => i.NeedsNewName && !KeepsTypedName(plan, i)).ToList());
        }

        /// <summary>The items "Same choice for all…" is about: those of the same kind and status.</summary>
        public static IReadOnlyList<DAImportItem> SameKindAndStatus(DAImportPlan plan, DAImportItem item) =>
            plan.AllItems.Where(i => i.Kind == item.Kind && i.Status == item.Status).ToList();

        /// <summary>
        /// The rename rules (spec §4.1): trimmed, not empty, not "." for a
        /// folder, unique in its scope among the library's assets and among
        /// the names this import will create.
        /// </summary>
        public bool IsNameAvailable(DAImportPlan plan, DAImportItem item, string name)
        {
            name = name?.Trim() ?? "";
            if (name.Length == 0) return false;
            if (item.Kind == DAAssetKind.Folder && name == RootFolderName) return false;
            if (LocalNamesInScope(item).Any(n => SameName(n, name))) return false;

            string scope = ScopeKey(item);
            return !plan.AllItems.Any(other => !ReferenceEquals(other, item) && other.Kind == item.Kind && other.Inserts
                                               && ScopeKey(other) == scope && SameName(ResultName(other), name));
        }

        /// <summary><paramref name="wanted"/>, else "wanted (2)", "(3)"… — the first name that is available.</summary>
        public string MakeUniqueName(DAImportPlan plan, DAImportItem item, string wanted)
        {
            wanted = string.IsNullOrWhiteSpace(wanted) ? item.IncomingName.Trim() : wanted.Trim();
            if (IsNameAvailable(plan, item, wanted)) return wanted;
            // A name that already ends with a number in brackets counts on from
            // it: "name (2)" is followed by "name (3)", never "name (2) (2)".
            string stem = wanted;
            int n = 2;
            var numbered = System.Text.RegularExpressions.Regex.Match(wanted, @"^(.*\S)\s*\((\d{1,6})\)$");
            if (numbered.Success)
            {
                stem = numbered.Groups[1].Value;
                n = int.Parse(numbered.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) + 1;
            }
            for (; ; n++)
            {
                string candidate = $"{stem} ({n})";
                if (IsNameAvailable(plan, item, candidate)) return candidate;
            }
        }

        /// <summary>The name an inserted item will carry in the library.</summary>
        public static string ResultName(DAImportItem item) =>
            item.NeedsNewName && !string.IsNullOrWhiteSpace(item.NewName) ? item.NewName!.Trim() : item.IncomingName;

        private string ScopeKey(DAImportItem item) => item.Kind switch
        {
            DAAssetKind.Folder => "folders",
            DAAssetKind.AudioItem => "items:" + IncomingFolderName(item).ToLowerInvariant(),
            DAAssetKind.Scene => "scenes",
            DAAssetKind.Preset => "presets",
            DAAssetKind.ScenarioGroup => "groups",
            _ when item.Parent?.Local is DAScenarioGroup g => $"{item.Kind}:group:{g.Id}",
            _ => $"{item.Kind}:{item.TargetContainerId}:{item.TargetContainerNewName}",
        };

        private IEnumerable<string> LocalNamesInScope(DAImportItem item)
        {
            switch (item.Kind)
            {
                case DAAssetKind.Folder: return _local.Folders.Select(f => f.Name);
                case DAAssetKind.AudioItem:
                    string folder = IncomingFolderName(item);
                    return _local.AudioItems.Where(i => SameName(LocalFolderName(i), folder)).Select(i => i.Name);
                case DAAssetKind.Scene: return _local.Scenes.Select(s => s.Name);
                case DAAssetKind.Preset: return _local.Presets.Select(p => p.Name);
                case DAAssetKind.ScenarioGroup: return _local.ScenarioGroups.Select(g => g.Name);
            }

            if (item.Parent?.Local is DAScenarioGroup parentGroup)
                return item.Kind == DAAssetKind.Scenario ? parentGroup.Scenarios.Select(s => s.Name) : parentGroup.LocalScenes.Select(s => s.Name);

            if (item.TargetContainerId is not Guid t || t == Guid.Empty) return Enumerable.Empty<string>();
            return item.Kind switch
            {
                DAAssetKind.LocalScene => _local.ScenarioGroups.FirstOrDefault(g => g.Id == t)?.LocalScenes.Select(s => s.Name) ?? Enumerable.Empty<string>(),
                DAAssetKind.Scenario => _local.ScenarioGroups.FirstOrDefault(g => g.Id == t)?.Scenarios.Select(s => s.Name) ?? Enumerable.Empty<string>(),
                DAAssetKind.ScenarioItem => _local.ScenarioGroups.SelectMany(g => g.Scenarios).FirstOrDefault(s => s.Id == t)?.Items.Select(i => i.Name) ?? Enumerable.Empty<string>(),
                _ => Enumerable.Empty<string>(),
            };
        }

        /// <summary>The name of the folder an incoming audio item belongs to ("." for the root), as the file names it.</summary>
        private string IncomingFolderName(DAImportItem item) => item.Incoming is DAAudioItemRecord r ? _recordFolderNames.GetValueOrDefault(r.Id, RootFolderName) : RootFolderName;

        private readonly Dictionary<Guid, string> _recordFolderNames = new();

        // ── Dependents (the skip warning) ──────────────────────────────────

        /// <summary>The items this import inserts that use <paramref name="item"/>.</summary>
        public IReadOnlyList<DAImportItem> DependentsOf(DAImportPlan plan, DAImportItem item) =>
            _dependents.TryGetValue(item, out var list) ? list.Where(d => d.Inserts).Distinct().ToList() : new List<DAImportItem>();

        private void BuildDependents(DABundle bundle, DAImportPlan plan, Dictionary<object, DAImportItem> byIncoming)
        {
            var itemsById = plan.Items.Where(i => i.Kind == DAAssetKind.AudioItem).ToDictionary(i => i.IncomingId!.Value);
            var foldersById = plan.Items.Where(i => i.Kind == DAAssetKind.Folder).ToDictionary(i => i.IncomingId!.Value);
            var tagsByName = plan.Items.Where(i => i.Kind == DAAssetKind.Tag).ToDictionary(i => i.IncomingName, StringComparer.Ordinal);
            var localScenesById = plan.Items.Where(i => i.Kind == DAAssetKind.LocalScene).ToDictionary(i => i.IncomingId!.Value);

            void Uses(DAImportItem? used, DAImportItem user)
            {
                if (used == null || ReferenceEquals(used, user)) return;
                if (!_dependents.TryGetValue(used, out var list)) _dependents[used] = list = new();
                list.Add(user);
            }
            void UsesAudio(IEnumerable<DAAudioUsage> usages, DAImportItem user)
            {
                foreach (var u in usages) Uses(itemsById.GetValueOrDefault(u.AudioItemId), user);
            }

            foreach (var f in bundle.Folders)
            {
                _recordFolderNames.TryAdd(f.Id, f.Name);
                if (f.ParentId is Guid p) Uses(foldersById.GetValueOrDefault(p), byIncoming[f]);
            }
            foreach (var r in bundle.AudioItems)
            {
                var user = byIncoming[r];
                _recordFolderNames[r.Id] = r.FolderId is Guid fid && bundle.Folders.FirstOrDefault(f => f.Id == fid) is { } fr ? fr.Name : RootFolderName;
                if (r.FolderId is Guid folder) Uses(foldersById.GetValueOrDefault(folder), user);
                foreach (var t in r.Tags) Uses(tagsByName.GetValueOrDefault(t), user);
            }
            foreach (var s in bundle.Scenes) UsesAudio(DABundleWalk.Usages(s), byIncoming[s]);
            foreach (var p in bundle.Presets) UsesAudio(DABundleWalk.Usages(p), byIncoming[p]);
            foreach (var g in bundle.ScenarioGroups)
            {
                UsesAudio(DABundleWalk.Usages(g), byIncoming[g]);
                foreach (var t in g.Scenarios.SelectMany(s => s.Items).SelectMany(DABundleWalk.CriteriaTags)) Uses(tagsByName.GetValueOrDefault(t), byIncoming[g]);
            }
            foreach (var r in bundle.LocalScenes) UsesAudio(DABundleWalk.Usages(r.Scene), byIncoming[r]);
            foreach (var r in bundle.Scenarios)
            {
                UsesAudio(DABundleWalk.Usages(r.Scenario), byIncoming[r]);
                foreach (var i in r.Scenario.Items)
                {
                    if (i.LocalSceneId is Guid ls) Uses(localScenesById.GetValueOrDefault(ls), byIncoming[r]);
                    foreach (var t in DABundleWalk.CriteriaTags(i)) Uses(tagsByName.GetValueOrDefault(t), byIncoming[r]);
                }
            }
            foreach (var r in bundle.ScenarioItems)
            {
                UsesAudio(DABundleWalk.Usages(r.Item), byIncoming[r]);
                if (r.Item.LocalSceneId is Guid ls) Uses(localScenesById.GetValueOrDefault(ls), byIncoming[r]);
                foreach (var t in DABundleWalk.CriteriaTags(r.Item)) Uses(tagsByName.GetValueOrDefault(t), byIncoming[r]);
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────

        private static bool SameName(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), NameComparison);

        private static string Unique(string wanted, Func<string, bool> taken)
        {
            if (!taken(wanted)) return wanted;
            for (int n = 2; ; n++)
                if (!taken($"{wanted} ({n})")) return $"{wanted} ({n})";
        }

        private string LocalFolderName(DAAudioItem item)
        {
            string? name = item.Folder?.Name;
            return string.IsNullOrEmpty(name) || name == RootFolderName || !_local.Folders.Any(f => f.Name == name) ? RootFolderName : name;
        }

        private List<string> LocalParentPath(DAVirtualAudioFolder folder)
        {
            var path = new List<string>();
            var seen = new HashSet<Guid> { folder.Id };
            var parentId = folder.ParentFolderId;
            while (parentId != Guid.Empty && _local.Folders.FirstOrDefault(f => f.Id == parentId) is { } parent && seen.Add(parent.Id))
            {
                path.Insert(0, parent.Name);
                parentId = parent.ParentFolderId;
            }
            return path;
        }

        private static List<string> BundleParentPath(DABundle bundle, DAFolderRecord folder)
        {
            var path = new List<string>();
            var seen = new HashSet<Guid> { folder.Id };
            var parentId = folder.ParentId;
            while (parentId is Guid p && bundle.Folders.FirstOrDefault(f => f.Id == p) is { } parent && seen.Add(parent.Id))
            {
                path.Insert(0, parent.Name);
                parentId = parent.ParentId;
            }
            return path;
        }
    }
}
