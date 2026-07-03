using Runnatics.Services.RFID;

namespace Runnatics.Services.Tests.RFID
{
    /// <summary>
    /// #2 SEQUENCE VALIDATION on manual time edits — CrossingSequence.FindViolation.
    /// The edited crossing must be STRICTLY after every lower gate's crossing and STRICTLY
    /// before every higher gate's crossing (equal timestamps violate).
    /// </summary>
    [TestClass]
    public class CrossingSequenceTests
    {
        private static readonly DateTime T0 = new(2026, 6, 29, 0, 3, 0, DateTimeKind.Utc);

        private static CrossingNeighbor N(string name, decimal distance, DateTime chip) =>
            new() { Name = name, Distance = distance, ChipTime = chip };

        [TestMethod]
        public void ClientExample_StartEditedAfterNextGate_MustBeBefore()
        {
            // "Start time 05:45:34 must be before 2.5 KM's 05:42:01" — the edited start (0 km)
            // lands AFTER the runner's 2.5 KM crossing.
            var violation = CrossingSequence.FindViolation(
                editedDistance: 0m,
                editedCrossingUtc: T0.AddMinutes(12).AddSeconds(34),          // 05:45:34-class
                otherCrossings: new[] { N("2.5 KM", 2.5m, T0.AddMinutes(9).AddSeconds(1)) }); // 05:42:01-class

            Assert.IsNotNull(violation);
            Assert.IsTrue(violation!.MustBeBefore);
            Assert.AreEqual("2.5 KM", violation.ConflictName);
            Assert.AreEqual(T0.AddMinutes(9).AddSeconds(1), violation.ConflictTime);
        }

        [TestMethod]
        public void MidEditedBeforePreviousGate_MustBeAfter()
        {
            var violation = CrossingSequence.FindViolation(
                editedDistance: 2.5m,
                editedCrossingUtc: T0.AddSeconds(10),
                otherCrossings: new[] { N("start", 0m, T0.AddSeconds(33)) });

            Assert.IsNotNull(violation);
            Assert.IsFalse(violation!.MustBeBefore);
            Assert.AreEqual("start", violation.ConflictName);
        }

        [TestMethod]
        public void EqualTimestamp_Violates_StrictOrdering()
        {
            var t = T0.AddMinutes(9);
            Assert.IsNotNull(CrossingSequence.FindViolation(2.5m, t, new[] { N("start", 0m, t) }),
                "equal to the PREVIOUS gate's time violates (strictly after)");
            Assert.IsNotNull(CrossingSequence.FindViolation(2.5m, t, new[] { N("Finish", 5m, t) }),
                "equal to the NEXT gate's time violates (strictly before)");
        }

        [TestMethod]
        public void ValidEdit_BetweenNeighbors_NoViolation()
        {
            var violation = CrossingSequence.FindViolation(
                editedDistance: 2.5m,
                editedCrossingUtc: T0.AddMinutes(9),
                otherCrossings: new[]
                {
                    N("start", 0m, T0.AddSeconds(33)),
                    N("Finish", 5m, T0.AddMinutes(18))
                });

            Assert.IsNull(violation);
        }

        [TestMethod]
        public void GapTolerant_NearestExistingCrossingIsTheBound()
        {
            // The adjacent gate (2.5) has no crossing — the bound is the nearest EXISTING one.
            var violation = CrossingSequence.FindViolation(
                editedDistance: 5m,
                editedCrossingUtc: T0.AddSeconds(10),                 // before the START crossing
                otherCrossings: new[] { N("start", 0m, T0.AddSeconds(33)) });

            Assert.IsNotNull(violation);
            Assert.IsFalse(violation!.MustBeBefore);
            Assert.AreEqual("start", violation.ConflictName);
        }

        [TestMethod]
        public void ClosestOffenderIsNamed()
        {
            // Two lower gates both conflict — the message names the closest (latest) one.
            var violation = CrossingSequence.FindViolation(
                editedDistance: 5m,
                editedCrossingUtc: T0.AddMinutes(1),
                otherCrossings: new[]
                {
                    N("start", 0m, T0.AddMinutes(2)),
                    N("2.5 KM", 2.5m, T0.AddMinutes(9))
                });

            Assert.IsNotNull(violation);
            Assert.AreEqual("2.5 KM", violation!.ConflictName, "the latest lower-gate crossing is the named conflict");
        }

        [TestMethod]
        public void NoOtherCrossings_NoViolation()
        {
            Assert.IsNull(CrossingSequence.FindViolation(0m, T0, Array.Empty<CrossingNeighbor>()));
        }
    }
}
