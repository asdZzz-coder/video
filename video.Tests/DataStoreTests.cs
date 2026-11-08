using System.IO;
using Video.Models;
using Video.Services;

namespace Video.Tests
{
    public class DataStoreTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "Video.Tests." + Guid.NewGuid().ToString("N"));
        private string File1 => Path.Combine(_root, "data.json");

        public DataStoreTests() => Directory.CreateDirectory(_root);

        public void Dispose() => Directory.Delete(_root, recursive: true);

        [Fact]
        public void MissingFile_ReturnsEmptyData()
        {
            var data = DataStore.Load(File1);

            Assert.Empty(data.Videos);
            Assert.Empty(data.Genres);
        }

        [Fact]
        public void SaveThenLoad_KeepsEverything_AndLeavesNoTempFile()
        {
            var data = new AppData();
            CatalogRules.Upsert(data, null, "片名", new[] { "演員甲", "演員乙" }, new[] { "動作", "喜劇" }, new DateTime(2026, 11, 7));

            DataStore.Save(data, File1);
            var loaded = DataStore.Load(File1);

            var video = Assert.Single(loaded.Videos);
            Assert.Equal("片名", video.Title);
            Assert.Equal(new[] { "演員甲", "演員乙" }, video.Actors);
            Assert.Equal(new[] { "動作", "喜劇" }, video.Genres);
            Assert.Equal(new DateTime(2026, 11, 7), video.CreatedAt);
            Assert.Equal(2, loaded.Genres.Count);
            Assert.False(File.Exists(File1 + ".tmp"));
            Assert.Contains("演員甲", File.ReadAllText(File1)); // 中文直接存成文字
            Assert.DoesNotContain("ActorsText", File.ReadAllText(File1));
        }

        [Fact]
        public void BrokenFile_Throws_AndKeepsFile()
        {
            File.WriteAllText(File1, "{ 這不是 JSON");

            Assert.Throws<InvalidDataException>(() => DataStore.Load(File1));
            Assert.Equal("{ 這不是 JSON", File.ReadAllText(File1));
        }

        [Fact]
        public void NullLists_BecomeEmpty()
        {
            File.WriteAllText(File1, """{ "Videos": [ { "Title": "一", "Actors": null, "Genres": null } ], "Genres": null }""");

            var video = Assert.Single(DataStore.Load(File1).Videos);

            Assert.Empty(video.Actors);
            Assert.Empty(video.Genres);
        }

        [Fact]
        public void GenresUsedByVideos_AreAddedToList()
        {
            File.WriteAllText(File1, """{ "Videos": [ { "Title": "一", "Genres": [ "動作" ] } ], "Genres": [ "喜劇" ] }""");

            var data = DataStore.Load(File1);

            Assert.Equal(2, data.Genres.Count);
            Assert.Contains("動作", data.Genres);
        }

        [Fact]
        public void Version1File_AllGenresAreRoots()
        {
            // 1.0 版的檔案沒有 GenreParents
            File.WriteAllText(File1, """{ "Videos": [ { "Title": "一", "Genres": [ "動作" ] } ], "Genres": [ "動作", "喜劇" ] }""");

            var data = DataStore.Load(File1);

            Assert.Empty(data.GenreParents);
            Assert.Equal(2, GenreTree.Roots(data).Count);
        }

        [Fact]
        public void SubGenres_SurviveSaveAndLoad_WithLooseMatching()
        {
            var data = new AppData();
            CatalogRules.AddGenre(data, "Action");
            CatalogRules.AddGenre(data, "Gun", "Action");
            DataStore.Save(data, File1);

            var loaded = DataStore.Load(File1);

            Assert.Equal("Action", GenreTree.ParentOf(loaded, "gun")); // 讀回來的對照表也不分大小寫
        }

        [Fact]
        public void HandEditedParents_AreCleanedOnLoad()
        {
            File.WriteAllText(File1, """{ "Genres": [ "動作", "槍戰" ], "GenreParents": { "槍戰": "動作", "沒有": "動作" } }""");

            var data = DataStore.Load(File1);

            Assert.Equal("動作", GenreTree.ParentOf(data, "槍戰"));
            Assert.Single(data.GenreParents);
        }
    }
}
