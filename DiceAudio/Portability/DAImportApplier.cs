/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Text.Json;

namespace DiceAudio.Portability
{
    /// <summary>
    /// The library an import writes into: its lists, changed in place, and the
    /// way an audio item joins it. DiceAudioService and the tag lists in the
    /// app; plain lists in the tests. Used on the UI thread only.
    /// </summary>
    public interface IDAImportTarget
    {
        List<DAAudioItem> AudioItems { get; }
        List<DAVirtualAudioFolder> Folders { get; }
        List<DAScene> Scenes { get; }
        List<DAScenarioGroup> ScenarioGroups { get; }
        List<DAPreset> Presets { get; }
        List<string> TypeTags { get; }
        List<string> LocationTags { get; }
        List<string> ExpressionTags { get; }

        /// <summary>Adds an item, resolving its folder by name to the library's own folder (DiceAudioService.AddAudioItem).</summary>
        void AddAudioItem(DAAudioItem item);
    }

    /// <summary>What an import did, and where each imported audio item ended up.</summary>
    public sealed class DAImportResult
    {
        public required ImportReport Report { get; init; }
        public required IdRemapper Map { get; init; }

        /// <summary>The library item each audio item of the file ended up as, by its id in the file. Skipped ones are absent.</summary>
        public required IReadOnlyDictionary<Guid, DAAudioItem> FinalItems { get; init; }
    }

    /// <summary>The import failed and the library was put back as it was before it.</summary>
    public sealed class DAImportFailedException : Exception
    {
        public DAImportFailedException(Exception inner) : base(inner.Message, inner) { }
    }

    /// <summary>
    /// The import failed, and saving the library as it was before failed too:
    /// the data from just before the import is in the backup named here.
    /// </summary>
    public sealed class DAImportRollbackFailedException : Exception
    {
        public DAImportRollbackFailedException(string? backupFileName, Exception inner) : base(inner.Message, inner)
        {
            BackupFileName = backupFileName;
        }

        public string? BackupFileName { get; }
    }

    /// <summary>
    /// A data file of the library could not be read at startup, so it is not
    /// saved in this session: an import, which must save everything, is
    /// refused before anything is changed.
    /// </summary>
    public sealed class DAImportBlockedException : Exception
    {
        public DAImportBlockedException(string fileName) : base($"{fileName} could not be read.") { FileName = fileName; }
        public string FileName { get; }
    }

    /// <summary>
    /// Puts a plan into the library (pipeline step 4, spec §3.2 and §4.3), in
    /// the order of spec §3.2: tags, folders, audio items, catalogue scenes,
    /// presets, scenario groups, then the parts of groups into their targets.
    /// Every object inserted is a copy of the bundle's, after the GUID map has
    /// been applied. Changes the lists only: saving is the runner's job.
    /// </summary>
    public static class DAImportApplier
    {
        private const string RootFolderName = ".";

        public static DAImportResult Apply(DAImportPlan plan, DABundle bundle, IDAImportTarget target,
                                           IReadOnlySet<Guid>? localFolderIds = null, Func<Guid>? newGuid = null)
        {
            var map = IdRemapper.BuildFrom(plan, newGuid, localFolderIds);
            map.Rewrite(bundle);

            var problems = new List<string>();
            var itemOf = new Dictionary<object, DAImportItem>(ReferenceEqualityComparer.Instance);
            foreach (var item in plan.AllItems) itemOf[item.Incoming] = item;
            DAImportItem? ItemFor(object incoming) => itemOf.GetValueOrDefault(incoming);

            // ── Tags ──
            foreach (var record in bundle.Tags)
            {
                var item = ItemFor(record);
                if (item == null) continue;
                if (item.Choice == ImportChoice.Import) AddTag(target, record.Name, record.Category);
                else if (item.Choice == ImportChoice.ReplaceLocal)
                {
                    target.TypeTags.Remove(record.Name);
                    target.LocationTags.Remove(record.Name);
                    target.ExpressionTags.Remove(record.Name);
                    AddTag(target, record.Name, record.Category);
                }
            }

            // ── Folders, parents first ──
            var resolved = new Dictionary<Guid, DAVirtualAudioFolder?>();
            foreach (var record in bundle.Folders.OrderBy(f => Depth(bundle, f)))
            {
                var item = ItemFor(record);
                DAVirtualAudioFolder? parent = record.ParentId is Guid p && resolved.TryGetValue(p, out var rp) ? rp : null;
                DAVirtualAudioFolder? result;
                switch (item?.Choice)
                {
                    case ImportChoice.Nothing or ImportChoice.Merge when item.Local is DAVirtualAudioFolder local:
                        result = target.Folders.FirstOrDefault(f => f.Id == local.Id) ?? local;
                        break;
                    case ImportChoice.Import or ImportChoice.ImportAsCopy or ImportChoice.ImportWithNewName:
                        string name = ConflictPlanner.ResultName(item);
                        name = UniqueFolderName(target, name);
                        result = new DAVirtualAudioFolder(name) { Id = record.Id, ParentFolderId = parent?.Id ?? Guid.Empty };
                        if (target.Folders.Any(f => f.Id == result.Id)) result.Id = (newGuid ?? Guid.NewGuid)();
                        target.Folders.Add(result);
                        break;
                    default:   // Skip: its contents go where its parent went
                        result = parent;
                        break;
                }
                resolved[record.Id] = result;
            }

            // ── Audio items ──
            var finalItems = new Dictionary<Guid, DAAudioItem>();
            foreach (var record in bundle.AudioItems)
            {
                var item = ItemFor(record);
                if (item == null || item.IncomingId is not Guid originalId) continue;
                var folder = record.FolderId is Guid fid && resolved.TryGetValue(fid, out var rf) ? rf : null;

                switch (item.Choice)
                {
                    case ImportChoice.Import or ImportChoice.ImportAsCopy or ImportChoice.ImportWithNewName or ImportChoice.KeepBothSameName:
                        var created = new DAAudioItem(ConflictPlanner.ResultName(item)) { Id = record.Id };
                        Describe(created, record, folder);
                        target.AddAudioItem(created);
                        finalItems[originalId] = target.AudioItems.FirstOrDefault(i => i.Id == created.Id) ?? created;
                        break;
                    case ImportChoice.ReplaceLocal when target.AudioItems.FirstOrDefault(i => i.Id == record.Id) is { } local:
                        // A full override of what describes it; its file stays its own.
                        local.Name = record.Name;
                        Describe(local, record, folder);
                        finalItems[originalId] = local;
                        break;
                    case ImportChoice.Skip:
                        break;
                    default:   // Nothing, KeepLocal, UseLocal: the local one
                        Guid localId = map.Resolve(record.Id);
                        if (target.AudioItems.FirstOrDefault(i => i.Id == localId) is { } kept) finalItems[originalId] = kept;
                        break;
                }
            }

            // ── Catalogue scenes and presets ──
            foreach (var scene in bundle.Scenes) InsertOrReplace(ItemFor(scene), scene, target.Scenes, s => s.Id, (s, n) => s.Name = n);
            foreach (var preset in bundle.Presets) InsertOrReplace(ItemFor(preset), preset, target.Presets, p => p.Id, (p, n) => p.Name = n);

            // ── Scenario groups ──
            foreach (var group in bundle.ScenarioGroups)
            {
                var item = ItemFor(group);
                if (item?.Choice == ImportChoice.Merge && target.ScenarioGroups.FirstOrDefault(g => g.Id == group.Id) is { } local)
                {
                    // The local group keeps its name, collection, settings and order; new parts go at the end.
                    foreach (var child in item.Children)
                    {
                        if (child.Incoming is DAScenario s) InsertOrReplace(child, s, local.Scenarios, x => x.Id, (x, n) => x.Name = n);
                        else if (child.Incoming is DAScene sc) InsertOrReplace(child, sc, local.LocalScenes, x => x.Id, (x, n) => x.Name = n);
                    }
                }
                else InsertOrReplace(item, group, target.ScenarioGroups, g => g.Id, (g, n) => g.Name = n);
            }

            // ── Parts of groups, into the containers the user chose ──
            var newGroupsByName = new Dictionary<string, DAScenarioGroup>(StringComparer.CurrentCultureIgnoreCase);
            var newGroupsBySource = new Dictionary<Guid, DAScenarioGroup>();
            DAScenarioGroup? GroupFor(DAImportItem item, Guid sourceGroupId)
            {
                if (item.TargetContainerId is Guid t && t != Guid.Empty)
                    return target.ScenarioGroups.FirstOrDefault(g => g.Id == t);
                string name = string.IsNullOrWhiteSpace(item.TargetContainerNewName) ? "Imported scenario group" : item.TargetContainerNewName!;
                if (!newGroupsByName.TryGetValue(name, out var group))
                {
                    group = new DAScenarioGroup { Name = name, Scenarios = new(), LocalScenes = new() };
                    target.ScenarioGroups.Add(group);
                    newGroupsByName[name] = group;
                }
                newGroupsBySource.TryAdd(sourceGroupId, group);
                return group;
            }

            foreach (var record in bundle.LocalScenes)
            {
                var item = ItemFor(record);
                if (item == null || !item.Inserts) continue;
                var group = GroupFor(item, record.SourceGroupId);
                if (group == null) { problems.Add($"The scenario group chosen for \"{record.Scene.Name}\" no longer exists."); continue; }
                InsertOrReplace(item, record.Scene, group.LocalScenes, s => s.Id, (s, n) => s.Name = n);
            }

            foreach (var record in bundle.Scenarios)
            {
                var item = ItemFor(record);
                if (item == null || !item.Inserts) continue;
                var group = GroupFor(item, record.SourceGroupId);
                if (group == null) { problems.Add($"The scenario group chosen for \"{record.Scenario.Name}\" no longer exists."); continue; }
                InsertOrReplace(item, record.Scenario, group.Scenarios, s => s.Id, (s, n) => s.Name = n);
            }

            var newScenarios = new Dictionary<(Guid, string), DAScenario>();
            foreach (var record in bundle.ScenarioItems)
            {
                var item = ItemFor(record);
                if (item == null || !item.Inserts) continue;

                DAScenario? scenario;
                if (item.TargetContainerId is Guid t && t != Guid.Empty)
                {
                    scenario = target.ScenarioGroups.SelectMany(g => g.Scenarios).FirstOrDefault(s => s.Id == t);
                }
                else
                {
                    // A new scenario, in the group made for the records of its source group,
                    // else in a new group of its own.
                    if (!newGroupsBySource.TryGetValue(record.SourceGroupId, out var group))
                    {
                        string groupName = UniqueGroupName(target, string.IsNullOrWhiteSpace(record.SourceGroupName) ? "Imported scenario group" : record.SourceGroupName);
                        group = new DAScenarioGroup { Name = groupName, Scenarios = new(), LocalScenes = new() };
                        target.ScenarioGroups.Add(group);
                        newGroupsBySource[record.SourceGroupId] = group;
                    }
                    string scenarioName = string.IsNullOrWhiteSpace(item.TargetContainerNewName) ? record.SourceScenarioName : item.TargetContainerNewName!;
                    if (!newScenarios.TryGetValue((group.Id, scenarioName), out scenario))
                    {
                        scenario = new DAScenario { Name = scenarioName, Items = new() };
                        group.Scenarios.Add(scenario);
                        newScenarios[(group.Id, scenarioName)] = scenario;
                    }
                }
                if (scenario == null) { problems.Add($"The scenario chosen for \"{record.Item.Name}\" no longer exists."); continue; }
                InsertOrReplace(item, record.Item, scenario.Items, i => i.Id, (i, n) => i.Name = n);
            }

            return new DAImportResult { Report = Count(plan, problems), Map = map, FinalItems = finalItems };
        }

        /// <summary>
        /// Inserts a copy, renamed when the choice says so, or replaces the
        /// local asset with the same id. Keep, use-local, skip and up-to-date
        /// leave the list as it is.
        /// </summary>
        private static void InsertOrReplace<T>(DAImportItem? item, T incoming, List<T> list, Func<T, Guid> id, Action<T, string> rename)
        {
            if (item == null) return;
            switch (item.Choice)
            {
                case ImportChoice.Import or ImportChoice.ImportAsCopy or ImportChoice.ImportWithNewName or ImportChoice.KeepBothSameName:
                    var copy = Clone(incoming);
                    rename(copy, ConflictPlanner.ResultName(item));
                    list.Add(copy);
                    break;
                case ImportChoice.ReplaceLocal:
                    int index = list.FindIndex(x => id(x) == id(incoming));
                    if (index >= 0) list[index] = Clone(incoming);
                    else list.Add(Clone(incoming));
                    break;
            }
        }

        private static void Describe(DAAudioItem item, DAAudioItemRecord record, DAVirtualAudioFolder? folder)
        {
            item.Kind = record.Kind;
            item.Tags = record.Tags.Select(t => new DAAudioTag(t)).ToList();
            item.Volume = record.Volume;
            item.Loop = record.Loop;
            item.FadeInDuration = record.FadeInDuration;
            item.StartDelay = record.StartDelay;
            item.SourceURL = record.SourceURL ?? "";
            // The library's own folder object, as AddAudioItem would set it; the root is a "." marker.
            item.Folder = folder ?? new DAVirtualAudioFolder(RootFolderName);
        }

        private static void AddTag(IDAImportTarget target, string name, string category)
        {
            var list = category switch
            {
                "Type" => target.TypeTags,
                "Location" => target.LocationTags,
                "Expression" => target.ExpressionTags,
                _ => null,   // Unknown: in no list, as before; the items still carry it
            };
            if (list != null && !list.Contains(name)) list.Add(name);
        }

        private static int Depth(DABundle bundle, DAFolderRecord folder)
        {
            int depth = 0;
            var seen = new HashSet<Guid> { folder.Id };
            var parent = folder.ParentId;
            while (parent is Guid p && bundle.Folders.FirstOrDefault(f => f.Id == p) is { } pf && seen.Add(pf.Id))
            {
                depth++;
                parent = pf.ParentId;
            }
            return depth;
        }

        private static string UniqueFolderName(IDAImportTarget target, string name)
        {
            bool Taken(string n) => target.Folders.Any(f => string.Equals(f.Name, n, StringComparison.CurrentCultureIgnoreCase));
            if (!Taken(name)) return name;
            for (int n = 2; ; n++) if (!Taken($"{name} ({n})")) return $"{name} ({n})";
        }

        private static string UniqueGroupName(IDAImportTarget target, string name)
        {
            bool Taken(string n) => target.ScenarioGroups.Any(g => string.Equals(g.Name, n, StringComparison.CurrentCultureIgnoreCase));
            if (!Taken(name)) return name;
            for (int n = 2; ; n++) if (!Taken($"{name} ({n})")) return $"{name} ({n})";
        }

        private static ImportReport Count(DAImportPlan plan, List<string> problems)
        {
            int added = 0, replaced = 0, kept = 0, merged = 0, skipped = 0, upToDate = 0;
            foreach (var item in plan.AllItems)
            {
                switch (item.Choice)
                {
                    case ImportChoice.Import or ImportChoice.ImportAsCopy or ImportChoice.ImportWithNewName or ImportChoice.KeepBothSameName: added++; break;
                    case ImportChoice.ReplaceLocal: replaced++; break;
                    case ImportChoice.KeepLocal or ImportChoice.UseLocal: kept++; break;
                    case ImportChoice.Merge: merged++; break;
                    case ImportChoice.Skip: skipped++; break;
                    case ImportChoice.Nothing: upToDate++; break;
                }
            }
            return new ImportReport(added, replaced, kept, merged, skipped, upToDate, problems);
        }

        private static T Clone<T>(T value) =>
            JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, DABundle.WriteOptions), DABundle.WriteOptions)!;
    }

    /// <summary>
    /// Runs an import with its safety net (pipeline step 4): the "before" step
    /// (a before-import backup, playback and downloads stopped), a copy of the
    /// lists, the apply, then every save awaited. If anything fails, the lists
    /// are put back as they were and saved again.
    /// </summary>
    public static class DAImportRunner
    {
        /// <param name="blockedFiles">The data files that failed to load: any of them refuses the import, before anything happens.</param>
        /// <param name="beforeApply">Makes the before-import backup (and stops what must stop); returns the backup's file name.</param>
        /// <exception cref="DAImportBlockedException">Nothing has been changed.</exception>
        /// <exception cref="DAImportFailedException">The library is as it was before the import.</exception>
        /// <exception cref="DAImportRollbackFailedException">Saving the library as it was failed too.</exception>
        public static async Task<DAImportResult> RunAsync(DAImportPlan plan, DABundle bundle, IDAImportTarget target,
                                                          IReadOnlyCollection<string> blockedFiles,
                                                          Func<Task<string?>> beforeApply, Func<Task> saveAll,
                                                          IReadOnlySet<Guid>? localFolderIds = null, Func<Guid>? newGuid = null)
        {
            if (blockedFiles.Count > 0) throw new DAImportBlockedException(blockedFiles.First());

            // Throws when the backup cannot be made: nothing has changed then.
            string? backupFileName = await beforeApply();

            var snapshot = DAImportSnapshot.Take(target);
            try
            {
                var result = DAImportApplier.Apply(plan, bundle, target, localFolderIds, newGuid);
                await saveAll();
                return result;
            }
            catch (Exception ex)
            {
                snapshot.Restore(target);
                try
                {
                    await saveAll();
                }
                catch (Exception saveEx)
                {
                    throw new DAImportRollbackFailedException(backupFileName, saveEx);
                }
                throw new DAImportFailedException(ex);
            }
        }
    }

    /// <summary>The library's lists as JSON, to put them back exactly, in the same list objects.</summary>
    public sealed class DAImportSnapshot
    {
        private readonly string _items, _folders, _scenes, _groups, _presets;
        private readonly List<string> _type, _location, _expression;

        private DAImportSnapshot(IDAImportTarget t)
        {
            _items = JsonSerializer.Serialize(t.AudioItems, DABundle.WriteOptions);
            _folders = JsonSerializer.Serialize(t.Folders, DABundle.WriteOptions);
            _scenes = JsonSerializer.Serialize(t.Scenes, DABundle.WriteOptions);
            _groups = JsonSerializer.Serialize(t.ScenarioGroups, DABundle.WriteOptions);
            _presets = JsonSerializer.Serialize(t.Presets, DABundle.WriteOptions);
            _type = t.TypeTags.ToList();
            _location = t.LocationTags.ToList();
            _expression = t.ExpressionTags.ToList();
        }

        public static DAImportSnapshot Take(IDAImportTarget target) => new(target);

        public void Restore(IDAImportTarget t)
        {
            Refill(t.AudioItems, _items);
            Refill(t.Folders, _folders);
            Refill(t.Scenes, _scenes);
            Refill(t.ScenarioGroups, _groups);
            Refill(t.Presets, _presets);
            Refill(t.TypeTags, _type);
            Refill(t.LocationTags, _location);
            Refill(t.ExpressionTags, _expression);
        }

        private static void Refill<T>(List<T> list, string json)
        {
            var values = JsonSerializer.Deserialize<List<T>>(json, DABundle.WriteOptions) ?? new();
            list.Clear();
            list.AddRange(values);
        }

        private static void Refill(List<string> list, List<string> values)
        {
            list.Clear();
            list.AddRange(values);
        }
    }
}
