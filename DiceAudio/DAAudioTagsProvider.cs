using MudBlazor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DiceAudio
{
    public sealed class DAAudioTagsProvider
    {
        private static readonly DAAudioTagsProvider instance = new DAAudioTagsProvider();

        private List<string> _categoryTypeDefaultList = new List<string>
        {
            "Ambience", "Music", "SFX"
        };

        private List<string> _categoryLocationDefaultList = new List<string>
        {
            "Generic", "Forest", "Jungle", "Desert", "Plains", "Swamp",
            "Mountain", "Nature", "Industrial", "Storm", "Village", "City", "Medieval",
            "Futuristic", "Cold", "Hot", "Snow", "Ice", "Balroom", "Tavern",
            "Market", "Council", "Cave", "Sewer"
        };

        private List<string> _categoryExpressionDefaultList = new List<string>
        {
            "Calm", "Stressed", "Joy", "Energic", "Love", "Emotional",
            "Sad", "Mysterious", "Battle", "Trip", "Dark", "Heroic",
            "Dramatic", "Beautiful", "ViewReveal", "PlotReveal", "Noble", "Jazzy",
            "Comedy", "Dream", "Magical", "Classical", "Contemplation", "Adventure",
            "Desolation", "Desperate", "Hope", "Mystic", "Ethnic", "IntroTheme"
        };

        public List<string> CategoryTypes = new List<string>();
        public List<string> CategoryLocations = new List<string>();
        public List<string> CategoryExpressions = new List<string>();

        // Explicit static constructor to tell C# compiler
        // not to mark type as beforefieldinit
        static DAAudioTagsProvider()
        {
        }

        private DAAudioTagsProvider()
        {
            if (!File.Exists(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryType.json")))
            {
                foreach (string tag in _categoryTypeDefaultList)
                {
                    CategoryTypes.Add(tag);
                }
                var option = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultBufferSize = 15 * 1024 * 1024 // 15MiB
                };
                string json = JsonSerializer.Serialize(CategoryTypes, option);
                File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryType.json"), json);                
            }

            if (!File.Exists(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryLocation.json")))
            {
                foreach (string tag in _categoryLocationDefaultList)
                {
                    CategoryLocations.Add(tag);
                }
                var option = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultBufferSize = 15 * 1024 * 1024 // 15MiB
                };
                string json = JsonSerializer.Serialize(CategoryLocations, option);
                File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryLocation.json"), json);
            }

            if (!File.Exists(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryExpression.json")))
            {
                foreach (string tag in _categoryExpressionDefaultList)
                {
                    CategoryExpressions.Add(tag);
                }
                var option = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultBufferSize = 15 * 1024 * 1024 // 15MiB
                };
                string json = JsonSerializer.Serialize(CategoryExpressions, option);
                File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryExpression.json"), json);
            }
            var options = new JsonSerializerOptions
            {
                DefaultBufferSize = 15 * 1024 * 1024 // 15MiB
            };

            var tmp = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryType.json")));
            CategoryTypes = tmp != null ? tmp : CategoryTypes;
            tmp = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryLocation.json")));
            CategoryLocations = tmp != null ? tmp : CategoryLocations;
            tmp = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryExpression.json")));
            CategoryExpressions = tmp != null ? tmp : CategoryExpressions;
        }

        public void SaveTags()
        {
            var option = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultBufferSize = 15 * 1024 * 1024 // 15MiB
            };

            string json = JsonSerializer.Serialize(CategoryTypes, option);
			File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryType.json"), json);

            json = JsonSerializer.Serialize(CategoryLocations, option);
			File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryLocation.json"), json);

            json = JsonSerializer.Serialize(CategoryExpressions, option);
			File.WriteAllText(Path.Combine(FileSystem.AppDataDirectory, "TagsCategoryExpression.json"), json);
		}

        public static DAAudioTagsProvider Instance
        {
            get
            {
                return instance;
            }
        }
    }
}
