using System;
using System.Collections.Generic;
using System.Linq;

namespace LCS_HR_MVC.Models
{
    public class DataSyncResult
    {
        public bool Success { get; set; }
        public DateTime SyncedAt { get; set; } = DateTime.Now;
        public List<SyncLogEntry> Log { get; set; } = new();

        public int TotalChanges  => Log.Count(e => e.Level == "CHANGE");
        public int TotalWarnings => Log.Count(e => e.Level == "WARN");
        public int TotalErrors   => Log.Count(e => e.Level == "ERROR");

        public List<string> Changes  => Log
            .Where(e => e.Level == "CHANGE")
            .Select(e => e.Message).ToList();
        public List<string> Errors   => Log
            .Where(e => e.Level == "ERROR")
            .Select(e => e.Message).ToList();
        public List<string> Warnings => Log
            .Where(e => e.Level == "WARN")
            .Select(e => e.Message).ToList();
    }

    public class SyncLogEntry
    {
        public DateTime Ts      { get; set; } = DateTime.Now;
        public string   Level   { get; set; } = "INFO";
        // Levels: INFO / CHANGE / WARN / ERROR
        public string   Table   { get; set; } = string.Empty;
        public string   Message { get; set; } = string.Empty;
    }
}
