using Video.Models;
using Video.Services;

namespace Video.Tests
{
    public class GenreTreeTests
    {
        private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0);

        /// <summary>動作（槍戰、追車）、劇情（愛情）、喜劇（沒有小項）。</summary>
        private static AppData Sample()
        {
            var data = new AppData();
            CatalogRules.AddGenre(data, "動作");
            CatalogRules.AddGenre(data, "槍戰", "動作");
            CatalogRules.AddGenre(data, "追車", "動作");
            CatalogRules.AddGenre(data, "劇情");
            CatalogRules.AddGenre(data, "愛情", "劇情");
            CatalogRules.AddGenre(data, "喜劇");
            return data;
        }

        private static VideoEntry Add(AppData data, string title, params string[] genres) =>
            CatalogRules.Upsert(data, null, title, Array.Empty<string>(), genres, Now);

        [Fact]
        public void AddGenre_WithParent_MakesSubGenre()
        {
            var data = Sample();

            Assert.Equal("動作", GenreTree.ParentOf(data, "槍戰"));
            Assert.Null(GenreTree.ParentOf(data, "動作"));
            Assert.Equal(new[] { "槍戰", "追車" }.OrderBy(x => x, CatalogRules.DisplayOrder), GenreTree.Children(data, "動作"));
            Assert.True(GenreTree.HasChildren(data, "動作"));
            Assert.False(GenreTree.HasChildren(data, "喜劇"));
        }

        [Fact]
        public void AddGenre_MissingParent_IsCreatedAsRoot()
        {
            var data = new AppData();
            Assert.True(CatalogRules.AddGenre(data, "喪屍", "恐怖"));

            Assert.Contains("恐怖", data.Genres);
            Assert.Null(GenreTree.ParentOf(data, "恐怖"));
            Assert.Equal("恐怖", GenreTree.ParentOf(data, "喪屍"));
        }

        [Fact]
        public void AddGenre_UnderSubGenre_GoesToItsRoot()
        {
            var data = Sample();
            CatalogRules.AddGenre(data, "狙擊", "槍戰");

            Assert.Equal("動作", GenreTree.ParentOf(data, "狙擊"));
        }

        [Fact]
        public void ParentLookup_IgnoresWidthCaseAndSpaces()
        {
            var data = new AppData();
            CatalogRules.AddGenre(data, "Action");
            CatalogRules.AddGenre(data, "Gun Fight", "action");

            Assert.Equal("Action", GenreTree.ParentOf(data, "ｇｕｎｆｉｇｈｔ"));
        }

        [Fact]
        public void Ordered_PutsChildrenAfterTheirRoot()
        {
            var data = Sample();
            var ordered = GenreTree.Ordered(data);

            var action = ordered.IndexOf("動作");
            Assert.Equal(action + 1, ordered.IndexOf(GenreTree.Children(data, "動作")[0]));
            Assert.Equal(action + 2, ordered.IndexOf(GenreTree.Children(data, "動作")[1]));
            Assert.Equal(ordered.IndexOf("劇情") + 1, ordered.IndexOf("愛情"));
            Assert.Equal(data.Genres.Count, ordered.Count);
        }

        [Fact]
        public void FilterByRoot_IncludesVideosTaggedWithSubGenre()
        {
            var data = Sample();
            var gun = Add(data, "一", "槍戰");
            var action = Add(data, "二", "動作");
            var love = Add(data, "三", "愛情");

            var filter = new VideoFilter { Catalog = data };
            filter.Genres.Add("動作");
            Assert.True(filter.Matches(gun));
            Assert.True(filter.Matches(action));
            Assert.False(filter.Matches(love));

            // 選小項只找標了那個小項的
            filter.Genres.Clear();
            filter.Genres.Add("槍戰");
            Assert.True(filter.Matches(gun));
            Assert.False(filter.Matches(action));
        }

        [Fact]
        public void KeywordSearch_FindsSubGenreByRootName()
        {
            var data = Sample();
            var gun = Add(data, "一", "槍戰");
            var comedy = Add(data, "二", "喜劇");

            var filter = new VideoFilter { Catalog = data };
            filter.SetText("動作");
            Assert.True(filter.Matches(gun));
            Assert.False(filter.Matches(comedy));
        }

        [Fact]
        public void CountGenres_RootCountsEachVideoOnce()
        {
            var data = Sample();
            Add(data, "一", "槍戰", "追車");
            Add(data, "二", "動作");
            Add(data, "三", "愛情");

            var counts = SearchRules.CountGenres(data.Videos, data);
            Assert.Equal(2, counts["動作"]);
            Assert.Equal(1, counts["槍戰"]);
            Assert.Equal(1, counts["劇情"]);
        }

        [Fact]
        public void Upsert_NewSubGenre_UsesGivenParent()
        {
            var data = Sample();
            var parents = new Dictionary<string, string>(NameComparer.Instance) { ["喪屍"] = "恐怖" };

            var video = CatalogRules.Upsert(data, null, "一", Array.Empty<string>(), new[] { "喪屍" }, Now, parents);

            Assert.Equal(new[] { "喪屍" }, video.Genres);
            Assert.Equal("恐怖", GenreTree.ParentOf(data, "喪屍"));
            Assert.Contains("恐怖", data.Genres);
        }

        [Fact]
        public void Upsert_SortsVideoGenresByTree()
        {
            var data = Sample();
            var video = Add(data, "一", "喜劇", "愛情", "槍戰", "動作");

            Assert.Equal(GenreTree.Ordered(data).Where(video.Genres.Contains), video.Genres);
            Assert.Equal(video.Genres.IndexOf("動作") + 1, video.Genres.IndexOf("槍戰"));
        }

        [Fact]
        public void Label_ShowsRootBeforeSubGenre()
        {
            var data = Sample();
            Assert.Equal("動作／槍戰", GenreTree.Label(data, "槍戰"));
            Assert.Equal("喜劇", GenreTree.Label(data, "喜劇"));
        }

        [Theory]
        [InlineData("動作/槍戰", "動作", "槍戰")]
        [InlineData(" 動作 ／ 槍戰 ", "動作", "槍戰")]
        public void SplitPath_ParsesParentAndChild(string text, string parent, string child)
        {
            Assert.Equal((parent, child), GenreTree.SplitPath(text));
        }

        [Theory]
        [InlineData("動作")]
        [InlineData("/槍戰")]
        [InlineData("動作/")]
        [InlineData("a/b/c")]
        public void SplitPath_RejectsOtherText(string text)
        {
            Assert.Null(GenreTree.SplitPath(text));
        }

        [Fact]
        public void MoveGenre_ChangesParentOrMakesRoot()
        {
            var data = Sample();

            Assert.Null(CatalogRules.MoveGenre(data, "喜劇", "劇情"));
            Assert.Equal("劇情", GenreTree.ParentOf(data, "喜劇"));

            Assert.Null(CatalogRules.MoveGenre(data, "槍戰", null));
            Assert.Null(GenreTree.ParentOf(data, "槍戰"));
        }

        [Fact]
        public void MoveGenre_KeepsTwoLevels()
        {
            var data = Sample();

            Assert.NotNull(CatalogRules.MoveGenre(data, "喜劇", "槍戰"));   // 小項底下不能再分
            Assert.NotNull(CatalogRules.MoveGenre(data, "動作", "劇情"));   // 有小項的大項不能變小項
            Assert.NotNull(CatalogRules.MoveGenre(data, "喜劇", "喜劇"));   // 不能放在自己底下
            Assert.NotNull(CatalogRules.MoveGenre(data, "喜劇", "沒有這個")); // 大項不存在
            Assert.Null(GenreTree.ParentOf(data, "喜劇"));
            Assert.Null(GenreTree.ParentOf(data, "動作"));
        }

        [Fact]
        public void RenameRoot_ChildrenFollow()
        {
            var data = Sample();
            var video = Add(data, "一", "動作");

            Assert.Equal(1, CatalogRules.RenameGenre(data, "動作", "動作片"));

            Assert.Equal("動作片", GenreTree.ParentOf(data, "槍戰"));
            Assert.Equal("動作片", GenreTree.ParentOf(data, "追車"));
            Assert.DoesNotContain("動作", data.Genres);
            Assert.Equal(new[] { "動作片" }, video.Genres);
        }

        [Fact]
        public void RenameSub_KeepsParent()
        {
            var data = Sample();
            var video = Add(data, "一", "槍戰");

            CatalogRules.RenameGenre(data, "槍戰", "槍戰片");

            Assert.Equal("動作", GenreTree.ParentOf(data, "槍戰片"));
            Assert.False(data.GenreParents.ContainsKey("槍戰"));
            Assert.Equal(new[] { "槍戰片" }, video.Genres);
        }

        [Fact]
        public void RenameRoot_OnlyCase_KeepsChildren()
        {
            var data = new AppData();
            CatalogRules.AddGenre(data, "action");
            CatalogRules.AddGenre(data, "gun", "action");

            CatalogRules.RenameGenre(data, "action", "Action");

            Assert.Equal(new[] { "Action", "gun" }, GenreTree.Ordered(data));
            Assert.Equal("Action", GenreTree.ParentOf(data, "gun"));
        }

        [Fact]
        public void MergeRootIntoRoot_MovesChildren()
        {
            var data = Sample();
            CatalogRules.RenameGenre(data, "動作", "劇情");

            Assert.Equal("劇情", GenreTree.ParentOf(data, "槍戰"));
            Assert.Equal("劇情", GenreTree.ParentOf(data, "愛情"));
            Assert.DoesNotContain("動作", data.Genres);
        }

        [Fact]
        public void MergeRootIntoSubOfAnotherRoot_ChildrenGoToThatRoot()
        {
            var data = Sample();
            CatalogRules.RenameGenre(data, "動作", "愛情");

            Assert.Equal("劇情", GenreTree.ParentOf(data, "槍戰"));
            Assert.Equal("劇情", GenreTree.ParentOf(data, "追車"));
            Assert.Equal("劇情", GenreTree.ParentOf(data, "愛情"));
        }

        [Fact]
        public void MergeRootIntoOwnChild_ChildBecomesRoot()
        {
            var data = Sample();
            var video = Add(data, "一", "動作", "槍戰");

            CatalogRules.RenameGenre(data, "動作", "槍戰");

            Assert.Null(GenreTree.ParentOf(data, "槍戰"));
            Assert.Equal("槍戰", GenreTree.ParentOf(data, "追車"));
            Assert.Equal(new[] { "槍戰" }, video.Genres);
        }

        [Fact]
        public void RemoveRoot_PromotesChildren()
        {
            var data = Sample();
            var video = Add(data, "一", "動作", "槍戰");

            Assert.Equal(1, CatalogRules.RemoveGenre(data, "動作"));

            Assert.DoesNotContain("動作", data.Genres);
            Assert.Contains("槍戰", data.Genres);
            Assert.Null(GenreTree.ParentOf(data, "槍戰"));
            Assert.Null(GenreTree.ParentOf(data, "追車"));
            Assert.Equal(new[] { "槍戰" }, video.Genres);
        }

        [Fact]
        public void RemoveSub_KeepsRoot()
        {
            var data = Sample();
            CatalogRules.RemoveGenre(data, "槍戰");

            Assert.False(data.GenreParents.ContainsKey("槍戰"));
            Assert.Equal(new[] { "追車" }, GenreTree.Children(data, "動作"));
        }

        [Fact]
        public void Normalize_FlattensBadHandEdits()
        {
            var data = new AppData { Genres = new() { "a", "b", "c", "x", "y" } };
            data.GenreParents = new()
            {
                ["a"] = "b",     // 三層：a → b → c
                ["b"] = "c",
                ["x"] = "y",     // 繞圈：x ↔ y
                ["y"] = "x",
                ["沒有"] = "c",  // 不存在的性質
                ["C"] = "c",     // 自己是自己的大項
            };

            GenreTree.Normalize(data);

            Assert.Equal("c", GenreTree.ParentOf(data, "a"));
            Assert.Equal("c", GenreTree.ParentOf(data, "b"));
            Assert.Null(GenreTree.ParentOf(data, "c"));
            Assert.Single(new[] { "x", "y" }, g => GenreTree.IsSub(data, g));
            Assert.Equal(3, data.GenreParents.Count);
        }
    }
}
