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
        Assert.Contains(firstItem.FileName, new[] { "edocat", "alfresco", "fso", "dynamic-a", "dynamic-b" });
    }

    [Fact]
    public void FsFindFirst_RootPath_UsesProvidersFromBridge()
    {
        var entryPoints = CreateEntryPoints(new[] { "dynamic-a", "dynamic-b" });

        var firstResult = entryPoints.FsFindFirst("\\", out var handle, out var firstItem);
        var secondResult = entryPoints.FsFindNext(handle, out var secondItem);

        Assert.Equal(WfxResultCodes.Success, firstResult);
        Assert.Equal(WfxResultCodes.Success, secondResult);
        Assert.NotNull(firstItem);
        Assert.NotNull(secondItem);
        Assert.Equal("dynamic-a", firstItem.FileName);
        Assert.Equal("dynamic-b", secondItem.FileName);
    }

    [Fact]
    public void FsFindFirst_RootPath_UsesCachedProvidersWithinTtl()
    {
        var (entryPoints, bridgeClient) = CreateEntryPointsAndClient(new[] { "dynamic-a", "dynamic-b" });

        var firstResult = entryPoints.FsFindFirst("\\", out var firstHandle, out _);
        var closeFirst = entryPoints.FsFindClose(firstHandle);
        var secondResult = entryPoints.FsFindFirst("\\", out var secondHandle, out _);
        var closeSecond = entryPoints.FsFindClose(secondHandle);

        Assert.Equal(WfxResultCodes.Success, firstResult);
        Assert.Equal(WfxResultCodes.Success, secondResult);
        Assert.Equal(WfxResultCodes.Success, closeFirst);
        Assert.Equal(WfxResultCodes.Success, closeSecond);
        Assert.Equal(1, bridgeClient.GetProvidersCallCount);
    }

    [Fact]
    public void FsFindFirst_RootPath_UsesStaleCacheWhenBridgeUnavailable()
    {
        Environment.SetEnvironmentVariable("TC_WFX_PROVIDERS_CACHE_SECONDS", "1");
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        try
        {
            var bridgeClient = new FakeBridgeClient(new[] { "dynamic-a", "dynamic-b" });
            var entryPoints = CreateEntryPoints(bridgeClient, () => now);

            var firstResult = entryPoints.FsFindFirst("\\", out var firstHandle, out var firstItem);
            var firstClose = entryPoints.FsFindClose(firstHandle);

            bridgeClient.FailGetProviders = true;
            now = now.AddSeconds(5);

            var secondResult = entryPoints.FsFindFirst("\\", out var secondHandle, out var secondItem);
            var secondClose = entryPoints.FsFindClose(secondHandle);

            Assert.Equal(WfxResultCodes.Success, firstResult);
            Assert.Equal(WfxResultCodes.Success, secondResult);
            Assert.Equal(WfxResultCodes.Success, firstClose);
            Assert.Equal(WfxResultCodes.Success, secondClose);
            Assert.NotNull(firstItem);
            Assert.NotNull(secondItem);
            Assert.Equal("dynamic-a", firstItem.FileName);
            Assert.Equal("dynamic-a", secondItem.FileName);
            Assert.Equal(2, bridgeClient.GetProvidersCallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_PROVIDERS_CACHE_SECONDS", null);
        }
    }

    [Fact]
    public void InvalidateProvidersCache_ForcesNextBridgeFetch()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "dynamic-a", "dynamic-b" });
        var entryPoints = CreateEntryPoints(bridgeClient);

        var firstResult = entryPoints.FsFindFirst("\\", out var firstHandle, out var firstItem);
        var firstClose = entryPoints.FsFindClose(firstHandle);

        bridgeClient.SetProviders(new[] { "new-a", "new-b" });

        var secondResult = entryPoints.FsFindFirst("\\", out var secondHandle, out var secondItem);
        var secondClose = entryPoints.FsFindClose(secondHandle);

        entryPoints.InvalidateProvidersCache();

        var thirdResult = entryPoints.FsFindFirst("\\", out var thirdHandle, out var thirdItem);
        var thirdClose = entryPoints.FsFindClose(thirdHandle);

        Assert.Equal(WfxResultCodes.Success, firstResult);
        Assert.Equal(WfxResultCodes.Success, secondResult);
        Assert.Equal(WfxResultCodes.Success, thirdResult);
        Assert.Equal(WfxResultCodes.Success, firstClose);
        Assert.Equal(WfxResultCodes.Success, secondClose);
        Assert.Equal(WfxResultCodes.Success, thirdClose);

        Assert.NotNull(firstItem);
        Assert.NotNull(secondItem);
        Assert.NotNull(thirdItem);
        Assert.Equal("dynamic-a", firstItem.FileName);
        Assert.Equal("dynamic-a", secondItem.FileName);
        Assert.Equal("new-a", thirdItem.FileName);
        Assert.Equal(2, bridgeClient.GetProvidersCallCount);
    }

    [Fact]
    public void FsFindNext_ExpiredHandle_ReturnsFileNotFound()
    {
        Environment.SetEnvironmentVariable("TC_WFX_FIND_CONTEXT_TTL_SECONDS", "1");
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        try
        {
            var entryPoints = CreateEntryPoints(new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" }), () => now);

            var firstResult = entryPoints.FsFindFirst("\\edocat", out var handle, out _);
            now = now.AddSeconds(2);
            var nextResult = entryPoints.FsFindNext(handle, out _);

            Assert.Equal(WfxResultCodes.Success, firstResult);
            Assert.Equal(WfxResultCodes.FileNotFound, nextResult);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_FIND_CONTEXT_TTL_SECONDS", null);
        }
    }

    [Fact]
    public void FsFindFirst_WhenMaxContextsExceeded_EvictsOldestHandle()
    {
        Environment.SetEnvironmentVariable("TC_WFX_MAX_FIND_CONTEXTS", "2");

        try
        {
            var entryPoints = CreateEntryPoints();

            var firstResult = entryPoints.FsFindFirst("\\edocat", out var handle1, out _);
            var secondResult = entryPoints.FsFindFirst("\\edocat", out var handle2, out _);
            var thirdResult = entryPoints.FsFindFirst("\\edocat", out var handle3, out _);

            var evictedNext = entryPoints.FsFindNext(handle1, out _);
            var keepSecond = entryPoints.FsFindNext(handle2, out _);
            var keepThird = entryPoints.FsFindNext(handle3, out _);

            Assert.Equal(WfxResultCodes.Success, firstResult);
            Assert.Equal(WfxResultCodes.Success, secondResult);
            Assert.Equal(WfxResultCodes.Success, thirdResult);

            Assert.Equal(WfxResultCodes.FileNotFound, evictedNext);
            Assert.Equal(WfxResultCodes.Success, keepSecond);
            Assert.Equal(WfxResultCodes.Success, keepThird);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_MAX_FIND_CONTEXTS", null);
        }
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

    private static WfxEntryPoints CreateEntryPoints(string[]? providers = null)
    {
        var bridgeClient = new FakeBridgeClient(providers ?? new[] { "edocat", "alfresco", "fso" });
        return CreateEntryPoints(bridgeClient);
    }

    private static (WfxEntryPoints EntryPoints, FakeBridgeClient BridgeClient) CreateEntryPointsAndClient(string[] providers)
    {
        var bridgeClient = new FakeBridgeClient(providers);
        return (CreateEntryPoints(bridgeClient), bridgeClient);
    }

    private static WfxEntryPoints CreateEntryPoints(FakeBridgeClient bridgeClient, Func<DateTime>? utcNow = null)
    {
        var facade = new WfxPluginFacade(bridgeClient);
        var authProvider = new StaticAuthProvider(new BridgeAuthContext
        {
            Mode = "credentials",
            Username = "test",
            Password = "test",
        });

        var runtime = new WfxPluginRuntime(facade, authProvider, utcNow);
        return new WfxEntryPoints(runtime);
    }

    private sealed class FakeBridgeClient : IWfxBridgeClient
    {
        private string[] _providers;
        public int GetProvidersCallCount { get; private set; }
        public bool FailGetProviders { get; set; }

        public FakeBridgeClient(string[] providers)
        {
            _providers = providers;
        }

        public Task<WfxResponse<WfxProvidersData>> GetProvidersAsync(CancellationToken cancellationToken = default)
        {
            GetProvidersCallCount++;

            if (FailGetProviders)
            {
                throw new InvalidOperationException("Bridge unavailable");
            }

            return Task.FromResult(new WfxResponse<WfxProvidersData>
            {
                Ok = true,
                Data = new WfxProvidersData
                {
                    Providers = _providers,
                    DefaultProvider = _providers.FirstOrDefault(),
                },
            });
        }

        public void SetProviders(string[] providers)
        {
            _providers = providers;
        }

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
