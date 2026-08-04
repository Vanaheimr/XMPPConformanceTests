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

using System.Collections.Concurrent;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Ratatoskr;
using org.GraphDefined.Vanaheimr.Ratatoskr.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Ratatoskr.Tests
{

    /// <summary>
    /// XEP-0198 stream management and resumption against a foreign counterpart
    /// - our client as the client.
    /// </summary>
    /// <remarks>
    /// The counting agrees against <see cref="XMPPServer"/>, that is against
    /// our own understanding of what a stanza is. That is precisely the
    /// delicate spot in XEP-0198: section 2 counts <c>message</c>,
    /// <c>presence</c> and <c>iq</c> and nothing else. Everything else -
    /// <c>&lt;enable/&gt;</c>, <c>&lt;r/&gt;</c>, <c>&lt;a/&gt;</c>, SASL
    /// elements, the stream header - does not count. Whoever counts one of them
    /// in never notices it on themselves, but only at a foreign server.
    ///
    /// The same holds for the resumption: against our own server both sides
    /// share an understanding of when a <c>&lt;resume/&gt;</c> may be sent, what
    /// belongs in it and what comes back. A foreign server does not have that
    /// understanding from us.
    ///
    /// The tests stand here and not in the derived classes: they check the same
    /// thing for every counterpart, and what differs - domain, endpoint, port,
    /// accounts, environment variable - the derived classes lay down. A third
    /// server thereby costs twenty lines.
    /// </remarks>
    public abstract class AForeignPeerStreamManagementTests
    {

        #region What makes up the counterpart

        /// <summary>Name of the counterpart - for error messages only.</summary>
        protected abstract String  PeerName      { get; }

        /// <summary>The domain the counterpart serves.</summary>
        protected abstract String  PeerDomain    { get; }

        /// <summary>The WebSocket endpoint (RFC 7395).</summary>
        protected abstract String  Endpoint      { get; }

        /// <summary>The port behind it - for the reachability check.</summary>
        protected abstract Int32   EndpointPort  { get; }

        /// <summary>
        /// The environment variable pointing at the certificate directory of
        /// the setup.
        /// </summary>
        protected abstract String  CertVariable  { get; }

        #endregion

        #region Data

        /// <summary>The account of the client itself.</summary>
        protected const String User      = "alice";

        /// <summary>A second account as the sender.</summary>
        protected const String User2     = "bob";

        // Stays German on purpose: this is the password of the real accounts
        // that tools/prosody/setup.sh and tools/ejabberd/setup.sh create in
        // the WSL setups. Translating it here would lock us out there.
        protected const String Password  = "geheim";

        private readonly List<XMPPClient>  _clients = [];
        private X509Certificate2           _ca = null!;

        #endregion

        #region Setting up / tearing down

        private String CertDirectory
            => Environment.GetEnvironmentVariable(CertVariable) ?? "";

        /// <summary>
        /// Logs a client in, or skips the test.
        /// </summary>
        /// <param name="localPart">Which of the two test accounts.</param>
        /// <param name="reconnect">
        /// How often the client may come back after a tear. Zero for everything
        /// that does not need the reconnect - then nothing is left running in
        /// the background at the end of the test.
        /// </param>
        protected async Task<XMPPClient> ConnectAsync(String  localPart  = User,
                                                       Int32   reconnect  = 0)
        {

            var directory = CertDirectory;

            if (directory.Length == 0 || !File.Exists(Path.Combine(directory, "ca.crt")))
                Assert.Ignore($"No {PeerName} setup: {CertVariable} points at no test CA.");

            if (!PortAnswers())
                Assert.Ignore($"On 127.0.0.1:{EndpointPort} no {PeerName} WebSocket answers.");

            _ca = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(directory, "ca.crt"));

            var connection = new XMPPConnection($"{localPart}@{PeerDomain}", Password, Endpoint) {
                                 KeepaliveEnabled            = false,
                                 MaxReconnectAttempts        = reconnect,
                                 InitialReconnectDelay       = TimeSpan.FromMilliseconds(300),
                                 StreamManagementEnabled     = true,
                                 ServerCertificateValidator  = TrustsTheTestCA
                             };

            var client = new XMPPClient(connection);
            _clients.Add(client);

            await client.ConnectAsync();

            Assert.That(client.StreamManagement, Is.Not.Null,
                        "Without a stream management manager this test has nothing to check.");

            return client;

        }

        [TearDown]
        public async Task CleanUp()
        {

            foreach (var client in _clients)
            {
                try { await client.DisposeAsync(); } catch { /* never mind in the teardown */ }
            }

            _clients.Clear();

        }

        #endregion

        #region Helper functions

        private Boolean PortAnswers()
        {
            try
            {
                using var s = new TcpClient();
                return s.ConnectAsync("127.0.0.1", EndpointPort).Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Accepts exactly the certificates that are signed by the test CA.
        /// </summary>
        /// <remarks>
        /// The name is deliberately not checked: what is dialled is 127.0.0.1,
        /// the certificate reads on the domain of the counterpart. A name could
        /// only be resolved through an entry in <c>/etc/hosts</c>, and that
        /// would need root. The chain is checked in full for that - "accept
        /// everything" would pass against any foreign counterpart too and would
        /// say nothing.
        /// </remarks>
        private Boolean TrustsTheTestCA(Object            sender,
                                       X509Certificate?  certificate,
                                       X509Chain?        chain,
                                       SslPolicyErrors   errors)
        {

            if (certificate is null)
                return false;

            var certificate2 = certificate as X509Certificate2
                                 ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());

            using var check = new X509Chain();

            check.ChainPolicy.TrustMode       = X509ChainTrustMode.CustomRootTrust;
            check.ChainPolicy.RevocationMode  = X509RevocationMode.NoCheck;
            check.ChainPolicy.CustomTrustStore.Add(_ca);

            return check.Build(certificate2);

        }

        private static async Task WaitFor(Func<Boolean> condition, String what)
        {

            var ok = await XMPPServer.WaitUntilAsync(condition, TimeSpan.FromSeconds(15));

            Assert.That(ok, Is.True, $"Timeout while waiting for: {what}");

        }

        /// <summary>
        /// Counts the transitions into <c>Connected</c> - the only moment at
        /// which the setup is demonstrably finished.
        /// </summary>
        /// <remarks>
        /// Waiting for something earlier - that the counterpart has picked the
        /// stream up, say - checks the client in the middle of its setup phase,
        /// in a state it is about to leave again.
        /// </remarks>
        /// <remarks>
        /// Since D33 the <b>way</b> there is recorded as well. The counting
        /// alone sufficed as long as everything went well; if the resumption
        /// failed to come, the message said only "timeout while waiting for: the
        /// resumed stream" — and thereby nothing about how far the client got:
        /// whether it tried at all, how often, and what it was down to.
        ///
        /// That is exactly what happened in D16 — one failure in one of four
        /// full runs, unexplainable, because the message gave nothing away. It
        /// has stood as an open point in the plan ever since.
        /// </remarks>
        private static History CountReconnects(XMPPClient client)
            => new(client);

        /// <summary>
        /// Counts the finished setups and records what happened in between.
        /// </summary>
        /// <remarks>
        /// Twenty targeted runs in D33 could not repeat the failure from D16:
        /// the forty executions all lay between 519 and 669 milliseconds,
        /// against a deadline of 15 seconds. The guess of the time, "tight under
        /// load", therefore does not carry — which is why the deadline stays as
        /// it was.
        ///
        /// What remains is the provision for next time: the history then stands
        /// in the message. <b>A failure that explains itself costs writing once;
        /// one that does not costs an investigation every time.</b>
        /// </remarks>
        protected sealed class History
        {

            private readonly ConcurrentQueue<String> _steps = new();
            private Int32 _connected;

            public History(XMPPClient client)
            {

                client.OnStateChanged += (oldState, newState) =>
                {

                    _steps.Enqueue($"{oldState}->{newState}");

                    if (newState == ConnectionState.Connected)
                        Interlocked.Increment(ref _connected);

                };

                client.OnError += message => _steps.Enqueue($"Error: {message}");

            }

            /// <summary>Has the client finished the setup at least once?</summary>
            public Boolean Reconnected
                => Volatile.Read(ref _connected) > 0;

            public override String ToString()
                => _steps.IsEmpty
                       ? "(nothing happened - the client did not even try)"
                       : String.Join(" | ", _steps);

        }

        /// <summary>
        /// What a connection setup against the counterpart may cost -
        /// negotiation, TLS, SASL, bind and resumption.
        /// </summary>
        /// <remarks>
        /// Generously chosen: the runs measured in D33 lay at around half a
        /// second for the whole procedure. This number covers the <i>failed</i>
        /// attempt as well, which can be far dearer than the successful one - a
        /// counterpart that has not yet noticed the tear does not answer with a
        /// refusal at once.
        /// </remarks>
        private static readonly TimeSpan SetupPerAttempt = TimeSpan.FromSeconds(3);

        /// <summary>
        /// How long this test waits for the resumption - derived from the
        /// client's own reconnection policy.
        /// </summary>
        /// <remarks>
        /// A fixed deadline of 15 seconds stood here, and the test failed at it
        /// once in D16. In D33 measurements were taken in consequence: forty
        /// executions between 519 and 669 ms, and from that it was concluded
        /// that the explanation "tight under load" does not carry.
        ///
        /// <b>The conclusion was wrong, and by arithmetic at that.</b> The
        /// client may come back five times here and waits in between with
        /// doubling: 300, 600, 1200, 2400 and 4800 milliseconds - <b>9.3
        /// seconds</b> of pure waiting on its own. Of the 15 there remained 5.7
        /// seconds for five complete connection setups. Two failed attempts
        /// suffice and the deadline is exceeded, while the client behaves
        /// exactly as it is configured to.
        ///
        /// The forty fast runs do not refute that - they all came through at
        /// the <i>first</i> attempt and say nothing about the case with
        /// repetitions. <b>An average made of nothing but successful runs does
        /// not bound the outlier; it only describes what it looks like when
        /// nothing goes wrong.</b>
        ///
        /// The patience is therefore no longer a guessed number, but the sum of
        /// what the client is allowed to do.
        /// </remarks>
        protected static TimeSpan Patience(XMPPConnection connection)
        {

            var total = TimeSpan.Zero;

            for (var attempt = 1; attempt <= Math.Max(connection.MaxReconnectAttempts, 1); attempt++)
            {

                var wait = Math.Min(
                                 connection.InitialReconnectDelay.TotalMilliseconds * Math.Pow(2, attempt - 1),
                                 connection.MaxReconnectDelay.TotalMilliseconds);

                total += TimeSpan.FromMilliseconds(wait) + SetupPerAttempt;

            }

            return total;

        }

        /// <summary>
        /// Waits for the resumption and names the history when it fails.
        /// </summary>
        private static async Task WaitForResumptionAsync(History history, XMPPClient client)
        {

            var patience  = Patience(client.Connection);

            var ok      = await XMPPServer.WaitUntilAsync(() => history.Reconnected, patience);

            Assert.That(ok, Is.True,
                        $"The stream was not resumed within {patience.TotalSeconds:0.#} seconds " +
                        $"- that is the time the client itself is allowed to take " +
                        $"({client.Connection.MaxReconnectAttempts} attempts, from " +
                        $"{client.Connection.InitialReconnectDelay.TotalMilliseconds:0} ms with " +
                        $"doubling). History: {history}");

        }

        #endregion


        #region TheServerAcceptsOurEnable()

        /// <summary>
        /// The counterpart accepts our <c>&lt;enable/&gt;</c>.
        /// </summary>
        /// <remarks>
        /// The weakest of these tests and not superfluous all the same: it
        /// vouches for our <c>&lt;enable/&gt;</c> standing in the right
        /// namespace (<c>urn:xmpp:sm:3</c>) and at the right place of the setup
        /// - after the bind, before everything further. If it stands wrongly, a
        /// <c>&lt;failed/&gt;</c> comes instead of an <c>&lt;enabled/&gt;</c>.
        /// </remarks>
        [Test]
        public async Task TheServerAcceptsOurEnable()
        {

            var client = await ConnectAsync();

            Assert.That(client.StreamManagement!.IsEnabled, Is.True,
                        $"{PeerName} has not switched stream management on.");

        }

        #endregion

        #region TheServerCountsTheSetupExactlyAsWeDo()

        /// <summary>
        /// After the setup both sides report the same state.
        /// </summary>
        /// <remarks>
        /// The test for whose sake this setup exists. Between the
        /// <c>&lt;enabled/&gt;</c> and this point the client sends carbons, a
        /// roster query and the first presence - and nonzas in between. If we
        /// count one of those in that the counterpart does not, the states
        /// differ here by exactly that one.
        ///
        /// Equality is checked, not merely an empty queue: too large an
        /// <c>h</c> would clear it as well, and a client that counts too few
        /// would get away with it.
        /// </remarks>
        [Test]
        public async Task TheServerCountsTheSetupExactlyAsWeDo()
        {

            var client  = await ConnectAsync();
            var sm      = client.StreamManagement!;

            var ours   = sm.OutboundCount;

            await sm.RequestAckAsync();
            await WaitFor(() => sm.LastAcknowledged == ours,
                           $"an <a/> over {ours} stanzas (last {sm.LastAcknowledged})");

            Assert.Multiple(() =>
            {

                Assert.That(sm.LastAcknowledged, Is.EqualTo(ours),
                            $"{PeerName} counts the setup differently from us.");

                Assert.That(sm.UnackedCount, Is.Zero,
                            "After a complete ack nothing may stay outstanding.");

            });

        }

        #endregion

        #region NonzasDoNotAdvanceTheCount()

        /// <summary>
        /// XEP-0198 section 2: nonzas do not count - on either side.
        /// </summary>
        /// <remarks>
        /// Three messages, an <c>&lt;r/&gt;</c> between each, and the
        /// counterpart answers every one of those with an <c>&lt;a/&gt;</c>. If
        /// one of the two sides counted the nonzas in, the states would drift
        /// apart - and in a way no counter-check against our own server ever
        /// showed, because both sides made the same mistake there.
        ///
        /// <b>The measurement runs against the recording, not against the
        /// intention.</b> It once said here "the state must have risen by
        /// exactly three", and that is exactly what the test fell over once in
        /// D34: Prosody acknowledged six - so exactly the three messages - and
        /// our counter stood at eight. So two stanzas went out that this test
        /// did not send, and after Prosody had acknowledged at that. A client
        /// does that quite rightly: it answers what comes in, and when that
        /// happens is not for the test to decide.
        ///
        /// The statement of section 2 is not a number anyway, but a relation:
        /// <i>the counter rises by the stanzas and by nothing else.</i> That is
        /// what stands there now - three is only the lower bound any more, so
        /// that something is measured at all.
        /// </remarks>
        [Test]
        public async Task NonzasDoNotAdvanceTheCount()
        {

            var client  = await ConnectAsync();
            var sm      = client.StreamManagement!;

            // What actually goes out. Numbers say *that* something is wrong,
            // and never *what* - the same dead end as in D16 and D29. Since D35
            // the recording is there; now it is also the yardstick.
            var outgoing = new ConcurrentQueue<String>();

            client.Connection.OnRawXml += x =>
            {
                if (x.StartsWith(">>> ", StringComparison.Ordinal))
                    outgoing.Enqueue(x[4..]);
            };

            var before  = sm.OutboundCount;

            for (var i = 0; i < 3; i++)
            {

                await client.SendRawAsync(
                          $"<message to='{User}@{PeerDomain}' type='chat' id='count-{i}'>" +
                          $"<body>{i}</body></message>");

                await sm.RequestAckAsync();

            }

            // Up to three attempts: every enquiry acknowledges what went out up
            // to then. If something went out after that - an answer to something
            // that just came in - the next attempt enquires anew. Without that
            // exactly this stanza would stay unacknowledged for ever, and the
            // equality would never come about.
            for (var attempt = 0; attempt < 3; attempt++)
            {

                await sm.RequestAckAsync();

                if (await XMPPServer.WaitUntilAsync(
                              () => sm.LastAcknowledged == sm.OutboundCount &&
                                    sm.OutboundCount    == before + Counted(outgoing) &&
                                    sm.OutboundCount    >= before + 3,
                              TimeSpan.FromSeconds(5)))
                {
                    break;
                }

            }

            var recording = String.Join("\n   ", outgoing);

            Assert.Multiple(() =>
            {

                Assert.That(sm.OutboundCount - before, Is.EqualTo(Counted(outgoing)),
                            "The counter does not match what went out:\n   " +
                            recording);

                Assert.That(sm.LastAcknowledged, Is.EqualTo(sm.OutboundCount),
                            $"{PeerName} counted differently from us. What went out is:\n   " +
                            recording);

                Assert.That(outgoing.Count(f => !IsStanza(f)), Is.GreaterThanOrEqualTo(3),
                            "Without nonzas in the outgoing traffic this test checks nothing:\n   " + recording);

                Assert.That(sm.OutboundCount, Is.GreaterThanOrEqualTo(before + 3),
                            "The three messages have not been counted in:\n   " + recording);

            });

        }

        /// <summary>How many stanzas stand in the recording.</summary>
        private static UInt32 Counted(IEnumerable<String> frames)
            => (UInt32) frames.Count(IsStanza);

        /// <summary>
        /// What XEP-0198, section 2 counts - here once more by hand.
        /// </summary>
        /// <remarks>
        /// Deliberately <b>not</b>
        /// <see cref="StreamManagementManager.IsCountableStanza"/>: that is the
        /// function whose result is being checked here. If the test took it, it
        /// would compare a number with itself and would pass even when it
        /// answers wrongly - the same trap for whose sake the test server counts
        /// independently as well.
        /// </remarks>
        private static Boolean IsStanza(String frame)
            => Regex.IsMatch(frame, @"^\s*<(message|presence|iq)(\s|/|>)");

        #endregion

        #region OurInboundCountIsNotTooHigh()

        /// <summary>
        /// The other direction: our <c>&lt;a h='...'/&gt;</c> does not exceed
        /// what the counterpart has sent.
        /// </summary>
        /// <remarks>
        /// For the incoming direction there is no value the counterpart names to
        /// us - so we cannot compare our counter directly. It does check it,
        /// though: an <c>h</c> greater than the number of stanzas actually sent
        /// is a protocol error and ends the stream.
        ///
        /// The proof therefore runs over the living on: we report our state and
        /// enquire afterwards. If the answer comes, the value was accepted.
        /// Downwards it is not secured by that - too small an <c>h</c> would be
        /// admissible and would not show up here.
        /// </remarks>
        [Test]
        public async Task OurInboundCountIsNotTooHigh()
        {

            var client  = await ConnectAsync();
            var sm      = client.StreamManagement!;

            await sm.SendAckAsync();

            var duration = await client.PingAsync();

            Assert.That(duration, Is.Not.Null,
                        $"{PeerName} did not answer any more after our <a h='{sm.InboundCount}'/> " +
                        "- presumably we counted more than it sent.");

        }

        #endregion

        #region TheServerPromisesToKeepTheStream()

        /// <summary>
        /// The counterpart promises the resumption.
        /// </summary>
        /// <remarks>
        /// If an id arrives here, it has understood our
        /// <c>&lt;enable resume='true'/&gt;</c>.
        /// </remarks>
        [Test]
        public async Task TheServerPromisesToKeepTheStream()
        {

            var alice = await ConnectAsync();

            Assert.Multiple(() =>
            {

                Assert.That(alice.StreamManagement!.CanResume, Is.True,
                            $"{PeerName} has not promised the resumption.");

                Assert.That(alice.StreamManagement.ResumeId, Is.Not.Null.And.Not.Empty);

            });

        }

        #endregion

        #region ThePatienceCoversWhatTheClientMayTake()

        /// <summary>
        /// The patience of this test covers what the client itself is allowed
        /// to take.
        /// </summary>
        /// <remarks>
        /// The only check here that needs no counterpart - and the only one that
        /// can catch the failure from D16: it occurred once and was afterwards
        /// not to be repeated in forty executions. What <b>cannot</b> be brought
        /// about cannot be held by a test that waits for it to occur either.
        ///
        /// It can be recomputed, though: five attempts with 300 milliseconds and
        /// doubling are 300 + 600 + 1200 + 2400 + 4800 = 9.3 seconds of <i>pure
        /// waiting</i>, plus five complete connection setups. A fixed deadline
        /// of 15 seconds left 5.7 seconds for that - and every failed attempt
        /// came off it.
        ///
        /// The numbers stand here by hand and not as a call of the same
        /// computation: otherwise the test would check the formula against
        /// itself.
        /// </remarks>
        [Test]
        public void ThePatienceCoversWhatTheClientMayTake()
        {

            var connection = new XMPPConnection($"{User}@{PeerDomain}", Password, Endpoint)
            {
                MaxReconnectAttempts   = 5,
                InitialReconnectDelay  = TimeSpan.FromMilliseconds(300),
                MaxReconnectDelay      = TimeSpan.FromSeconds(30)
            };

            Assert.That(Patience(connection),
                        Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(9300) +
                                                TimeSpan.FromSeconds(5 * 3)),
                        "The patience falls short of what the client itself is allowed to take - " +
                        "9.3 seconds of waiting between five attempts and the attempts themselves.");

        }

        #endregion

        #region TheStreamSurvivesABrokenConnection()

        /// <summary>
        /// After a tear the client ties on to the same stream instead of
        /// binding a new resource.
        /// </summary>
        /// <remarks>
        /// The connection is torn from <b>our</b> side - against a foreign
        /// counterpart there is no other way, and signing off properly would be
        /// precisely the opposite of what is to be checked here.
        ///
        /// The unchanged id is the proof; the full JID alone is no good, because
        /// the resource is fixed per process and a new bind would give the same
        /// address.
        /// </remarks>
        [Test]
        public async Task TheStreamSurvivesABrokenConnection()
        {

            var alice = await ConnectAsync(reconnect: 5);

            var before   = alice.FullJid;
            var resumeId  = alice.StreamManagement!.ResumeId;

            // Without the resumption promised, the id would be null on both
            // sides - and thereby "unchanged". The comparison below would then
            // say nothing.
            Assert.That(alice.StreamManagement.CanResume, Is.True,
                        $"{PeerName} has not promised the resumption at all.");

            var reconnected = CountReconnects(alice);

            alice.KillConnection();

            await WaitForResumptionAsync(reconnected, alice);

            Assert.Multiple(() =>
            {

                Assert.That(alice.FullJid, Is.EqualTo(before),
                            "A new resource handed out - then a new bind took place.");

                Assert.That(alice.StreamManagement.ResumeId, Is.EqualTo(resumeId),
                            "A new id, so negotiated afresh instead of resumed.");

            });

        }

        #endregion

        #region TheServerHoldsBackWhatArrivedDuringTheOutage()

        /// <summary>
        /// What arrived during the tear the counterpart hands on afterwards.
        /// </summary>
        /// <remarks>
        /// The real gain, and the place where a foreign counterpart says more
        /// than our own: our server buffers because we taught it to.
        ///
        /// <b>That the message arrives does not suffice as proof.</b> A server
        /// delivers it even when the resumption is not attempted at all and the
        /// client binds a new resource - it then simply goes there, and the test
        /// would pass without knowing anything about the resumption. That is
        /// exactly what happened to it at the mutation "never resume". So both
        /// are checked.
        ///
        /// Alice and Bob need no subscription for this - a message goes without
        /// one, only presence does not.
        /// </remarks>
        [Test]
        public async Task TheServerHoldsBackWhatArrivedDuringTheOutage()
        {

            var alice = await ConnectAsync(reconnect: 5);
            var bob   = await ConnectAsync(User2);

            var before  = alice.FullJid;
            var resumeId = alice.StreamManagement!.ResumeId;

            Assert.That(alice.StreamManagement.CanResume, Is.True,
                        $"{PeerName} has not promised the resumption at all.");

            var arrived = new List<String>();
            alice.OnMessage += m => { lock (arrived) arrived.Add(m.Body); };

            var reconnected = CountReconnects(alice);

            // The counterpart knows nothing of the tear yet: what Bob sends now
            // goes into the stream that is being held.
            alice.KillConnection();

            await bob.SendMessageAsync($"{User}@{PeerDomain}", "Sent in the dark");

            await WaitForResumptionAsync(reconnected, alice);

            await WaitFor(() => { lock (arrived) return arrived.Contains("Sent in the dark"); },
                           "the message handed on afterwards");

            Assert.Multiple(() =>
            {

                Assert.That(alice.FullJid, Is.EqualTo(before),
                            "The message arrived, but at a newly bound resource - " +
                            "then this test does not check the resumption.");

                Assert.That(alice.StreamManagement.ResumeId, Is.EqualTo(resumeId));

            });

        }

        #endregion

    }

}
