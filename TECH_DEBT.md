# Tech Debt

## Legacy single-basis rank columns on Results (logged 2026-08-17)

`Results.OverallRank / GenderRank / CategoryRank` are now redundant: every rank run
(`RankCalculator.AssignRanks`) populates the explicit dual-basis pairs
`NetOverallRank/NetGenderRank/NetCategoryRank` and `GunOverallRank/GunGenderRank/GunCategoryRank`,
and the legacy three are just a copy of whichever explicit set matches the configured basis
(RankOnNet / per-view leaderboard settings).

**Retire the legacy columns once all consumers read the explicit pairs directly.** Current
legacy-column readers to migrate first:

- Public leaderboard, podium and overall list (`PublicResultsService`)
- Admin leaderboard (`ResultsService`)
- Admin participant detail (`ParticipantDetailsResponseBuilder`)
- Export (`ResultsExportService`)
- Certificates (`CertificatesService`)
- Completion SMS (`RaceNotificationService`)

Each should resolve the basis via `RankCalculator.ResolveBasis` and pick the matching explicit
set. After migration: drop the three columns (idempotent SQL script) and delete the legacy
copy-back in `RankCalculator.AssignRanks`.
