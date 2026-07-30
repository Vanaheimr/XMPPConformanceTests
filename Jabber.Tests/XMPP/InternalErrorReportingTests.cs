/*
 * Copyright (c) 2010-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Hermod <https://www.github.com/Vanaheimr/Hermod>
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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Scheitert das Verarbeiten eines Frames, wird es gemeldet statt
    /// verschluckt.
    /// </summary>
    /// <remarks>
    /// Um das Verarbeiten eines Frames stand ein <c>catch</c> ohne Filter, mit
    /// dem Vermerk „Verbindung abgerissen - im Test der Normalfall". Eine
    /// Messung über die gesamte Sammlung fing dort <b>keine einzige</b>
    /// Ausnahme: Der Normalfall war er längst nicht mehr. Was er noch leistete,
    /// war das lautlose Verschlucken von Programmierfehlern — in D15 überlebte
    /// eine Mutation nur deshalb, weil ihre <c>NullReferenceException</c> dort
    /// verschwand.
    ///
    /// Diese Sammlung prüft den Meldeweg selbst. Alles andere prüft ihn
    /// nebenbei: Die Wache hängt an jedem Test, und jede Meldung lässt ihn
    /// scheitern.
    /// </remarks>
    [TestFixture]
    public class InternalErrorReportingTests : AXMPPTests
    {

        #region AFailureWhileHandlingAFrame_IsReported()

        /// <summary>
        /// Der Kern: Eine Ausnahme beim Verarbeiten eines Frames wird gemeldet,
        /// mit Ausnahme <b>und</b> Frame.
        /// </summary>
        /// <remarks>
        /// Der Frame gehört dazu und ist nicht Zierde. Eine Meldung, die nur
        /// „NullReferenceException" sagt, nützt bei einem Server, der tausend
        /// Frames verarbeitet, fast nichts; erst mit der Stanza in der Hand ist
        /// der Weg nachvollziehbar, der dorthin geführt hat.
        /// </remarks>
        [Test]
        public async Task AFailureWhileHandlingAFrame_IsReported()
        {

            ExpectInternalErrors();

            var alice = await ConnectClientAsync("alice");

            var gemeldet = new ConcurrentQueue<(String Frame, Exception Error)>();
            Server.OnInternalError += (session, frame, e) => gemeldet.Enqueue((frame, e));

            // Erst jetzt, sonst käme der Client nicht einmal durch die
            // Aufbauphase.
            Server.FailFrameHandling = true;

            await alice.SendRawAsync("<message to='bob@localhost' id='ausloeser'><body>Hallo</body></message>");

            await WaitFor(() => !gemeldet.IsEmpty, "die Meldung des internen Fehlers");

            gemeldet.TryDequeue(out var meldung);

            Assert.Multiple(() =>
            {

                Assert.That(meldung.Error, Is.TypeOf<InvalidOperationException>());

                Assert.That(meldung.Frame, Does.Contain("ausloeser"),
                            "Die Meldung muss den Frame nennen, bei dem es schiefging.");

                Assert.That(InternalErrors, Is.Not.Empty,
                            "Und die Wache der Testbasis muss dasselbe sehen.");

            });

        }

        #endregion

        #region TheConnectionSurvivesAReportedFailure()

        /// <summary>
        /// Nach der Meldung geht es weiter: Der Server wirft die Ausnahme nicht
        /// noch einmal und reisst den Stream nicht ab.
        /// </summary>
        /// <remarks>
        /// Das ist die Zusage, die den Umbau ungefährlich macht. Am Verhalten
        /// des Servers ändert sich nichts — er verarbeitet den nächsten Frame wie
        /// zuvor; nur erfährt jetzt jemand von dem Fehlschlag. Ohne diesen Test
        /// bliebe offen, ob die Meldung nicht doch etwas mitreisst, und
        /// „gemeldet statt verschluckt" wäre ein Versprechen ohne Deckung.
        /// </remarks>
        [Test]
        public async Task TheConnectionSurvivesAReportedFailure()
        {

            ExpectInternalErrors();

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var eingang = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += m => eingang.Enqueue(m);

            Server.FailFrameHandling = true;

            await alice.SendRawAsync($"<message to='{bob.FullJid}' type='chat' id='faellt-aus'>" +
                                     "<body>Geht schief</body></message>");

            await WaitFor(() => InternalErrors.Count > 0, "die Meldung");

            // Und nun wieder im Normalbetrieb.
            Server.FailFrameHandling = false;

            await alice.SendRawAsync($"<message to='{bob.FullJid}' type='chat' id='geht-wieder'>" +
                                     "<body>Und jetzt wieder</body></message>");

            await WaitFor(() => eingang.Any(m => m.MessageId == "geht-wieder"),
                          "die Zustellung nach dem Fehlschlag");

            Assert.That(eingang.Any(m => m.MessageId == "faellt-aus"), Is.False,
                        "Der gescheiterte Frame darf nicht doch noch zugestellt werden.");

        }

        #endregion

        #region ASecondServer_IsWatchedThroughWatched()

        /// <summary>
        /// <c>Watched</c> stellt auch einen zweiten Server unter dieselbe Wache -
        /// und gibt ihn zurück.
        /// </summary>
        /// <remarks>
        /// Elf Fixtures betreiben eigene Server und verdrahten sie über diesen
        /// einen Weg. Wäre er ein Durchreicher, der nichts anhängt, wären sie
        /// alle unbewacht, und keiner der übrigen Tests würde es merken: Wo kein
        /// Fehler auftritt, sieht eine fehlende Wache wie eine wirksame aus -
        /// dieselbe Falle wie beim alten <c>catch</c>.
        ///
        /// Geprüft wird deshalb am echten Weg und nicht bloss die Rückgabe: Der
        /// zweite Server bekommt einen Client, scheitert absichtlich, und die
        /// Meldung muss bei der Wache dieses Tests ankommen.
        /// </remarks>
        [Test]
        public async Task ASecondServer_IsWatchedThroughWatched()
        {

            ExpectInternalErrors();

            var roh = new XMPPServer("zweiter.example");

            await using var zweiter = Watched(roh);

            Assert.That(zweiter, Is.SameAs(roh),
                        "Watched muss denselben Server zurückgeben - sonst zeigt die " +
                        "Wache auf einen anderen als der Test benutzt.");

            zweiter.Start();
            zweiter.AddAccount("carol");

            var verbindung = new XMPPConnection($"carol@{zweiter.Domain}", "pw", zweiter.Uri)
            {
                KeepaliveEnabled            = false,
                MaxReconnectAttempts        = 0,
                ServerCertificateValidator  = zweiter.IsOwnCertificate
            };

            await using var carol = new XMPPClient(verbindung);
            await carol.ConnectAsync();

            Assert.That(zweiter, Is.SameAs(zweiter), "Watched muss den Server zurückgeben.");

            zweiter.FailFrameHandling = true;

            await carol.SendRawAsync("<message to='dave@zweiter.example' id='am-zweiten'/>");

            await WaitFor(() => InternalErrors.Any(e => e.Contains("am-zweiten")),
                          "die Meldung des zweiten Servers bei derselben Wache");

        }

        #endregion

        #region TheGuardItselfFailsAndForgivesAsItShould()

        /// <summary>
        /// Die Wache selbst: Sie schweigt, solange nichts gemeldet ist, lässt
        /// scheitern, sobald etwas gemeldet ist, und verzeiht nur, wenn man sie
        /// darum bittet.
        /// </summary>
        /// <remarks>
        /// Ein Wächter, den nichts auslöst, ist selbst unbewacht — dieselbe
        /// Falle, die den alten <c>catch</c> so lange gedeckt hat, nur eine
        /// Ebene höher. Die Mutation „gib immer frei" überlebte jeden anderen
        /// Test: Wo kein Fehler gemeldet wird, verhält sich eine wirkungslose
        /// Wache genau wie eine wirksame, und ein Test, der scheitern <i>muss</i>,
        /// lässt sich nicht als bestehender Test schreiben.
        ///
        /// Deshalb wird hier nicht der Weg über den Server genommen, sondern die
        /// Wache unmittelbar befragt. Das ist die einzige Stelle der Sammlung,
        /// an der ein <c>Assert</c> geprüft wird, statt zu prüfen.
        /// </remarks>
        [Test]
        public void TheGuardItselfFailsAndForgivesAsItShould()
        {

            var wache = new InternalErrorGuard();

            Assert.Multiple(() =>
            {

                Assert.DoesNotThrow(wache.AssertClean,
                                    "Ohne Meldung darf sie nicht scheitern.");

                wache.Record("NullReferenceException: Objektverweis", "<message id='x'/>");

                var fehlschlag = Assert.Throws<AssertionException>(wache.AssertClean,
                                     "Mit Meldung muss sie scheitern.");

                Assert.That(fehlschlag!.Message, Does.Contain("NullReferenceException"),
                            "Und dabei sagen, was gemeldet wurde.");

                Assert.That(fehlschlag.Message, Does.Contain("<message id='x'/>"),
                            "Samt dem Frame, bei dem es schiefging.");

                wache.Expect();

                Assert.DoesNotThrow(wache.AssertClean,
                                    "Wer sie um Nachsicht bittet, bekommt sie.");

                wache.Reset();

                Assert.That(wache.Errors, Is.Empty,
                            "Und der nächste Test beginnt mit leerer Liste.");

            });

        }

        #endregion

        #region AGreenRunReportsNothing()

        /// <summary>
        /// Die Gegenprobe, und die wichtigste: Ein gewöhnlicher Ablauf meldet
        /// nichts.
        /// </summary>
        /// <remarks>
        /// Sie ist der Grund, warum die Wache an jedem Test hängen darf, ohne
        /// die Sammlung unbrauchbar zu machen. Meldete der normale Betrieb
        /// laufend etwas — abgerissene Verbindungen etwa, wie der alte Kommentar
        /// behauptete —, wäre eine Wache, die darauf scheitert, nicht zu
        /// gebrauchen, und man müsste doch filtern.
        ///
        /// Der Test steht hier ausdrücklich und nicht bloss implizit in den
        /// übrigen: Was er behauptet, ist eine Aussage über den Server und nicht
        /// über diesen einen Ablauf. Und er hält fest, dass die Messung, die
        /// diesen Umbau begründet hat, kein Zufall eines Laufs war.
        /// </remarks>
        [Test]
        public async Task AGreenRunReportsNothing()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            MakeContacts("alice", "bob");

            var eingang = new ConcurrentQueue<XMPPMessage>();
            bob.OnMessage += m => eingang.Enqueue(m);

            await alice.SendMessageAsync(bob.BareJid, "Ein ganz gewöhnlicher Ablauf");
            await WaitFor(() => !eingang.IsEmpty, "die Nachricht");

            // Ein Abriss - genau das, was der alte Kommentar für den Normalfall
            // hielt. Die Sitzung bleibt dabei in der Liste: Ein Stream mit
            // zugesagter Wiederaufnahme wird aufgehoben und nicht abgemeldet
            // (XEP-0198, Abschnitt 5). Gewartet wird deshalb darauf, dass die
            // Verbindung weg ist, nicht die Sitzung.
            Server.KillSessionsOf(bob.BareJid);

            await WaitFor(() => Server.SessionsOf(bob.BareJid).All(s => !s.IsOpen),
                          "das Ende der Verbindung");

            await alice.SendMessageAsync(bob.BareJid, "Und noch eine, ins Leere");

            Assert.That(InternalErrors, Is.Empty,
                        "Auch ein Abriss ist kein interner Fehler.");

        }

        #endregion

    }

}
