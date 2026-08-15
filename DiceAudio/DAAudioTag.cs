/*
 * DiceAudio - Copyright (C) 2025 Yann Charbon
 * SPDX-License-Identifier: GPL-3.0-or-later
 *
 * This file is part of DiceAudio, released under the GNU GPL v3.
 * See the LICENSE file in the repository root for details.
 */

using System.Linq;
using System.Runtime.Serialization;
using static MudBlazor.Colors;

namespace DiceAudio
{
    public class DAAudioTag
    {
        public enum TagCategory { Unknown, Type, Location, Expression };

        public string Name { get; set; } = "Unknown";

        public DAAudioTag() { }

        public DAAudioTag(string name)
        {
            Name = name;
        }

        public TagCategory GetCategory()
        {
            if (DAAudioTagsProvider.Instance.CategoryTypes.Contains(Name))
            {
                return TagCategory.Type;
            }
            else if (DAAudioTagsProvider.Instance.CategoryLocations.Contains(Name))
            {
                return TagCategory.Location;
            }
            else if (DAAudioTagsProvider.Instance.CategoryExpressions.Contains(Name))
            {
                return TagCategory.Expression;
            }
            return TagCategory.Unknown;
        }

        public static TagCategory GetCategory(string tagName)
        {
            if (DAAudioTagsProvider.Instance.CategoryTypes.Contains(tagName))
            {
                return TagCategory.Type;
            }
            else if (DAAudioTagsProvider.Instance.CategoryLocations.Contains(tagName))
            {
                return TagCategory.Location;
            }
            else if (DAAudioTagsProvider.Instance.CategoryExpressions.Contains(tagName))
            {
                return TagCategory.Expression;
            }
            return TagCategory.Unknown;
        }

        public static List<string> GetAllCategoryTypeTags()
        {
            return DAAudioTagsProvider.Instance.CategoryTypes;
        }

        public static List<string> GetAllCategoryLocationTags()
        {
            return DAAudioTagsProvider.Instance.CategoryLocations;
        }
        public static List<string> GetAllCategoryExpressionTags()
        {
            return DAAudioTagsProvider.Instance.CategoryExpressions;
        }

        /*public static bool operator ==(AudioInfoTag tag1, AudioInfoTag tag2)
		{
            if (tag1.Name == tag2.Name)
            {
                return true;
            }

			return false;
		}

		public static bool operator !=(AudioInfoTag tag1, AudioInfoTag tag2)
		{

			if (tag1.Name != tag2.Name)
			{
				return true;
			}

			return false;
		}*/

        public override bool Equals(object? obj)
        {
            return this.Equals(obj as DAAudioTag);
        }

        public bool Equals(DAAudioTag tag)
		{
            if (tag.Name == this.Name)
            {
                return true;
            }

            return false;
		}

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }
    }
}
