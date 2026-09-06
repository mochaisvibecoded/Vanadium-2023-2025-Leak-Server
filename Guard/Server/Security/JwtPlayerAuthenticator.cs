using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using VanadiumGuard.Server.Hosting;

namespace VanadiumGuard.Server.Security
{
    /// <summary>
    /// Authenticates the bearer token the guard presents at session open by validating it as
    /// the game server's own JWT - same symmetric secret, same issuer.
    /// <para>
    /// It deliberately does no database lookup. Only the game server can mint a token that
    /// verifies against the shared secret, so a valid signature already proves the identity;
    /// requiring the AC service to also reach the game's player store would couple two
    /// services that are meant to be able to run apart, for no extra assurance. The player id
    /// returned is the <c>sub</c>/<c>pid</c> claim, which the game server sets equal.
    /// </para>
    /// </summary>
    public sealed class JwtPlayerAuthenticator : IPlayerAuthenticator
    {
        private readonly TokenValidationParameters _parameters;
        private readonly JwtSecurityTokenHandler _handler = new JwtSecurityTokenHandler();
        private readonly ILogger<JwtPlayerAuthenticator> _logger;

        public JwtPlayerAuthenticator(GuardServerOptions options, ILogger<JwtPlayerAuthenticator> logger)
        {
            _logger = logger;

            _parameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.JwtSecret)),
                ValidateIssuer = true,
                ValidIssuer = options.JwtIssuer,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
        }

        public Task<string?> AuthenticateAsync(string? bearerToken)
        {
            if (string.IsNullOrWhiteSpace(bearerToken))
                return Task.FromResult<string?>(null);

            try
            {
                _handler.ValidateToken(bearerToken, _parameters, out SecurityToken validated);
                var jwt = (JwtSecurityToken)validated;

                string? sub = jwt.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
                string? pid = jwt.Claims.FirstOrDefault(c => c.Type == "pid")?.Value;

                // sub and pid are set equal by the game server; a token where they disagree
                // is not one it minted, so it is refused the same as a bad signature.
                if (string.IsNullOrEmpty(sub) || sub != pid)
                    return Task.FromResult<string?>(null);

                return Task.FromResult<string?>(sub);
            }
            catch (Exception ex)
            {
                // An invalid, expired, or forged token is an ordinary "no" here, not an error
                // worth a stack trace - it is exactly what this method exists to catch.
                _logger.LogDebug("token rejected: {Message}", ex.Message);
                return Task.FromResult<string?>(null);
            }
        }
    }
}
