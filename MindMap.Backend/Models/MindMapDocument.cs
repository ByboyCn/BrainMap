using System.ComponentModel.DataAnnotations;

namespace MindMap.Backend.Models;

public class MindMapDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(120)]
    public string Title { get; set; } = "未命名脑图";

    public string ContentJson { get; set; } = "{\"nodes\":[]}";

    public Guid OwnerId { get; set; }
    public User? Owner { get; set; }

    [MaxLength(24)]
    public string? ShareCode { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
