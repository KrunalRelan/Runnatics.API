using System.Collections.Generic;

namespace Runnatics.Models.Client.Responses.RFID
{
    public class CompleteRFIDProcessingResponse
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public long TotalProcessingTimeMs { get; set; }

        // Phase 1: Processing Stats
        public int TotalBatchesProcessed { get; set; }
        public int SuccessfulBatches { get; set; }
        public int FailedBatches { get; set; }
        public int TotalRawReadingsProcessed { get; set; }

        // Phase 1.5: Checkpoint Assignment Stats (Loop Races)
        public long Phase15AssignmentMs { get; set; }
        public int CheckpointsAssigned { get; set; }

        // Phase 2: Deduplication Stats
        public int TotalNormalizedReadings { get; set; }
        public int DuplicatesRemoved { get; set; }
        public int CheckpointsProcessed { get; set; }
        public int ParticipantsProcessed { get; set; }

        // Phase 2.5: Split Times Stats
        public long Phase25SplitTimesMs { get; set; }
        public int SplitTimesCreated { get; set; }

        // Phase 3: Results Calculation Stats
        public int TotalFinishers { get; set; }
        public int ResultsCreated { get; set; }
        public int ResultsUpdated { get; set; }
        public int DNFCount { get; set; }
        public int CategoriesProcessed { get; set; }
        public GenderBreakdown? GenderStats { get; set; }

        // Detailed Phase Timings
        public long Phase1ProcessingMs { get; set; }
        public long Phase2DeduplicationMs { get; set; }
        public long Phase3CalculationMs { get; set; }

        // Error Details
        public List<string> Errors { get; set; } = new List<string>();
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
