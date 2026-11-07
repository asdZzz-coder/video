using System.Text.Json.Serialization;

namespace Video.Models
{
    /// <summary>一部影片：片名、演員（可以好幾位）、性質（可以好幾種）。</summary>
    public class VideoEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "";
        public List<string> Actors { get; set; } = new();
        public List<string> Genres { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        /// <summary>表格顯示和排序用，不存檔。</summary>
        [JsonIgnore]
        public string ActorsText => string.Join("、", Actors);

        [JsonIgnore]
        public string GenresText => string.Join("、", Genres);

        public override string ToString() => Title;
    }
}
