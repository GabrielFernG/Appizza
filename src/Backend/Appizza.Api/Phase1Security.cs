using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Appizza.Modules.Devices;
using Appizza.Modules.Establishments;
using Appizza.Modules.Identity;
using Appizza.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Appizza.Api;

#pragma warning disable CA1305 // Token numeric claims use digit-only invariant representations.

public sealed class Phase1SecurityOptions
{
    public const string SectionName = "Phase1Security";
    public string Issuer { get; init; } = "Appizza";
    public string Audience { get; init; } = "Appizza";
    public string SigningKey { get; init; } = null!;
    public string CpfEncryptionKey { get; init; } = null!;
    public string CpfHmacKey { get; init; } = null!;
    public int AccessTokenMinutes { get; init; } = 15;
    public int DeviceAccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 14;
    public int ConfigurationTokenMinutes { get; init; } = 30;

    public void Validate()
    {
        if (Encoding.UTF8.GetByteCount(SigningKey ?? "") < 32) throw new InvalidOperationException("Phase1Security:SigningKey must contain at least 32 bytes.");
        if (Convert.FromBase64String(CpfEncryptionKey).Length != 32) throw new InvalidOperationException("Phase1Security:CpfEncryptionKey must be a Base64 AES-256 key.");
        if (Convert.FromBase64String(CpfHmacKey).Length < 32) throw new InvalidOperationException("Phase1Security:CpfHmacKey must contain at least 32 bytes.");
    }
}

public sealed record IssuedToken(string AccessToken, int ExpiresInSeconds);

public sealed class Phase1TokenService(Phase1SecurityOptions options)
{
    private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes(options.SigningKey));

    public IssuedToken IssueUser(User user, Guid sessionId, IEnumerable<string> permissions) => Issue(
        user.Id, user.EstablishmentId, "user", sessionId, null, options.AccessTokenMinutes, permissions);

    public IssuedToken IssueDevice(Device device, Guid sessionId) => Issue(
        device.Id, device.EstablishmentId!.Value, "device", sessionId, device.CredentialVersion, options.DeviceAccessTokenMinutes, []);

    public string IssueConfiguration(Device device)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer, Audience = options.Audience,
            Subject = new ClaimsIdentity([new("sub", device.Id.ToString()), new("token_type", "configuration"), new("credential_version", device.CredentialVersion.ToString())]),
            NotBefore = now, Expires = now.AddMinutes(options.ConfigurationTokenMinutes), SigningCredentials = new(_key, SecurityAlgorithms.HmacSha256)
        };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }

    public ClaimsPrincipal ValidateConfiguration(string token)
    {
        var principal = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, ValidationParameters(), out _);
        if (principal.FindFirstValue("token_type") != "configuration") throw new SecurityTokenException("Invalid token type.");
        return principal;
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true, ValidIssuer = options.Issuer, ValidateAudience = true, ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = _key, ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub", RoleClaimType = ClaimTypes.Role
    };

    public static string NewRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private IssuedToken Issue(Guid subject, Guid establishmentId, string type, Guid sessionId, int? credentialVersion, int minutes, IEnumerable<string> permissions)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim> { new("sub", subject.ToString()), new("establishment_id", establishmentId.ToString()), new("token_type", type), new("session_id", sessionId.ToString()) };
        if (credentialVersion is not null) claims.Add(new("credential_version", credentialVersion.Value.ToString()));
        claims.AddRange(permissions.Select(permission => new Claim("permission", permission)));
        var descriptor = new SecurityTokenDescriptor { Issuer = options.Issuer, Audience = options.Audience, Subject = new ClaimsIdentity(claims), NotBefore = now, Expires = now.AddMinutes(minutes), SigningCredentials = new(_key, SecurityAlgorithms.HmacSha256) };
        return new(new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor), minutes * 60);
    }
}

public sealed record ProtectedCpf(string Ciphertext, string Nonce, string Tag, string Hash, string Masked);

public sealed class CpfProtector(Phase1SecurityOptions options)
{
    private readonly byte[] _encryptionKey = Convert.FromBase64String(options.CpfEncryptionKey);
    private readonly byte[] _hmacKey = Convert.FromBase64String(options.CpfHmacKey);

    public ProtectedCpf Protect(string value)
    {
        var normalized = NormalizeAndValidate(value);
        var plaintext = Encoding.UTF8.GetBytes(normalized); var nonce = RandomNumberGenerator.GetBytes(12); var ciphertext = new byte[plaintext.Length]; var tag = new byte[16];
        using var aes = new AesGcm(_encryptionKey, 16); aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return new(Convert.ToBase64String(ciphertext), Convert.ToBase64String(nonce), Convert.ToBase64String(tag), Convert.ToHexString(HMACSHA256.HashData(_hmacKey, plaintext)), $"***.***.***-{normalized[^2..]}");
    }

    public string Hash(string value) => Convert.ToHexString(HMACSHA256.HashData(_hmacKey, Encoding.UTF8.GetBytes(NormalizeAndValidate(value))));

    private static string NormalizeAndValidate(string value)
    {
        var cpf = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (cpf.Length != 11 || cpf.Distinct().Count() == 1 || Digit(cpf, 9) != cpf[9] - '0' || Digit(cpf, 10) != cpf[10] - '0') throw new ArgumentException("Invalid CPF.", nameof(value));
        return cpf;
    }

    private static int Digit(string cpf, int position)
    {
        var sum = 0; for (var i = 0; i < position; i++) sum += (cpf[i] - '0') * (position + 1 - i);
        var remainder = sum % 11; return remainder < 2 ? 0 : 11 - remainder;
    }
}

public static class ClaimsPrincipalExtensions
{
    public static Guid RequiredGuid(this ClaimsPrincipal principal, string claim) => Guid.Parse(principal.FindFirstValue(claim) ?? throw new UnauthorizedAccessException($"Missing {claim}."));
    public static bool IsTokenType(this ClaimsPrincipal principal, string type) => principal.FindFirstValue("token_type") == type;
}

public static class PermissionResolver
{
    public static async Task<HashSet<string>> ResolveAsync(AppizzaDbContext db, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var roleAllows = await (from ur in db.Set<UserRole>() join rp in db.Set<RolePermission>() on ur.RoleId equals rp.RoleId join p in db.Set<Permission>() on rp.PermissionId equals p.Id where ur.UserId == userId && rp.ScopeType == null && (ur.ValidFrom == null || ur.ValidFrom <= now) && (ur.ValidUntil == null || ur.ValidUntil > now) select p.Code).ToListAsync(ct);
        var direct = await (from up in db.Set<UserPermission>() join p in db.Set<Permission>() on up.PermissionId equals p.Id where up.UserId == userId && up.ScopeType == null && (up.ValidFrom == null || up.ValidFrom <= now) && (up.ValidUntil == null || up.ValidUntil > now) select new { p.Code, up.Effect }).ToListAsync(ct);
        var result = roleAllows.ToHashSet(StringComparer.Ordinal); foreach (var allow in direct.Where(x => x.Effect == "allow")) result.Add(allow.Code); foreach (var deny in direct.Where(x => x.Effect == "deny")) result.Remove(deny.Code); return result;
    }
}
