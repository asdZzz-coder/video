namespace Video.Models
{
    public class AppData
    {
        public List<VideoEntry> Videos { get; set; } = new();

        /// <summary>性質清單。沒有影片使用的性質也會留著，選項才不會消失。</summary>
        public List<string> Genres { get; set; } = new();
    }
}
