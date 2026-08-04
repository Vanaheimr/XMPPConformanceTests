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
    /// Federation against ejabberd - the second foreign far end.
    /// </summary>
    /// <remarks>
    /// Why a second one when there is one already: Prosody alone establishes
    /// that we can manage with Prosody. Where our understanding of the protocol
    /// departs from the norm but Prosody goes along with the same departure -
    /// be it out of leniency, be it out of the same reading -, that does not
    /// stand out. Only a second, independently grown implementation separates
    /// "right" from "congruent with one understanding".
    ///
    /// ejabberd is the interesting opponent for that: written in Erlang, a
    /// different history, a different circle of authors, its own preferences in
    /// the handshake. What carries against both probably carries.
    ///
    /// The set-up stands in <c>tools/ejabberd/setup.sh</c>, the mechanics in
    /// <see cref="AForeignPeerFederationTests"/>. The tests skip themselves
    /// when no ejabberd answers on 25269.
    /// </remarks>
    [TestFixture]
    [Category("ejabberd")]
    public class EjabberdFederationTests : AForeignPeerFederationTests
    {

        #region Data

        protected override String  PeerName      => "ejabberd";
        protected override String  PeerDomain    => "ejabberd.test";
        protected override Int32   PeerPort      => 25269;
        protected override String  CertVariable  => "JABBER_EJABBERD_CERTS";

        /// <summary>
        /// 5270, not 5269 - so that both far ends can run next to each other
        /// and no run hangs on the wrong one by accident.
        /// </summary>
        /// <remarks>
        /// The port is freely choosable only because ejabberd has an express
        /// switch for it: without an SRV record it takes
        /// <c>outgoing_s2s_port</c>. Prosody knows none and stays fixed at
        /// 5269 - that is where the Prosody set-up listens.
        /// </remarks>
        protected override Int32   InboundPort   => 5270;

        #endregion


        #region TheStreamToEjabberdCarriesAStanza()

        /// <summary>
        /// The outbound path against the second far end: STARTTLS,
        /// SASL-EXTERNAL, a stanza out.
        /// </summary>
        /// <remarks>
        /// The same path as against Prosody, but against a checker that has
        /// read it independently of it: our stream header, our STARTTLS set-up,
        /// our client certificate, our
        /// <c>&lt;auth mechanism='EXTERNAL'/&gt;</c> and the restart of the
        /// stream afterwards. If the stanza gets through, neither of the two
        /// understandings had anything to object to.
        ///
        /// Why an <c>iq</c> of type <c>result</c> and no message: a message to
        /// the bare domain has no recipient there and is turned away. For the
        /// turning away ejabberd lays down an outbound connection to
        /// <c>jabber.test</c> - and that one outlives this test, because our
        /// server lay on an ephemeral port that does not exist any more after
        /// the tear-down. The next test then gets it presented out of the cache
        /// and loses its answer in it. A <c>result</c> may never be answered
        /// according to RFC 6120, section 8.3.1, and therefore leaves nothing
        /// behind.
        /// </remarks>
        [Test]
        public async Task TheStreamToEjabberdCarriesAStanza()
        {

            BuildUp();

            var arrived = await Links!.DeliverAsync(
                              PeerDomain,
                                 $"<iq from='alice@{LocalDomain}' to='{PeerDomain}' " +
                                 "type='result' id='hello-ejabberd'/>",
                                 CancellationToken.None);

            Assert.That(arrived, Is.True,
                        "The stream to ejabberd did not come about.");

        }

        #endregion

        #region APingOverABidirectionalStream()

        /// <summary>
        /// XEP-0288 against the second far end.
        /// </summary>
        /// <remarks>
        /// The test with the greatest yield, because the extension is the
        /// youngest and the least settled one: it prescribes <i>where</i> in
        /// the handshake <c>&lt;bidi/&gt;</c> has to stand - after STARTTLS,
        /// before SASL and before dialback -, and a far end that would still
        /// let it through at another place would cover up an error at our end.
        ///
        /// ejabberd announces <c>urn:xmpp:features:bidi</c> as soon as
        /// <c>mod_s2s_bidi</c> is loaded; <c>tools/ejabberd/setup.sh</c>
        /// switches it on.
        /// </remarks>
        [Test]
        public async Task APingOverABidirectionalStream()
        {

            BuildUp(bidi: true);

            var alice = await AliceAsync();

            var duration = await alice.PingAsync(PeerDomain);

            Assert.That(duration, Is.Not.Null,
                        "ejabberd did not answer the ping over the return direction.");

        }

        #endregion

        #region EjabberdDialsUsAndTheAnswerArrives()

        /// <summary>
        /// The inbound path: ejabberd builds the connection up, we accept.
        /// </summary>
        /// <remarks>
        /// What is checked is our accepting side - stream header as the
        /// answering one, feature announcement, acceptance of a foreign
        /// <c>&lt;auth mechanism='EXTERNAL'/&gt;</c>, identity check from the
        /// presented certificate -, this time before a second counterpart.
        ///
        /// Without XEP-0288, and that is on purpose: precisely then ejabberd
        /// answers the ping over a connection of its own to us, and that one
        /// our listener has to accept. With bidi the answer would come over the
        /// existing stream, and the inbound path would stay unchecked.
        ///
        /// <b>This test runs only inside WSL.</b> From Windows ejabberd does
        /// not reach us - the Hyper-V firewall discards every connection from
        /// WSL to the host, and to change that would mean setting a firewall
        /// rule. In the same net everything is loopback.
        /// </remarks>
        [Test]
        public async Task EjabberdDialsUsAndTheAnswerArrives()
        {

            if (!OperatingSystem.IsLinux())
                Assert.Ignore("Only inside WSL: from Windows ejabberd does not reach this server.");

            BuildUp(reachable: true);

            var alice = await AliceAsync();

            var duration = await alice.PingAsync(PeerDomain);

            Assert.Multiple(() =>
            {

                Assert.That(duration, Is.Not.Null,
                            "ejabberd did not answer the ping.");

                Assert.That(Links!.InboundConnectionCount, Is.GreaterThan(0),
                            "The answer came, but not over an inbound connection - " +
                            "then this test does not check what it is supposed to check.");

                Assert.That(Links.BidirectionalDeliveryCount, Is.Zero,
                            "Set-up of the test: no return direction is supposed to be in play here.");

                Assert.That(Links.DialbackVerificationCount, Is.Zero,
                            "SASL-EXTERNAL is supposed to carry here, not dialback.");

            });

        }

        #endregion

        #region DialbackCarriesBothDirections()

        /// <summary>
        /// XEP-0220 against the second far end - in both roles.
        /// </summary>
        /// <remarks>
        /// A ping round trip exercises both roles at once, because every
        /// direction builds its own connection up and every building side has
        /// to identify itself: our <b>authoritative</b> role answers ejabberd's
        /// query after our key, our <b>checking</b> role queries
        /// <c>ejabberd.test</c> after its own.
        ///
        /// That ejabberd permits dialback at all hangs on one line of the
        /// configuration: <c>s2s_use_starttls: required</c> instead of
        /// <c>required_trusted</c>. The second demands a valid certificate
        /// chain on top and would rule dialback out. Which procedure comes into
        /// play is thereby decided by our side - if we present no client
        /// certificate, only dialback is left.
        /// </remarks>
        [Test]
        public async Task DialbackCarriesBothDirections()
        {

            if (!OperatingSystem.IsLinux())
                Assert.Ignore("Only inside WSL: ejabberd's query does not reach this server otherwise.");

            BuildUp(reachable: true, dialback: true);

            var alice = await AliceAsync();

            var duration = await alice.PingAsync(PeerDomain);

            Assert.Multiple(() =>
            {

                Assert.That(duration, Is.Not.Null,
                            "ejabberd did not answer the ping - one of the two " +
                            "queries has failed.");

                Assert.That(Links!.DialbackVerificationCount, Is.GreaterThan(0),
                            "We never queried ejabberd's key.");

                Assert.That(Links.InboundConnectionCount, Is.GreaterThan(0),
                            "Without an inbound connection there was nothing to check either.");

            });

        }

        #endregion

    }

}
