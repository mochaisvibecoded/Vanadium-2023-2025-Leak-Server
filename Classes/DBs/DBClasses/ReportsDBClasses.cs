using System;
using LiteDB;

namespace Vanadium.Classes.DBs.DBClasses
{
    public class ReportsDBClasses
    {
        public class BugReport
        {
            [BsonId]
            public string ReportId { get; set; }
            public long PlayerId { get; set; }
            public string TriggerId { get; set; }
            public int TriggerType { get; set; }
            public DateTime TriggeredAt { get; set; }
            public string Title { get; set; }
            public string Description { get; set; }
            public string TraceLogPath { get; set; }
            public string GameScreenshotPath { get; set; }
            public bool IsCompleted { get; set; } = false;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public class ReportInitializationRequest
        {
            public ClientInfo Client { get; set; }
            public string ReportId { get; set; }
        }

        public class ClientInfo
        {
            public string TriggerId { get; set; }
            public int TriggerType { get; set; }
            public DateTime TriggeredAt { get; set; }
        }

        public class ReportUserInfoRequest
        {
            public string Title { get; set; }
            public string Description { get; set; }
        }
    }
}