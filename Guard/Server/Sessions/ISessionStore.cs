using System.Collections.Generic;

namespace VanadiumGuard.Server.Sessions
{
    /// <summary>
    /// Session persistence. The in-memory implementation is fine for a single instance;
    /// swap in Redis or a database behind this interface to run more than one.
    /// </summary>
    public interface ISessionStore
    {
        void Add(GuardSession session);

        GuardSession? Get(string sessionId);

        /// <summary>Looks a session up by the match id the game server knows it as.</summary>
        GuardSession? GetByGameSession(string gameSessionId);

        void Remove(string sessionId);

        IReadOnlyList<GuardSession> All();
    }
}
