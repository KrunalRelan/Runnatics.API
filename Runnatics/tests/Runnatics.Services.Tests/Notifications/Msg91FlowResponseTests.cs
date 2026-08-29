namespace Runnatics.Services.Tests.Notifications
{
    /// <summary>
    /// Msg91NotificationSmsService.ParseFlowResponse.
    ///
    /// The shipped parser discarded TryGetProperty's return value, read the v2 key
    /// ("request_id") against a v5 Flow body, and swallowed the resulting
    /// InvalidOperationException in an empty catch — so ProviderMessageId was null on all
    /// 173 rows in NotificationLogs and nobody saw an error. It also branched on the HTTP
    /// status alone, so a Flow rejection (HTTP 200 with type="error") was recorded as a
    /// success.
    ///
    /// Fixtures are the real Flow v5 shapes: "message" carries the request id on acceptance
    /// and the error text on rejection, so it may only be read as an id when type is not
    /// "error".
    /// </summary>
    [TestClass]
    public class Msg91FlowResponseTests
    {
        private static Msg91NotificationSmsService.Msg91FlowResponse Parse(string body) =>
            Msg91NotificationSmsService.ParseFlowResponse(body);

        // ─────────────────────────────────────────────────────────────────
        // Acceptance — the request id must be captured
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Accepted_FlowV5_TakesRequestIdFromMessage()
        {
            var r = Parse(@"{""message"":""3d1e9f4a5b6c7d8e9f001122"",""type"":""success""}");

            Assert.IsTrue(r.Accepted);
            Assert.AreEqual("3d1e9f4a5b6c7d8e9f001122", r.RequestId);
            Assert.AreEqual("success", r.Type);
            Assert.IsNull(r.Error);
        }

        [TestMethod]
        public void Accepted_ExplicitRequestIdKey_IsPreferredOverMessage()
        {
            var r = Parse(@"{""request_id"":""explicit-id"",""message"":""ignored"",""type"":""success""}");

            Assert.IsTrue(r.Accepted);
            Assert.AreEqual("explicit-id", r.RequestId);
        }

        [TestMethod]
        public void Accepted_NoTypeField_StillYieldsRequestId()
        {
            // Defensive: some MSG91 endpoints omit "type" on success.
            var r = Parse(@"{""message"":""abc123""}");

            Assert.IsTrue(r.Accepted);
            Assert.AreEqual("abc123", r.RequestId);
        }

        // ─────────────────────────────────────────────────────────────────
        // Rejection arriving as HTTP 200 — must NOT be logged as a success
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Rejected_TypeError_IsNotAccepted_AndCarriesTheErrorText()
        {
            var r = Parse(@"{""message"":""Invalid template id"",""type"":""error""}");

            Assert.IsFalse(r.Accepted);
            Assert.AreEqual("Invalid template id", r.Error);
            Assert.IsNull(r.RequestId, "the error text must never be stored as a request id");
        }

        [TestMethod]
        public void Rejected_TypeErrorMixedCase_IsStillRejected()
        {
            var r = Parse(@"{""message"":""bad authkey"",""type"":""ERROR""}");

            Assert.IsFalse(r.Accepted);
        }

        [TestMethod]
        public void Rejected_ObjectMessage_IsSerialisedIntoTheError()
        {
            var r = Parse(@"{""message"":{""mobiles"":""invalid number""},""type"":""error""}");

            Assert.IsFalse(r.Accepted);
            StringAssert.Contains(r.Error!, "invalid number");
        }

        // ─────────────────────────────────────────────────────────────────
        // Degenerate bodies — accept without an id rather than risk a duplicate send
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Malformed_Json_IsAcceptedWithoutARequestId()
        {
            var r = Parse("not json at all");

            Assert.IsTrue(r.Accepted, "a 2xx with an unreadable body may still have been sent");
            Assert.IsNull(r.RequestId);
        }

        [TestMethod]
        public void Empty_Body_IsAcceptedWithoutARequestId()
        {
            var r = Parse("");

            Assert.IsTrue(r.Accepted);
            Assert.IsNull(r.RequestId);
        }

        [TestMethod]
        public void NonObject_Json_IsAcceptedWithoutARequestId()
        {
            var r = Parse("[1,2,3]");

            Assert.IsTrue(r.Accepted);
            Assert.IsNull(r.RequestId);
        }

        [TestMethod]
        public void Accepted_BlankMessage_YieldsNullNotEmptyString()
        {
            var r = Parse(@"{""message"":""   "",""type"":""success""}");

            Assert.IsTrue(r.Accepted);
            Assert.IsNull(r.RequestId, "blank must normalise to null so the column stays honest");
        }

        // ─────────────────────────────────────────────────────────────────
        // Regression: the exact body that produced 173 null ProviderMessageIds
        // ─────────────────────────────────────────────────────────────────

        [TestMethod]
        public void Regression_V5Body_NoLongerLosesTheRequestId()
        {
            // Old code: TryGetProperty("request_id") -> false -> Undefined.GetString() throws
            // -> empty catch -> null. This is the shape every live send returned.
            var r = Parse(@"{""message"":""69e08448cd4818fe270e6b32"",""type"":""success""}");

            Assert.IsNotNull(r.RequestId);
            Assert.AreEqual("69e08448cd4818fe270e6b32", r.RequestId);
        }
    }
}
