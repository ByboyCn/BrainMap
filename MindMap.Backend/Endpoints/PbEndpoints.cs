using System.Security.Claims;
using System.Text.Json;
using MindMap.Backend.Contracts;
using MindMap.Backend.Data;
using MindMap.Backend.Models;
using MindMap.Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace MindMap.Backend.Endpoints;

public static class PbEndpoints
{
    public static IEndpointRouteBuilder MapPbEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/pb");

        group.MapPost("/auth/register", RegisterAsync);
        group.MapPost("/auth/login", LoginAsync);

        var maps = group.MapGroup("/mindmaps").RequireAuthorization();
        maps.MapPost("/list", ListMapsAsync);
        maps.MapPost("/create", CreateMapAsync);
        maps.MapPost("/get", GetMapAsync);
        maps.MapPost("/update", UpdateMapAsync);
        maps.MapPost("/delete", DeleteMapAsync);
        maps.MapPost("/share", CreateShareAsync);

        var todos = group.MapGroup("/todos").RequireAuthorization();
        todos.MapPost("/list", ListTodosAsync);
        todos.MapPost("/create", CreateTodoAsync);
        todos.MapPost("/get", GetTodoAsync);
        todos.MapPost("/update", UpdateTodoAsync);
        todos.MapPost("/delete", DeleteTodoAsync);
        todos.MapPost("/share", CreateTodoShareAsync);

        var share = group.MapGroup("/share");
        share.MapPost("/get", GetSharedAsync);
        share.MapPost("/update", UpdateSharedAsync);
        share.MapPost("/history/list", GetShareHistoryAsync);
        share.MapPost("/history/add", AddShareHistoryAsync);

        return app;
    }

    private static async Task<IResult> RegisterAsync(HttpRequest request, AppDbContext db, PasswordService passwordService, JwtTokenService jwt)
    {
        var body = await PbIo.ReadAsync<PbAuthRequest>(request);
        if (string.IsNullOrWhiteSpace(body.UserName) || string.IsNullOrWhiteSpace(body.Password))
        {
            return PbIo.Write(new PbAuthResponse { Success = false, Message = "username and password are required" });
        }

        var normalized = body.UserName.Trim().ToLowerInvariant();
        var exists = await db.Users.AnyAsync(x => x.NormalizedUserName == normalized);
        if (exists)
        {
            return PbIo.Write(new PbAuthResponse { Success = false, Message = "username already exists" });
        }

        var user = new User
        {
            UserName = body.UserName.Trim(),
            NormalizedUserName = normalized
        };
        var (hash, salt) = passwordService.HashPassword(body.Password);
        user.PasswordHash = hash;
        user.PasswordSalt = salt;
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return PbIo.Write(new PbAuthResponse
        {
            Success = true,
            UserId = user.Id.ToString(),
            UserName = user.UserName,
            Token = jwt.GenerateToken(user)
        });
    }

    private static async Task<IResult> LoginAsync(HttpRequest request, AppDbContext db, PasswordService passwordService, JwtTokenService jwt)
    {
        var body = await PbIo.ReadAsync<PbAuthRequest>(request);
        var normalized = body.UserName?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(body.Password))
        {
            return PbIo.Write(new PbAuthResponse { Success = false, Message = "username and password are required" });
        }

        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedUserName == normalized);
        if (user is null || !passwordService.VerifyPassword(body.Password, user.PasswordHash, user.PasswordSalt))
        {
            return PbIo.Write(new PbAuthResponse { Success = false, Message = "invalid username or password" });
        }

        return PbIo.Write(new PbAuthResponse
        {
            Success = true,
            UserId = user.Id.ToString(),
            UserName = user.UserName,
            Token = jwt.GenerateToken(user)
        });
    }

    private static async Task<IResult> ListMapsAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = principal.GetUserId();

        var docs = await db.MindMaps
            .Where(m => m.OwnerId == userId)
            .OrderByDescending(m => m.UpdatedAtUtc)
            .ToListAsync();

        var maps = docs
            .Where(m => !IsTodoContent(m.ContentJson))
            .Select(m => new PbMindMapSummary
            {
                Id = m.Id.ToString(),
                Title = m.Title,
                UpdatedAtUnixMs = new DateTimeOffset(m.UpdatedAtUtc).ToUnixTimeMilliseconds(),
                ShareCode = m.ShareCode ?? string.Empty,
                ShareEnabled = m.ShareEnabled,
                ShareRequireLogin = m.ShareRequireLogin,
                ShareAllowGuestEdit = m.ShareAllowGuestEdit,
            })
            .ToList();

        return PbIo.Write(new PbMindMapListResponse
        {
            Success = true,
            Maps = maps
        });
    }

    private static async Task<IResult> CreateMapAsync(ClaimsPrincipal principal, HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbCreateMindMapRequest>(request);
        var entity = new MindMapDocument
        {
            OwnerId = principal.GetUserId(),
            Title = string.IsNullOrWhiteSpace(body.Title) ? "Untitled MindMap" : body.Title.Trim(),
            ContentJson = string.IsNullOrWhiteSpace(body.ContentJson)
                ? "{\"docType\":\"mindmap\",\"nodes\":[],\"edges\":[],\"meta\":{\"backgroundColor\":\"#ffffff\"}}"
                : body.ContentJson
        };

        db.MindMaps.Add(entity);
        await db.SaveChangesAsync();

        return PbIo.Write(ToDetail(entity));
    }

    private static async Task<IResult> GetMapAsync(ClaimsPrincipal principal, HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbMindMapIdRequest>(request);
        if (!Guid.TryParse(body.MapId, out var mapId))
        {
            return PbIo.Write(new PbMindMapDetailResponse { Success = false, Message = "invalid map id" });
        }

        var userId = principal.GetUserId();
        var map = await db.MindMaps.SingleOrDefaultAsync(x => x.Id == mapId && x.OwnerId == userId);
        if (map is null || IsTodoContent(map.ContentJson))
        {
            return PbIo.Write(new PbMindMapDetailResponse { Success = false, Message = "mindmap not found" });
        }

        return PbIo.Write(ToDetail(map));
    }

    private static async Task<IResult> UpdateMapAsync(ClaimsPrincipal principal, HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbUpdateMindMapRequest>(request);
        if (!Guid.TryParse(body.MapId, out var mapId))
        {
            return PbIo.Write(new PbMindMapDetailResponse { Success = false, Message = "invalid map id" });
        }

        var userId = principal.GetUserId();
        var map = await db.MindMaps.SingleOrDefaultAsync(x => x.Id == mapId && x.OwnerId == userId);
        if (map is null || IsTodoContent(map.ContentJson))
        {
            return PbIo.Write(new PbMindMapDetailResponse { Success = false, Message = "mindmap not found" });
        }

        if (!string.IsNullOrWhiteSpace(body.Title))
        {
            map.Title = body.Title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(body.ContentJson))
        {
            map.ContentJson = body.ContentJson;
        }

        map.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return PbIo.Write(ToDetail(map));
    }

    private static async Task<IResult> DeleteMapAsync(ClaimsPrincipal principal, HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbMindMapIdRequest>(request);
        if (!Guid.TryParse(body.MapId, out var mapId))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "invalid map id" });
        }

        var userId = principal.GetUserId();
        var map = await db.MindMaps.SingleOrDefaultAsync(x => x.Id == mapId && x.OwnerId == userId);
        if (map is null || IsTodoContent(map.ContentJson))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "mindmap not found" });
        }

        db.MindMaps.Remove(map);
        await db.SaveChangesAsync();

        return PbIo.Write(new PbStatusResponse { Success = true, Message = "deleted" });
    }

    private static async Task<IResult> CreateShareAsync(ClaimsPrincipal principal, HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbCreateShareRequest>(request);
        if (!Guid.TryParse(body.MapId, out var mapId))
        {
            return PbIo.Write(new PbShareResponse { Success = false, Message = "invalid map id" });
        }

        var userId = principal.GetUserId();
        var map = await db.MindMaps.SingleOrDefaultAsync(x => x.Id == mapId && x.OwnerId == userId);
        if (map is null || IsTodoContent(map.ContentJson))
        {
            return PbIo.Write(new PbShareResponse { Success = false, Message = "mindmap not found" });
        }

        if (body.Enabled && string.IsNullOrWhiteSpace(map.ShareCode))
        {
            map.ShareCode = await ShareCodeGenerator.GenerateUniqueCodeAsync(db);
        }
        map.ShareEnabled = body.Enabled;
        map.ShareRequireLogin = body.RequireLogin;
        map.ShareAllowGuestEdit = body.GuestCanEdit;
        map.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return PbIo.Write(new PbShareResponse
        {
            Success = true,
            ShareCode = map.ShareCode ?? string.Empty,
            RelativeUrl = map.ShareEnabled && !string.IsNullOrWhiteSpace(map.ShareCode) ? $"/share/{map.ShareCode}" : string.Empty,
            RequireLogin = map.ShareRequireLogin,
            Enabled = map.ShareEnabled,
            GuestCanEdit = map.ShareAllowGuestEdit,
        });
    }

    private static async Task<IResult> CreateTodoShareAsync(ClaimsPrincipal principal, HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbCreateTodoShareRequest>(request);
        if (!Guid.TryParse(body.TodoId, out var todoId))
        {
            return PbIo.Write(new PbShareResponse { Success = false, Message = "invalid todo id" });
        }

        var userId = principal.GetUserId();
        var todo = await db.MindMaps.SingleOrDefaultAsync(x => x.Id == todoId && x.OwnerId == userId);
        if (todo is null || !IsTodoContent(todo.ContentJson))
        {
            return PbIo.Write(new PbShareResponse { Success = false, Message = "todo not found" });
        }

        if (body.Enabled && string.IsNullOrWhiteSpace(todo.ShareCode))
        {
            todo.ShareCode = await ShareCodeGenerator.GenerateUniqueCodeAsync(db);
        }
        todo.ShareEnabled = body.Enabled;
        todo.ShareRequireLogin = body.RequireLogin;
        todo.ShareAllowGuestEdit = body.GuestCanEdit;
        todo.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return PbIo.Write(new PbShareResponse
        {
            Success = true,
            ShareCode = todo.ShareCode ?? string.Empty,
            RelativeUrl = todo.ShareEnabled && !string.IsNullOrWhiteSpace(todo.ShareCode) ? $"/share/{todo.ShareCode}" : string.Empty,
            RequireLogin = todo.ShareRequireLogin,
            Enabled = todo.ShareEnabled,
            GuestCanEdit = todo.ShareAllowGuestEdit,
        });
    }

    private static async Task<IResult> GetSharedAsync(HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbShareCodeRequest>(request);
        if (string.IsNullOrWhiteSpace(body.ShareCode))
        {
            return PbIo.Write(new PbMindMapDetailResponse { Success = false, Message = "share code is required" });
        }

        var map = await db.MindMaps.SingleOrDefaultAsync(x => x.ShareCode == body.ShareCode);
        if (map is null || IsTodoContent(map.ContentJson))
        {
            return PbIo.Write(new PbMindMapDetailResponse { Success = false, Message = "share link not found" });
        }

        if (!map.ShareEnabled)
        {
            return PbIo.Write(new PbMindMapDetailResponse { Success = false, Message = "share is disabled" });
        }

        if (map.ShareRequireLogin && !IsAuthenticated(request.HttpContext))
        {
            return PbIo.Write(new PbMindMapDetailResponse { Success = false, Message = "login required for this share link" });
        }

        await LogShareHistoryAsync(request.HttpContext, db, map, "open", "{\"source\":\"share\"}");

        return PbIo.Write(ToDetail(map));
    }

    private static async Task<IResult> UpdateSharedAsync(HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbUpdateSharedRequest>(request);
        if (string.IsNullOrWhiteSpace(body.ShareCode))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "share code is required" });
        }

        var map = await db.MindMaps.SingleOrDefaultAsync(x => x.ShareCode == body.ShareCode);
        if (map is null || IsTodoContent(map.ContentJson))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "share link not found" });
        }

        if (!map.ShareEnabled)
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "share is disabled" });
        }

        if (!CanEditByShare(request.HttpContext, map))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "no permission to edit shared content" });
        }

        map.ContentJson = string.IsNullOrWhiteSpace(body.ContentJson) ? map.ContentJson : body.ContentJson;
        map.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return PbIo.Write(new PbStatusResponse { Success = true, Message = "saved" });
    }

    private static async Task<IResult> GetShareHistoryAsync(HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbShareHistoryListRequest>(request);
        var shareCode = body.ShareCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(shareCode))
        {
            return PbIo.Write(new PbShareHistoryListResponse { Success = false, Message = "share code is required" });
        }

        var map = await db.MindMaps.SingleOrDefaultAsync(x => x.ShareCode == shareCode);
        if (map is null || IsTodoContent(map.ContentJson))
        {
            return PbIo.Write(new PbShareHistoryListResponse { Success = false, Message = "share link not found" });
        }

        if (!map.ShareEnabled)
        {
            return PbIo.Write(new PbShareHistoryListResponse { Success = false, Message = "share is disabled" });
        }

        if (map.ShareRequireLogin && !IsAuthenticated(request.HttpContext))
        {
            return PbIo.Write(new PbShareHistoryListResponse { Success = false, Message = "login required for this share link" });
        }

        var limit = Math.Clamp(body.Limit <= 0 ? 40 : body.Limit, 1, 200);
        var items = await db.MindMapShareHistories
            .Where(x => x.ShareCode == shareCode)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(limit)
            .Select(x => new PbShareHistoryItem
            {
                Id = x.Id,
                ShareCode = x.ShareCode,
                ActionType = x.ActionType,
                ActorDisplayName = x.ActorDisplayName,
                DetailJson = x.DetailJson,
                CreatedAtUnixMs = new DateTimeOffset(x.CreatedAtUtc).ToUnixTimeMilliseconds(),
            })
            .ToListAsync();

        return PbIo.Write(new PbShareHistoryListResponse
        {
            Success = true,
            Items = items,
        });
    }

    private static async Task<IResult> AddShareHistoryAsync(HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbShareHistoryAddRequest>(request);
        var shareCode = body.ShareCode?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(shareCode))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "share code is required" });
        }

        var map = await db.MindMaps.SingleOrDefaultAsync(x => x.ShareCode == shareCode);
        if (map is null || IsTodoContent(map.ContentJson))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "share link not found" });
        }

        if (!map.ShareEnabled)
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "share is disabled" });
        }

        if (!CanEditByShare(request.HttpContext, map))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "no permission to edit shared content" });
        }

        var actionType = NormalizeActionType(body.ActionType);
        var detailJson = NormalizeDetailJson(body.DetailJson);
        await LogShareHistoryAsync(request.HttpContext, db, map, actionType, detailJson, body.ActorDisplayName);

        return PbIo.Write(new PbStatusResponse { Success = true, Message = "history saved" });
    }

    private static async Task<IResult> ListTodosAsync(ClaimsPrincipal principal, AppDbContext db)
    {
        var userId = principal.GetUserId();
        var docs = await db.MindMaps
            .Where(m => m.OwnerId == userId)
            .OrderByDescending(m => m.UpdatedAtUtc)
            .ToListAsync();

        var todos = docs
            .Where(m => IsTodoContent(m.ContentJson))
            .Select(m => new PbTodoSummary
            {
                Id = m.Id.ToString(),
                Title = m.Title,
                UpdatedAtUnixMs = new DateTimeOffset(m.UpdatedAtUtc).ToUnixTimeMilliseconds(),
                ShareCode = m.ShareCode ?? string.Empty,
                ShareEnabled = m.ShareEnabled,
                ShareRequireLogin = m.ShareRequireLogin,
                ShareAllowGuestEdit = m.ShareAllowGuestEdit,
            })
            .ToList();

        return PbIo.Write(new PbTodoListResponse
        {
            Success = true,
            Todos = todos,
        });
    }

    private static async Task<IResult> CreateTodoAsync(ClaimsPrincipal principal, HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbCreateTodoRequest>(request);
        var entity = new MindMapDocument
        {
            OwnerId = principal.GetUserId(),
            Title = string.IsNullOrWhiteSpace(body.Title) ? "Untitled Todo" : body.Title.Trim(),
            ContentJson = string.IsNullOrWhiteSpace(body.ContentJson)
                ? "{\"docType\":\"todo\",\"sortBy\":\"natural\",\"items\":[]}"
                : body.ContentJson
        };

        db.MindMaps.Add(entity);
        await db.SaveChangesAsync();

        return PbIo.Write(ToTodoDetail(entity));
    }

    private static async Task<IResult> GetTodoAsync(ClaimsPrincipal principal, HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbTodoIdRequest>(request);
        if (!Guid.TryParse(body.TodoId, out var todoId))
        {
            return PbIo.Write(new PbTodoDetailResponse { Success = false, Message = "invalid todo id" });
        }

        var userId = principal.GetUserId();
        var todo = await db.MindMaps.SingleOrDefaultAsync(x => x.Id == todoId && x.OwnerId == userId);
        if (todo is null || !IsTodoContent(todo.ContentJson))
        {
            return PbIo.Write(new PbTodoDetailResponse { Success = false, Message = "todo not found" });
        }

        return PbIo.Write(ToTodoDetail(todo));
    }

    private static async Task<IResult> UpdateTodoAsync(ClaimsPrincipal principal, HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbUpdateTodoRequest>(request);
        if (!Guid.TryParse(body.TodoId, out var todoId))
        {
            return PbIo.Write(new PbTodoDetailResponse { Success = false, Message = "invalid todo id" });
        }

        var userId = principal.GetUserId();
        var todo = await db.MindMaps.SingleOrDefaultAsync(x => x.Id == todoId && x.OwnerId == userId);
        if (todo is null || !IsTodoContent(todo.ContentJson))
        {
            return PbIo.Write(new PbTodoDetailResponse { Success = false, Message = "todo not found" });
        }

        if (!string.IsNullOrWhiteSpace(body.Title))
        {
            todo.Title = body.Title.Trim();
        }

        if (!string.IsNullOrWhiteSpace(body.ContentJson))
        {
            todo.ContentJson = body.ContentJson;
        }

        todo.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return PbIo.Write(ToTodoDetail(todo));
    }

    private static async Task<IResult> DeleteTodoAsync(ClaimsPrincipal principal, HttpRequest request, AppDbContext db)
    {
        var body = await PbIo.ReadAsync<PbTodoIdRequest>(request);
        if (!Guid.TryParse(body.TodoId, out var todoId))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "invalid todo id" });
        }

        var userId = principal.GetUserId();
        var todo = await db.MindMaps.SingleOrDefaultAsync(x => x.Id == todoId && x.OwnerId == userId);
        if (todo is null || !IsTodoContent(todo.ContentJson))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "todo not found" });
        }

        db.MindMaps.Remove(todo);
        await db.SaveChangesAsync();

        return PbIo.Write(new PbStatusResponse { Success = true, Message = "deleted" });
    }

    private static PbMindMapDetailResponse ToDetail(MindMapDocument map) =>
        new()
        {
            Success = true,
            Id = map.Id.ToString(),
            Title = map.Title,
            ContentJson = map.ContentJson,
            UpdatedAtUnixMs = new DateTimeOffset(map.UpdatedAtUtc).ToUnixTimeMilliseconds(),
            ShareCode = map.ShareCode ?? string.Empty,
            ShareRequireLogin = map.ShareRequireLogin,
            ShareEnabled = map.ShareEnabled,
            ShareAllowGuestEdit = map.ShareAllowGuestEdit,
        };

    private static PbTodoDetailResponse ToTodoDetail(MindMapDocument todo) =>
        new()
        {
            Success = true,
            Id = todo.Id.ToString(),
            Title = todo.Title,
            ContentJson = todo.ContentJson,
            UpdatedAtUnixMs = new DateTimeOffset(todo.UpdatedAtUtc).ToUnixTimeMilliseconds(),
            ShareCode = todo.ShareCode ?? string.Empty,
            ShareEnabled = todo.ShareEnabled,
            ShareRequireLogin = todo.ShareRequireLogin,
            ShareAllowGuestEdit = todo.ShareAllowGuestEdit,
        };

    private static bool IsTodoContent(string contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(contentJson);
            if (!doc.RootElement.TryGetProperty("docType", out var typeElement))
            {
                return false;
            }

            return string.Equals(typeElement.GetString(), "todo", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAuthenticated(HttpContext context)
    {
        return context.User?.Identity?.IsAuthenticated == true;
    }

    private static bool CanEditByShare(HttpContext context, MindMapDocument map)
    {
        if (!map.ShareEnabled)
        {
            return false;
        }

        var isAuthenticated = IsAuthenticated(context);
        if (map.ShareRequireLogin && !isAuthenticated)
        {
            return false;
        }

        if (!isAuthenticated && !map.ShareAllowGuestEdit)
        {
            return false;
        }

        return true;
    }

    private static async Task LogShareHistoryAsync(
        HttpContext context,
        AppDbContext db,
        MindMapDocument map,
        string actionType,
        string detailJson,
        string? actorDisplayNameOverride = null)
    {
        var actorId = context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var actorName = string.IsNullOrWhiteSpace(actorDisplayNameOverride)
            ? context.User?.Identity?.Name ?? "Guest"
            : actorDisplayNameOverride.Trim();
        var clientIp = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        db.MindMapShareHistories.Add(new MindMapShareHistory
        {
            MindMapId = map.Id,
            ShareCode = map.ShareCode ?? string.Empty,
            ActionType = NormalizeActionType(actionType),
            ActorId = actorId.Length > 64 ? actorId[..64] : actorId,
            ActorDisplayName = actorName.Length > 64 ? actorName[..64] : actorName,
            DetailJson = NormalizeDetailJson(detailJson),
            ClientIp = clientIp.Length > 64 ? clientIp[..64] : clientIp,
            CreatedAtUtc = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    private static string NormalizeActionType(string? actionType)
    {
        var normalized = string.IsNullOrWhiteSpace(actionType)
            ? "update"
            : actionType.Trim().ToLowerInvariant();
        return normalized.Length > 48 ? normalized[..48] : normalized;
    }

    private static string NormalizeDetailJson(string? detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson))
        {
            return "{}";
        }

        var raw = detailJson.Trim();
        if (raw.Length > 4096)
        {
            raw = raw[..4096];
        }

        try
        {
            using var _ = JsonDocument.Parse(raw);
            return raw;
        }
        catch
        {
            var text = raw.Length > 512 ? raw[..512] : raw;
            return JsonSerializer.Serialize(new { text });
        }
    }

    private static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
