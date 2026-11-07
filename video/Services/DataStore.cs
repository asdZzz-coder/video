using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Video.Models;

namespace Video.Services
{
    /// <summary>
    /// 資料存在 %AppData%\Video\data.json（一般的 JSON 檔，可以直接備份，解除安裝也不會刪掉）。
    /// 設定環境變數 VIDEO_DATA_DIR 可以改用別的資料夾（測試用，不會動到正式資料）。
    /// </summary>
    public static class DataStore
    {
        private const string CustomDirectoryVariable = "VIDEO_DATA_DIR";

        public static bool IsCustomDirectory =>
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(CustomDirectoryVariable));

        public static string DataDirectory { get; } = ResolveDirectory();

        public static string DataFile => Path.Combine(DataDirectory, "data.json");

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), // 中文直接存成文字，打開檔案看得懂
        };

        private static string ResolveDirectory()
        {
            var custom = Environment.GetEnvironmentVariable(CustomDirectoryVariable);
            return string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Video")
                : Path.GetFullPath(custom);
        }

        public static AppData Load() => Load(DataFile);

        /// <summary>讀取資料；檔案不存在時回傳空的資料，格式錯誤時丟出 InvalidDataException（不要蓋掉原本的檔案）。</summary>
        public static AppData Load(string file)
        {
            if (!File.Exists(file)) return new AppData();
            try
            {
                var data = JsonSerializer.Deserialize<AppData>(File.ReadAllText(file), Options) ?? new AppData();
                data.Videos ??= new();
                data.Genres ??= new();
                foreach (var video in data.Videos)
                {
                    video.Title ??= "";
                    video.Actors ??= new();
                    video.Genres ??= new();
                }

                // 手動改過檔案時，影片用到但清單沒有的性質補回清單
                data.Genres = data.Genres.Concat(data.Videos.SelectMany(v => v.Genres))
                    .Distinct(NameComparer.Instance)
                    .ToList();
                CatalogRules.SortGenres(data);
                return data;
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"資料檔格式錯誤：{file}\n{ex.Message}", ex);
            }
        }

        public static void Save(AppData data) => Save(data, DataFile);

        /// <summary>先寫到 .tmp 再換掉正式檔，寫到一半中斷也不會弄壞原本的資料。</summary>
        public static void Save(AppData data, string file)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options));
            File.Move(tmp, file, overwrite: true);
        }
    }
}
