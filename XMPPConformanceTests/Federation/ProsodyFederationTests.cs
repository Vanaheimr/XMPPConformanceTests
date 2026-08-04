/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Ratatoskr <https://www.github.com/Vanaheimr/Ratatoskr>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using NUnit.Framework;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// Federation against Prosody - a foreign, fully grown far end.
    /// </summary>
    /// <remarks>
    /// These tests skip themselves when no Prosody answers on 15269. The
    /// set-up stands in <c>tools/prosody/</c>: Prosody is unpacked in WSL
    /// without root, gets a certificate signed by a test CA and listens on
    /// 127.0.0.1:15269. The same CA signs our certificate - with that
    /// SASL-EXTERNAL carries (XEP-0178), and dialback is not needed.
    ///
    /// The mechanics of the set-up stand in
    /// <see cref="AForeignPeerFederationTests"/>; what stands here is only what
    /// is peculiar to Prosody.
    /// </remarks>
    [TestFixture]
    [Category("Prosody")]
    public class ProsodyFederationTests : AForeignPeerFederationTests
    {

        #region Data

        protected override String  PeerName      => "Prosody";
        protected override String  PeerDomain    => "prosody.test";
        protected override Int32   PeerPort      => 15269;
        protected override String  CertVariable  => "JABBER_PROSODY_CERTS";

        /// <summary>
        /// 5269 - the port Prosody falls back on without an SRV record. Prosody
        /// knows no switch for it, so in the inbound run Prosody itself moves
        /// aside to 15269 and leaves this one to us.
        /// </summary>
        protected override Int32   InboundPort   => 5269;

        #endregion


        #region TheStreamToProsodyCarriesAStanza()

        /// <summary>
        /// The outbound path against a foreign far end: STARTTLS,
        /// SASL-EXTERNAL, a stanza out.
        /// </summary>
        /// <remarks>
        /// A <c>true</c> from <c>DeliverAsync</c> means more here than with a
        /// run against our own far end: Prosody has taken the STARTTLS set-up,
        /// checked our certificate against its CA, offered <c>EXTERNAL</c>,
        /// derived our identity from it and cleared the stream. Every one of
        /// these steps was so far only checked against our own understanding of
        /// it.
        /// </remarks>
        [Test]
        public async Task TheStreamToProsodyCarriesAStanza()
        {

            BuildUp();

            var arrived = await Links!.DeliverAsync(
                              PeerDomain,
                                 $"<message from='alice@{LocalDomain}' to='{PeerDomain}' type='chat'>" +
                                 "<body>Hello Prosody</body></message>",
                                 CancellationToken.None);

            Assert.That(arrived, Is.True,
                        "The stream to Prosody did not come about.");

        }

        #endregion

        #region APingOverABidirectionalStream()

        /// <summary>
        /// The same with XEP-0288: the answer takes the connection the question
        /// came over.
        /// </summary>
        /// <remarks>
        /// The test for whose sake the Prosody set-up exists. The return
        /// direction is otherwise only checked against our own far end - and a
        /// negotiation in which both sides have the same idea of the extension
        /// establishes nothing about the extension.
        ///
        /// Prosody announces <c>urn:xmpp:features:bidi</c> as soon as
        /// <c>mod_s2s_bidi</c> runs; <c>tools/prosody/setup.sh</c> switches it
        /// on. If the answer arrives, our <c>&lt;bidi/&gt;</c> stood in the
        /// right form, in the right namespace and at the right place of the
        /// handshake.
        /// </remarks>
        [Test]
        public async Task APingOverABidirectionalStream()
        {

            BuildUp(bidi: true);

            var alice = await AliceAsync();

            var duration = await alice.PingAsync(PeerDomain);

            Assert.That(duration, Is.Not.Null,
                        "Prosody did not answer the ping over the return direction.");

        }

        #endregion

        #region ProsodyDialsUsAndTheAnswerArrives()

        /// <summary>
        /// The inbound path: Prosody builds the connection up, we accept.
        /// </summary>
        /// <remarks>
        /// Up to here our accepting side never stood before a foreign far end.
        /// What is checked here for the first time is our stream header as the
        /// answering one, our feature announcement, our acceptance of a foreign
        /// <c>&lt;auth mechanism='EXTERNAL'/&gt;</c> and the identity check
        /// from the presented certificate. The way back from S9 did run in the
        /// inbound direction, but over a stream <i>we</i> had built up.
        ///
        /// Without XEP-0288, and that is on purpose: precisely then Prosody
        /// answers the ping over a connection of its own to us, and that one
        /// our listener has to accept. With bidi the answer would come over the
        /// existing stream, and the inbound path would stay unchecked again.
        ///
        /// <b>This test runs only inside WSL.</b> From Windows Prosody does not
        /// reach us - the Hyper-V firewall discards every connection from WSL
        /// to the host, and to change that would mean setting a firewall rule.
        /// In the same net everything is loopback.
        /// </remarks>
        [Test]
        public async Task ProsodyDialsUsAndTheAnswerArrives()
        {

            if (!OperatingSystem.IsLinux())
                Assert.Ignore("Only inside WSL: from Windows Prosody does not reach this server.");

            BuildUp(reachable: true);

            var alice = await AliceAsync();

            var duration = await alice.PingAsync(PeerDomain);

            Assert.Multiple(() =>
            {

                Assert.That(duration, Is.Not.Null,
                            "Prosody did not answer the ping.");

                Assert.That(Links!.InboundConnectionCount, Is.GreaterThan(0),
                            "The answer came, but not over an inbound connection - " +
                            "then this test does not check what it is supposed to check.");

                Assert.That(Links.BidirectionalDeliveryCount, Is.Zero,
                            "Set-up of the test: no return direction is supposed to be in play here.");

                // And the proof that Prosody identified itself over its
                // certificate and not over dialback: otherwise *we* would have
                // had to query back.
                Assert.That(Links.DialbackVerificationCount, Is.Zero,
                            "SASL-EXTERNAL is supposed to carry here, not dialback.");

            });

        }

        #endregion

        #region DialbackCarriesBothDirections()

        /// <summary>
        /// XEP-0220 against a foreign far end - in both roles.
        /// </summary>
        /// <remarks>
        /// Dialback was until recently the only procedure that was checked
        /// against our own far end alone. A ping round trip exercises both
        /// roles at once, because every direction builds its own connection up
        /// and every building side has to identify itself:
        ///
        /// <list type="number">
        ///   <item>
        ///     We dial and send <c>&lt;db:result/&gt;</c>. Prosody thereupon
        ///     queries the authoritative server of our domain - that is us
        ///     again, on 5269. Here our <b>authoritative</b> role answers a
        ///     foreign far end.
        ///   </item>
        ///   <item>
        ///     Prosody dials in order to deliver the answer, and sends
        ///     <c>&lt;db:result/&gt;</c> in turn. We query
        ///     <c>prosody.test</c>. Here our <b>checking</b> role works against
        ///     a foreign far end.
        ///   </item>
        /// </list>
        ///
        /// That the ping arrives establishes both: had Prosody's query to us
        /// failed, it would not take our stanza; had our query to Prosody
        /// failed, we would not take its answer.
        /// <c>DialbackVerificationCount</c> holds the second role fast on top -
        /// without it the test would pass even if we had let somebody through
        /// unchecked.
        /// </remarks>
        [Test]
        public async Task DialbackCarriesBothDirections()
        {

            if (!OperatingSystem.IsLinux())
                Assert.Ignore("Only inside WSL: Prosody's query does not reach this server otherwise.");

            BuildUp(reachable: true, dialback: true);

            var alice = await AliceAsync();

            var duration = await alice.PingAsync(PeerDomain);

            Assert.Multiple(() =>
            {

                Assert.That(duration, Is.Not.Null,
                            "Prosody did not answer the ping - one of the two " +
                            "queries has failed.");

                Assert.That(Links!.DialbackVerificationCount, Is.GreaterThan(0),
                            "We never queried Prosody's key.");

                Assert.That(Links.InboundConnectionCount, Is.GreaterThan(0),
                            "Without an inbound connection there was nothing to check either.");

            });

        }

        #endregion

    }

}
