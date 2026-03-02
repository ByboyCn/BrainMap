using System.Security.Claims;
using MindMap.Backend.Data;
using MindMap.Backend.Endpoints;
using MindMap.Backend.Hubs;
using MindMap.Backend.Models;
using MindMap.Backend.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSignalR();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        JwtTokenService.ConfigureJwtBearer(options, builder.Configuration);
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrWhiteSpace(accessToken) && path.StartsWithSegments("/hubs/share"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            // Testing mode: allow LAN/IP origins.
            policy.SetIsOriginAllowed(_ => true)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
            return;
        }

        policy.SetIsOriginAllowed(origin =>
                Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
                uri.Scheme == "http" &&
                (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("frontend");
app.UseAuthentication();
app.UseAuthorization();

var auth = app.MapGroup("/api/auth");
auth.MapPost("/register", async (RegisterRequest request, AppDbContext db, PasswordService passwordService, JwtTokenService jwt) =>
{
    if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "用户名和密码不能为空。" });
    }

    var normalized = request.UserName.Trim().ToLowerInvariant();
    var exists = await db.Users.AnyAsync(u => u.NormalizedUserName == normalized);
    if (exists)
    {
        return Results.BadRequest(new { message = "用户名已存在。" });
    }

    var user = new User
    {
        UserName = request.UserName.Trim(),
        NormalizedUserName = normalized
    };

    var (hash, salt) = passwordService.HashPassword(request.Password);
    user.PasswordHash = hash;
    user.PasswordSalt = salt;

    db.Users.Add(user);
    await db.SaveChangesAsync();

    var token = jwt.GenerateToken(user);
    return Results.Ok(new AuthResponse(user.Id, user.UserName, token));
});

auth.MapPost("/login", async (LoginRequest request, AppDbContext db, PasswordService passwordService, JwtTokenService jwt) =>
{
    var normalized = request.UserName?.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { message = "用户名和密码不能为空。" });
    }

    var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedUserName == normalized);
    if (user is null || !passwordService.VerifyPassword(request.Password, user.PasswordHash, user.PasswordSalt))
    {
        return Results.BadRequest(new { message = "用户名或密码错误。" });
    }

    var token = jwt.GenerateToken(user);
    return Results.Ok(new AuthResponse(user.Id, user.UserName, token));
});

var maps = app.MapGroup("/api/mindmaps").RequireAuthorization();

maps.MapGet("/", async (ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = principal.GetUserId();
    var list = await db.MindMaps
        .Where(m => m.OwnerId == userId)
        .OrderByDescending(m => m.UpdatedAtUtc)
        .Select(m => new MindMapSummaryResponse(m.Id, m.Title, m.UpdatedAtUtc, m.ShareCode))
        .ToListAsync();

    return Results.Ok(list);
});

maps.MapPost("/", async (ClaimsPrincipal principal, CreateMindMapRequest request, AppDbContext db) =>
{
    var title = string.IsNullOrWhiteSpace(request.Title) ? "未命名脑图" : request.Title.Trim();
    var entity = new MindMapDocument
    {
        OwnerId = principal.GetUserId(),
        Title = title,
        ContentJson = string.IsNullOrWhiteSpace(request.ContentJson) ? "{\"nodes\":[]}" : request.ContentJson
    };

    db.MindMaps.Add(entity);
    await db.SaveChangesAsync();

    return Results.Ok(new MindMapDetailResponse(entity.Id, entity.Title, entity.ContentJson, entity.UpdatedAtUtc, entity.ShareCode));
});

maps.MapGet("/{id:guid}", async (ClaimsPrincipal principal, Guid id, AppDbContext db) =>
{
    var userId = principal.GetUserId();
    var map = await db.MindMaps.SingleOrDefaultAsync(m => m.Id == id && m.OwnerId == userId);
    return map is null
        ? Results.NotFound(new { message = "脑图不存在。" })
        : Results.Ok(new MindMapDetailResponse(map.Id, map.Title, map.ContentJson, map.UpdatedAtUtc, map.ShareCode));
});

maps.MapPut("/{id:guid}", async (ClaimsPrincipal principal, Guid id, UpdateMindMapRequest request, AppDbContext db) =>
{
    var userId = principal.GetUserId();
    var map = await db.MindMaps.SingleOrDefaultAsync(m => m.Id == id && m.OwnerId == userId);
    if (map is null)
    {
        return Results.NotFound(new { message = "脑图不存在。" });
    }

    map.Title = string.IsNullOrWhiteSpace(request.Title) ? map.Title : request.Title.Trim();
    map.ContentJson = string.IsNullOrWhiteSpace(request.ContentJson) ? map.ContentJson : request.ContentJson;
    map.UpdatedAtUtc = DateTime.UtcNow;

    await db.SaveChangesAsync();
    return Results.Ok(new MindMapDetailResponse(map.Id, map.Title, map.ContentJson, map.UpdatedAtUtc, map.ShareCode));
});

maps.MapPost("/{id:guid}/share", async (ClaimsPrincipal principal, Guid id, AppDbContext db) =>
{
    var userId = principal.GetUserId();
    var map = await db.MindMaps.SingleOrDefaultAsync(m => m.Id == id && m.OwnerId == userId);
    if (map is null)
    {
        return Results.NotFound(new { message = "脑图不存在。" });
    }

    map.ShareCode ??= await ShareCodeGenerator.GenerateUniqueCodeAsync(db);
    map.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new ShareResponse(map.ShareCode, $"/share/{map.ShareCode}"));
});

var share = app.MapGroup("/api/share");

share.MapGet("/{shareCode}", async (string shareCode, AppDbContext db) =>
{
    var map = await db.MindMaps.SingleOrDefaultAsync(m => m.ShareCode == shareCode);
    if (map is null)
    {
        return Results.NotFound(new { message = "分享链接不存在。" });
    }

    return Results.Ok(new SharedMindMapResponse(map.Id, map.Title, map.ContentJson, map.ShareCode!, map.UpdatedAtUtc));
});

share.MapPut("/{shareCode}", async (string shareCode, UpdateSharedMindMapRequest request, AppDbContext db) =>
{
    var map = await db.MindMaps.SingleOrDefaultAsync(m => m.ShareCode == shareCode);
    if (map is null)
    {
        return Results.NotFound(new { message = "分享链接不存在。" });
    }

    map.ContentJson = request.ContentJson;
    map.UpdatedAtUtc = DateTime.UtcNow;
    await db.SaveChangesAsync();

    return Results.Ok(new { message = "已保存" });
});

app.MapPbEndpoints();
app.MapHub<ShareHub>("/hubs/share");

app.Run();

record RegisterRequest(string UserName, string Password);
record LoginRequest(string UserName, string Password);
record AuthResponse(Guid UserId, string UserName, string Token);
record CreateMindMapRequest(string Title, string? ContentJson);
record UpdateMindMapRequest(string? Title, string? ContentJson);
record UpdateSharedMindMapRequest(string ContentJson);
record MindMapSummaryResponse(Guid Id, string Title, DateTime UpdatedAtUtc, string? ShareCode);
record MindMapDetailResponse(Guid Id, string Title, string ContentJson, DateTime UpdatedAtUtc, string? ShareCode);
record SharedMindMapResponse(Guid Id, string Title, string ContentJson, string ShareCode, DateTime UpdatedAtUtc);
record ShareResponse(string ShareCode, string RelativeUrl);

static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}
