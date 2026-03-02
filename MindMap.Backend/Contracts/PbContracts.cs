using ProtoBuf;

namespace MindMap.Backend.Contracts;

[ProtoContract]
public class PbAuthRequest
{
    [ProtoMember(1)] public string UserName { get; set; } = string.Empty;
    [ProtoMember(2)] public string Password { get; set; } = string.Empty;
}

[ProtoContract]
public class PbAuthResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
    [ProtoMember(3)] public string UserId { get; set; } = string.Empty;
    [ProtoMember(4)] public string UserName { get; set; } = string.Empty;
    [ProtoMember(5)] public string Token { get; set; } = string.Empty;
}

[ProtoContract]
public class PbEmptyRequest
{
}

[ProtoContract]
public class PbMindMapIdRequest
{
    [ProtoMember(1)] public string MapId { get; set; } = string.Empty;
}

[ProtoContract]
public class PbCreateMindMapRequest
{
    [ProtoMember(1)] public string Title { get; set; } = string.Empty;
    [ProtoMember(2)] public string ContentJson { get; set; } = string.Empty;
}

[ProtoContract]
public class PbUpdateMindMapRequest
{
    [ProtoMember(1)] public string MapId { get; set; } = string.Empty;
    [ProtoMember(2)] public string Title { get; set; } = string.Empty;
    [ProtoMember(3)] public string ContentJson { get; set; } = string.Empty;
}

[ProtoContract]
public class PbShareCodeRequest
{
    [ProtoMember(1)] public string ShareCode { get; set; } = string.Empty;
}

[ProtoContract]
public class PbUpdateSharedRequest
{
    [ProtoMember(1)] public string ShareCode { get; set; } = string.Empty;
    [ProtoMember(2)] public string ContentJson { get; set; } = string.Empty;
}

[ProtoContract]
public class PbMindMapSummary
{
    [ProtoMember(1)] public string Id { get; set; } = string.Empty;
    [ProtoMember(2)] public string Title { get; set; } = string.Empty;
    [ProtoMember(3)] public long UpdatedAtUnixMs { get; set; }
    [ProtoMember(4)] public string ShareCode { get; set; } = string.Empty;
}

[ProtoContract]
public class PbMindMapListResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
    [ProtoMember(3)] public List<PbMindMapSummary> Maps { get; set; } = [];
}

[ProtoContract]
public class PbMindMapDetailResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
    [ProtoMember(3)] public string Id { get; set; } = string.Empty;
    [ProtoMember(4)] public string Title { get; set; } = string.Empty;
    [ProtoMember(5)] public string ContentJson { get; set; } = string.Empty;
    [ProtoMember(6)] public long UpdatedAtUnixMs { get; set; }
    [ProtoMember(7)] public string ShareCode { get; set; } = string.Empty;
}

[ProtoContract]
public class PbShareResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
    [ProtoMember(3)] public string ShareCode { get; set; } = string.Empty;
    [ProtoMember(4)] public string RelativeUrl { get; set; } = string.Empty;
}

[ProtoContract]
public class PbStatusResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
}
