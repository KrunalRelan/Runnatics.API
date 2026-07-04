using System.ComponentModel.DataAnnotations;
using Runnatics.Models.Client.Requests.Participant;
using Runnatics.Models.Data.Constants;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// #4/#5 — status display mapping ("Finished"→"OK", "DQ"→"DSQ"), the canonical stored DSQ
    /// value (the enum-vs-string trap: one spelling in storage, ever), and the computed-only
    /// RunStatus request validation (only DSQ settable; reason mandatory).
    /// </summary>
    [TestClass]
    public class DsqAndStatusDisplayTests
    {
        // ─── Display mapping (stored values unchanged) ───

        [TestMethod]
        public void ToDisplay_MapsStoredValuesToLabels()
        {
            Assert.AreEqual("OK", ResultStatus.ToDisplay("Finished"));
            Assert.AreEqual("DSQ", ResultStatus.ToDisplay("DQ"));
            Assert.AreEqual("DNF", ResultStatus.ToDisplay("DNF"));
            Assert.AreEqual("DNS", ResultStatus.ToDisplay("DNS"));
            Assert.AreEqual("Registered", ResultStatus.ToDisplay("Registered"), "unknown values pass through");
            Assert.AreEqual(string.Empty, ResultStatus.ToDisplay(null));
        }

        // ─── Canonical stored DSQ value (pinning test, per review callout) ───

        [TestMethod]
        public void IsDsq_AcceptsEverySpelling_CaseInsensitive()
        {
            Assert.IsTrue(ResultStatus.IsDsq("DSQ"));
            Assert.IsTrue(ResultStatus.IsDsq("dsq"));
            Assert.IsTrue(ResultStatus.IsDsq("DQ"));
            Assert.IsTrue(ResultStatus.IsDsq("Disqualified"));
            Assert.IsTrue(ResultStatus.IsDsq("disqualified"));

            Assert.IsFalse(ResultStatus.IsDsq("OK"));
            Assert.IsFalse(ResultStatus.IsDsq("DNF"));
            Assert.IsFalse(ResultStatus.IsDsq("DNS"));
            Assert.IsFalse(ResultStatus.IsDsq(null));
        }

        [TestMethod]
        public void CanonicalStoredValue_IsDQ()
        {
            // Storage must never hold "DSQ"/"Disqualified" — the boundary normalizes to this.
            Assert.AreEqual("DQ", ResultStatus.DQ);
            Assert.AreEqual("DSQ", ResultStatus.ToDisplay(ResultStatus.DQ), "…and DISPLAYS as DSQ");
        }

        // ─── UN-DSQ: "Recompute" is the one clear-DSQ action value, disjoint from DSQ ───

        [TestMethod]
        public void IsClearDsq_AcceptsRecompute_CaseInsensitive_NothingElse()
        {
            Assert.IsTrue(ResultStatus.IsClearDsq("Recompute"));
            Assert.IsTrue(ResultStatus.IsClearDsq("recompute"));
            Assert.IsTrue(ResultStatus.IsClearDsq("RECOMPUTE"));

            Assert.IsFalse(ResultStatus.IsClearDsq("DSQ"));
            Assert.IsFalse(ResultStatus.IsClearDsq("DQ"));
            Assert.IsFalse(ResultStatus.IsClearDsq("OK"));
            Assert.IsFalse(ResultStatus.IsClearDsq(null));

            // Disjoint action sets: "Recompute" is never a DSQ, and it is NEVER stored.
            Assert.IsFalse(ResultStatus.IsDsq(ResultStatus.Recompute));
            Assert.AreEqual("Recompute", ResultStatus.ToDisplay("Recompute"),
                "no display mapping — the value never reaches storage or display");
        }

        // ─── #4: RunStatus is computed-only; only DSQ can be set manually ───

        private static List<ValidationResult> Validate(UpdateParticipantRequest request) =>
            request.Validate(new ValidationContext(request)).ToList();

        [TestMethod]
        public void RunStatus_ComputedValues_AreRejected()
        {
            foreach (var computed in new[] { "OK", "DNF", "DNS", "Finished" })
            {
                var results = Validate(new UpdateParticipantRequest { RunStatus = computed });
                Assert.IsTrue(results.Any(r => r.ErrorMessage!.Contains("only DSQ")),
                    $"'{computed}' must be rejected — statuses are computed from timing data (#7)");
            }
        }

        [TestMethod]
        public void RunStatus_DsqWithReason_Valid()
        {
            foreach (var spelling in new[] { "DSQ", "DQ", "Disqualified" })
            {
                var results = Validate(new UpdateParticipantRequest
                {
                    RunStatus = spelling,
                    DisqualificationReason = "Course cutting at 2.5 KM"
                });
                Assert.AreEqual(0, results.Count, $"'{spelling}' with a reason must validate");
            }
        }

        [TestMethod]
        public void RunStatus_DsqWithoutReason_Rejected()
        {
            var results = Validate(new UpdateParticipantRequest { RunStatus = "DSQ" });
            Assert.IsTrue(results.Any(r => r.ErrorMessage!.Contains("DisqualificationReason is required")),
                "the reason is MANDATORY for a disqualification");

            var blank = Validate(new UpdateParticipantRequest { RunStatus = "DSQ", DisqualificationReason = "   " });
            Assert.IsTrue(blank.Any(r => r.ErrorMessage!.Contains("DisqualificationReason is required")));
        }

        [TestMethod]
        public void RunStatus_Null_NoStatusValidation()
        {
            Assert.AreEqual(0, Validate(new UpdateParticipantRequest()).Count,
                "a request without RunStatus edits other fields freely");
        }

        [TestMethod]
        public void RunStatus_Recompute_ValidWithoutReason()
        {
            // UN-DSQ: clearing a disqualification needs NO reason (the clear nulls the stored one).
            Assert.AreEqual(0, Validate(new UpdateParticipantRequest { RunStatus = "Recompute" }).Count);
            Assert.AreEqual(0, Validate(new UpdateParticipantRequest { RunStatus = "recompute" }).Count,
                "case-insensitive like the DSQ spellings");

            // A stray reason alongside Recompute is tolerated — the service nulls it regardless.
            Assert.AreEqual(0, Validate(new UpdateParticipantRequest
            {
                RunStatus = "Recompute",
                DisqualificationReason = "left over from the DSQ form"
            }).Count);
        }
    }
}
