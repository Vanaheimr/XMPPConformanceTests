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

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die Weiche für eingehende Rahmen entscheidet am <b>Elementnamen</b> und
    /// nicht an einem Präfix — und was sie nicht kennt, beendet den Stream mit
    /// <c>&lt;unsupported-stanza-type/&gt;</c> (RFC 6120, Abschnitt 4.9.3.24).
    /// </summary>
    /// <remarks>
    /// Ein Vergleich mit <c>StartsWith("&lt;iq")</c> trifft auch
    /// <c>&lt;iqbogus/&gt;</c>, <c>StartsWith("&lt;presence")</c> auch
    /// <c>&lt;presence-probe/&gt;</c>, <c>StartsWith("&lt;open")</c> auch
    /// <c>&lt;opencast/&gt;</c>. Der Elementname ist bis zum ersten Zeichen zu
    /// lesen, das nicht mehr zum Namen gehört; alles andere ist geraten.
    ///
    /// Der Schaden ist nicht theoretisch und beim <c>iq</c> noch der harmloseste.
    /// Ein <c>&lt;presence-probe/&gt;</c> lief in die Presence-Behandlung und
    /// galt dort als <b>Anwesenheit</b> — der Absender wurde seinen Kontakten
    /// als online gemeldet, weil sein Element zufällig mit denselben acht
    /// Zeichen beginnt. Und ein <c>&lt;opencast/&gt;</c> zählte als
    /// Stream-Eröffnung.
    ///
    /// Dass die richtige Prüfung im Haus schon existierte, macht es nicht
    /// besser: <c>StreamManagementManager.IsCountableStanza</c> liest den Namen
    /// seit jeher vollständig — nur beantwortet sie eine andere Frage und stand
    /// an einer anderen Stelle. Sie ist jetzt der gemeinsame Nenner.
    /// </remarks>
    [TestFixture]
    public class FrameDispatchTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>Sammelt die Stream-Fehler eines Clients.</summary>
        private static ConcurrentQueue<StreamError> Fehlerkorb(XMPPClient client)
        {

            var korb = new ConcurrentQueue<StreamError>();
            client.OnStreamError += e => korb.Enqueue(e);

            return korb;

        }

        /// <summary>
        /// Ein verbundener Client, der nach einem Abriss nicht von selbst
        /// wiederkommt — sonst liefe er dem Test in die Messung.
        /// </summary>
        private async Task<XMPPClient> EinzelnAsync(String localPart = "alice")
        {

            Server.AddAccount(localPart);

            var client = CreateClient(localPart, maxReconnectAttempts: 0);

            await client.ConnectAsync();

            return client;

        }

        #endregion


        #region AnElementThatOnlyBeginsLikeAStanza_IsNotOne()

        /// <summary>
        /// Der Kern: Drei Elemente, die mit dem Namen einer Stanza
        /// <b>beginnen</b> und keine sind.
        /// </summary>
        /// <remarks>
        /// Alle drei nahmen bisher den Weg des Elements, mit dem sie anfangen.
        /// Geprüft wird über den Stream-Fehler, weil er beide Aussagen in einer
        /// trägt: Er kommt nur, wenn die Weiche das Element <b>nicht</b>
        /// zugeordnet hat, und er nennt den Grund.
        /// </remarks>
        [Test]
        [TestCase("<iqbogus id='x'/>",       TestName = "AnIqbogus_IsNotAnIq")]
        [TestCase("<messages id='x'/>",      TestName = "AMessages_IsNotAMessage")]
        [TestCase("<presence-probe/>",       TestName = "APresenceProbe_IsNotAPresence")]
        [TestCase("<closet/>",               TestName = "ACloset_IsNotAStreamClose")]
        [TestCase("<quatsch xmlns='urn:example:nein'/>",
                                             TestName = "AnUnknownElement_IsRefusedToo")]
        public async Task AnElementThatOnlyBeginsLikeAStanza_IsNotOne(String rahmen)
        {

            var alice   = await EinzelnAsync();
            var sitzung = Server.SessionOf(alice.FullJid!)!;
            var fehler  = Fehlerkorb(alice);

            await alice.SendRawAsync(rahmen);

            await WaitFor(() => !fehler.IsEmpty, "den Stream-Fehler");

            fehler.TryDequeue(out var gemeldet);

            Assert.Multiple(() =>
            {

                Assert.That(gemeldet!.Condition, Is.EqualTo("unsupported-stanza-type"));

                // RFC 6120, Abschnitt 4.9.1.1: Stream-Fehler sind
                // unwiederbringlich. Ein Stream, der danach weiterliefe, wäre
                // ein Widerspruch in sich.
                Assert.That(gemeldet.IsRecoverable, Is.False,
                            "Wer dasselbe noch einmal schickt, bekommt dasselbe " +
                            "zurück - ein Reconnect hilft nicht.");

            });

            await WaitFor(() => !sitzung.IsOpen, "das Ende des Streams");

        }

        #endregion

        #region TheRefusalIsNotAStanzaError()

        /// <summary>
        /// Und ausdrücklich <b>kein</b> <c>&lt;bad-request/&gt;</c>: Das wäre
        /// eine Auskunft über ein IQ, das es nicht gibt.
        /// </summary>
        /// <remarks>
        /// Genau das tat der Server seit D25. Die Typ-Prüfung aus Abschnitt
        /// 8.2.3 Regel 2 griff auf einem Element, das gar keine IQ-Stanza ist,
        /// und antwortete ihm mit der Stanza-Art <c>iq</c> — eine Antwort auf
        /// eine Frage, die niemand gestellt hat. Der Fehler lag nicht in der
        /// Prüfung, sondern in der Weiche davor; sichtbar wurde er erst, als die
        /// Prüfung anfing zu antworten.
        /// </remarks>
        [Test]
        public async Task TheRefusalIsNotAStanzaError()
        {

            var alice = await EinzelnAsync();

            var rohe = new ConcurrentQueue<String>();

            alice.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<", StringComparison.Ordinal))
                    rohe.Enqueue(x);
            };

            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync("<iqbogus id='keine-frage'/>");

            await WaitFor(() => !fehler.IsEmpty, "den Stream-Fehler");

            Assert.That(rohe.Any(x => x.Contains("bad-request", StringComparison.Ordinal)),
                        Is.False,
                        "Ein Element, das kein IQ ist, bekommt keine IQ-Antwort.");

        }

        #endregion

        #region APresenceLookalike_DoesNotMakeAnyoneAvailable()

        /// <summary>
        /// Der greifbarste Schaden: <c>&lt;presence-probe/&gt;</c> galt als
        /// Anwesenheit.
        /// </summary>
        /// <remarks>
        /// Die Presence-Behandlung liest ein fehlendes <c>type</c> als „ist
        /// da". Ein Element, das nur zufällig mit denselben acht Zeichen
        /// beginnt, meldete den Absender damit seinen Kontakten als online —
        /// eine Aussage über einen Menschen, hergeleitet aus einem
        /// Zeichenkettenvergleich.
        ///
        /// Geprüft wird an der Sitzung und nicht an Bobs Client: Der Zustand
        /// steht dort, wo die Behandlung ihn hinschreibt, und ein Client, der
        /// nichts bekommt, bewiese auch nichts über den Zeitpunkt.
        ///
        /// Der Umweg über die Abmeldung ist nötig, weil der Client sich beim
        /// Verbinden von selbst anmeldet: Ohne ihn stünde die Verfügbarkeit
        /// schon, bevor der Test etwas geschickt hat, und der Nachweis wäre
        /// keiner.
        /// </remarks>
        [Test]
        public async Task APresenceLookalike_DoesNotMakeAnyoneAvailable()
        {

            var alice   = await EinzelnAsync();
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await alice.SendRawAsync("<presence type='unavailable'/>");

            await WaitFor(() => !sitzung.IsAvailable, "die Abmeldung");

            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync("<presence-probe/>");

            await WaitFor(() => !fehler.IsEmpty, "den Stream-Fehler");

            Assert.That(sitzung.IsAvailable, Is.False,
                        "Ein Element, das kein <presence/> ist, macht niemanden verfügbar.");

        }

        #endregion

        #region ALookalikeOfTheStreamOpen_DoesNotCount()

        /// <summary>
        /// <c>&lt;opencast/&gt;</c> ist keine Stream-Eröffnung.
        /// </summary>
        /// <remarks>
        /// Die Zählung der Eröffnungen entscheidet, ob der Server die
        /// Aushandlung von vorn beginnt. Ein falsch mitgezähltes Element
        /// verschöbe sie mitten in einer laufenden Sitzung.
        /// </remarks>
        [Test]
        public async Task ALookalikeOfTheStreamOpen_DoesNotCount()
        {

            var alice   = await EinzelnAsync();
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            var vorher = sitzung.OpenCount;
            var fehler = Fehlerkorb(alice);

            await alice.SendRawAsync("<opencast/>");

            await WaitFor(() => !fehler.IsEmpty, "den Stream-Fehler");

            Assert.That(sitzung.OpenCount, Is.EqualTo(vorher),
                        "Nur ein <open/> eröffnet einen Stream.");

        }

        #endregion

        #region AFrameWithoutAnElement_IsIgnored()

        /// <summary>
        /// Ein leerer Rahmen ist kein unbekanntes Element, sondern gar keines —
        /// und beendet nichts.
        /// </summary>
        /// <remarks>
        /// Abschnitt 4.9.3.24 spricht von „a first-level child of the stream
        /// that is not supported". Ein leerer Rahmen ist kein Kind, das nicht
        /// unterstützt wird; er ist kein Kind.
        ///
        /// In D26 fiel er noch mit unter den Stream-Fehler — eine Zeile zu
        /// weit, aufgefallen erst, als D27 dieselbe Regel für den S2S-Stream
        /// aufschrieb und die Frage dort unumgänglich war (Leerraum als
        /// Keepalive ist nach Abschnitt 4.6.1 erlaubt).
        ///
        /// Der Ping danach ist der eigentliche Nachweis: Auf einem Stream wird
        /// der Reihe nach verarbeitet. Kommt seine Antwort an, hat der Server
        /// den leeren Rahmen bereits in der Hand gehabt und sich entschieden.
        /// Damit braucht dieser Test keine Wartezeit, innerhalb derer nichts
        /// passieren darf.
        /// </remarks>
        [Test]
        public async Task AFrameWithoutAnElement_IsIgnored()
        {

            var alice   = await EinzelnAsync();
            var sitzung = Server.SessionOf(alice.FullJid!)!;
            var fehler  = Fehlerkorb(alice);

            var antworten = new ConcurrentQueue<String>();

            alice.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<",             StringComparison.Ordinal) &&
                    x.Contains("id='danach'",       StringComparison.Ordinal))
                {
                    antworten.Enqueue(x);
                }
            };

            await alice.SendRawAsync("   ");

            await alice.SendRawAsync("<iq type='get' id='danach'><ping xmlns='urn:xmpp:ping'/></iq>");

            await WaitFor(() => !antworten.IsEmpty, "die Antwort auf den Ping danach");

            Assert.Multiple(() =>
            {

                // Ohne diese Vorbedingung prüfte der Test nichts: Käme der
                // leere Rahmen gar nicht erst an, bestünde er auch dann, wenn
                // der Server ihn tödlich fände.
                Assert.That(sitzung.Received.Any(f => f.Trim().Length == 0), Is.True,
                            "Vorbedingung: der leere Rahmen muss den Server erreicht haben.");

                Assert.That(fehler,          Is.Empty, "Ein leerer Rahmen ist kein Stream-Fehler.");
                Assert.That(sitzung.IsOpen,  Is.True);

            });

        }

        #endregion

        #region TheThreeStanzas_StillReachTheirHandlers()

        /// <summary>
        /// Die Gegenprobe: Die drei echten Stanzas gehen weiterhin ihren Weg.
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde diese Sammlung auch dann, wenn die Weiche
        /// <b>alles</b> abwiese. Ein Ping genügt als Nachweis für <c>iq</c>,
        /// weil er beantwortet wird; für <c>message</c> und <c>presence</c>
        /// zählt, dass der Stream stehen bleibt — bei einer Abweisung wäre er
        /// nach der ersten Stanza zu.
        /// </remarks>
        [Test]
        public async Task TheThreeStanzas_StillReachTheirHandlers()
        {

            var alice   = await EinzelnAsync();
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            var antworten = new ConcurrentQueue<String>();

            alice.Connection.OnRawXml += x =>
            {
                if (x.StartsWith("<<<",                    StringComparison.Ordinal) &&
                    x.Contains("id='noch-da'",             StringComparison.Ordinal))
                {
                    antworten.Enqueue(x);
                }
            };

            await alice.SendRawAsync("<presence><show>away</show></presence>");
            await alice.SendRawAsync($"<message to='alice@{Server.Domain}'><body>an mich</body></message>");
            await alice.SendRawAsync("<iq type='get' id='noch-da'><ping xmlns='urn:xmpp:ping'/></iq>");

            await WaitFor(() => !antworten.IsEmpty, "die Antwort auf den Ping");

            Assert.That(sitzung.IsOpen, Is.True,
                        "Keine der drei darf den Stream beenden.");

        }

        #endregion

        #region APrefixedStanza_IsStillAStanza()

        /// <summary>
        /// Ein Namensraum-Präfix ändert den Stanza-Typ nicht:
        /// <c>&lt;client:iq/&gt;</c> ist ein <c>iq</c>.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 4.8.1 schreibt keinen bestimmten Präfix vor,
        /// sondern nur den Namensraum. Ein Server, der am Präfix scheitert,
        /// scheitert an einer Freiheit, die der RFC ausdrücklich lässt.
        ///
        /// Geprüft wird nur die Zuordnung — dass daraus kein
        /// <c>&lt;unsupported-stanza-type/&gt;</c> wird. Was der IQ-Weg mit
        /// einem präfigierten Element weiter anstellt, ist eine andere Frage
        /// und steht unter „Später".
        /// </remarks>
        [Test]
        public async Task APrefixedStanza_IsStillAStanza()
        {

            var alice   = await EinzelnAsync();
            var sitzung = Server.SessionOf(alice.FullJid!)!;
            var fehler  = Fehlerkorb(alice);

            await alice.SendRawAsync(
                      "<client:iq xmlns:client='jabber:client' type='get' id='mit-praefix'>" +
                      "<ping xmlns='urn:xmpp:ping'/></client:iq>");

            await WaitAgainst(() => !fehler.IsEmpty, "einen Stream-Fehler");

            Assert.That(sitzung.IsOpen, Is.True);

        }

        #endregion

    }

}
