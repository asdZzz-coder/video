using Video.Models;
using Video.Services;

namespace Video.Tests
{
    public class SearchRulesTests
    {
        private static VideoEntry Sample() => new()
        {
            Title = "Ｔｈｅ 測試 Movie",
            Actors = new() { "王 小明", "Alice" },
            Genres = new() { "動作", "喜劇" },
        };

        private static bool Search(string text, VideoEntry? video = null)
        {
            var filter = new VideoFilter();
            filter.SetText(text);
            return filter.Matches(video ?? Sample());
        }

        [Theory]
        [InlineData("測試")]        // 片名
        [InlineData("Alice")]       // 演員
        [InlineData("喜劇")]        // 性質
        [InlineData("alice")]       // 不分大小寫
        [InlineData("the")]         // 全形片名用半形找
        [InlineData("ＡＬＩＣＥ")]  // 半形名字用全形找
        [InlineData("王小明")]      // 名字有空白，打的時候沒有
        [InlineData("王 小明")]     // 空白當成兩個關鍵字，一樣找得到
        [InlineData("王　小明")]    // 全形空白
        public void Text_MatchesAnyField(string text) => Assert.True(Search(text));

        [Fact]
        public void Keywords_MustAllMatch_InAnyField()
        {
            Assert.True(Search("alice 動作 測試"));
            Assert.False(Search("alice 恐怖"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("、，")]
        public void EmptyText_MatchesEverything(string text) => Assert.True(Search(text));

        [Fact]
        public void Keyword_DoesNotSpanFields()
        {
            // 「movie王」前半在片名、後半在演員，不算符合
            Assert.False(Search("movie王"));
        }

        [Fact]
        public void BrokenUnicode_DoesNotThrow()
        {
            Assert.False(Search("\uD83D"));
        }

        [Fact]
        public void SelectedActorsAndGenres_MustAllMatch()
        {
            var filter = new VideoFilter();
            filter.Actors.Add("alice");
            filter.Genres.Add("動作");
            Assert.True(filter.Matches(Sample()));

            filter.Genres.Add("恐怖");
            Assert.False(filter.Matches(Sample()));
        }

        [Fact]
        public void SelectedActor_MatchesWholeNameOnly()
        {
            var filter = new VideoFilter();
            filter.Actors.Add("Ali");
            Assert.False(filter.Matches(Sample()));
        }

        [Fact]
        public void IsActive_FollowsConditions()
        {
            var filter = new VideoFilter();
            Assert.False(filter.IsActive);
            filter.SetText(" a ");
            Assert.True(filter.IsActive);
            filter.SetText("");
            Assert.False(filter.IsActive);
            filter.Genres.Add("動作");
            Assert.True(filter.IsActive);
        }

        [Fact]
        public void Count_CountsEachVideoOnce_AndTreatsSameNamesAsOne()
        {
            var videos = new[]
            {
                new VideoEntry { Actors = new() { "Alice", "ALICE" } },
                new VideoEntry { Actors = new() { "ａｌｉｃｅ", "Bob" } },
            };

            var counts = SearchRules.Count(videos, v => v.Actors);

            Assert.Equal(2, counts["alice"]);
            Assert.Equal(1, counts["Bob"]);
            Assert.Equal(2, counts.Count);
        }
    }
}
