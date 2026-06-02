using System.Text.Json;
using TcWfxPlugin.Bridge;
using TcWfxPlugin.Contracts;
using TcWfxPlugin.Wfx;

namespace TcWfxPlugin.Tests;

public sealed class WfxEntryPointsTests
{
    [Fact]
    public void FsFindFirst_ThenFindNext_IteratesOverItems()
    {
        var entryPoints = CreateEntryPoints();

        var firstResult = entryPoints.FsFindFirst("\\edocat", out var handle, out var firstItem);
        var secondResult = entryPoints.FsFindNext(handle, out var secondItem);
        var noMoreResult = entryPoints.FsFindNext(handle, out var none);
        var closeResult = entryPoints.FsFindClose(handle);

        Assert.Equal(WfxResultCodes.Success, firstResult);
        Assert.Equal(WfxResultCodes.Success, secondResult);
        Assert.Equal(WfxResultCodes.NoMoreFiles, noMoreResult);
        Assert.Equal(WfxResultCodes.Success, closeResult);

        Assert.NotNull(firstItem);
        Assert.Equal("FolderA", firstItem.FileName);

        Assert.NotNull(secondItem);
        Assert.Equal("FileB.txt", secondItem.FileName);

        Assert.Null(none);
    }

    [Fact]
    public void FsFindFirst_RootPath_ReturnsProviderFolders()
    {
        var entryPoints = CreateEntryPoints();

        var firstResult = entryPoints.FsFindFirst("\\", out var handle, out var firstItem);

        Assert.Equal(WfxResultCodes.Success, firstResult);
        Assert.NotEqual(0, handle);
        Assert.NotNull(firstItem);
        Assert.True(firstItem.IsDirectory);
        Assert.Contains(firstItem.FileName, new[] { "edocat", "alfresco", "fso" });
    }

    [Fact]
    public void FsFindFirst_WildcardPath_MapsToProviderDirectory()
    {
        var entryPoints = CreateEntryPoints();

        var firstResult = entryPoints.FsFindFirst("\\edocat\\*.*", out _, out var firstItem);

        Assert.Equal(WfxResultCodes.Success, firstResult);
        Assert.NotNull(firstItem);
        Assert.Equal("FolderA", firstItem.FileName);
    }

    [Fact]
    public void FsMkDir_InvalidPath_ReturnsFileNotFound()
    {
        var entryPoints = CreateEntryPoints();

        var result = entryPoints.FsMkDir("invalid-path");

        Assert.Equal(WfxResultCodes.FileNotFound, result);
    }

    [Fact]
    public void FsGetFile_InvalidPath_ReturnsFileNotFound()
    {
        var entryPoints = CreateEntryPoints();
        var localPath = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");

        var result = entryPoints.FsGetFile("invalid-path", localPath);

        Assert.Equal(WfxResultCodes.FileNotFound, result);
    }

    [Fact]
    public void FsPutFile_ValidPath_ReturnsSuccess()
    {
        var entryPoints = CreateEntryPoints();
        var localPath = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");
        File.WriteAllText(localPath, "hello");

        var result = entryPoints.FsPutFile(localPath, "\\edocat\\incoming", overwrite: true);

        Assert.Equal(WfxResultCodes.Success, result);
    }

    private static WfxEntryPoints CreateEntryPoints()
    {
        var facade = new WfxPluginFacade(new FakeBridgeClient());
        var authProvider = new StaticAuthProvider(new BridgeAuthContext
        {
            Mode = "credentials",
            Username = "test",
            Password = "test",
        });

        var runtime = new WfxPluginRuntime(facade, authProvider);
        return new WfxEntryPoints(runtime);
    }

    private sealed class FakeBridgeClient : IWfxBridgeClient
    {
        public Task<WfxResponse<WfxListingData>> ListAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WfxResponse<WfxListingData>
            {
                Ok = true,
                Data = new WfxListingData
                {
                    Provider = "edocat",
                    Path = providerPath,
                    Total = 2,
                    Items =
                    [
                        new WfxItemDto
                        {
                            Id = "1",
                            Name = "FolderA",
                            Path = "edocat:/FolderA",
                            IsFolder = true,
                        },
                        new WfxItemDto
                        {
                            Id = "2",
                            Name = "FileB.txt",
                            Path = "edocat:/FileB.txt",
                            IsFolder = false,
                            Size = 123,
                            MimeType = "text/plain",
                        },
                    ],
                },
            });
        }

        public Task<WfxResponse<JsonElement>> StatAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(new WfxResponse<JsonElement> { Ok = true });

        public Task<WfxResponse<JsonElement>> MkdirAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(new WfxResponse<JsonElement> { Ok = true });

        public Task<WfxResponse<JsonElement>> DeleteAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(new WfxResponse<JsonElement> { Ok = true });

        public Task<WfxResponse<JsonElement>> RenameAsync(string source, string destination, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(new WfxResponse<JsonElement> { Ok = true });

        public Task<WfxResponse<JsonElement>> CopyAsync(string source, string destination, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(new WfxResponse<JsonElement> { Ok = true });

        public Task<WfxResponse<JsonElement>> DownloadAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            var data = JsonDocument.Parse("{" + "\"content_base64\":\"aGVsbG8=\"" + "}").RootElement;
            return Task.FromResult(new WfxResponse<JsonElement> { Ok = true, Data = data });
        }

        public Task<WfxResponse<JsonElement>> UploadAsync(string destination, string fileName, BridgeAuthContext auth, string? contentBase64, bool overwrite, CancellationToken cancellationToken = default)
            => Task.FromResult(new WfxResponse<JsonElement> { Ok = true });
    }
}
