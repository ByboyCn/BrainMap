using ProtoBuf;

namespace MindMap.Backend.Endpoints;

public static class PbIo
{
    public static async Task<T> ReadAsync<T>(HttpRequest request) where T : class, new()
    {
        using var stream = new MemoryStream();
        await request.Body.CopyToAsync(stream);
        stream.Position = 0;
        return Serializer.Deserialize<T>(stream);
    }

    public static IResult Write<T>(T response) where T : class
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, response);
        return Results.File(stream.ToArray(), "application/x-protobuf");
    }
}
