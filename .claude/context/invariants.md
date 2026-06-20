# Cross-Cutting Invariants & Recurring Failure Patterns

## META-LESSON (read first)
VERIFY diagnoses against actual prod data BEFORE acting. Multiple confident, internally-consistent diagnoses this session were WRONG because they assumed a data state that one query disproved (assumed May reads → were Jan artifact from a bad CROSS JOIN; assumed shared gun → was staggered; assumed day-early StartTime bug → was correct pre-5:30 rollback). When a result looks wildly off, suspect the query before the data.

## Recurring failure patterns
1. Divergent dual implementations (RFIDImportService vs ResultsService): fix one, the other still has the bug. Check both.
2. Insert-only pipeline + skip-guards: fixes don't reach EXISTING data without forceReprocess=true. Always reprocess after a timing/normalization fix.
3. enum .ToString() vs canonical DB string mismatch (status filters; gender ×3 sites): "Completed"≠"Finished", "Male"≠"M". Use explicit mapping.
4. Guards/thresholds evaluated over the POST-EXCLUSION reprocess subset fire inconsistently between full runs and incremental reprocess (the daysDiff guard). Compute against a race-stable baseline, not the current subset.
5. Bib reuse across races: ALWAYS key diagnostics/joins on ParticipantId, never bib (one bib → many people across races).
