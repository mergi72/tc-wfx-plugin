using System.Text.Json;
using TcWfxPlugin.Contracts;

namespace TcWfxPlugin.Tests;

public sealed class BridgeJsonDeserializerTests
{
    [Fact]
    public void ListingResponse_ParsesModifiedAtOffsetWithoutColon()
    {
        const string json = """
        {
          "ok": true,
          "error_code": 0,
          "message": null,
          "data": {
            "provider": "alfresco",
            "path": "/",
            "total": 1,
            "items": [
              {
                "id": "195b51c6-0f3f-4ca4-9a11-3775ceaa7be1",
                "name": "folder",
                "path": "/",
                "is_folder": true,
                "size": null,
                "mime_type": null,
                "modified_at": "2026-06-03T08:53:43.991+0000",
                "is_read_only": null
              }
            ]
          },
          "metadata": {
            "provider": "alfresco"
          }
        }
        """;

        var parsed = JsonSerializer.Deserialize(json, BridgeJsonSerializerContext.Default.WfxResponseWfxListingData);

        Assert.NotNull(parsed);
        Assert.True(parsed!.Ok);
        Assert.NotNull(parsed.Data);
        Assert.Single(parsed.Data!.Items);
        Assert.Equal(new DateTimeOffset(2026, 6, 3, 8, 53, 43, 991, TimeSpan.Zero), parsed.Data.Items[0].ModifiedAt);
    }
}
