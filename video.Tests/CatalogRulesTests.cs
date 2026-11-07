using Video.Models;
using Video.Services;

namespace Video.Tests
{
    public class CatalogRulesTests
    {
        private static readonly DateTime Now = new(2026, 11, 7, 8, 0, 0);

        private static VideoEntry Add(AppData data, string title, string[]? actors = null, string[]? genres = null) =>
            CatalogRules.Upsert(data, null, title, actors ?? Array.Empty<string>(), genres ?? Array.Empty<string>(), Now);

        [Fact]
        public void Upsert_New_CleansTextAndAddsGenres()
        {
            var data = new AppData();

            var video = Add(data, "  片名   一 ", new[] { " 小明 ", "小明", "" }, new[] { "動作", "喜劇" });

            Assert.Equal("片名 一", video.Title);
            Assert.Equal(new[] { "小明" }, video.Actors);
            Assert.Equal(new[] { "動作", "喜劇" }, video.Genres);
            Assert.Contains("動作", data.Genres);
            Assert.Contains("喜劇", data.Genres);
            Assert.Single(data.Videos);
            Assert.Equal(Now, video.CreatedAt);
        }

        [Fact]
        public void Upsert_UsesExistingSpelling()
        {
            var data = new AppData();
            Add(data, "一", new[] { "Alice" }, new[] { "Action" });

            var second = Add(data, "二", new[] { "ａｌｉｃｅ" }, new[] { "ACTION" });

            Assert.Equal(new[] { "Alice" }, second.Actors);
            Assert.Equal(new[] { "Action" }, second.Genres);
            Assert.Equal(new[] { "Action" }, data.Genres);
        }

        [Fact]
        public void Upsert_Existing_KeepsIdAndCreatedAt()
        {
            var data = new AppData();
            var video = Add(data, "一");
            var id = video.Id;

            var later = Now.AddDays(1);
            var edited = CatalogRules.Upsert(data, video, "二", new[] { "A" }, new[] { "B" }, later);

            Assert.Same(video, edited);
            Assert.Equal(id, edited.Id);
            Assert.Equal("二", edited.Title);
            Assert.Equal(Now, edited.CreatedAt);
            Assert.Equal(later, edited.UpdatedAt);
            Assert.Single(data.Videos);
        }

        [Fact]
        public void FindDuplicate_IgnoresWidthCaseAndSpacing_ButNotItself()
        {
            var data = new AppData();
            var video = Add(data, "Movie 1");

            Assert.Same(video, CatalogRules.FindDuplicate(data, "ｍｏｖｉｅ1", null));
            Assert.Null(CatalogRules.FindDuplicate(data, "Movie 1", video));
            Assert.Null(CatalogRules.FindDuplicate(data, "Movie 2", null));
        }

        [Fact]
        public void Suggest_ContainsMatch_PrefixFirst_SkipsChosen_RespectsLimit()
        {
            var known = new[] { "小明", "明美", "大明", "小華" };

            Assert.Equal("明美", CatalogRules.Suggest("明", known, Array.Empty<string>(), 10)[0]);
            Assert.DoesNotContain("明美", CatalogRules.Suggest("明", known, new[] { "明美" }, 10));
            Assert.Equal(2, CatalogRules.Suggest("明", known, Array.Empty<string>(), 2).Count);
            Assert.Empty(CatalogRules.Suggest("  ", known, Array.Empty<string>(), 10));
        }

        [Fact]
        public void AddGenre_RejectsEmptyAndDuplicate()
        {
            var data = new AppData();

            Assert.True(CatalogRules.AddGenre(data, "Action"));
            Assert.False(CatalogRules.AddGenre(data, "ａｃｔｉｏｎ"));
            Assert.False(CatalogRules.AddGenre(data, "  "));
            Assert.Single(data.Genres);
        }

        [Fact]
        public void RenameGenre_UpdatesVideos()
        {
            var data = new AppData();
            var video = Add(data, "一", genres: new[] { "動做", "喜劇" });
            Add(data, "二", genres: new[] { "喜劇" });

            var affected = CatalogRules.RenameGenre(data, "動做", "動作");

            Assert.Equal(1, affected);
            Assert.Equal(new[] { "動作", "喜劇" }, video.Genres);
            Assert.Contains("動作", data.Genres);
            Assert.DoesNotContain("動做", data.Genres);
        }

        [Fact]
        public void RenameGenre_ToExisting_Merges()
        {
            var data = new AppData();
            var both = Add(data, "一", genres: new[] { "A", "B" });

            CatalogRules.RenameGenre(data, "A", "b");

            Assert.Equal(new[] { "B" }, both.Genres);
            Assert.Equal(new[] { "B" }, data.Genres);
        }

        [Fact]
        public void RenameGenre_OnlyCase_KeepsNewSpelling()
        {
            var data = new AppData();
            var video = Add(data, "一", genres: new[] { "action" });

            CatalogRules.RenameGenre(data, "action", "Action");

            Assert.Equal(new[] { "Action" }, video.Genres);
            Assert.Equal(new[] { "Action" }, data.Genres);
        }

        [Fact]
        public void RemoveGenre_KeepsVideos()
        {
            var data = new AppData();
            var video = Add(data, "一", genres: new[] { "A", "B" });

            Assert.Equal(1, CatalogRules.RemoveGenre(data, "A"));

            Assert.Equal(new[] { "B" }, video.Genres);
            Assert.Equal(new[] { "B" }, data.Genres);
            Assert.Single(data.Videos);
        }

        [Fact]
        public void RenameActor_ToExisting_MergesAndUsesExistingSpelling()
        {
            var data = new AppData();
            var first = Add(data, "一", new[] { "Alicia" });
            var both = Add(data, "二", new[] { "Alice", "Alicia" });

            var affected = CatalogRules.RenameActor(data, "Alicia", "ALICE");

            Assert.Equal(2, affected);
            Assert.Equal(new[] { "Alice" }, first.Actors);
            Assert.Equal(new[] { "Alice" }, both.Actors);
        }

        [Fact]
        public void RenameActor_OnlyCase_KeepsNewSpelling()
        {
            var data = new AppData();
            var video = Add(data, "一", new[] { "alice" });

            CatalogRules.RenameActor(data, "alice", "Alice");

            Assert.Equal(new[] { "Alice" }, video.Actors);
        }

        [Fact]
        public void RemoveActor_KeepsVideos()
        {
            var data = new AppData();
            var video = Add(data, "一", new[] { "A", "B" });

            Assert.Equal(1, CatalogRules.RemoveActor(data, "a"));

            Assert.Equal(new[] { "B" }, video.Actors);
            Assert.Single(data.Videos);
        }

        [Theory]
        [InlineData("A、B", 2)]
        [InlineData("A,B，C", 3)]
        [InlineData("A;B；C\nD", 4)]
        [InlineData(" 王 小明 ", 1)]
        [InlineData("、、", 0)]
        public void SplitNames_SplitsOnSeparators(string text, int count) =>
            Assert.Equal(count, NameRules.SplitNames(text).Count);
    }
}
