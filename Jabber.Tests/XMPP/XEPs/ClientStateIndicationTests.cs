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

using System.Net.WebSockets;
using System.Text;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// XEP-0352: Client State Indication - der Client sagt, ob ein Mensch
    /// hinsieht.
    /// </summary>
    /// <remarks>
    /// Zwei Ebenen, und beide sind nötig. Die Einteilung - was warten kann,
    /// was fallengelassen wird, was sofort hinausgeht - ist eine reine
    /// Funktion und einzeln prüfbar. Ob der Server sich daran hält,
    /// beantwortet nur ein Durchlauf: Der Puffer sitzt in derselben Methode,
    /// die zählt (XEP-0198) und aufhebt (Wiederaufnahme), und was er dort
    /// verschiebt, fällt an der Funktion nicht auf.
    /// </remarks>
    [TestFixture]
    public class ClientStateIndicationTests : AXMPPTests
    {

        #region Hilfsfunktionen

        private String Presence(String von = "bob", String resource = "x")
            => $"<presence from='{von}@{Server.Domain}/{resource}' to='alice@{Server.Domain}/r'/>";

        /// <summary>
        /// Verbindet Alice und erklärt ihre Sitzung für inaktiv - über den
        /// Client, damit auch der Weg über die Leitung geprüft ist.
        /// </summary>
        private async Task<(XMPPClient Client, XMPPSession Session)> InaktivAsync()
        {

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            Assert.That(await client.SetActiveAsync(false), Is.True,
                        "Der Server hat XEP-0352 nicht angekündigt.");

            await WaitFor(() => !session.ClientIsActive,
                          "die vom Server übernommene Inaktivität");

            return (client, session);

        }

        /// <summary>Die an den Client geschickten Rahmen, die diesen Text enthalten.</summary>
        private static IReadOnlyList<String> Zugestellt(XMPPSession session, String enthaelt)
            => [.. session.Sent.Where(f => f.Contains(enthaelt, StringComparison.Ordinal))];

        #endregion


        #region APresenceUpdate_CanWait()

        /// <summary>
        /// Eine Presence-Änderung ist das Beispiel, mit dem XEP-0352 anfängt.
        /// </summary>
        [Test]
        public void APresenceUpdate_CanWait()
        {
            Assert.That(ClientStateIndication.HandlingOf(
                            "<presence xmlns='jabber:client' from='bob@example/x'><show>away</show></presence>"),
                        Is.EqualTo(ClientStateHandling.Queued));
        }

        #endregion

        #region ASubscriptionRequest_CannotWait()

        /// <summary>
        /// Eine Kontaktanfrage ist eine Presence und trotzdem keine
        /// Anwesenheitsmeldung: Sie wartet auf die Entscheidung eines
        /// Menschen (RFC 6121, Abschnitt 3.1.3).
        /// </summary>
        /// <remarks>
        /// Der Unterschied ist der zwischen „später auch noch wahr" und „wird
        /// nie beantwortet". Wer sie zurückhält, hält keinen Verkehr auf,
        /// sondern ein Gespräch, das noch nicht angefangen hat.
        /// </remarks>
        [Test]
        public void ASubscriptionRequest_CannotWait()
        {
            Assert.Multiple(() =>
            {

                foreach (var art in new[] { "subscribe", "subscribed", "unsubscribe", "unsubscribed" })
                    Assert.That(ClientStateIndication.HandlingOf(
                                    $"<presence xmlns='jabber:client' type='{art}' from='bob@example'/>"),
                                Is.EqualTo(ClientStateHandling.Immediately),
                                $"type='{art}'");

            });
        }

        #endregion

        #region AMessageWithText_IsTheReasonTheDeviceRings()

        /// <summary>
        /// Eine Nachricht mit Text geht sofort hinaus.
        /// </summary>
        /// <remarks>
        /// XEP-0352 ist eine Sparmassnahme für den Akku und keine
        /// Ruhefunktion für den Menschen davor. Wer hier zurückhielte, machte
        /// aus einer Verkehrsersparnis eine Zustellverzögerung.
        /// </remarks>
        [Test]
        public void AMessageWithText_IsTheReasonTheDeviceRings()
        {
            Assert.That(ClientStateIndication.HandlingOf(
                            "<message xmlns='jabber:client' from='bob@example/x' type='chat'>" +
                            "<body>Hallo</body></message>"),
                        Is.EqualTo(ClientStateHandling.Immediately));
        }

        #endregion

        #region AChatState_IsDiscardedAndNotHeld()

        /// <summary>
        /// „schreibt gerade" wird fallengelassen und nicht aufgehoben.
        /// </summary>
        /// <remarks>
        /// Der Grund ist nicht Sparsamkeit, sondern Wahrheit: Ein
        /// zurückgehaltenes <c>&lt;composing/&gt;</c> wäre bei der Zustellung
        /// keine verspätete Auskunft mehr, sondern eine falsche - der Kontakt
        /// hat längst aufgehört. XEP-0352, Abschnitt 3 nennt genau das:
        /// „Discard messages containing only Chat State Notifications ...
        /// payloads."
        /// </remarks>
        [Test]
        public void AChatState_IsDiscardedAndNotHeld()
        {
            Assert.That(ClientStateIndication.HandlingOf(
                            "<message xmlns='jabber:client' from='bob@example/x' type='chat'>" +
                            "<composing xmlns='http://jabber.org/protocol/chatstates'/></message>"),
                        Is.EqualTo(ClientStateHandling.Discarded));
        }

        #endregion

        #region AChatStateWithAThread_IsStillOnlyAChatState()

        /// <summary>
        /// Ein <c>&lt;thread/&gt;</c> daneben macht daraus keine Nachricht.
        /// </summary>
        /// <remarks>
        /// XEP-0085 empfiehlt genau diese Kombination. Wer die Kinder zählt,
        /// statt die Erweiterungen zu betrachten, hält jede Chat-State-Meldung
        /// mit Thread für etwas Inhaltliches - und hebt dann eben doch auf,
        /// was in fünf Minuten gelogen ist.
        /// </remarks>
        [Test]
        public void AChatStateWithAThread_IsStillOnlyAChatState()
        {
            Assert.That(ClientStateIndication.HandlingOf(
                            "<message xmlns='jabber:client' from='bob@example/x' type='chat'>" +
                            "<composing xmlns='http://jabber.org/protocol/chatstates'/>" +
                            "<thread>abc</thread></message>"),
                        Is.EqualTo(ClientStateHandling.Discarded));
        }

        #endregion

        #region AnEmptyBody_IsNotText()

        /// <summary>
        /// Ein leeres <c>&lt;body/&gt;</c> ist kein Text.
        /// </summary>
        /// <remarks>
        /// Manche Clients führen es neben ihren Chat States mit. Zählte es als
        /// Inhalt, ginge jede „schreibt gerade"-Meldung dieser Clients sofort
        /// hinaus - und die Sparmassnahme wäre gegenüber genau den Clients
        /// wirkungslos, die am meisten davon hätten.
        /// </remarks>
        [Test]
        public void AnEmptyBody_IsNotText()
        {
            Assert.That(ClientStateIndication.HandlingOf(
                            "<message xmlns='jabber:client' from='bob@example/x' type='chat'>" +
                            "<body>   </body>" +
                            "<composing xmlns='http://jabber.org/protocol/chatstates'/></message>"),
                        Is.EqualTo(ClientStateHandling.Discarded));
        }

        #endregion

        #region AReceipt_IsHeldAndNotDiscarded()

        /// <summary>
        /// Eine Empfangsbestätigung (XEP-0184) wartet, aber sie verfällt nicht.
        /// </summary>
        /// <remarks>
        /// Der Unterschied zum Chat State: „angekommen" bleibt wahr. Wer sie
        /// fallenliesse, nähme dem Absender eine Auskunft, die er nie wieder
        /// bekommt.
        /// </remarks>
        [Test]
        public void AReceipt_IsHeldAndNotDiscarded()
        {
            Assert.Multiple(() =>
            {

                Assert.That(ClientStateIndication.HandlingOf(
                                "<message xmlns='jabber:client' from='bob@example/x'>" +
                                "<received xmlns='urn:xmpp:receipts' id='m1'/></message>"),
                            Is.EqualTo(ClientStateHandling.Queued));

                // Und eine Nachricht ganz ohne Erweiterung erst recht nicht:
                // „nur Chat States" heisst mindestens einer. Ohne diese
                // Untergrenze verfiele jede Nachricht, die keine Erweiterung
                // mitbringt - eine Betreffänderung etwa.
                Assert.That(ClientStateIndication.HandlingOf(
                                "<message xmlns='jabber:client' from='bob@example/x' type='groupchat'>" +
                                "<subject>Mittagessen</subject></message>"),
                            Is.EqualTo(ClientStateHandling.Queued));

            });
        }

        #endregion

        #region AnIq_IsNeverHeldBack()

        /// <summary>
        /// Ein <c>iq</c> ist eine Frage mit Frist.
        /// </summary>
        /// <remarks>
        /// Wer es zurückhält, lässt beim Absender die Frist ablaufen und
        /// stellt es danach zu - die Antwort käme zu einer Frage, die niemand
        /// mehr stellt. Dasselbe gilt für jede Nonza: Ein <c>&lt;a/&gt;</c>
        /// gehört nicht zum Verkehr, sondern zum Stream.
        /// </remarks>
        [Test]
        public void AnIq_IsNeverHeldBack()
        {
            Assert.Multiple(() =>
            {

                Assert.That(ClientStateIndication.HandlingOf(
                                "<iq xmlns='jabber:client' type='get' id='p1' from='example'>" +
                                "<ping xmlns='urn:xmpp:ping'/></iq>"),
                            Is.EqualTo(ClientStateHandling.Immediately));

                Assert.That(ClientStateIndication.HandlingOf("<a xmlns='urn:xmpp:sm:3' h='7'/>"),
                            Is.EqualTo(ClientStateHandling.Immediately));

                Assert.That(ClientStateIndication.HandlingOf(
                                "<message xmlns='jabber:client' type='error' from='bob@example'>" +
                                "<error type='cancel'><service-unavailable " +
                                "xmlns='urn:ietf:params:xml:ns:xmpp-stanzas'/></error></message>"),
                            Is.EqualTo(ClientStateHandling.Immediately));

            });
        }

        #endregion

        #region TheLatestPresencePerContact_Wins()

        /// <summary>
        /// Abgelöst wird je Full-JID, nicht je Mensch.
        /// </summary>
        /// <remarks>
        /// Abschnitt 3: „push the latest presence from <b>each contact</b>".
        /// Zwei Geräte desselben Menschen sind zwei Anwesenheiten - verdrängte
        /// die eine die andere, verschwände sein Telefon aus der Liste, weil
        /// sein Rechner sich abgemeldet hat.
        /// </remarks>
        [Test]
        public void TheLatestPresencePerContact_Wins()
        {

            var handy   = "<presence xmlns='jabber:client' from='bob@example/handy'/>";
            var weg     = "<presence xmlns='jabber:client' from='bob@example/handy' type='unavailable'/>";
            var rechner = "<presence xmlns='jabber:client' from='bob@example/rechner'/>";

            Assert.Multiple(() =>
            {

                Assert.That(ClientStateIndication.SupersedeKey(weg),
                            Is.EqualTo(ClientStateIndication.SupersedeKey(handy)),
                            "Eine Abmeldung löst die Anmeldung derselben Resource ab.");

                Assert.That(ClientStateIndication.SupersedeKey(rechner),
                            Is.Not.EqualTo(ClientStateIndication.SupersedeKey(handy)),
                            "Zwei Geräte sind zwei Anwesenheiten.");

                Assert.That(ClientStateIndication.SupersedeKey(
                                "<message xmlns='jabber:client' from='bob@example/handy'>" +
                                "<received xmlns='urn:xmpp:receipts' id='m1'/></message>"),
                            Is.Null,
                            "Eine Nachricht wird durch nichts abgelöst.");

            });

        }

        #endregion


        #region TheFeature_IsAnnouncedAfterAuthentication()

        /// <summary>
        /// XEP-0352, Abschnitt 4.1: Der Server kündigt die Erweiterung in den
        /// Features nach der Anmeldung an.
        /// </summary>
        [Test]
        public async Task TheFeature_IsAnnouncedAfterAuthentication()
        {

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            Assert.Multiple(() =>
            {

                Assert.That(client.SupportsClientStateIndication, Is.True,
                            "Der Client hat die Ankündigung nicht gelesen.");

                Assert.That(session.Sent.Count(f => f.StartsWith("<stream:features", StringComparison.Ordinal) &&
                                                    f.Contains(ClientStateIndication.Namespace, StringComparison.Ordinal)),
                            Is.EqualTo(1),
                            "Die Ankündigung steht nicht in genau einem der beiden Feature-Sätze.");

            });

        }

        #endregion

        #region WithoutTheAnnouncement_TheClientSaysNothing()

        /// <summary>
        /// Ohne Ankündigung schickt der Client kein <c>&lt;inactive/&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Ein Server, der die Erweiterung nicht kennt, sieht ein unbekanntes
        /// Element auf Stream-Ebene und darf den Stream beenden (RFC 6120,
        /// Abschnitt 4.9.3.24). Aus der Sparmassnahme würde ein
        /// Verbindungsabbruch - und zwar genau dann, wenn niemand hinsieht.
        /// </remarks>
        [Test]
        public async Task WithoutTheAnnouncement_TheClientSaysNothing()
        {

            Server.OfferClientStateIndication = false;

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            var gemeldet = await client.SetActiveAsync(false);

            Assert.Multiple(() =>
            {

                Assert.That(client.SupportsClientStateIndication, Is.False);

                Assert.That(gemeldet, Is.False,
                            "Der Client meldet einen Erfolg, den es nicht gab.");

                Assert.That(client.IsActive, Is.True,
                            "Der Client hält sich für inaktiv, der Server weiss nichts davon.");

                Assert.That(session.Received.Any(f => f.Contains(ClientStateIndication.Namespace,
                                                                 StringComparison.Ordinal)),
                            Is.False,
                            "Es ging doch etwas hinaus.");

            });

        }

        #endregion

        #region TheServerAnswersNothing()

        /// <summary>
        /// XEP-0352, Abschnitt 4.2: „There is no reply from the server to
        /// either of these elements."
        /// </summary>
        /// <remarks>
        /// Eine Bestätigung wäre der Widerspruch in sich: Sie weckte das
        /// Gerät genau in dem Augenblick, in dem es sich schlafen legt.
        /// </remarks>
        [Test]
        public async Task TheServerAnswersNothing()
        {

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            var vorher = session.Sent.Count;

            await client.SetActiveAsync(false);

            await WaitFor(() => !session.ClientIsActive, "die übernommene Inaktivität");

            Assert.That(session.Sent.Count, Is.EqualTo(vorher),
                        "Der Server hat auf das <inactive/> geantwortet: " +
                        String.Join(" | ", session.Sent.Skip(vorher)));

        }

        #endregion

        #region AnInactiveClient_GetsNoPresence()

        /// <summary>
        /// Presence wird zurückgehalten, solange niemand hinsieht - und beim
        /// <c>&lt;active/&gt;</c> nachgeliefert.
        /// </summary>
        [Test]
        public async Task AnInactiveClient_GetsNoPresence()
        {

            var (client, session) = await InaktivAsync();

            await session.SendAsync(Presence());

            Assert.Multiple(() =>
            {
                Assert.That(Zugestellt(session, "bob@"), Is.Empty, "Die Presence ging trotzdem hinaus.");
                Assert.That(session.HeldWhileInactive, Is.EqualTo(1));
            });

            Assert.That(await client.SetActiveAsync(true), Is.True);

            await WaitFor(() => Zugestellt(session, "bob@").Count == 1,
                          "die nachgelieferte Presence");

            Assert.That(session.HeldWhileInactive, Is.EqualTo(0));

        }

        #endregion

        #region AMessage_TakesTheHeldStanzasWithIt()

        /// <summary>
        /// Was zurückgehalten wurde, geht <b>vor</b> der Nachricht hinaus, die
        /// den Puffer leert.
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 10.1 verlangt die Reihenfolge zwischen zwei
        /// Entitäten. Ohne diese Regel überholte Bobs Nachricht seine eigene
        /// Presence: Alice sähe erst „Bob schreibt: bin unterwegs" und danach,
        /// dass Bob online gegangen ist.
        /// </remarks>
        [Test]
        public async Task AMessage_TakesTheHeldStanzasWithIt()
        {

            var (_, session) = await InaktivAsync();

            await session.SendAsync(Presence());
            await session.SendAsync($"<message from='bob@{Server.Domain}/x' to='{session.FullJid}' " +
                                    "type='chat'><body>Bin unterwegs</body></message>");

            var bob = Zugestellt(session, "bob@");

            Assert.Multiple(() =>
            {

                Assert.That(bob.Count, Is.EqualTo(2), "Nicht beides angekommen.");

                Assert.That(bob[0], Does.StartWith("<presence"),
                            "Die Nachricht hat die zurückgehaltene Presence überholt.");

                Assert.That(session.HeldWhileInactive, Is.EqualTo(0));

            });

        }

        #endregion

        #region OnlyTheLatestPresence_ArrivesOnTheWire()

        /// <summary>
        /// Fünf Wechsel eines Kontakts hinterlassen eine Presence, nicht fünf.
        /// </summary>
        [Test]
        public async Task OnlyTheLatestPresence_ArrivesOnTheWire()
        {

            var (client, session) = await InaktivAsync();

            for (var i = 0; i < 4; i++)
            {
                await session.SendAsync(Presence());
                await session.SendAsync($"<presence from='bob@{Server.Domain}/x' " +
                                        $"to='alice@{Server.Domain}/r' type='unavailable'/>");
            }

            // Ein zweites Gerät desselben Kontakts wird davon nicht verdrängt.
            await session.SendAsync(Presence(resource: "rechner"));

            Assert.That(session.HeldWhileInactive, Is.EqualTo(2),
                        "Zurückgehalten werden sollten genau zwei Anwesenheiten: " +
                        String.Join(" | ", session.HeldStanzas));

            await client.SetActiveAsync(true);

            await WaitFor(() => Zugestellt(session, "bob@").Count == 2,
                          "die beiden nachgelieferten Presences");

            var zugestellt = Zugestellt(session, "bob@");

            Assert.Multiple(() =>
            {

                Assert.That(zugestellt[0], Does.Contain("type='unavailable'"),
                            "Nachgeliefert wurde nicht die letzte Presence des Handys.");

                Assert.That(zugestellt[1], Does.Contain("/rechner"),
                            "Das zweite Gerät fehlt.");

            });

        }

        #endregion

        #region AChatStateWhileInactive_NeverArrives()

        /// <summary>
        /// Ein Chat State wird fallengelassen und kommt auch später nicht.
        /// </summary>
        [Test]
        public async Task AChatStateWhileInactive_NeverArrives()
        {

            var (client, session) = await InaktivAsync();

            await session.SendAsync($"<message from='bob@{Server.Domain}/x' to='{session.FullJid}' " +
                                    "type='chat'><composing xmlns='http://jabber.org/protocol/chatstates'/></message>");

            Assert.That(session.DiscardedWhileInactive, Is.EqualTo(1));

            await client.SetActiveAsync(true);

            // Der Puffer geht beim <active/> heraus; wäre der Chat State darin
            // gelandet, käme er jetzt.
            await session.SendAsync($"<message from='bob@{Server.Domain}/x' to='{session.FullJid}' " +
                                    "type='chat'><body>Da bin ich</body></message>");

            await WaitFor(() => Zugestellt(session, "Da bin ich").Count == 1, "die Nachricht danach");

            Assert.That(Zugestellt(session, "chatstates"), Is.Empty,
                        "Der Chat State wurde aufgehoben statt fallengelassen.");

        }

        #endregion

        #region AnIqWhileInactive_ArrivesAtOnce()

        /// <summary>
        /// Ein <c>iq</c> geht auch an einen schlafenden Client sofort hinaus.
        /// </summary>
        [Test]
        public async Task AnIqWhileInactive_ArrivesAtOnce()
        {

            var (_, session) = await InaktivAsync();

            await session.SendAsync($"<iq from='{Server.Domain}' to='{session.FullJid}' " +
                                    "type='get' id='csi-ping'><ping xmlns='urn:xmpp:ping'/></iq>");

            Assert.Multiple(() =>
            {
                Assert.That(Zugestellt(session, "csi-ping"), Is.Not.Empty, "Das iq wurde zurückgehalten.");
                Assert.That(session.HeldWhileInactive, Is.EqualTo(0));
            });

        }

        #endregion

        #region AFullBuffer_EmptiesItself()

        /// <summary>
        /// Der Puffer hat eine Obergrenze - und geht beim Überlauf hinaus,
        /// statt etwas wegzuwerfen.
        /// </summary>
        /// <remarks>
        /// Ein Client, der sich für inaktiv erklärt und dann nicht mehr
        /// wiederkommt, nötigte dem Server sonst mit einem einzigen
        /// <c>&lt;inactive/&gt;</c> unbegrenzt Speicher ab. Beim Überlauf
        /// bekommt er Verkehr, den er gerade nicht wollte - das ist die
        /// freundlichere der beiden Möglichkeiten.
        /// </remarks>
        [Test]
        public async Task AFullBuffer_EmptiesItself()
        {

            var (_, session) = await InaktivAsync();

            session.MaxHeldWhileInactive = 2;

            // Drei verschiedene Kontakte, damit sich nichts gegenseitig ablöst.
            await session.SendAsync(Presence("bob"));
            await session.SendAsync(Presence("carol"));

            Assert.That(session.HeldWhileInactive, Is.EqualTo(2), "Zu früh geleert.");

            await session.SendAsync(Presence("dave"));

            Assert.Multiple(() =>
            {

                Assert.That(session.HeldWhileInactive, Is.EqualTo(0), "Der volle Puffer blieb liegen.");

                foreach (var kontakt in new[] { "bob@", "carol@", "dave@" })
                    Assert.That(Zugestellt(session, kontakt).Count, Is.EqualTo(1), kontakt);

            });

        }

        #endregion

        #region ANonza_DoesNotWakeTheBuffer()

        /// <summary>
        /// Eine Nonza geht hinaus, ohne den Puffer mitzunehmen.
        /// </summary>
        /// <remarks>
        /// Ein <c>&lt;r/&gt;</c> des Servers (XEP-0198) fragt nach dem
        /// Empfangszähler und trägt keine Reihenfolge. Leerte es den Puffer,
        /// wäre jede Zählnachfrage ein Weckruf durch die Hintertür - und der
        /// Server hebelte seine eigene Sparmassnahme aus, ohne dass der Client
        /// je <c>&lt;active/&gt;</c> gesagt hätte.
        ///
        /// Die Zählung bleibt dabei stimmig: Was zurückgehalten wird, ist
        /// nicht gesendet und damit auch nicht gezählt - der Client meldet
        /// genau so viel, wie ihn erreicht hat.
        /// </remarks>
        [Test]
        public async Task ANonza_DoesNotWakeTheBuffer()
        {

            var (_, session) = await InaktivAsync();

            await session.SendAsync(Presence());
            await session.RequestAckAsync();

            Assert.Multiple(() =>
            {
                Assert.That(session.HeldWhileInactive, Is.EqualTo(1), "Das <r/> hat den Puffer geleert.");
                Assert.That(Zugestellt(session, "<r "),  Is.Not.Empty, "Das <r/> selbst kam nicht hinaus.");
            });

        }

        #endregion

        #region WithoutTheAnnouncement_TheServerDoesNotObey()

        /// <summary>
        /// Ein Server, der die Erweiterung nicht angeboten hat, handelt auch
        /// nicht danach.
        /// </summary>
        /// <remarks>
        /// Der umgekehrte Fall wäre der gefährlichere: Ein Server, der
        /// schweigt und trotzdem zurückhält, liesse den Client seine Kontakte
        /// für still halten. Deshalb gilt das <c>&lt;inactive/&gt;</c> hier
        /// wie jedes andere unangekündigte Element auf Stream-Ebene -
        /// RFC 6120, Abschnitt 4.9.3.24.
        /// </remarks>
        [Test]
        public async Task WithoutTheAnnouncement_TheServerDoesNotObey()
        {

            Server.OfferClientStateIndication = false;

            var client   = await ConnectClientAsync(maxReconnectAttempts: 0);
            var session  = Server.SessionOf(client.FullJid)!;

            await client.SendRawAsync(ClientStateIndication.InactiveXml);

            await WaitFor(() => session.Sent.Any(f => f.Contains("unsupported-stanza-type",
                                                                 StringComparison.Ordinal)),
                          "den Stream-Fehler auf das unangekündigte Element");

            Assert.That(session.ClientIsActive, Is.True,
                        "Der Server hat einen Zustand übernommen, den er nie angeboten hat.");

        }

        #endregion

        #region BeforeAuthentication_TheStateIsNotAccepted()

        /// <summary>
        /// Vor der Anmeldung gibt es niemanden, dessen Zustand zu schonen wäre.
        /// </summary>
        /// <remarks>
        /// XEP-0352, Abschnitt 4.1: angekündigt wird die Erweiterung in den
        /// Features <b>nach</b> der Anmeldung. Was noch nicht angekündigt war,
        /// gilt auch noch nicht - sonst hätte ein Unangemeldeter einen Zustand
        /// an einer Sitzung, die noch niemandem gehört.
        /// </remarks>
        [Test]
        public async Task BeforeAuthentication_TheStateIsNotAccepted()
        {

            using var socket = new ClientWebSocket();

            socket.Options.AddSubProtocol("xmpp");
            socket.Options.RemoteCertificateValidationCallback = Server.IsOwnCertificate;

            await socket.ConnectAsync(new Uri(Server.Uri), CancellationToken.None);

            async Task Sende(String rahmen)
                => await socket.SendAsync(Encoding.UTF8.GetBytes(rahmen),
                                          WebSocketMessageType.Text, true, CancellationToken.None);

            await Sende("<open xmlns='urn:ietf:params:xml:ns:xmpp-framing' " +
                        $"to='{Server.Domain}' version='1.0'/>");

            await WaitFor(() => Server.Sessions.Any(s => s.Sent.Any(f => f.Contains("mechanisms",
                                                                                     StringComparison.Ordinal))),
                          "die Features des Servers");

            var session = Server.Sessions.Last();

            await Sende(ClientStateIndication.InactiveXml);

            await WaitFor(() => session.Sent.Any(f => f.Contains("unsupported-stanza-type",
                                                                 StringComparison.Ordinal)),
                          "den Stream-Fehler auf das Element vor der Anmeldung");

            Assert.That(session.ClientIsActive, Is.True,
                        "Ein Unangemeldeter hat den Zustand der Sitzung verändert.");

        }

        #endregion

        #region AtTheEndOfTheStream_NothingIsLeftBehind()

        /// <summary>
        /// Reisst die Verbindung, während etwas zurückgehalten wird, landet es
        /// im Puffer der unbestätigten Stanzas - und geht mit der
        /// Wiederaufnahme nach.
        /// </summary>
        /// <remarks>
        /// Ohne das wäre die Sparmassnahme bei jedem Abriss ein Verlust: Der
        /// Rückkehrer bekäme alles nachgeliefert ausser dem, was der Server
        /// eigens für ihn beiseitegelegt hat. Und niemand erführe davon - die
        /// Stanza wurde nie gezählt, also fehlt sie auch keiner Zählung.
        /// </remarks>
        [Test]
        public async Task AtTheEndOfTheStream_NothingIsLeftBehind()
        {

            var client = await ConnectClientAsync(streamManagement: false, maxReconnectAttempts: 0);

            await client.SendRawAsync("<enable xmlns='urn:xmpp:sm:3' resume='true'/>");

            var session = Server.SessionOf(client.FullJid)!;

            await WaitFor(() => session.StreamManagementEnabled, "ausgehandeltes Stream Management");

            await client.SendRawAsync(ClientStateIndication.InactiveXml);

            await WaitFor(() => !session.ClientIsActive, "die übernommene Inaktivität");

            await session.SendAsync(Presence());

            Assert.That(session.HeldWhileInactive, Is.EqualTo(1));

            session.Kill();

            await WaitFor(() => Server.ResumableStreamCount > 0, "die abgelegte Sitzung");

            Assert.Multiple(() =>
            {

                Assert.That(session.HeldWhileInactive, Is.EqualTo(0),
                            "Der Puffer blieb an der toten Sitzung hängen.");

                Assert.That(session.PendingToClient.Any(e => e.Stanza.Contains("bob@", StringComparison.Ordinal)),
                            Is.True,
                            "Die zurückgehaltene Presence ist nicht in den Puffer der unbestätigten gelangt.");

            });

        }

        #endregion

        #region AfterAReconnect_TheClientSaysItAgain()

        /// <summary>
        /// XEP-0352, Abschnitt 5.2: Auch ein wiederaufgenommener Stream fängt
        /// aktiv an - also erklärt sich der Client erneut.
        /// </summary>
        /// <remarks>
        /// „Stream resumption does not affect the current CSI state, which
        /// always defaults to 'active' for new and resumed streams." Der
        /// Server hat den Zustand vergessen, das Gerät liegt aber in derselben
        /// Tasche wie vorher. Ohne diese Wiederholung wäre jede Störung ein
        /// stilles Ende der Sparmassnahme - und niemand bemerkte es, denn
        /// alles funktioniert ja weiter.
        /// </remarks>
        [Test]
        public async Task AfterAReconnect_TheClientSaysItAgain()
        {

            var client   = await ConnectClientAsync(streamManagement: true);
            var session  = Server.SessionOf(client.FullJid)!;

            await client.SetActiveAsync(false);

            await WaitFor(() => !session.ClientIsActive, "die übernommene Inaktivität");

            session.Kill();

            await WaitFor(() => Server.Sessions.Any(s => s.IsOpen &&
                                                         !ReferenceEquals(s, session) &&
                                                         !s.ClientIsActive),
                          "die erneute Erklärung auf dem neuen Stream");

        }

        #endregion

    }

}
