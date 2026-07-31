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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die Wache über alle Server: Sie findet auch den, den niemand
    /// angemeldet hat.
    /// </summary>
    /// <remarks>
    /// Der Fall, um den es geht, ist nicht der Fehler, sondern das
    /// <b>Vergessen</b>: Jemand schreibt ein neues Fixture, erzeugt einen
    /// Server ohne <c>Watched(…)</c> — und ab da verschluckt dieser Server
    /// Programmierfehler wieder lautlos, ohne dass irgendetwas rot wird.
    /// Genau deshalb hielt kein Test die Verdrahtung; gesichert war sie durch
    /// eine Quelltextprüfung von Hand (siehe D19).
    ///
    /// Diese Tests erzeugen den vergessenen Server mit Absicht. Sie sind die
    /// einzige Stelle der Sammlung, an der ein <c>new XMPPServer(…)</c>
    /// <b>ohne</b> <c>Watched(…)</c> richtig ist.
    /// </remarks>
    [TestFixture]
    public class GlobalErrorWatchTests : AXMPPTests
    {

        #region AnUnwatchedServer_IsStillSeen()

        /// <summary>
        /// Ein Server, den kein Fixture angemeldet hat, meldet trotzdem.
        /// </summary>
        /// <remarks>
        /// <c>ExpectInternalErrors()</c> steht hier nicht, weil der Fehler
        /// erwartet <i>wäre</i>, sondern weil er es <b>ist</b>: Ohne diese
        /// Zeile liesse die Wache diesen Test scheitern - und das ist der
        /// Nachweis, den er führt.
        /// </remarks>
        [Test]
        public async Task AnUnwatchedServer_IsStillSeen()
        {

            ExpectInternalErrors();

            // Ohne Watched(…) - der Fall, den es zu erwischen gilt.
            await using var vergessen = new XMPPServer("vergessen.example");

            vergessen.Start();
            vergessen.FailFrameHandling = true;

            var client = new XMPPClient(
                             new XMPPConnection($"alice@{vergessen.Domain}", "pw", vergessen.Uri)
                             {
                                 MaxReconnectAttempts        = 0,
                                 KeepaliveEnabled            = false,
                                 ServerCertificateValidator  = vergessen.IsOwnCertificate
                             });

            vergessen.AddAccount("alice");

            // Der Aufbau scheitert, denn schon der erste Rahmen fliegt dem
            // Server um die Ohren. Genau das ist der Zweck.
            try { await client.ConnectAsync(); }
            catch { /* erwartet */ }

            await WaitFor(() => GlobalErrorWatchAttribute.Errors.Count > 0,
                          "die Meldung der Wache über alle Server");

            Assert.That(GlobalErrorWatchAttribute.Errors[0],
                        Does.Contain("FailFrameHandling"),
                        "Gemeldet wird die Ausnahme samt Grund.");

            await client.DisposeAsync();

        }

        #endregion

        #region TheWatchFailsTheTest_AndStartsTheNextOneClean()

        /// <summary>
        /// Die Wache lässt tatsächlich scheitern — und beginnt den nächsten
        /// Test wieder mit leeren Händen.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test wäre die schlimmste Fassung eine bestandene: eine
        /// Wache, die alles aufnimmt und nie etwas daraus macht. Sie sähe aus
        /// wie eine Sicherung, wäre keine, und die ganze Sammlung bliebe grün —
        /// genau dieselbe Falle, die
        /// <see cref="InternalErrorGuard.Record"/> für die Wache je Fixture
        /// entschärft.
        ///
        /// Der zweite Teil gehört dazu, weil er sonst von der Reihenfolge der
        /// Tests abhinge: Bliebe eine Meldung über das Testende hinaus stehen,
        /// fiele das nur dem <i>nachfolgenden</i> Test auf — und welcher das
        /// ist, entscheidet der Testläufer. Hier wird der Übergang selbst
        /// nachgestellt: melden, scheitern lassen, den nächsten Test beginnen,
        /// nachsehen.
        /// </remarks>
        [Test]
        public void TheWatchFailsTheTest_AndStartsTheNextOneClean()
        {

            var wache = new GlobalErrorWatchAttribute();

            GlobalErrorWatchAttribute.Record("Erfunden: NullReferenceException im Zustellweg");

            Assert.That(GlobalErrorWatchAttribute.Errors, Is.Not.Empty);

            Assert.That(() => wache.AfterTest(null!),
                        Throws.InstanceOf<AssertionException>(),
                        "Eine Wache, die nur aufnimmt und nie etwas daraus macht, ist keine.");

            // Der nächste Test beginnt - und findet nichts mehr vor. Damit ist
            // zugleich der echte Durchgang am Ende dieses Tests wieder still.
            wache.BeforeTest(null!);

            Assert.That(GlobalErrorWatchAttribute.Errors, Is.Empty,
                        "Eine Meldung, die stehenbleibt, lässt den nächsten Test scheitern.");

        }

        #endregion

        #region AWatchedServerWithoutErrors_KeepsTheWatchSilent()

        /// <summary>
        /// Und die Gegenprobe: Ein gewöhnlicher Test lässt sie schweigen.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test wäre eine Wache, die <i>immer</i> meldet, eine
        /// bestandene Lösung - und die ganze Sammlung rot. Dass sie es nicht
        /// ist, prüft zwar jeder andere Test mit, aber nur als Nebenwirkung;
        /// hier steht es als Zusicherung.
        ///
        /// Die Zusicherung gilt zugleich der Trennung zwischen den Tests: Was
        /// der vorige gemeldet hat, muss zu Beginn dieses hier fort sein,
        /// sonst schlüge er fehl.
        /// </remarks>
        [Test]
        public async Task AWatchedServerWithoutErrors_KeepsTheWatchSilent()
        {

            var alice = await ConnectClientAsync();

            await alice.SendMessageAsync($"alice@{Server.Domain}", "An mich selbst");

            await WaitAgainst(() => GlobalErrorWatchAttribute.Errors.Count > 0,
                              "eine Meldung, obwohl nichts schiefgegangen ist");

        }

        #endregion

    }

}
