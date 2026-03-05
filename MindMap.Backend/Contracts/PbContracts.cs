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
public class PbCreateShareRequest
{
    [ProtoMember(1)] public string MapId { get; set; } = string.Empty;
    [ProtoMember(2)] public bool RequireLogin { get; set; }
}

[ProtoContract]
public class PbUpdateSharedRequest
{
    [ProtoMember(1)] public string ShareCode { get; set; } = string.Empty;
    [ProtoMember(2)] public string ContentJson { get; set; } = string.Empty;
}

[ProtoContract]
public class PbShareHistoryListRequest
{
    [ProtoMember(1)] public string ShareCode { get; set; } = string.Empty;
    [ProtoMember(2)] public int Limit { get; set; } = 40;
}

[ProtoContract]
public class PbShareHistoryAddRequest
{
    [ProtoMember(1)] public string ShareCode { get; set; } = string.Empty;
    [ProtoMember(2)] public string ActionType { get; set; } = string.Empty;
    [ProtoMember(3)] public string DetailJson { get; set; } = string.Empty;
    [ProtoMember(4)] public string ActorDisplayName { get; set; } = string.Empty;
}

[ProtoContract]
public class PbTodoIdRequest
{
    [ProtoMember(1)] public string TodoId { get; set; } = string.Empty;
}

[ProtoContract]
public class PbCreateTodoRequest
{
    [ProtoMember(1)] public string Title { get; set; } = string.Empty;
    [ProtoMember(2)] public string ContentJson { get; set; } = string.Empty;
}

[ProtoContract]
public class PbUpdateTodoRequest
{
    [ProtoMember(1)] public string TodoId { get; set; } = string.Empty;
    [ProtoMember(2)] public string Title { get; set; } = string.Empty;
    [ProtoMember(3)] public string ContentJson { get; set; } = string.Empty;
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
    [ProtoMember(8)] public bool ShareRequireLogin { get; set; }
}

[ProtoContract]
public class PbShareResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
    [ProtoMember(3)] public string ShareCode { get; set; } = string.Empty;
    [ProtoMember(4)] public string RelativeUrl { get; set; } = string.Empty;
    [ProtoMember(5)] public bool RequireLogin { get; set; }
}

[ProtoContract]
public class PbStatusResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
}

[ProtoContract]
public class PbShareHistoryItem
{
    [ProtoMember(1)] public long Id { get; set; }
    [ProtoMember(2)] public string ShareCode { get; set; } = string.Empty;
    [ProtoMember(3)] public string ActionType { get; set; } = string.Empty;
    [ProtoMember(4)] public string ActorDisplayName { get; set; } = string.Empty;
    [ProtoMember(5)] public string DetailJson { get; set; } = string.Empty;
    [ProtoMember(6)] public long CreatedAtUnixMs { get; set; }
}

[ProtoContract]
public class PbShareHistoryListResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
    [ProtoMember(3)] public List<PbShareHistoryItem> Items { get; set; } = [];
}

[ProtoContract]
public class PbTodoSummary
{
    [ProtoMember(1)] public string Id { get; set; } = string.Empty;
    [ProtoMember(2)] public string Title { get; set; } = string.Empty;
    [ProtoMember(3)] public long UpdatedAtUnixMs { get; set; }
}

[ProtoContract]
public class PbTodoListResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
    [ProtoMember(3)] public List<PbTodoSummary> Todos { get; set; } = [];
}

[ProtoContract]
public class PbTodoDetailResponse
{
    [ProtoMember(1)] public bool Success { get; set; }
    [ProtoMember(2)] public string Message { get; set; } = string.Empty;
    [ProtoMember(3)] public string Id { get; set; } = string.Empty;
    [ProtoMember(4)] public string Title { get; set; } = string.Empty;
    [ProtoMember(5)] public string ContentJson { get; set; } = string.Empty;
    [ProtoMember(6)] public long UpdatedAtUnixMs { get; set; }
}
