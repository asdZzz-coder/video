using Video.Services;

namespace Video.Models
{
    public class AppData
    {
        public List<VideoEntry> Videos { get; set; } = new();

        /// <summary>性質清單（大項和小項都在裡面）。沒有影片使用的性質也會留著，選項才不會消失。</summary>
        public List<string> Genres { get; set; } = new();

        /// <summary>
        /// 小項 → 所屬的大項。不在這裡的性質就是大項。只有兩層：小項底下不能再分。
        /// 分開存是為了和 1.0 版的資料檔相容：舊檔沒有這一欄，讀進來全部都是大項。
        /// </summary>
        public Dictionary<string, string> GenreParents { get; set; } = new(NameComparer.Instance);
    }
}
