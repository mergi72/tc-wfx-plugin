using System.Text.Json;
using TcWfxPlugin.Bridge;
using TcWfxPlugin.Contracts;
using TcWfxPlugin.Wfx;

namespace TcWfxPlugin.Tests;

public sealed class WfxServicesTests
{
    [Fact]
    public void ContextManager_WhenTtlExpires_HandleIsNotFound()
    {
        Environment.SetEnvironmentVariable("TC_WFX_FIND_CONTEXT_TTL_SECONDS", "1");
        Environment.SetEnvironmentVariable("TC_WFX_MAX_FIND_CONTEXTS", null);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        try
        {
            var manager = new WfxContextManager(() => now);
            var (handle, firstItem) = manager.Register([
                new WfxFindData
                {
                    FileName = "A",
                    FullPath = "edocat:/A",
                    IsDirectory = true,
                },
            ]);

            Assert.NotNull(firstItem);

            now = now.AddSeconds(2);
            var nextResult = manager.FindNext(handle, out var nextItem);

            Assert.Equal(WfxResultCodes.FileNotFound, nextResult);
            Assert.Null(nextItem);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_FIND_CONTEXT_TTL_SECONDS", null);
            Environment.SetEnvironmentVariable("TC_WFX_MAX_FIND_CONTEXTS", null);
        }
    }

    [Fact]
    public void ContextManager_WhenCapacityExceeded_EvictsOldestContext()
    {
        Environment.SetEnvironmentVariable("TC_WFX_MAX_FIND_CONTEXTS", "2");
        Environment.SetEnvironmentVariable("TC_WFX_FIND_CONTEXT_TTL_SECONDS", null);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        try
        {
            var manager = new WfxContextManager(() => now);
            var (h1, _) = manager.Register(CreateSingleItem("1"));
            var (h2, _) = manager.Register(CreateSingleItem("2"));
            var (h3, _) = manager.Register(CreateSingleItem("3"));

            var r1 = manager.FindNext(h1, out _);
            var r2 = manager.FindNext(h2, out _);
            var r3 = manager.FindNext(h3, out _);

            Assert.Equal(WfxResultCodes.FileNotFound, r1);
            Assert.Equal(WfxResultCodes.NoMoreFiles, r2);
            Assert.Equal(WfxResultCodes.NoMoreFiles, r3);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_MAX_FIND_CONTEXTS", null);
        }
    }

    [Fact]
    public async Task ListingService_NonRootInvalidPath_ReturnsFileNotFound()
    {
        var client = new FakeBridgeClient();
        var service = CreateListingService(client);

        var (resultCode, items) = await service.ResolveItemsAsync("invalid-path");

        Assert.Equal(WfxResultCodes.FileNotFound, resultCode);
        Assert.Empty(items);
    }

    [Fact]
    public async Task ListingService_MapsBridgeListingToFindData()
    {
        var client = new FakeBridgeClient
        {
            ListResponse = new WfxResponse<WfxListingData>
            {
                Ok = true,
                Data = new WfxListingData
                {
                    Provider = "edocat",
                    Path = "edocat:/",
                    Total = 1,
                    Items =
                    [
                        new WfxItemDto
                        {
                            Id = "1",
                            Name = "File.txt",
                            Path = "edocat:/File.txt",
                            IsFolder = false,
                            Size = null,
                            MimeType = "text/plain",
                        },
                    ],
                },
            },
        };

        var service = CreateListingService(client);
        var (resultCode, items) = await service.ResolveItemsAsync("\\edocat");

        Assert.Equal(WfxResultCodes.Success, resultCode);
        Assert.Single(items);
        Assert.Equal("File.txt", items[0].FileName);
        Assert.Equal("edocat:/File.txt", items[0].FullPath);
        Assert.False(items[0].IsDirectory);
        Assert.Equal(0, items[0].Size);
        Assert.Equal("text/plain", items[0].MimeType);
    }

    [Fact]
    public async Task ListingService_EmptyBridgeListing_ReturnsNoMoreFiles()
    {
        var client = new FakeBridgeClient
        {
            ListResponse = new WfxResponse<WfxListingData>
            {
                Ok = true,
                Data = new WfxListingData
                {
                    Provider = "edocat",
                    Path = "edocat:/",
                    Total = 0,
                    Items = Array.Empty<WfxItemDto>(),
                },
            },
        };

        var service = CreateListingService(client);
        var (resultCode, items) = await service.ResolveItemsAsync("\\edocat");

        Assert.Equal(WfxResultCodes.NoMoreFiles, resultCode);
        Assert.Empty(items);
    }

    [Fact]
    public async Task ListingService_RootPath_UsesConfiguredProvidersFromEnvironment()
    {
        Environment.SetEnvironmentVariable("TC_WFX_PROVIDERS", "  edocat ; edocat, alfresco  ");

        try
        {
            var client = new FakeBridgeClient();
            var service = CreateListingService(client);

            var (resultCode, items) = await service.ResolveItemsAsync("\\");

            Assert.Equal(WfxResultCodes.Success, resultCode);
            Assert.Equal(2, items.Length);
            Assert.Equal("edocat", items[0].FileName);
            Assert.Equal("alfresco", items[1].FileName);
            Assert.Equal(0, client.GetProvidersCallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_PROVIDERS", null);
        }
    }

    [Fact]
    public async Task TransferService_GetFile_InvalidBase64_ReturnsReadError()
    {
        var client = new FakeBridgeClient
        {
            DownloadResponse = JsonResponse(true, "{\"content_base64\":\"not-base64\"}"),
        };

        var service = CreateTransferService(client);
        var target = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.bin");

        try
        {
            var result = await service.GetFileAsync("\\edocat\\file.bin", target);

            Assert.Equal(WfxResultCodes.ReadError, result);
            Assert.False(File.Exists(target));
        }
        finally
        {
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }

    [Fact]
    public async Task TransferService_GetFile_ValidPayload_WritesFile()
    {
        var client = new FakeBridgeClient
        {
            DownloadResponse = JsonResponse(true, "{\"content_base64\":\"aGVsbG8=\"}"),
        };

        var service = CreateTransferService(client);
        var target = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}", "hello.txt");

        try
        {
            var result = await service.GetFileAsync("\\edocat\\hello.txt", target);

            Assert.Equal(WfxResultCodes.Success, result);
            Assert.True(File.Exists(target));
            Assert.Equal("hello", await File.ReadAllTextAsync(target));
        }
        finally
        {
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    [Fact]
    public async Task TransferService_PutFile_DelegatesOverwriteAndFilename()
    {
        var client = new FakeBridgeClient
        {
            UploadResponse = JsonResponse(true, "{}"),
        };

        var service = CreateTransferService(client);
        var source = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(source, "payload");

        try
        {
            var result = await service.PutFileAsync(source, "\\edocat\\incoming", overwrite: true);

            Assert.Equal(WfxResultCodes.Success, result);
            Assert.Equal("edocat:/incoming", client.LastUploadDestination);
            Assert.Equal(Path.GetFileName(source), client.LastUploadFileName);
            Assert.True(client.LastUploadOverwrite);
            Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("payload")), client.LastUploadContentBase64);
        }
        finally
        {
            if (File.Exists(source))
            {
                File.Delete(source);
            }
        }
    }

    [Fact]
    public async Task TransferService_MkDir_MapsBridgeError()
    {
        var client = new FakeBridgeClient
        {
            MkdirResponse = JsonResponse(false, "{}", errorCode: 403),
        };

        var service = CreateTransferService(client);

        var result = await service.MkDirAsync("\\edocat\\new-dir");

        Assert.Equal(WfxResultCodes.AccessDenied, result);
    }

    [Fact]
    public async Task TransferService_PathExists_WhenStatOk_ReturnsTrue()
    {
        var client = new FakeBridgeClient
        {
            StatResponse = JsonResponse(true, "{}"),
        };

        var service = CreateTransferService(client);
        var exists = await service.PathExistsAsync("\\edocat\\existing.txt");

        Assert.True(exists);
    }

    [Fact]
    public async Task TransferService_PathExists_WhenBridgeReturnsNotFound_ReturnsFalse()
    {
        var client = new FakeBridgeClient
        {
            StatResponse = JsonResponse(false, "{}", errorCode: 2),
        };

        var service = CreateTransferService(client);
        var exists = await service.PathExistsAsync("\\edocat\\missing.txt");

        Assert.False(exists);
    }

    [Fact]
    public async Task Runtime_GetFileAsync_WhenCanceled_ReturnsUserAbort()
    {
        var client = new FakeBridgeClient
        {
            BlockDownloadUntilCanceled = true,
        };

        var runtime = CreateRuntime(client);
        var target = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.bin");

        try
        {
            var transferTask = runtime.GetFileAsync("\\edocat\\file.bin", target);
            await client.WaitForDownloadStartAsync();

            runtime.CancelCurrentTransfer();
            var result = await transferTask;

            Assert.Equal(WfxResultCodes.UserAbort, result);
        }
        finally
        {
            if (File.Exists(target))
            {
                File.Delete(target);
            }
        }
    }

    [Fact]
    public async Task Runtime_GetFileAsync_ReportsTransferProgress()
    {
        var client = new FakeBridgeClient
        {
            DownloadResponse = JsonResponse(true, "{\"content_base64\":\"aGVsbG8=\"}"),
        };

        var runtime = CreateRuntime(client);
        var progressEvents = new List<WfxTransferProgress>();
        runtime.TransferProgressChanged += progress => progressEvents.Add(progress);

        var target = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}", "hello.txt");

        try
        {
            var result = await runtime.GetFileAsync("\\edocat\\hello.txt", target);

            Assert.Equal(WfxResultCodes.Success, result);
            Assert.NotEmpty(progressEvents);
            Assert.Contains(progressEvents, evt => evt.Operation == "download" && evt.BytesTransferred == 0 && evt.IsCompleted == false);
            Assert.Contains(progressEvents, evt => evt.Operation == "download" && evt.IsCompleted == true && evt.BytesTransferred == 5);
        }
        finally
        {
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            {
                Directory.Delete(parent, recursive: true);
            }
        }
    }

    private static WfxListingService CreateListingService(FakeBridgeClient bridgeClient)
    {
        var facade = new WfxPluginFacade(bridgeClient);
        return new WfxListingService(facade, CreateAuthProvider(), () => DateTime.UtcNow);
    }

    private static WfxTransferService CreateTransferService(FakeBridgeClient bridgeClient)
    {
        var facade = new WfxPluginFacade(bridgeClient);
        return new WfxTransferService(facade, CreateAuthProvider());
    }

    private static WfxPluginRuntime CreateRuntime(FakeBridgeClient bridgeClient)
    {
        var facade = new WfxPluginFacade(bridgeClient);
        return new WfxPluginRuntime(facade, CreateAuthProvider(), () => DateTime.UtcNow);
    }

    private static StaticAuthProvider CreateAuthProvider()
    {
        return new StaticAuthProvider(new BridgeAuthContext
        {
            Mode = "credentials",
            Username = "test",
            Password = "test",
        });
    }

    private static IReadOnlyList<WfxFindData> CreateSingleItem(string name)
    {
        return
        [
            new WfxFindData
            {
                FileName = name,
                FullPath = $"edocat:/{name}",
                IsDirectory = false,
            },
        ];
    }

    private static WfxResponse<JsonElement> JsonResponse(bool ok, string json, int errorCode = 0)
    {
        return new WfxResponse<JsonElement>
        {
            Ok = ok,
            ErrorCode = errorCode,
            Data = JsonDocument.Parse(json).RootElement.Clone(),
        };
    }

    private sealed class FakeBridgeClient : IWfxBridgeClient
    {
        public WfxResponse<WfxProvidersData> ProvidersResponse { get; set; } = new WfxResponse<WfxProvidersData>
        {
            Ok = true,
            Data = new WfxProvidersData
            {
                Providers = ["edocat", "alfresco", "fso"],
                DefaultProvider = "edocat",
            },
        };

        public WfxResponse<WfxListingData> ListResponse { get; set; } = new WfxResponse<WfxListingData>
        {
            Ok = true,
            Data = new WfxListingData
            {
                Provider = "edocat",
                Path = "edocat:/",
                Total = 1,
                Items =
                [
                    new WfxItemDto
                    {
                        Id = "1",
                        Name = "FolderA",
                        Path = "edocat:/FolderA",
                        IsFolder = true,
                    },
                ],
            },
        };

        public WfxResponse<JsonElement> DownloadResponse { get; set; } = JsonResponse(true, "{\"content_base64\":\"aGVsbG8=\"}");
        public WfxResponse<JsonElement> UploadResponse { get; set; } = JsonResponse(true, "{}");
        public WfxResponse<JsonElement> MkdirResponse { get; set; } = JsonResponse(true, "{}");
        public WfxResponse<JsonElement> DeleteResponse { get; set; } = JsonResponse(true, "{}");
        public WfxResponse<JsonElement> RenameResponse { get; set; } = JsonResponse(true, "{}");
        public WfxResponse<JsonElement> CopyResponse { get; set; } = JsonResponse(true, "{}");
        public WfxResponse<JsonElement> StatResponse { get; set; } = JsonResponse(true, "{}");

        public int GetProvidersCallCount { get; private set; }

        public string? LastUploadDestination { get; private set; }
        public string? LastUploadFileName { get; private set; }
        public string? LastUploadContentBase64 { get; private set; }
        public bool LastUploadOverwrite { get; private set; }
        public bool BlockDownloadUntilCanceled { get; set; }
        private readonly TaskCompletionSource<bool> _downloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WfxResponse<WfxProvidersData>> GetProvidersAsync(CancellationToken cancellationToken = default)
        {
            GetProvidersCallCount++;
            return Task.FromResult(ProvidersResponse);
        }

        public Task<WfxResponse<WfxListingData>> ListAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(ListResponse);

        public Task<WfxResponse<JsonElement>> StatAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(StatResponse);

        public Task<WfxResponse<JsonElement>> MkdirAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(MkdirResponse);

        public Task<WfxResponse<JsonElement>> DeleteAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(DeleteResponse);

        public Task<WfxResponse<JsonElement>> RenameAsync(string source, string destination, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(RenameResponse);

        public Task<WfxResponse<JsonElement>> CopyAsync(string source, string destination, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(CopyResponse);

        public async Task<WfxResponse<JsonElement>> DownloadAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            if (BlockDownloadUntilCanceled)
            {
                _downloadStarted.TrySetResult(true);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return DownloadResponse;
        }

        public Task WaitForDownloadStartAsync()
        {
            return _downloadStarted.Task;
        }

        public Task<WfxResponse<JsonElement>> UploadAsync(string destination, string fileName, BridgeAuthContext auth, string? contentBase64, bool overwrite, CancellationToken cancellationToken = default)
        {
            LastUploadDestination = destination;
            LastUploadFileName = fileName;
            LastUploadContentBase64 = contentBase64;
            LastUploadOverwrite = overwrite;
            return Task.FromResult(UploadResponse);
        }
    }
}