using System.Collections.Concurrent;

namespace MindMap.Backend.Services;

public class PresenceService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, OnlineParticipant>> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _connectionRooms = new();

    public JoinResult Join(string roomCode, string connectionId, string userKey, string displayName)
    {
        var participants = _rooms.GetOrAdd(roomCode, _ => new ConcurrentDictionary<string, OnlineParticipant>());
        var joined = new OnlineParticipant(connectionId, userKey, displayName, 0, 0);
        participants[connectionId] = joined;
        _connectionRooms[connectionId] = roomCode;
        return new JoinResult(joined, participants.Values.ToList());
    }

    public OnlineParticipant? UpdateCursor(string roomCode, string connectionId, double x, double y)
    {
        if (!_rooms.TryGetValue(roomCode, out var participants))
        {
            return null;
        }

        if (!participants.TryGetValue(connectionId, out var existing))
        {
            return null;
        }

        var updated = existing with { X = x, Y = y };
        participants[connectionId] = updated;
        return updated;
    }

    public LeaveResult? Remove(string connectionId)
    {
        if (!_connectionRooms.TryRemove(connectionId, out var roomCode))
        {
            return null;
        }

        if (!_rooms.TryGetValue(roomCode, out var participants))
        {
            return null;
        }

        if (!participants.TryRemove(connectionId, out var removed))
        {
            return null;
        }

        if (participants.IsEmpty)
        {
            _rooms.TryRemove(roomCode, out _);
        }

        return new LeaveResult(roomCode, removed);
    }
}

public record OnlineParticipant(string ConnectionId, string UserKey, string DisplayName, double X, double Y);
public record JoinResult(OnlineParticipant Joined, IReadOnlyCollection<OnlineParticipant> CurrentUsers);
public record LeaveResult(string RoomCode, OnlineParticipant Left);
