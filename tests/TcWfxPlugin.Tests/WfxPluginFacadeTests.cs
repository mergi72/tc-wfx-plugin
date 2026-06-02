using System.Text.Json;
using TcWfxPlugin.Bridge;
using TcWfxPlugin.Contracts;
using TcWfxPlugin.Wfx;

namespace TcWfxPlugin.Tests;

public sealed class WfxPluginFacadeTests
{
    [Fact]
    public async Task ListDirectoryAsync_InvalidPath_ReturnsFailedResponse()
    {
        var bridgeClient = new FakeBridgeClient();
        var facade = new WfxPluginFacade(bridgeClient);

        var result = await facade.ListDirectoryAsync("invalid-path", CreateAuth());

        Assert.False(result.Ok);
        Assert.NotEqual(0, result.ErrorCode);
    }

    [Fact]
    public async Task ListDirectoryAsync_ValidPath_DelegatesToBridgeClient()
    {
        var bridgeClient = new FakeBridgeClient();
        var facade = new WfxPluginFacade(bridgeClient);

        var result = await facade.ListDirectoryAsync("edocat:/", CreateAuth());

        Assert.True(result.Ok);
        Assert.Equal("edocat", result.Data?.Provider);
    }

    private static BridgeAuthContext CreateAuth()
    {
        return new BridgeAuthContext
        {
            Mode = "credentials",
            Username = "test",
            Password = "test",
        };
    }

    private sealed class FakeBridgeClient : IWfxBridgeClient
    {
        public Task<WfxResponse<WfxProvidersData>> GetProvidersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WfxResponse<WfxProvidersData>
            {
                Ok = true,
                Data = new WfxProvidersData
                {
                    Providers = new[] { "edocat", "alfresco", "fso" },
                    DefaultProvider = "edocat",
                },
            });
        }

        public Task<WfxResponse<WfxListingData>> ListAsync(string providerPath, BridgeAuthContext auth, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new WfxResponse<WfxListingData>
            {
                Ok = true,
                ErrorCode = 0,
                Data = new WfxListingData
                {
                    Provider = "edocat",
                    Path = providerPath,
                    Total = 0,
                    Items = Array.Empty<WfxItemDto>(),
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
            => Task.FromResult(new WfxResponse<JsonElement> { Ok = true });

        public Task<WfxResponse<JsonElement>> UploadAsync(string destination, string fileName, BridgeAuthContext auth, string? contentBase64, bool overwrite, CancellationToken cancellationToken = default)
            => Task.FromResult(new WfxResponse<JsonElement> { Ok = true });
    }
}
