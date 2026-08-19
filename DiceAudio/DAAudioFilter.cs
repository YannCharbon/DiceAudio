/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

namespace DiceAudio
{
    /// <summary>
    /// Tag expression used by the Explorer to narrow the visible audio items —
    /// e.g. "ambiance and mysterious and not combat".
    ///
    /// A filter is an ordered list of criteria applied successively to the
    /// candidate set, exactly like the random-play criteria of a scenario item;
    /// both share <see cref="Apply"/> so the two features can never drift apart.
    /// This is view state: it is not persisted.
    /// </summary>
    public class DAAudioFilter
    {
        public List<DARandomPlayCriteria> Criterias { get; set; } = new();

        /// <summary>
        /// When set, the filter also reaches into the sub-folders of the current
        /// folder, and the matches found there are shown alongside the current
        /// folder's own items.
        /// </summary>
        public bool IncludeSubfolders { get; set; }

        /// <summary>Criteria that actually constrain anything (a tagless one is still being edited).</summary>
        public IEnumerable<DARandomPlayCriteria> EffectiveCriterias =>
            Criterias.Where(c => c.TagNames.Count > 0);

        public bool IsActive => EffectiveCriterias.Any();

        /// <summary>
        /// Narrows <paramref name="items"/> by each criteria in turn: NOT drops the
        /// items carrying any of its tags, AND keeps those carrying all of them,
        /// OR keeps those carrying at least one.
        /// </summary>
        public static IEnumerable<DAAudioItem> Apply(IEnumerable<DAAudioItem> items,
                                                     IEnumerable<DARandomPlayCriteria> criterias)
        {
            foreach (var criteria in criterias)
            {
                items = criteria.Condition switch
                {
                    DARandomPlayCriteria.ConditionType.Not =>
                        items.Where(a => !a.Tags.Any(t => criteria.TagNames.Contains(t.Name))),
                    DARandomPlayCriteria.ConditionType.And =>
                        items.Where(a => criteria.TagNames.All(tn => a.Tags.Any(t => t.Name == tn))),
                    _ =>
                        items.Where(a => criteria.TagNames.Any(tn => a.Tags.Any(t => t.Name == tn))),
                };
            }
            return items;
        }

        /// <summary>
        /// Applies the filter, ignoring criteria that carry no tag yet — an
        /// half-built criteria narrows nothing instead of emptying the view.
        /// </summary>
        public IEnumerable<DAAudioItem> Apply(IEnumerable<DAAudioItem> items) =>
            Apply(items, EffectiveCriterias);

        /// <summary>
        /// The expression in words: "ambiance and mysterious and not combat".
        /// Criteria are intersected, so they read joined by "and".
        /// </summary>
        public string Describe()
        {
            var parts = EffectiveCriterias.Select(c => c.Condition switch
            {
                // NOT drops items having ANY of its tags, i.e. none of them may be present.
                DARandomPlayCriteria.ConditionType.Not =>
                    string.Join(" and ", c.TagNames.Select(t => "not " + t)),
                DARandomPlayCriteria.ConditionType.And =>
                    string.Join(" and ", c.TagNames),
                _ => c.TagNames.Count > 1
                    ? "(" + string.Join(" or ", c.TagNames) + ")"
                    : c.TagNames[0],
            });
            return string.Join(" and ", parts);
        }
    }
}
