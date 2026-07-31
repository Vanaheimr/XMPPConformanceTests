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
using System.Diagnostics;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die Aushandlung wartet nicht ewig: Bleibt eine Antwort aus, scheitert
    /// der Verbindungsaufbau mit einer Meldung statt zu hängen.
    /// </summary>
    /// <remarks>
    /// Der Fall ist an fünf Mutationen aufgefallen, verteilt über D25 bis D29,
    /// und jedes Mal auf dieselbe Weise: Der Lauf hing, statt zu scheitern —
    /// ein Ergebnis, das keines ist. Fünfmal derselbe Befund aus fünf
    /// Richtungen ist keine Beobachtung mehr, sondern eine Eigenschaft.
    ///
    /// **Und der Vermerk dazu war im Detail falsch.** Er lautete „ConnectAsync
    /// wartet ohne eigene Frist auf die Antwort zum Resource Binding". Das
    /// Binding hat sehr wohl eine Frist — <c>SendIqAsync</c> setzt sie seit
    /// jeher. Ohne Frist waren die <b>Lese-Schritte</b> der Aushandlung: Der
    /// Stream-Kopf, die Features, jede SASL-Runde gehen über
    /// <c>ReceiveStanzaAsync</c>, und das wartete allein auf dem Token des
    /// Aufrufers. Ein aus dem Kopf geschriebener Vermerk ist eben keine
    /// Bestandsaufnahme — dieselbe Lehre wie in D19 und D23, diesmal an einer
    /// Diagnose statt an einer Liste.
    ///
    /// Was ein Fehlschlag nicht herstellt, ist Schweigen: Ein Fehler kommt an,
    /// ein geschlossener Socket kommt an. Deshalb der Schalter
    /// <see cref="XMPPServer.AnswerStreamOpen"/> — eine Gegenstelle, die die
    /// Verbindung annimmt und dann nichts mehr sagt.
    /// </remarks>
    [TestFixture]
    public class NegotiationTimeoutTests : AXMPPTests
    {

        #region ASilentServer_DoesNotHangTheSetup()

        /// <summary>
        /// Der Kern: Schweigt der Server nach der Stream-Eröffnung, scheitert
        /// <c>ConnectAsync</c> — und zwar in endlicher Zeit.
        /// </summary>
        /// <remarks>
        /// Die eigene Frist des Tests ist grosszügiger als die des Clients und
        /// trägt trotzdem die Aussage: Läuft sie ab, hat der Client nicht
        /// aufgegeben, und genau das ist der Fehler, um den es geht. Ohne sie
        /// hinge dieser Test so, wie der Verbindungsaufbau hing — ein Test, der
        /// den Fehler nachstellt, den er prüft, ist keiner.
        ///
        /// Geprüft wird die <b>Rückkehr</b> und der gemeldete Fehler, nicht
        /// eine Ausnahme: <c>ConnectInternalAsync</c> fängt jeden
        /// Verbindungsfehler ab und meldet ihn über <c>OnError</c> und den
        /// Zustand. Das ist die Bauart des Hauses und war nie der Mangel — der
        /// Mangel war, dass der Aufruf gar nicht zurückkam. Ob ein
        /// stillschweigend zurückkehrendes <c>ConnectAsync</c> eine gute
        /// Schnittstelle ist, ist eine andere Frage und steht unter „Später".
        /// </remarks>
        [Test]
        public async Task ASilentServer_DoesNotHangTheSetup()
        {

            Server.AnswerStreamOpen = false;
            Server.AddAccount("alice");

            var client = CreateClient("alice", maxReconnectAttempts: 0);

            var gemeldet = new ConcurrentQueue<String>();
            client.OnError += m => gemeldet.Enqueue(m);

            var uhr     = Stopwatch.StartNew();
            var versuch = FailingConnectAsync(client);

            var fertig = await Task.WhenAny(versuch, Task.Delay(TimeSpan.FromSeconds(40)));

            Assert.That(fertig, Is.SameAs(versuch),
                        "Der Verbindungsaufbau hängt: Der Server schweigt, und der " +
                        "Client wartet ohne Frist auf eine Antwort, die nie kommt.");

            await versuch;
            uhr.Stop();

            Assert.Multiple(() =>
            {

                Assert.That(client.IsConnected, Is.False,
                            "Ein schweigender Server ist kein gelungener Aufbau.");

                Assert.That(gemeldet, Is.Not.Empty,
                            "Und er muss gemeldet werden - sonst ist er von einem " +
                            "gelungenen Aufbau nicht zu unterscheiden.");

            });

        }

        #endregion

        #region TheFailureNamesTheStepThatTimedOut()

        /// <summary>
        /// Die Meldung nennt den Schritt, an dem es hing.
        /// </summary>
        /// <remarks>
        /// Eine abgelaufene Frist ohne Angabe, worauf gewartet wurde, verschiebt
        /// die Suche nur: Der Aufrufer weiss dann, dass etwas nicht kam, aber
        /// nicht, was. Genau daran habe ich heute mehrfach Zeit verloren.
        /// </remarks>
        [Test]
        public async Task TheFailureNamesTheStepThatTimedOut()
        {

            Server.AnswerStreamOpen = false;
            Server.AddAccount("alice");

            var client = CreateClient("alice", maxReconnectAttempts: 0);

            var gemeldet = new ConcurrentQueue<String>();
            client.OnError += m => gemeldet.Enqueue(m);

            var versuch = FailingConnectAsync(client);
            var fertig  = await Task.WhenAny(versuch, Task.Delay(TimeSpan.FromSeconds(40)));

            Assert.That(fertig, Is.SameAs(versuch), "Der Verbindungsaufbau hängt.");

            await versuch;

            gemeldet.TryDequeue(out var meldung);

            Assert.Multiple(() =>
            {

                Assert.That(meldung, Does.Contain("Aushandlung"),
                            "Die Meldung muss sagen, in welcher Phase es hing.");

                Assert.That(meldung, Does.Contain("Stream-Kopf"),
                            "Und auf welchen Schritt gewartet wurde.");

            });

        }

        #endregion

        #region AFailedSetup_ThrowsInsteadOfReturningQuietly()

        /// <summary>
        /// Ein gescheiterter Aufbau wirft — er kehrt nicht stillschweigend
        /// zurück.
        /// </summary>
        /// <remarks>
        /// Bis D31 meldete <c>ConnectAsync</c> einen Fehlschlag nur über
        /// <c>OnError</c> und den Zustand. Wer nichts abonniert hatte, sah
        /// zwischen gelungen und gescheitert <b>keinen Unterschied</b> — und
        /// arbeitete auf einer Verbindung weiter, die es nicht gibt.
        ///
        /// Das ist dasselbe Übel wie in D30, eine Ebene höher: Dort kam gar
        /// keine Antwort, hier kommt eine, die nichts sagt. Ein Rückgabewert
        /// hätte es nicht behoben — einen kann man ignorieren, und ein
        /// ignorierter Rückgabewert ist wieder Schweigen.
        ///
        /// Nur der ausdrückliche Aufruf wirft. Der Wiederverbindungsversuch im
        /// Hintergrund hat keinen Aufrufer, dem er etwas schulden könnte, und
        /// meldet weiterhin über Ereignisse.
        /// </remarks>
        [Test]
        public void AFailedSetup_ThrowsInsteadOfReturningQuietly()
        {

            Server.AnswerStreamOpen = false;
            Server.AddAccount("alice");

            var client = CreateClient("alice", maxReconnectAttempts: 0);

            var fehler = Assert.CatchAsync(async () => await client.ConnectAsync());

            Assert.Multiple(() =>
            {

                Assert.That(fehler, Is.Not.Null,
                            "Ein schweigender Server ist kein gelungener Aufbau - " +
                            "und muss dem Aufrufer auffallen, auch ohne Abonnement.");

                // Der geworfene Fehler trägt dieselbe Auskunft wie der
                // gemeldete. Stünde sie nur im Ereignis, müsste der Aufrufer
                // doch wieder abonnieren, um zu erfahren, was los war - und
                // genau das sollte der Wurf ihm ersparen.
                Assert.That(fehler!.Message, Does.Contain("Stream-Kopf"),
                            "Der geworfene Fehler muss den Schritt nennen, nicht nur " +
                            "die Tatsache des Scheiterns.");

            });

        }

        #endregion

        #region TheOriginalErrorSurvivesTheThrow()

        /// <summary>
        /// Geworfen wird der ursprüngliche Fehler, nicht eine Hülle darum.
        /// </summary>
        /// <remarks>
        /// Ein falsches Passwort ist etwas anderes als eine
        /// Zeitüberschreitung, und der Aufrufer soll das eine vom anderen
        /// unterscheiden können, ohne in einer Meldung zu lesen. Eine
        /// gemeinsame Hülle über allem nähme ihm genau das.
        /// </remarks>
        [Test]
        public void TheOriginalErrorSurvivesTheThrow()
        {

            Server.AddAccount("alice");

            var client = CreateClient("alice", password: "falsch", maxReconnectAttempts: 0);

            Assert.That(async () => await client.ConnectAsync(),
                        Throws.InstanceOf<AuthenticationException>(),
                        "Ein falsches Passwort bleibt ein Authentifizierungsfehler.");

        }

        #endregion

        #region AnAnsweringServer_IsUnaffected()

        /// <summary>
        /// Die Gegenprobe: Der gewöhnliche Aufbau bleibt unberührt.
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde die Sammlung auch dann, wenn die Frist so knapp
        /// wäre, dass sie jeden Aufbau abwürgt.
        /// </remarks>
        [Test]
        public async Task AnAnsweringServer_IsUnaffected()
        {

            var client = await ConnectClientAsync("alice");

            Assert.That(client.FullJid, Is.Not.Null);

        }

        #endregion

    }

}
