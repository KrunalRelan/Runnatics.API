using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Runnatics.Data.EF;
using Runnatics.Models.Data.Entities;
using Runnatics.Repositories.Interface;

namespace Runnatics.Services.Helpers
{
    /// <summary>
    /// Resolves an AgeCategory to a single canonical CASING per event, so inconsistent
    /// spellings ("30 to Under 50 Yrs" vs "... yrs") don't fragment a category into two
    /// buckets (which splits its runners and produces two category winners).
    ///
    /// On write: case-insensitively match an AgeCategory already used IN THAT EVENT and
    /// reuse its EXACT casing; the first occurrence establishes the canonical casing.
    /// The per-event map is cached once (per DI scope / batch), not per row.
    ///
    /// Registered scoped so the cache lives for one request/batch and shares the request
    /// DbContext.
    /// </summary>
    public interface ICategoryNormalizer
    {
        Task<string> ResolveAgeCategoryAsync(int eventId, string? raw, CancellationToken ct = default);
    }

    public class CategoryNormalizer(IUnitOfWork<RaceSyncDbContext> repository) : ICategoryNormalizer
    {
        public const string Unknown = "Unknown";

        private readonly IUnitOfWork<RaceSyncDbContext> _repository = repository;

        // eventId → (lowercased category → canonical casing to store)
        private readonly Dictionary<int, Dictionary<string, string>> _cache = [];

        private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

        /// <summary>Trim + collapse internal whitespace; empty → "Unknown".</summary>
        public static string Normalize(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return Unknown;
            var collapsed = WhitespaceRun.Replace(raw.Trim(), " ");
            return collapsed.Length == 0 ? Unknown : collapsed;
        }

        public async Task<string> ResolveAgeCategoryAsync(int eventId, string? raw, CancellationToken ct = default)
        {
            var normalized = Normalize(raw);
            if (string.Equals(normalized, Unknown, StringComparison.OrdinalIgnoreCase))
                return Unknown;

            var map = await GetOrLoadMapAsync(eventId, ct);
            var key = normalized.ToLowerInvariant();

            if (map.TryGetValue(key, out var canonical))
                return canonical;

            // First time this logical category appears for the event — establish its casing.
            map[key] = normalized;
            return normalized;
        }

        private async Task<Dictionary<string, string>> GetOrLoadMapAsync(int eventId, CancellationToken ct)
        {
            if (_cache.TryGetValue(eventId, out var cached))
                return cached;

            var existing = await _repository.GetRepository<Participant>()
                .GetQuery(p => p.EventId == eventId
                            && p.AuditProperties.IsActive
                            && !p.AuditProperties.IsDeleted
                            && p.AgeCategory != null)
                .AsNoTracking()
                .Select(p => p.AgeCategory!)
                .Distinct()
                .ToListAsync(ct);

            var map = new Dictionary<string, string>();
            foreach (var value in existing)
            {
                var normalized = Normalize(value);
                if (string.Equals(normalized, Unknown, StringComparison.OrdinalIgnoreCase))
                    continue;
                var key = normalized.ToLowerInvariant();
                // First-seen casing wins; the SQL convergence unifies historical splits.
                map.TryAdd(key, normalized);
            }

            _cache[eventId] = map;
            return map;
        }
    }
}
