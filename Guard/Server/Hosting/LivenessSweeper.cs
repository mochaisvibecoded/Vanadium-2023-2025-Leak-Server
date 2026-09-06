using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VanadiumGuard.Protocol;
using VanadiumGuard.Server.Challenges;
using VanadiumGuard.Server.Policy;
using VanadiumGuard.Server.Sessions;

namespace VanadiumGuard.Server.Hosting
{
    /// <summary>
    /// Notices guards that have stopped talking, and challenges that were never answered.
    /// <para>
    /// This closes the hole that made the old design pointless. Every check the old build
    /// ran was inside the game process, so the cheapest possible defeat was to stop the
    /// guard from running - and a guard that is not running reports nothing, which looked
    /// exactly like a clean player. Here, silence is the signal: the service knows a match
    /// is live because the game server told it, so a guard that stops checking in is a
    /// detection in its own right.
    /// </para>
    /// </summary>
    public sealed class LivenessSweeper : BackgroundService
    {
        private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);

        private readonly ILogger<LivenessSweeper> _logger;
        private readonly ISessionStore _sessions;
        private readonly PolicyEngine _policy;
        private readonly GuardServerOptions _options;

        public LivenessSweeper(
            ILogger<LivenessSweeper> logger,
            ISessionStore sessions,
            PolicyEngine policy,
            GuardServerOptions options)
        {
            _logger = logger;
            _sessions = sessions;
            _policy = policy;
            _options = options;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Liveness sweeper started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Sweep();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Liveness sweep failed");
                }

                try
                {
                    await Task.Delay(SweepInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void Sweep()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var idleTimeout = TimeSpan.FromSeconds(_options.SessionIdleTimeoutSeconds);

            foreach (GuardSession session in _sessions.All())
            {
                // Challenges that aged out. A guard that answers some challenges but not
                // others is more interesting than one that answers none.
                IReadOnlyList<MemoryChallenge> expired = session.ExpiredChallenges(ChallengeService.AnswerWindow);
                foreach (MemoryChallenge challenge in expired)
                {
                    _policy.RecordServerSide(
                        session, DetectionCode.ChallengeUnreadable, challenge.Module, 50,
                        new Dictionary<string, string> { ["reason"] = "no answer within the window" });
                }

                TimeSpan silence = now - session.LastSeenUtc;

                if (silence < idleTimeout)
                    continue;

                // Only meaningful while the match is still running. Without a game session
                // id we cannot tell a killed guard from a player who closed the game, and
                // accusing the second group would be a false positive machine.
                if (string.IsNullOrEmpty(session.GameSessionId))
                {
                    _sessions.Remove(session.SessionId);
                    continue;
                }

                if (!session.Terminated)
                {
                    _policy.RecordServerSide(
                        session, DetectionCode.GuardSilent, session.PlayerId, 80,
                        new Dictionary<string, string>
                        {
                            ["silentSeconds"] = ((int)silence.TotalSeconds).ToString(),
                            ["lastSeen"] = session.LastSeenUtc.ToString("O"),
                        });

                    _logger.LogWarning(
                        "Guard silent for {Seconds}s player={Player} gameSession={GameSession}",
                        (int)silence.TotalSeconds, session.PlayerId, session.GameSessionId);
                }

                // Keep the session around a while longer so the game server can still query
                // its status; drop it once it is thoroughly stale.
                if (silence > idleTimeout + TimeSpan.FromMinutes(10))
                    _sessions.Remove(session.SessionId);
            }
        }
    }
}
