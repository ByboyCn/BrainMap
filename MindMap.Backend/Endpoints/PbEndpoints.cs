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

        var share = group.MapGroup("/share");
        share.MapPost("/get", GetSharedAsync);
        share.MapPost("/update", UpdateSharedAsync);

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
                ShareCode = m.ShareCode ?? string.Empty
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

        map.ShareCode ??= await ShareCodeGenerator.GenerateUniqueCodeAsync(db);
        map.ShareRequireLogin = body.RequireLogin;
        map.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return PbIo.Write(new PbShareResponse
        {
            Success = true,
            ShareCode = map.ShareCode,
            RelativeUrl = $"/share/{map.ShareCode}",
            RequireLogin = map.ShareRequireLogin,
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

        if (map.ShareRequireLogin && !IsAuthenticated(request.HttpContext))
        {
            return PbIo.Write(new PbMindMapDetailResponse { Success = false, Message = "login required for this share link" });
        }

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

        if (map.ShareRequireLogin && !IsAuthenticated(request.HttpContext))
        {
            return PbIo.Write(new PbStatusResponse { Success = false, Message = "login required for this share link" });
        }

        map.ContentJson = string.IsNullOrWhiteSpace(body.ContentJson) ? map.ContentJson : body.ContentJson;
        map.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        return PbIo.Write(new PbStatusResponse { Success = true, Message = "saved" });
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
        };

    private static PbTodoDetailResponse ToTodoDetail(MindMapDocument todo) =>
        new()
        {
            Success = true,
            Id = todo.Id.ToString(),
            Title = todo.Title,
            ContentJson = todo.ContentJson,
            UpdatedAtUnixMs = new DateTimeOffset(todo.UpdatedAtUtc).ToUnixTimeMilliseconds(),
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

    private static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
