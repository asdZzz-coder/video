using System.Text.Json;
using Video.Services;

namespace Video.Tests
{
    public class UpdateServiceTests
    {
        private const string LocalAppData = @"C:\Users\someone\AppData\Local";

        [Theory]
        [InlineData(@"C:\Users\someone\AppData\Local\Programs\Video\", true)]
        [InlineData(@"C:\Users\someone\AppData\Local\Programs\Video", true)]
        [InlineData(@"c:\users\SOMEONE\appdata\local\programs\video\", true)]
        [InlineData(@"C:\Users\someone\AppData\Local\Programs\VideoPlayer\", false)]
        [InlineData(@"C:\vscode studio\video\video\bin\Release\net10.0-windows\", false)]
        public void IsInstalledPath_OnlyInsideInstallFolder(string baseDirectory, bool expected) =>
            Assert.Equal(expected, UpdateService.IsInstalledPath(baseDirectory, LocalAppData));

        private static JsonElement Release(string tag, string assetName) =>
            JsonDocument.Parse($$"""
                { "tag_name": "{{tag}}", "assets": [ { "name": "{{assetName}}", "browser_download_url": "https://example.com/a.msi", "size": 123 } ] }
                """).RootElement;

        [Fact]
        public void ParseRelease_NewerVersionWithInstaller_ReturnsInfo()
        {
            var info = UpdateService.ParseRelease(Release("v1.2.0", UpdateService.InstallerAssetName), new Version(1, 0, 0));

            Assert.NotNull(info);
            Assert.Equal(new Version(1, 2, 0), info!.Version);
            Assert.Equal(123, info.Size);
        }

        [Theory]
        [InlineData("v1.0.0")]
        [InlineData("v0.9.0")]
        [InlineData("不是版本")]
        public void ParseRelease_NotNewer_ReturnsNull(string tag) =>
            Assert.Null(UpdateService.ParseRelease(Release(tag, UpdateService.InstallerAssetName), new Version(1, 0, 0)));

        [Fact]
        public void ParseRelease_InstallerNotUploadedYet_ReturnsNull() =>
            Assert.Null(UpdateService.ParseRelease(Release("v2.0.0", "other.zip"), new Version(1, 0, 0)));
    }
}
