using System.Text.Json;
using System.Text.Json.Serialization;

namespace TcWfxPlugin.Contracts;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(BridgeAuthContext))]
[JsonSerializable(typeof(WfxPathRequest))]
[JsonSerializable(typeof(WfxMoveRequest))]
[JsonSerializable(typeof(WfxUploadRequest))]
[JsonSerializable(typeof(BridgeHealthResponse))]
[JsonSerializable(typeof(WfxItemDto))]
[JsonSerializable(typeof(WfxListingData))]
[JsonSerializable(typeof(WfxProvidersData))]
[JsonSerializable(typeof(WfxResponse<JsonElement>))]
[JsonSerializable(typeof(WfxResponse<WfxListingData>))]
[JsonSerializable(typeof(WfxResponse<WfxProvidersData>))]
internal sealed partial class BridgeJsonSerializerContext : JsonSerializerContext
{
}
