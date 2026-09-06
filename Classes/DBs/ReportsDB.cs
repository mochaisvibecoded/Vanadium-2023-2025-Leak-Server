using LiteDB;
using System.IO;
using Vanadium.Classes.DBs.DBClasses;
using static Vanadium.Classes.DBs.DBClasses.ReportsDBClasses;

namespace Vanadium.Classes.DBs
{
    public static class ReportsDB
    {
        public static LiteDatabase ReportsDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Reports.db") + ";Connection=shared");
        public static readonly ILiteCollection<BugReport> Reports = ReportsDBFile.GetCollection<BugReport>("BugReports");

        public static void Setup()
        {
            Reports.EnsureIndex(x => x.ReportId, unique: true);
        }

        public static BugReport GetOrCreateReport(string reportId, long playerId)
        {
            var report = Reports.FindOne(x => x.ReportId == reportId);
            if (report == null)
            {
                report = new BugReport
                {
                    ReportId = reportId,
                    PlayerId = playerId
                };
                Reports.Insert(report);
            }
            return report;
        }

        public static BugReport GetReport(string reportId)
        {
            return Reports.FindOne(x => x.ReportId == reportId);
        }

        public static void UpdateReport(BugReport report)
        {
            Reports.Update(report);
        }
    }
}