namespace VanadiumGuard.Server.Hosting
{
    /// <summary>Bound from the Guard section of appsettings.json.</summary>
    public sealed class GuardServerOptions
    {
        public string PolicyDirectory { get; set; } = "policies";

        public string ReferenceImageDirectory { get; set; } = "builds";

        /// <summary>Where the machine link table and hardware ban list are kept.</summary>
        public string BanDirectory { get; set; } = "bans";

        /// <summary>
        /// Whether a client that cannot identify its machine may open a session. Left on by
        /// default: locked-down and unusual machines legitimately fail to produce an
        /// identity, and refusing them would turn a hardware ban feature into a hardware
        /// allow-list nobody asked for. The refusal is still recorded either way.
        /// </summary>
        public bool AllowUnidentifiedMachines { get; set; } = true;

        /// <summary>
        /// Whether a session may open without a TPM-backed device key. Left on by default:
        /// plenty of machines have no TPM, have it switched off in firmware, or run a
        /// provider that refuses to create keys, and none of that is cheating. Turning this
        /// off is a hardware requirement for playing, not an anti-cheat tweak.
        /// </summary>
        public bool RequireDeviceAttestation { get; set; } = false;

        /// <summary>
        /// Shared secret for the internal endpoints the game server calls. These endpoints
        /// expose per-player risk data, so they must never be reachable by a game client.
        /// </summary>
        public string InternalApiKey { get; set; } = string.Empty;

        /// <summary>
        /// Shared HMAC secret for the game's JWTs. The AC service validates the player's
        /// game token at session open with the SAME secret and issuer the game server uses,
        /// so it authenticates without a callback. Must match the game server exactly.
        /// </summary>
        public string JwtSecret { get; set; } = string.Empty;

        /// <summary>Issuer the game server stamps on its JWTs. Must match exactly.</summary>
        public string JwtIssuer { get; set; } = string.Empty;

        /// <summary>
        /// Allow a session to open with no valid player token, tied to the machine instead of
        /// an account. This exists because the guard mod is not yet wired to hand over the
        /// player's game token; with it on, the guard still connects, reports, and is subject
        /// to hardware bans - it just cannot be attributed to a specific account until the
        /// token is supplied. Turn it OFF once the client sends a token, so every session is
        /// player-attributed and no one can report anonymously.
        /// </summary>
        public bool AllowAnonymousSessions { get; set; } = false;

        /// <summary>Silence beyond this is treated as the guard having gone away.</summary>
        public int SessionIdleTimeoutSeconds { get; set; } = 90;

        public int MissedHeartbeatsBeforeSilent { get; set; } = 3;
    }
}
