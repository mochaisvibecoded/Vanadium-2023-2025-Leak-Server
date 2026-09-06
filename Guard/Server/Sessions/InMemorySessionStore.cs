using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace VanadiumGuard.Server.Sessions
{
    /// <inheritdoc />
    public sealed class InMemorySessionStore : ISessionStore
    {
        private readonly ConcurrentDictionary<string, GuardSession> _bySessionId =
            new ConcurrentDictionary<string, GuardSession>(StringComparer.Ordinal);

        private readonly ConcurrentDictionary<string, string> _gameSessionToSessionId =
            new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

        public void Add(GuardSession session)
        {
            _bySessionId[session.SessionId] = session;

            if (!string.IsNullOrEmpty(session.GameSessionId))
                _gameSessionToSessionId[session.GameSessionId] = session.SessionId;
        }

        public GuardSession? Get(string sessionId)
        {
            return _bySessionId.TryGetValue(sessionId, out GuardSession? session) ? session : null;
        }

        public GuardSession? GetByGameSession(string gameSessionId)
        {
            if (!_gameSessionToSessionId.TryGetValue(gameSessionId, out string? sessionId))
                return null;

            return Get(sessionId);
        }

        public void Remove(string sessionId)
        {
            if (!_bySessionId.TryRemove(sessionId, out GuardSession? session))
                return;

            if (!string.IsNullOrEmpty(session.GameSessionId))
                _gameSessionToSessionId.TryRemove(session.GameSessionId, out _);
        }

        public IReadOnlyList<GuardSession> All()
        {
            return _bySessionId.Values.ToList();
        }
    }
}
