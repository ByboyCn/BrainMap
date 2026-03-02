using System.Security.Cryptography;
using MindMap.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace MindMap.Backend.Services;

public static class ShareCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static async Task<string> GenerateUniqueCodeAsync(AppDbContext dbContext, int length = 8)
    {
        while (true)
        {
            var code = GenerateCode(length);
            var exists = await dbContext.MindMaps.AnyAsync(x => x.ShareCode == code);
            if (!exists)
            {
                return code;
            }
        }
    }

    private static string GenerateCode(int length)
    {
        var chars = new char[length];
        var bytes = RandomNumberGenerator.GetBytes(length);

        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        }

        return new string(chars);
    }
}
