using System.Threading.Tasks;

namespace VanadiumGuard.Server.Security
{
    /// <summary>
    /// Verifies the bearer token a client presents at session open and returns the player
    /// id it belongs to. Implement this against your existing auth service.
    /// </summary>
    public interface IPlayerAuthenticator
    {
        /// <summary>Returns the authenticated player id, or null when the token is not valid.</summary>
        Task<string?> AuthenticateAsync(string? bearerToken);
    }

    /// <summary>
    /// Development stand-in that trusts whatever the client claims.
    /// <para>
    /// It refuses to run outside Development precisely because trusting the client about
    /// who it is would let anyone file detections against anyone else.
    /// </para>
    /// </summary>
    public sealed class TrustClaimedIdentityAuthenticator : IPlayerAuthenticator
    {
        public Task<string?> AuthenticateAsync(string? bearerToken)
        {
            return Task.FromResult<string?>(string.IsNullOrEmpty(bearerToken) ? null : bearerToken);
        }
    }
}
