using static Vanadium.Classes.DBs.DBClasses.AnnoucementDBClasses;
using System.Text.RegularExpressions;
using LiteDB;
using System.Collections.Generic;
using System;
using System.IO;
using Vanadium.Controllers;

namespace Vanadium.Classes.DBs
{
    public class AnnoucementDB
    {
        public static LiteDatabase AnnoucementDBFile = new LiteDatabase("Filename=" + Path.Join(Program.dataDir, "DBs", "Annoucements.db") + ";Connection=shared");
        public static readonly ILiteCollection<Announcement> Annoucements = AnnoucementDBFile.GetCollection<Announcement>("Annoucements");

        private static readonly Random _random = new Random();

        public static List<Announcement> GetAllAnnouncements()
        {
            return Annoucements.FindAll().ToList();
        }

        public static async Task<bool> CreateAnnouncement(Announcement newAnnouncement, bool sendWs = false)
        {
            long uniqueId;
            while (true)
            {
                uniqueId = _random.Next(1147483647, 2147483647);
                if (!Annoucements.Exists(x => x.AnnouncementId == uniqueId))
                    break;
            }
            newAnnouncement.AnnouncementId = uniqueId;
            var result = Annoucements.Insert(newAnnouncement);
            bool success = result.AsInt64 == uniqueId;

            if (sendWs && success)
                await NotificationsController.SendAnnoucementUpdate(newAnnouncement);

            return success;
        }

        public static bool DeleteAnnouncement(long id)
        {
            return Annoucements.Delete(id);
        }

        public static async Task<int> DeleteAllAnnouncements(bool sendWs = false)
        {
            var ids = Annoucements.FindAll().Select(x => x.AnnouncementId).ToList();
            int count = Annoucements.DeleteAll();
            if (sendWs)
            {
                foreach (var id in ids)
                    await NotificationsController.SendAnnoucementDelete(id);
            }

            return count;
        }
    }
}