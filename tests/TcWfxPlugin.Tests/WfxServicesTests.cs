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
    public async Task Runtime_Delete_AccessDenied_RetriesAfterAuthReset()
    {
        var client = new FakeBridgeClient
        {
            DeleteResponder = auth =>
            {
                if (string.Equals(auth.Username, "first-user", StringComparison.Ordinal))
                {
                    return JsonResponse(false, "{}", errorCode: 403);
                }

                return JsonResponse(true, "{}");
            },
        };

        var authProvider = new SwitchingAuthProvider(
            new BridgeAuthContext { Mode = "credentials", Username = "first-user", Password = "bad" },
            new BridgeAuthContext { Mode = "credentials", Username = "second-user", Password = "good" });

        var runtime = CreateRuntime(client, authProvider);
        var result = await runtime.DeleteAsync(@"\edocat\to-delete.txt");

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Equal(1, authProvider.ResetCount);
        Assert.Equal(2, client.DeleteCallCount);
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
                            ModifiedAt = DateTimeOffset.Parse("2026-06-03T08:55:12Z"),
                            IsReadOnly = true,
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
        Assert.Equal(DateTimeOffset.Parse("2026-06-03T08:55:12Z"), items[0].LastWriteTimeUtc);
        Assert.True(items[0].IsReadOnly);
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
    public async Task ListingService_IgnoresBridgeItemsWithBlankNameOrPath()
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
                    Total = 2,
                    Items =
                    [
                        new WfxItemDto
                        {
                            Id = "1",
                            Name = "",
                            Path = "edocat:/",
                            IsFolder = false,
                        },
                        new WfxItemDto
                        {
                            Id = "2",
                            Name = "Valid.txt",
                            Path = "edocat:/Valid.txt",
                            IsFolder = false,
                        },
                    ],
                },
            },
        };

        var service = CreateListingService(client);
        var (resultCode, items) = await service.ResolveItemsAsync("\\edocat");

        Assert.Equal(WfxResultCodes.Success, resultCode);
        Assert.Single(items);
        Assert.Equal("Valid.txt", items[0].FileName);
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
    public async Task ListingService_Capabilities_AreCachedLongerThanProviderDiscovery()
    {
        Environment.SetEnvironmentVariable("TC_WFX_CAPABILITIES_CACHE_SECONDS", "3600");

        try
        {
            var client = new FakeBridgeClient
            {
                ProvidersResponse = new WfxResponse<WfxProvidersData>
                {
                    Ok = true,
                    Data = new WfxProvidersData
                    {
                        Providers = ["edocat"],
                        DefaultProvider = "edocat",
                        Capabilities = new Dictionary<string, WfxProviderCapabilities>
                        {
                            ["edocat"] = new WfxProviderCapabilities { Download = false },
                        },
                    },
                },
            };

            var service = CreateListingService(client);
            var first = await service.ResolveProviderCapabilitiesAsync("edocat");
            var second = await service.ResolveProviderCapabilitiesAsync("edocat");

            Assert.False(first.Download);
            Assert.False(second.Download);
            Assert.Equal(1, client.GetProvidersCallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_CAPABILITIES_CACHE_SECONDS", null);
        }
    }

    [Fact]
    public async Task ListingService_Capabilities_AreRefetchedAfterReconnectInvalidation()
    {
        Environment.SetEnvironmentVariable("TC_WFX_CAPABILITIES_CACHE_SECONDS", "3600");

        try
        {
            var client = new FakeBridgeClient
            {
                ProvidersResponse = new WfxResponse<WfxProvidersData>
                {
                    Ok = true,
                    Data = new WfxProvidersData
                    {
                        Providers = ["edocat"],
                        DefaultProvider = "edocat",
                        Capabilities = new Dictionary<string, WfxProviderCapabilities>
                        {
                            ["edocat"] = new WfxProviderCapabilities { Download = false },
                        },
                    },
                },
            };

            var service = CreateListingService(client);
            var first = await service.ResolveProviderCapabilitiesAsync("edocat");

            client.ProvidersResponse = new WfxResponse<WfxProvidersData>
            {
                Ok = true,
                Data = new WfxProvidersData
                {
                    Providers = ["edocat"],
                    DefaultProvider = "edocat",
                    Capabilities = new Dictionary<string, WfxProviderCapabilities>
                    {
                        ["edocat"] = new WfxProviderCapabilities { Download = true },
                    },
                },
            };

            var cached = await service.ResolveProviderCapabilitiesAsync("edocat");
            service.InvalidateCapabilitiesCache();
            var refreshed = await service.ResolveProviderCapabilitiesAsync("edocat");

            Assert.False(first.Download);
            Assert.False(cached.Download);
            Assert.True(refreshed.Download);
            Assert.Equal(2, client.GetProvidersCallCount);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TC_WFX_CAPABILITIES_CACHE_SECONDS", null);
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
            Assert.Null(client.LastUploadContentBase64);
            Assert.Equal(source, client.LastUploadSourcePath);
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
    public async Task TransferService_PutFile_WhenDestinationIsFile_UsesRemoteFileNameAndParentFolder()
    {
        var client = new FakeBridgeClient
        {
            UploadResponse = JsonResponse(true, "{}"),
            StatResponse = JsonResponse(true, "{\"is_folder\":false}"),
        };

        var service = CreateTransferService(client);
        var source = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(source, "payload");

        try
        {
            var result = await service.PutFileAsync(source, "\\alfresco\\path\\target.docx", overwrite: true);

            Assert.Equal(WfxResultCodes.Success, result);
            Assert.Equal("alfresco:/path", client.LastUploadDestination);
            Assert.Equal("target.docx", client.LastUploadFileName);
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

    [Fact]
    public async Task Runtime_PutFileAsync_ReportsTransferProgress()
    {
        var client = new FakeBridgeClient
        {
            UploadResponse = JsonResponse(true, "{}"),
            UploadProgressOffsets = [50, 100],
        };

        var runtime = CreateRuntime(client);
        var progressEvents = new List<WfxTransferProgress>();
        runtime.TransferProgressChanged += progress => progressEvents.Add(progress);
        var source = Path.Combine(Path.GetTempPath(), $"tc-wfx-plugin-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(source, new string('x', 100));

        try
        {
            var result = await runtime.PutFileAsync(source, "\\edocat\\incoming", overwrite: true);
            var timeoutAt = DateTime.UtcNow.AddSeconds(1);
            while (progressEvents.Count < 3 && DateTime.UtcNow < timeoutAt)
            {
                await Task.Delay(10);
            }

            Assert.Equal(WfxResultCodes.Success, result);
            Assert.NotEmpty(progressEvents);
            Assert.Contains(progressEvents, evt => evt.Operation == "upload" && evt.BytesTransferred == 0 && evt.IsCompleted == false);
            Assert.Contains(progressEvents, evt => evt.Operation == "upload" && evt.BytesTransferred == 45 && evt.IsCompleted == false);
            Assert.Contains(progressEvents, evt => evt.Operation == "upload" && evt.BytesTransferred == 90 && evt.IsCompleted == false);
            Assert.Contains(progressEvents, evt => evt.Operation == "upload" && evt.BytesTransferred == 100 && evt.IsCompleted == true);
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
    public async Task Runtime_Move_ReportsTransferProgress_AsMoveOperation()
    {
        var client = new FakeBridgeClient
        {
            RenameResponse = JsonResponse(true, "{}"),
        };

        var runtime = CreateRuntime(client);
        var progressEvents = new List<WfxTransferProgress>();
        runtime.TransferProgressChanged += progress => progressEvents.Add(progress);

        var result = await runtime.RenameAsync("\\edocat\\source.txt", "\\edocat\\target.txt");
        var timeoutAt = DateTime.UtcNow.AddSeconds(1);
        while (progressEvents.Count < 2 && DateTime.UtcNow < timeoutAt)
        {
            await Task.Delay(10);
        }

        Assert.Equal(WfxResultCodes.Success, result);
        Assert.Contains(progressEvents, evt => evt.Operation == "move" && evt.BytesTransferred == 0 && evt.IsCompleted == false);
        Assert.Contains(progressEvents, evt => evt.Operation == "move" && evt.IsCompleted == true);
        Assert.DoesNotContain(progressEvents, evt => evt.Operation == "rename");
    }

    [Fact]
    public async Task Runtime_FindFirst_AccessDenied_RetriesAfterAuthReset()
    {
        var client = new FakeBridgeClient
        {
            ListResponder = auth =>
            {
                if (string.Equals(auth.Username, "first-user", StringComparison.Ordinal))
                {
                    return new WfxResponse<WfxListingData> { Ok = false, ErrorCode = 403 };
                }

                return new WfxResponse<WfxListingData>
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
                                Name = "Recovered.txt",
                                Path = "edocat:/Recovered.txt",
                                IsFolder = false,
                            },
                        ],
                    },
                };
            },
        };

        var authProvider = new SwitchingAuthProvider(
            new BridgeAuthContext { Mode = "credentials", Username = "first-user", Password = "bad" },
            new BridgeAuthContext { Mode = "credentials", Username = "second-user", Password = "good" });

        var runtime = CreateRuntime(client, authProvider);
        var result = await runtime.FindFirstAsync("\\edocat");

        Assert.Equal(WfxResultCodes.Success, result.ResultCode);
        Assert.Equal(1, authProvider.ResetCount);
        Assert.Equal(2, client.ListCallCount);
        Assert.NotNull(result.FirstItem);
        Assert.Equal("Recovered.txt", result.FirstItem.FileName);
    }

    [Fact]
    public async Task Runtime_MkDir_AccessDenied_DoesNotRetryAuthReset()
    {
        var client = new FakeBridgeClient
        {
            MkdirResponder = auth =>
            {
                if (string.Equals(auth.Username, "first-user", StringComparison.Ordinal))
                {
                    return JsonResponse(false, "{}", errorCode: 403);
                }

                return JsonResponse(true, "{}");
            },
        };

        var authProvider = new SwitchingAuthProvider(
            new BridgeAuthContext { Mode = "credentials", Username = "first-user", Password = "bad" },
            new BridgeAuthContext { Mode = "credentials", Username = "second-user", Password = "good" });

        var runtime = CreateRuntime(client, authProvider);
        var result = await runtime.MkDirAsync("\\edocat\\new-dir");

        Assert.Equal(WfxResultCodes.AccessDenied, result);
        Assert.Equal(0, authProvider.ResetCount);
        Assert.Equal(1, client.MkdirCallCount);
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

    private static WfxPluginRuntime CreateRuntime(FakeBridgeClient bridgeClient, IWfxAuthProvider authProvider)
    {
        var facade = new WfxPluginFacade(bridgeClient);
        return new WfxPluginRuntime(facade, authProvider, () => DateTime.UtcNow);
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
        public Func<BridgeAuthContext, WfxResponse<WfxListingData>>? ListResponder { get; set; }
        public Func<BridgeAuthContext, WfxResponse<JsonElement>>? MkdirResponder { get; set; }
        public Func<BridgeAuthContext, WfxResponse<JsonElement>>? DeleteResponder { get; set; }

        public int GetProvidersCallCount { get; private set; }
        public int ListCallCount { get; private set; }
        public int MkdirCallCount { get; private set; }
        public int DeleteCallCount { get; private set; }

        public string? LastUploadDestination { get; private set; }
        public string? LastUploadFileName { get; private set; }
        public string? LastUploadContentBase64 { get; private set; }
        public string? LastUploadSourcePath { get; private set; }
        public bool LastUploadOverwrite { get; private set; }
        public IReadOnlyList<long>? UploadProgressOffsets { get; set; }
        public bool BlockDownloadUntilCanceled { get; set; }
        private readonly TaskCompletionSource<bool> _downloadStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<WfxResponse<WfxProvidersData>> GetProvidersAsync(CancellationToken cancellationToken = default)
        {
            GetProvidersCallCount++;
            return Task.FromResult(ProvidersResponse);
        }

        public Task<WfxResponse<WfxListingData>> ListAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            ListCallCount++;
            return Task.FromResult(ListResponder is not null ? ListResponder(auth) : ListResponse);
        }

        public Task<WfxResponse<JsonElement>> StatAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
            => Task.FromResult(StatResponse);

        public Task<WfxResponse<JsonElement>> MkdirAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            MkdirCallCount++;
            return Task.FromResult(MkdirResponder is not null ? MkdirResponder(auth) : MkdirResponse);
        }

        public Task<WfxResponse<JsonElement>> DeleteAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            DeleteCallCount++;
            return Task.FromResult(DeleteResponder is not null ? DeleteResponder(auth) : DeleteResponse);
        }

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
            LastUploadSourcePath = null;
            LastUploadOverwrite = overwrite;
            return Task.FromResult(UploadResponse);
        }

        public Task<WfxResponse<JsonElement>> UploadFromSourceAsync(string destination, string fileName, BridgeAuthContext auth, string sourcePath, bool overwrite, CancellationToken cancellationToken = default)
        {
            LastUploadDestination = destination;
            LastUploadFileName = fileName;
            LastUploadContentBase64 = null;
            LastUploadSourcePath = sourcePath;
            LastUploadOverwrite = overwrite;
            return Task.FromResult(UploadResponse);
        }

        public Task<WfxResponse<JsonElement>> UploadRawAsync(string destination, string fileName, BridgeAuthContext auth, string sourcePath, bool overwrite, IProgress<long>? progress = null, CancellationToken cancellationToken = default)
        {
            LastUploadDestination = destination;
            LastUploadFileName = fileName;
            LastUploadContentBase64 = null;
            LastUploadSourcePath = sourcePath;
            LastUploadOverwrite = overwrite;
            if (progress is not null && UploadProgressOffsets is not null)
            {
                foreach (var bytesTransferred in UploadProgressOffsets)
                {
                    progress.Report(bytesTransferred);
                }
            }

            return Task.FromResult(UploadResponse);
        }
    }

    private sealed class SwitchingAuthProvider : IWfxAuthProvider
    {
        private readonly BridgeAuthContext _first;
        private readonly BridgeAuthContext _second;
        private bool _useSecond;

        public int ResetCount { get; private set; }

        public SwitchingAuthProvider(BridgeAuthContext first, BridgeAuthContext second)
        {
            _first = first;
            _second = second;
        }

        public BridgeAuthContext GetAuthContext(string? provider = null)
        {
            return _useSecond ? _second : _first;
        }

        public void ResetCachedAuth()
        {
            ResetCount++;
            _useSecond = true;
        }
    }
}
