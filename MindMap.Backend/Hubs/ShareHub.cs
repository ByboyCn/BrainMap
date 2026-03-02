using System.Security.Claims;
using MindMap.Backend.Data;
using MindMap.Backend.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace MindMap.Backend.Hubs;

public class ShareHub(AppDbContext db, PresenceService presenceService) : Hub
{
    private readonly AppDbContext _db = db;
    private readonly PresenceService _presenceService = presenceService;

    public async Task JoinShare(string shareCode, string? displayName)
    {
        var exists = await _db.MindMaps.AnyAsync(m => m.ShareCode == shareCode);
        if (!exists)
        {
            throw new HubException("分享房间不存在。");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, shareCode);

        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? $"guest:{Context.ConnectionId}";
        var userName = string.IsNullOrWhiteSpace(displayName)
            ? Context.User?.Identity?.Name ?? "Guest"
            : displayName.Trim();

        var joinResult = _presenceService.Join(shareCode, Context.ConnectionId, userId, userName);
        var joined = ToClientUser(joinResult.Joined);
        var current = joinResult.CurrentUsers.Select(ToClientUser).ToList();

        await Clients.Caller.SendAsync("OnlineUsers", current);
        await Clients.OthersInGroup(shareCode).SendAsync("UserJoined", joined);
    }

    public async Task UpdateCursor(string shareCode, double x, double y)
    {
        var updated = _presenceService.UpdateCursor(shareCode, Context.ConnectionId, x, y);
        if (updated is null)
        {
            return;
        }

        await Clients.OthersInGroup(shareCode).SendAsync("CursorMoved", ToClientUser(updated));
    }

    public async Task UpdateSharedContent(string shareCode, string contentJson)
    {
        if (string.IsNullOrWhiteSpace(shareCode) || string.IsNullOrWhiteSpace(contentJson))
        {
            return;
        }

        await Clients.OthersInGroup(shareCode).SendAsync("ContentUpdated", new
        {
            contentJson,
            connectionId = Context.ConnectionId
        });
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var left = _presenceService.Remove(Context.ConnectionId);
        if (left is not null)
        {
            await Clients.Group(left.RoomCode).SendAsync("UserLeft", left.Left.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private static object ToClientUser(OnlineParticipant user) => new
    {
        connectionId = user.ConnectionId,
        userKey = user.UserKey,
        displayName = user.DisplayName,
        x = user.X,
        y = user.Y
    };
}
