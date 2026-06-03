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
        var previousProviders = Environment.GetEnvironmentVariable("TC_WFX_PROVIDERS");
        Environment.SetEnvironmentVariable("TC_WFX_PROVIDERS", null);

        try
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
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_PROVIDERS", previousProviders);
        }
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
    public void FsFindFirst_AlfrescoLikeRootItems_WithSlashPath_AreReturned()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" })
        {
            ListItemsOverride =
            [
                new WfxItemDto
                {
                    Id = "a1",
                    Name = "03 zakázky v realizaci",
                    Path = "/",
                    IsFolder = true,
                },
                new WfxItemDto
                {
                    Id = "a2",
                    Name = "04 zakázky ukončené",
                    Path = "/",
                    IsFolder = true,
                },
            ],
        };

        var entryPoints = CreateEntryPoints(bridgeClient);

        var firstResult = entryPoints.FsFindFirst("\\alfresco", out var handle, out var firstItem);
        var secondResult = entryPoints.FsFindNext(handle, out var secondItem);

        Assert.Equal(WfxResultCodes.Success, firstResult);
        Assert.Equal(WfxResultCodes.Success, secondResult);
        Assert.NotNull(firstItem);
        Assert.NotNull(secondItem);
        Assert.True(firstItem.IsDirectory);
        Assert.True(secondItem.IsDirectory);
        Assert.Equal("03 zakázky v realizaci", firstItem.FileName);
        Assert.Equal("04 zakázky ukončené", secondItem.FileName);
    }

    [Fact]
    public void FsMkDir_InvalidPath_ReturnsFileNotFound()
    {
        var entryPoints = CreateEntryPoints();

        var result = entryPoints.FsMkDir("invalid-path");

        Assert.Equal(WfxResultCodes.FileNotFound, result);
    }

    [Fact]
    public void FsDeleteFile_NotFound_IsTreatedAsSuccess()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" })
        {
            DeleteErrorCode = 404,
            StatErrorCode = 404,
        };
        var entryPoints = CreateEntryPoints(bridgeClient);

        var result = entryPoints.FsDeleteFile("\\alfresco\\missing\\file.txt");

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(1, bridgeClient.DeleteCallCount);
    }

    [Fact]
    public void FsDeleteFile_AccessDenied_ButPathMissing_IsTreatedAsSuccess()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" })
        {
            DeleteErrorCode = 5,
            StatErrorCode = 404,
        };
        var entryPoints = CreateEntryPoints(bridgeClient);

        var result = entryPoints.FsDeleteFile("\\alfresco\\missing\\file.txt");

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(1, bridgeClient.DeleteCallCount);
    }

    [Fact]
    public void FsDeleteFile_AccessDenied_PathExistsThenMissing_IsTreatedAsSuccess()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" })
        {
            DeleteErrorCode = 3,
            StatErrorCodesSequence = new Queue<int?>(new int?[] { null, 404 }),
        };
        var entryPoints = CreateEntryPoints(bridgeClient);

        var result = entryPoints.FsDeleteFile("\\alfresco\\eventual\\file.txt");

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(2, bridgeClient.StatCallCount);
    }

    [Fact]
    public void FsDeleteFile_AccessDenied_WhenPathStillExists_ReturnsAccessDenied()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" })
        {
            DeleteErrorCode = 3,
            StatErrorCode = null,
        };
        var entryPoints = CreateEntryPoints(bridgeClient);

        var result = entryPoints.FsDeleteFile("\\alfresco\\existing\\file.txt");

        Assert.Equal(WfxResultCodes.AccessDenied, result);
        Assert.Equal(2, bridgeClient.DeleteCallCount);
    }

    [Fact]
    public void FsRenMovFile_Move_DmsToFso_UsesDownloadThenDelete()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" });
        var entryPoints = CreateEntryPoints(bridgeClient);
        var localPath = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");

        try
        {
            var result = entryPoints.FsRenMovFile("\\alfresco\\source\\file.txt", localPath, move: true);

            Assert.Equal(WfxResultCodes.Success, result);
            Assert.Equal(1, bridgeClient.DownloadCallCount);
            Assert.Equal(1, bridgeClient.DeleteCallCount);
            Assert.Equal(0, bridgeClient.RenameCallCount);
        }
        finally
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
        }
    }

    [Fact]
    public void FsRenMovFile_Move_DmsToFso_TargetDirectory_AppendsSourceLeafName()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" });
        var entryPoints = CreateEntryPoints(bridgeClient);
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDirectory);

        try
        {
            var result = entryPoints.FsRenMovFile("\\alfresco\\source\\file.txt", targetDirectory + "\\", move: true);
            var expectedFile = Path.Combine(targetDirectory, "file.txt");

            Assert.Equal(WfxResultCodes.Success, result);
            Assert.True(File.Exists(expectedFile));
            Assert.Equal(1, bridgeClient.DownloadCallCount);
            Assert.Equal(1, bridgeClient.DeleteCallCount);
        }
        finally
        {
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public void FsRenMovFile_Move_DmsToFso_DeleteNotFound_IsTreatedAsSuccess()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" })
        {
            DeleteErrorCode = 404,
        };
        var entryPoints = CreateEntryPoints(bridgeClient);
        var localPath = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");

        try
        {
            var result = entryPoints.FsRenMovFile("\\alfresco\\source\\file.txt", localPath, move: true);

            Assert.Equal(WfxResultCodes.Success, result);
            Assert.Equal(1, bridgeClient.DownloadCallCount);
            Assert.Equal(1, bridgeClient.DeleteCallCount);
        }
        finally
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
        }
    }

    [Fact]
    public void FsRenMovFile_Move_DmsToDms_UsesBridgeMove()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" });
        var entryPoints = CreateEntryPoints(bridgeClient);

        var result = entryPoints.FsRenMovFile("\\alfresco\\source\\file.txt", "\\alfresco\\target\\file.txt", move: true);

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(1, bridgeClient.RenameCallCount);
        Assert.Equal(0, bridgeClient.DownloadCallCount);
        Assert.Equal(0, bridgeClient.UploadCallCount);
    }

    [Fact]
    public void FsRenMovFile_Move_SamePath_IsHandledAsDelete()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" })
        {
            DeleteErrorCode = 404,
            StatErrorCode = 404,
        };
        var entryPoints = CreateEntryPoints(bridgeClient);

        var path = "\\alfresco\\source\\ping_copy.xls";
        var result = entryPoints.FsRenMovFile(path, path, move: true);

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(1, bridgeClient.DeleteCallCount);
        Assert.Equal(0, bridgeClient.RenameCallCount);
    }

    [Fact]
    public void FsRenMovFile_RenameFlagFalse_SamePath_IsHandledAsDelete()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" })
        {
            DeleteErrorCode = 404,
            StatErrorCode = 404,
        };
        var entryPoints = CreateEntryPoints(bridgeClient);

        var path = "\\alfresco\\source\\ping_copy.xls";
        var result = entryPoints.FsRenMovFile(path, path, move: false);

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(1, bridgeClient.DeleteCallCount);
        Assert.Equal(0, bridgeClient.CopyCallCount);
    }

    [Fact]
    public void FsRenMovFile_SameProviderPath_WithAndWithoutLeadingSlash_IsHandledAsDelete()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" })
        {
            DeleteErrorCode = 404,
            StatErrorCode = 404,
        };
        var entryPoints = CreateEntryPoints(bridgeClient);

        var result = entryPoints.FsRenMovFile("\\alfresco\\source\\doc.docx", "alfresco\\source\\doc.docx", move: false);

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(1, bridgeClient.DeleteCallCount);
    }

    [Fact]
    public void FsRenMovFile_Move_FsoToDms_UsesUploadThenDeletesLocalSource()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" });
        var entryPoints = CreateEntryPoints(bridgeClient);
        var localPath = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");
        File.WriteAllText(localPath, "hello");

        var result = entryPoints.FsRenMovFile(localPath, "\\alfresco\\incoming\\file.txt", move: true);

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(1, bridgeClient.UploadCallCount);
        Assert.False(File.Exists(localPath));
        Assert.Equal(0, bridgeClient.RenameCallCount);
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
    public void FsGetFile_CopyFlagResume_IsIgnored()
    {
        var entryPoints = CreateEntryPoints();
        var localPath = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");

        var result = entryPoints.FsGetFile("\\edocat\\file.txt", localPath, copyFlags: 0x04);

        Assert.Equal(WfxResultCodes.Success, result);

        if (File.Exists(localPath))
        {
            File.Delete(localPath);
        }
    }

    [Fact]
    public void FsGetFile_WithoutOverwrite_WhenTargetExists_ReturnsWriteError()
    {
        var entryPoints = CreateEntryPoints();
        var localPath = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");
        File.WriteAllText(localPath, "existing");

        try
        {
            var result = entryPoints.FsGetFile("\\edocat\\file.txt", localPath, copyFlags: 0);

            Assert.Equal(WfxResultCodes.WriteError, result);
        }
        finally
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
        }
    }

    [Fact]
    public void FsGetFile_WithOverwrite_WhenTargetExists_ReturnsSuccess()
    {
        var entryPoints = CreateEntryPoints();
        var localPath = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");
        File.WriteAllText(localPath, "existing");

        try
        {
            var result = entryPoints.FsGetFile("\\edocat\\file.txt", localPath, copyFlags: 0x02);

            Assert.Equal(WfxResultCodes.Success, result);
        }
        finally
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
        }
    }

    [Fact]
    public void FsGetFile_PluginTargetPath_UsesDirectCopyInsteadOfDownload()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" });
        var entryPoints = CreateEntryPoints(bridgeClient);

        var result = entryPoints.FsGetFile("\\edocat\\source\\file.txt", "\\edocat\\target", copyFlags: 0x02);

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(1, bridgeClient.CopyCallCount);
        Assert.Equal(0, bridgeClient.DownloadCallCount);
    }

    [Fact]
    public void FsGetFile_PluginTargetPathWithoutLeadingSlash_UsesDirectCopyInsteadOfDownload()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" });
        var entryPoints = CreateEntryPoints(bridgeClient);

        var result = entryPoints.FsGetFile("alfresco\\source\\file.txt", "alfresco\\target", copyFlags: 0x02);

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(1, bridgeClient.CopyCallCount);
        Assert.Equal(0, bridgeClient.DownloadCallCount);
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

    [Fact]
    public void FsPutFile_CopyFlagResume_IsIgnored()
    {
        var entryPoints = CreateEntryPoints();
        var localPath = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");
        File.WriteAllText(localPath, "hello");

        try
        {
            var result = entryPoints.FsPutFile(localPath, "\\edocat\\incoming", copyFlags: 0x04);

            Assert.Equal(WfxResultCodes.Success, result);
        }
        finally
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
        }
    }

    [Fact]
    public void FsPutFile_CopyFlags_MapsOverwriteToUpload()
    {
        var bridgeClient = new FakeBridgeClient(new[] { "edocat", "alfresco", "fso" });
        var entryPoints = CreateEntryPoints(bridgeClient);
        var localPath = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");
        File.WriteAllText(localPath, "hello");

        try
        {
            var withoutOverwrite = entryPoints.FsPutFile(localPath, "\\edocat\\incoming", copyFlags: 0);
            var firstOverwriteFlag = bridgeClient.LastUploadOverwrite;
            var withOverwrite = entryPoints.FsPutFile(localPath, "\\edocat\\incoming", copyFlags: 0x02);
            var secondOverwriteFlag = bridgeClient.LastUploadOverwrite;

            Assert.Equal(WfxResultCodes.Success, withoutOverwrite);
            Assert.False(firstOverwriteFlag);

            Assert.Equal(WfxResultCodes.Success, withOverwrite);
            Assert.True(secondOverwriteFlag);
        }
        finally
        {
            if (File.Exists(localPath))
            {
                File.Delete(localPath);
            }
        }
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
        public int CopyCallCount { get; private set; }
        public int DownloadCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }
        public int RenameCallCount { get; private set; }
        public int UploadCallCount { get; private set; }
        public bool FailGetProviders { get; set; }
        public bool LastUploadOverwrite { get; private set; }
        public int? DeleteErrorCode { get; set; }
        public int? StatErrorCode { get; set; }
        public Queue<int?>? StatErrorCodesSequence { get; set; }
        public int StatCallCount { get; private set; }
        public IReadOnlyList<WfxItemDto>? ListItemsOverride { get; set; }

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
            var items = ListItemsOverride ??
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
            ];

            return Task.FromResult(new WfxResponse<WfxListingData>
            {
                Ok = true,
                Data = new WfxListingData
                {
                    Provider = "edocat",
                    Path = providerPath,
                    Total = items.Count,
                    Items = items,
                },
            });
        }

        public Task<WfxResponse<JsonElement>> StatAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            StatCallCount++;

            if (StatErrorCodesSequence is { Count: > 0 })
            {
                var next = StatErrorCodesSequence.Dequeue();
                if (next is int sequenceErrorCode)
                {
                    return Task.FromResult(new WfxResponse<JsonElement> { Ok = false, ErrorCode = sequenceErrorCode, Message = "stat failed" });
                }

                return Task.FromResult(new WfxResponse<JsonElement> { Ok = true });
            }

            if (StatErrorCode is int errorCode)
            {
                return Task.FromResult(new WfxResponse<JsonElement> { Ok = false, ErrorCode = errorCode, Message = "stat failed" });
            }

            return Task.FromResult(new WfxResponse<JsonElement> { Ok = true });
        }

        public Task<WfxResponse<JsonElement>> MkdirAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(new WfxResponse<JsonElement> { Ok = true });

        public Task<WfxResponse<JsonElement>> DeleteAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            if (DeleteErrorCode is int errorCode)
            {
                return Task.FromResult(new WfxResponse<JsonElement> { Ok = false, ErrorCode = errorCode, Message = "delete failed" });
            }

            return Task.FromResult(new WfxResponse<JsonElement> { Ok = true });
        }

        public Task<WfxResponse<JsonElement>> RenameAsync(string source, string destination, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            RenameCallCount++;
            return Task.FromResult(new WfxResponse<JsonElement> { Ok = true });
        }

        public Task<WfxResponse<JsonElement>> CopyAsync(string source, string destination, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            CopyCallCount++;
            return Task.FromResult(new WfxResponse<JsonElement> { Ok = true });
        }

        public Task<WfxResponse<JsonElement>> DownloadAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            DownloadCallCount++;
            var data = JsonDocument.Parse("{" + "\"content_base64\":\"aGVsbG8=\"" + "}").RootElement;
            return Task.FromResult(new WfxResponse<JsonElement> { Ok = true, Data = data });
        }

        public Task<WfxResponse<JsonElement>> UploadAsync(string destination, string fileName, BridgeAuthContext auth, string? contentBase64, bool overwrite, CancellationToken cancellationToken = default)
        {
            UploadCallCount++;
            LastUploadOverwrite = overwrite;
            return Task.FromResult(new WfxResponse<JsonElement> { Ok = true });
        }
    }
}
