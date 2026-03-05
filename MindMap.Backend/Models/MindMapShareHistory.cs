using System.ComponentModel.DataAnnotations;

namespace MindMap.Backend.Models;

public class MindMapShareHistory
{
    public long Id { get; set; }

    public Guid MindMapId { get; set; }
    public MindMapDocument? MindMap { get; set; }

    [MaxLength(24)]
    public string ShareCode { get; set; } = string.Empty;

    [MaxLength(48)]
    public string ActionType { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ActorId { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ActorDisplayName { get; set; } = string.Empty;

    public string DetailJson { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ClientIp { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
