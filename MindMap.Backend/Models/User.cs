using System.ComponentModel.DataAnnotations;

namespace MindMap.Backend.Models;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [MaxLength(64)]
    public string UserName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string NormalizedUserName { get; set; } = string.Empty;

    public byte[] PasswordHash { get; set; } = [];
    public byte[] PasswordSalt { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<MindMapDocument> MindMaps { get; set; } = [];
}
