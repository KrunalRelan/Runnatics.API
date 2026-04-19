namespace Runnatics.Models.Client.Responses.RFID
{
    public class RFIDDiagnosticsResponse
    {
        public string Status { get; set; } = "Ok";
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

        public RaceEventSetupSection RaceEventSetup { get; set; } = new();
        public CheckpointsSection Checkpoints { get; set; } = new();
        public ParticipantsSection Participants { get; set; } = new();
        public UploadBatchesSection UploadBatches { get; set; } = new();
        public RawReadingsSection RawReadings { get; set; } = new();
        public AssignmentsSection Assignments { get; set; } = new();
        public NormalizedReadingsSection NormalizedReadings { get; set; } = new();
        public SplitTimesSection SplitTimes { get; set; } = new();
        public ResultsSection Results { get; set; } = new();
        public EpcBibMappingSection EpcBibMapping { get; set; } = new();
        public DiagnosisSummarySection DiagnosisSummary { get; set; } = new();
    }

    public class RaceEventSetupSection
    {
        public int EventId { get; set; }
        public int RaceId { get; set; }
        public string? EventName { get; set; }
        public string? RaceName { get; set; }
        public bool RaceExists { get; set; }
        public bool? RaceIsActive { get; set; }
        public bool? RaceIsDeleted { get; set; }
        public decimal? RaceDistance { get; set; }
        public bool? HasLoops { get; set; }
        public decimal? LoopLength { get; set; }
        public DateTime? EventDate { get; set; }
        public string? TimeZone { get; set; }
        public DateTime? RaceStartTime { get; set; }
    }

    public class CheckpointsSection
    {
        public int TotalCount { get; set; }
        public List<CheckpointDiagnosticInfo> Items { get; set; } = new();
        public List<string> Flags { get; set; } = new();
    }

    public class CheckpointDiagnosticInfo
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal DistanceFromStart { get; set; }
        public int DeviceId { get; set; }
        public int? ParentDeviceId { get; set; }
        public bool IsMandatory { get; set; }
        public bool IsActive { get; set; }
        public bool IsDeleted { get; set; }
        public string? DeviceName { get; set; }
        public string? DeviceMacAddress { get; set; }
        public bool DeviceFound { get; set; }
    }

    public class ParticipantsSection
    {
        public int TotalActive { get; set; }
        public int WithChipAssignment { get; set; }
        public int WithoutChipAssignment { get; set; }
    }

    public class UploadBatchesSection
    {
        public int TotalCount { get; set; }
        public List<UploadBatchDiagnosticInfo> Items { get; set; } = new();
    }

    public class UploadBatchDiagnosticInfo
    {
        public int BatchId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string SourceType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string? OriginalFileName { get; set; }
        public string? StoredFilePath { get; set; }
        public int? TotalReadings { get; set; }
        public string DeviceMacAddress { get; set; } = string.Empty;
    }

    public class RawReadingsSection
    {
        public int TotalCount { get; set; }
        public Dictionary<string, int> ByProcessResult { get; set; } = new();
        public Dictionary<string, int> ByDeviceMac { get; set; } = new();
        public List<RawReadingSample> Samples { get; set; } = new();
        public List<string> Flags { get; set; } = new();
        public List<string> UnknownMacsInDevicesTable { get; set; } = new();
        public List<string> MacsWithoutCheckpoint { get; set; } = new();
    }

    public class RawReadingSample
    {
        public long Id { get; set; }
        public string Epc { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public DateTime ReadTimeUtc { get; set; }
        public decimal? RssiDbm { get; set; }
        public string ProcessResult { get; set; } = string.Empty;
    }

    public class AssignmentsSection
    {
        public int TotalCount { get; set; }
        public Dictionary<int, int> ByCheckpointId { get; set; } = new();
    }

    public class NormalizedReadingsSection
    {
        public int TotalCount { get; set; }
        public int DistinctParticipants { get; set; }
    }

    public class SplitTimesSection
    {
        public int TotalCount { get; set; }
        public Dictionary<int, int> ByCheckpointId { get; set; } = new();
    }

    public class ResultsSection
    {
        public int TotalCount { get; set; }
        public Dictionary<string, int> ByStatus { get; set; } = new();
    }

    public class EpcBibMappingSection
    {
        public int ParticipantsWithChip { get; set; }
        public List<ParticipantChipSample> Samples { get; set; } = new();
        public List<EpcLookupResult> RawEpcLookups { get; set; } = new();
    }

    public class ParticipantChipSample
    {
        public int ParticipantId { get; set; }
        public string? BibNumber { get; set; }
        public string? Epc { get; set; }
    }

    public class EpcLookupResult
    {
        public string Epc { get; set; } = string.Empty;
        public bool MatchesAnyParticipant { get; set; }
        public int? ParticipantId { get; set; }
        public string? BibNumber { get; set; }
    }

    public class DiagnosisSummarySection
    {
        public List<string> LikelyIssues { get; set; } = new();
    }
}
