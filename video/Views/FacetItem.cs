namespace Video.Views
{
    /// <summary>左邊篩選的一個選項。</summary>
    public sealed record FacetItem(string Name, int Count, bool IsSelected)
    {
        public string Display => $"{Name} {Count}";

        /// <summary>目前結果裡沒有這個性質時不能按，按了只會什麼都找不到；已經選的要能取消。</summary>
        public bool CanClick => Count > 0 || IsSelected;

        public override string ToString() => Name;
    }
}
