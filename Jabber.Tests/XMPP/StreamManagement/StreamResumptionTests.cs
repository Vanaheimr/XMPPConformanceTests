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
    /// XEP-0198, Abschnitt 5: der Server hält einen abgerissenen Stream bereit,
    /// statt ihn sofort zu beerdigen.
    /// </summary>
    /// <remarks>
    /// Was hier geprüft wird, ist die Hälfte, die ohne Rückkehrer auskommt:
    /// dass ein <c>&lt;enable resume='true'/&gt;</c> beantwortet wird, dass ein
    /// Verbindungsabriss die Sitzung <b>nicht</b> beendet, und dass sie nach
    /// Ablauf der Frist doch endet. Das <c>&lt;resume/&gt;</c> selbst sitzt in
    /// der Aufbauphase des Clients - vor dem Resource Binding, nach der
    /// Anmeldung - und ist erst prüfbar, wenn der Client es schickt.
    ///
    /// Der Unterschied ist für die Kontakte sichtbar und deshalb heikel: bis
    /// jetzt erzeugte der Server beim Abriss sofort eine Abmeldung im Namen des
    /// Clients (RFC 6121, Abschnitt 4.5.2). Wer wiederkommen darf, darf nicht
    /// abgemeldet werden - sonst sähen die Kontakte ein Aus und Ein, wo in
    /// Wahrheit nichts geschehen ist. Bleibt der Rückkehrer aber aus, muss die
    /// Abmeldung nachkommen, sonst führen die Kontakte die Resource für immer
    /// als online.
    /// </remarks>
    [TestFixture]
    public class StreamResumptionTests : AXMPPTests
    {

        #region Data

        /// <summary>
        /// Kurz genug, dass der Verfall im Test abzuwarten ist, lang genug,
        /// dass er nicht mitten in den Aufbau fällt.
        /// </summary>
        private static readonly TimeSpan Frist = TimeSpan.FromSeconds(2);

        #endregion

        #region Hilfsfunktionen

        /// <summary>
        /// Verbindet einen Client ohne eigenes Stream Management und handelt es
        /// danach von Hand aus - mit oder ohne Wiederaufnahme.
        /// </summary>
        /// <remarks>
        /// Von Hand, weil der Client die Wiederaufnahme noch nicht selbst
        /// anfordert. Sobald er es tut, kann das hier verschwinden.
        /// </remarks>
        private async Task<(XMPPClient Client, XMPPSession Session)> MitStreamManagementAsync(
                                                                        Boolean resume,
                                                                        String  localPart = "alice")
        {

            var client = await ConnectClientAsync(localPart,
                                                  streamManagement:     false,
                                                  maxReconnectAttempts: 0);

            await client.SendRawAsync(
                      $"<enable xmlns='urn:xmpp:sm:3'{(resume ? " resume='true'" : "")}/>");

            var session = Server.SessionOf(client.FullJid)!;

            await WaitFor(() => session.StreamManagementEnabled,
                          "ausgehandeltes Stream Management");

            return (client, session);

        }

        /// <summary>Die Antwort des Servers auf <c>&lt;enable/&gt;</c>.</summary>
        private static String? EnabledFrame(XMPPSession session)
            => session.Sent.LastOrDefault(f => f.StartsWith("<enabled", StringComparison.Ordinal));

        /// <summary>
        /// Die letzte Abweisung, die der Server über eine seiner offenen
        /// Sitzungen geschickt hat.
        /// </summary>
        /// <remarks>
        /// Über alle Sitzungen und nicht über eine bestimmte, weil die
        /// Abweisung auf dem <b>neuen</b> Stream ankommt - der alte ist gerade
        /// der Grund für die Anfrage. Die abgerissene Sitzung ist nicht mehr
        /// offen und steht deshalb nicht mehr in <c>Sessions</c>.
        /// </remarks>
        private String? Abweisung()
            => Server.Sessions
                     .SelectMany(s => s.Sent)
                     .LastOrDefault(f => f.StartsWith("<failed", StringComparison.Ordinal));

        /// <summary>
        /// Reisst die Sitzung ab und wartet, bis der Server sie als
        /// wiederaufnehmbar abgelegt hat.
        /// </summary>
        /// <remarks>
        /// Ohne dieses Warten steht in jedem Test, der eine gelungene
        /// Wiederaufnahme erwartet, ein Wettlauf: Der Client kommt nach seiner
        /// Reconnect-Frist wieder, der Server legt die Sitzung in seinem
        /// eigenen Takt ab. Kommt der Client zuerst, findet sein
        /// <c>&lt;resume/&gt;</c> nichts vor und bindet neu — was richtig ist,
        /// nur prüft der Test dann etwas anderes, als er soll.
        ///
        /// Aufgefallen als seltener Fehlschlag im vollen Durchlauf, nie allein.
        /// Die Meldung lautete dann „der Stream wurde neu ausgehandelt" — was
        /// zutraf und über den geprüften Code nichts aussagte. Mit dieser
        /// Vorbedingung schlägt stattdessen sie fehl, und zwar mit dem Grund.
        /// </remarks>
        private async Task KillAndAwaitParked(XMPPSession session)
        {

            session.Kill();

            await WaitFor(() => Server.ResumableStreamCount > 0,
                          "die vom Server abgelegte Sitzung");

        }

        #endregion


        #region EnableWithResume_IsAnsweredWithAnUnguessableId()

        /// <summary>
        /// Fragt der Client nach Wiederaufnahme, bekommt er eine Kennung.
        /// </summary>
        /// <remarks>
        /// XEP-0198, Abschnitt 5.1: die Kennung ist das einzige Geheimnis, das
        /// den Rückkehrer ausweist. Wer sie kennt, kann den Stream übernehmen -
        /// deshalb darf sie nicht aus etwas Öffentlichem abzuleiten sein.
        ///
        /// Die frühere Fassung schickte <c>id='sm-{Verbindungsnummer}'</c>.
        /// Das ist eine kleine Zahl, die jeder Mitlesende mitzählen kann, und
        /// wäre mit der Wiederaufnahme zu einem Einfallstor geworden. Ohne
        /// Wiederaufnahme war die Kennung folgenlos - genau deshalb ist sie
        /// nie aufgefallen.
        /// </remarks>
        [Test]
        public async Task EnableWithResume_IsAnsweredWithAnUnguessableId()
        {

            var (_, erste)  = await MitStreamManagementAsync(resume: true, localPart: "alice");
            var (_, zweite) = await MitStreamManagementAsync(resume: true, localPart: "bob");

            Assert.Multiple(() =>
            {

                Assert.That(EnabledFrame(erste), Does.Contain("resume='true'"),
                            "Der Server hat die Wiederaufnahme nicht zugesagt.");

                Assert.That(erste.ResumptionId, Is.Not.Null.And.Length.GreaterThanOrEqualTo(22),
                            "Zu kurz, um nicht erraten zu werden.");

                Assert.That(EnabledFrame(erste), Does.Contain($"id='{erste.ResumptionId}'"));

                // Genau die frühere Form.
                Assert.That(erste.ResumptionId, Is.Not.EqualTo($"sm-{erste.ConnectionNumber}"));

                // Und zwei Sitzungen bekommen verschiedene Kennungen.
                //
                // Hier stand zuerst eine Prüfung, die Kennung dürfe die
                // Verbindungsnummer nicht als Teilzeichenkette enthalten. Das
                // sagt nichts: eine zufällige Kennung aus 22 Zeichen enthält
                // fast jede einzelne Ziffer irgendwo. Allein ausgeführt bestand
                // der Test, im vollen Lauf - mit anderer Verbindungsnummer -
                // fiel er durch.
                Assert.That(erste.ResumptionId, Is.Not.EqualTo(zweite.ResumptionId));

            });

        }

        #endregion

        #region EnableWithoutResume_PromisesNothing()

        /// <summary>
        /// Ohne Nachfrage keine Zusage - und damit auch nichts, was der Server
        /// aufheben müsste.
        /// </summary>
        [Test]
        public async Task EnableWithoutResume_PromisesNothing()
        {

            var (_, session) = await MitStreamManagementAsync(resume: false);

            Assert.Multiple(() =>
            {
                Assert.That(EnabledFrame(session), Does.Not.Contain("resume='true'"));
                Assert.That(session.ResumptionId,  Is.Null);
            });

        }

        #endregion

        #region ADroppedResumableStream_DoesNotLogTheUserOut()

        /// <summary>
        /// Reisst die Verbindung eines wiederaufnehmbaren Streams ab, bleibt
        /// die Resource für ihre Kontakte verfügbar.
        /// </summary>
        /// <remarks>
        /// Das ist der Sinn der ganzen Übung. Ohne sie erzeugt der Server beim
        /// Abriss sofort eine Abmeldung, und ein Client, der zwei Sekunden
        /// später wiederkommt, hat seinen Kontakten in der Zwischenzeit ein
        /// Verschwinden vorgeführt, das nie stattgefunden hat.
        /// </remarks>
        [Test]
        public async Task ADroppedResumableStream_DoesNotLogTheUserOut()
        {

            MakeContacts("alice", "bob");

            var (_, aliceSession) = await MitStreamManagementAsync(resume: true);
            var bob                   = await ConnectClientAsync("bob", createAccount: false);

            var abmeldungen = 0;
            bob.OnPresenceChanged += (from, type) =>
            {
                if (type == "unavailable" && from.StartsWith($"alice@{Server.Domain}", StringComparison.Ordinal))
                    Interlocked.Increment(ref abmeldungen);
            };

            // Die erste Presence schickt der Client schon beim Aufbau; ohne
            // sie gilt die Resource nicht als verfügbar (RFC 6121, 4.2.1) und
            // es gäbe auch nichts abzumelden.
            await WaitFor(() => aliceSession.IsAvailable, "Alice ist verfügbar");

            aliceSession.Kill();

            await WaitAgainst(() => abmeldungen > 0,
                              "eine Abmeldung von Alice, obwohl ihr Stream aufgehoben wird");

            Assert.That(Server.ResumableStreamCount, Is.EqualTo(1),
                        "Der Stream wurde nicht aufgehoben.");

        }

        #endregion

        #region AKeptStreamExpires_AndThenTheContactsSeeIt()

        /// <summary>
        /// Kommt niemand zurück, endet die Sitzung doch - und die Abmeldung
        /// wird nachgeholt.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zum vorigen Test, und ohne sie wäre der Zugewinn
        /// keiner: eine aufgeschobene Abmeldung, die nie kommt, ist schlimmer
        /// als eine zu frühe. Die Kontakte führten die Resource dann für immer
        /// als online, und kein Fehler wäre je sichtbar.
        /// </remarks>
        [Test]
        public async Task AKeptStreamExpires_AndThenTheContactsSeeIt()
        {

            Server.ResumptionTimeout = Frist;

            MakeContacts("alice", "bob");

            var (_, aliceSession) = await MitStreamManagementAsync(resume: true);
            var bob                   = await ConnectClientAsync("bob", createAccount: false);

            var abmeldungen = 0;
            bob.OnPresenceChanged += (from, type) =>
            {
                if (type == "unavailable" && from.StartsWith($"alice@{Server.Domain}", StringComparison.Ordinal))
                    Interlocked.Increment(ref abmeldungen);
            };

            // Die erste Presence schickt der Client schon beim Aufbau; ohne
            // sie gilt die Resource nicht als verfügbar (RFC 6121, 4.2.1) und
            // es gäbe auch nichts abzumelden.
            await WaitFor(() => aliceSession.IsAvailable, "Alice ist verfügbar");

            aliceSession.Kill();

            await WaitFor(() => abmeldungen > 0,
                          "die nachgeholte Abmeldung nach Ablauf der Frist",
                          Frist + TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {

                Assert.That(abmeldungen, Is.EqualTo(1),
                            "Die Abmeldung kam mehr als einmal.");

                Assert.That(Server.ResumableStreamCount, Is.Zero,
                            "Der verfallene Stream liegt noch herum.");

            });

        }

        #endregion

        #region AnInvisibleClient_KeepsItsResumableStream()

        /// <summary>
        /// Die Wiederaufnahme hängt am Stream, nicht an der Presence: Auch ein
        /// Client, der sich unsichtbar gemacht hat, behält seinen aufgehobenen
        /// Stream.
        /// </summary>
        /// <remarks>
        /// Hier wurden zwei Dinge verwechselt. Zugesagt wird die Wiederaufnahme
        /// mit <c>&lt;enabled resume='true'/&gt;</c> und gehört damit dem
        /// Stream; die Presence sagt den Kontakten etwas über den Menschen
        /// davor. Das Ablegen verlangte trotzdem eine <i>verfügbare</i>
        /// Sitzung — und wer sich abmeldete, ohne die Verbindung zu beenden,
        /// verlor die Zusage stillschweigend: Sein <c>&lt;resume/&gt;</c>
        /// bekam ein <c>&lt;failed/&gt;</c>, und alles Unbestätigte war fort.
        ///
        /// Aufgefallen ist das nicht an diesem Fall, sondern an einem Test, der
        /// gelegentlich in die Zeitüberschreitung lief: Er riss die Verbindung
        /// ab, sobald die Wiederaufnahme zugesagt war — und das ist im Aufbau
        /// des Clients <i>vor</i> seiner ersten Presence. Auf einer ruhigen
        /// Maschine kam die Presence rechtzeitig, unter Last nicht immer.
        /// </remarks>
        [Test]
        public async Task AnInvisibleClient_KeepsItsResumableStream()
        {

            var alice   = await ConnectClientAsync("alice", maxReconnectAttempts: 0);
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            // Unsichtbar, aber verbunden - der Stream läuft weiter.
            await alice.SendRawAsync("<presence type='unavailable'/>");

            await WaitFor(() => !sitzung.IsAvailable,
                          "die abgemeldete, aber offene Sitzung");

            sitzung.Kill();

            await WaitFor(() => Server.ResumableStreamCount == 1,
                          "den aufgehobenen Stream");

            Assert.That(sitzung.ResumptionId, Is.Not.Null,
                        "Die Kennung gehört dem Stream und überlebt die Abmeldung.");

        }

        #endregion

        #region AStreamWithoutResume_IsAnnouncedAtOnce()

        /// <summary>
        /// Ohne zugesagte Wiederaufnahme bleibt es beim bisherigen Verhalten.
        /// </summary>
        /// <remarks>
        /// Der Test hält fest, dass die Aufschiebung an der Zusage hängt und
        /// nicht am Stream Management überhaupt. Ohne ihn liesse sich die
        /// Abmeldung versehentlich für alle aufschieben, und die
        /// Verzögerung fiele erst im Betrieb auf.
        /// </remarks>
        [Test]
        public async Task AStreamWithoutResume_IsAnnouncedAtOnce()
        {

            MakeContacts("alice", "bob");

            var (_, aliceSession) = await MitStreamManagementAsync(resume: false);
            var bob                   = await ConnectClientAsync("bob", createAccount: false);

            var abmeldungen = 0;
            bob.OnPresenceChanged += (from, type) =>
            {
                if (type == "unavailable" && from.StartsWith($"alice@{Server.Domain}", StringComparison.Ordinal))
                    Interlocked.Increment(ref abmeldungen);
            };

            // Die erste Presence schickt der Client schon beim Aufbau; ohne
            // sie gilt die Resource nicht als verfügbar (RFC 6121, 4.2.1) und
            // es gäbe auch nichts abzumelden.
            await WaitFor(() => aliceSession.IsAvailable, "Alice ist verfügbar");

            aliceSession.Kill();

            await WaitFor(() => abmeldungen > 0, "die sofortige Abmeldung");

            Assert.That(Server.ResumableStreamCount, Is.Zero);

        }

        #endregion

        #region AcknowledgedStanzas_LeaveTheBuffer()

        /// <summary>
        /// Was der Client bestätigt hat, hebt der Server nicht länger auf.
        /// </summary>
        /// <remarks>
        /// Der Puffer trägt die Stanzas, die nach einer Wiederaufnahme
        /// nachzusenden wären (XEP-0198, Abschnitt 5). Er darf nur das
        /// enthalten, was noch nicht angekommen ist - sonst wüchse er ohne
        /// Ende, und der Rückkehrer bekäme alles doppelt, was er längst hat.
        /// </remarks>
        [Test]
        public async Task AcknowledgedStanzas_LeaveTheBuffer()
        {

            MakeContacts("alice", "bob");

            var (_, aliceSession) = await MitStreamManagementAsync(resume: true);
            var bob               = await ConnectClientAsync("bob", createAccount: false);

            for (var i = 0; i < 3; i++)
                await bob.SendMessageAsync($"alice@{Server.Domain}", $"Nachricht {i}");

            await WaitFor(() => aliceSession.StanzasSentToClient >= 3,
                          "drei zugestellte Nachrichten");

            Assert.That(aliceSession.UnacknowledgedToClient, Is.GreaterThanOrEqualTo(3),
                        "Nichts gepuffert - dann gäbe es nach einer Wiederaufnahme nichts nachzusenden.");

            // Auf den Stand *zum Zeitpunkt der Nachfrage* beziehen, nicht auf
            // einen leeren Puffer: es läuft weiter Verkehr. Bobs Client
            // quittiert die drei Nachrichten mit XEP-0184-Empfangsbestätigungen,
            // und die sind ihrerseits Stanzas an Alice - trifft eine davon
            // zwischen dem <r/> und dem <a/> ein, ist der Puffer nie leer.
            //
            // "Der Puffer ist leer" war in etwa jedem dritten vollen Lauf
            // falsch, allein ausgeführt nie. Was der Test meint, ist: was
            // bestätigt wurde, liegt nicht mehr drin.
            var standBeiderNachfrage = aliceSession.StanzasSentToClient;

            await aliceSession.RequestAckAsync();

            await WaitFor(() => aliceSession.LastAckFromClient >= standBeiderNachfrage,
                          "das <a/> des Clients über den Stand zum Zeitpunkt der Nachfrage");

            Assert.That(aliceSession.PendingToClient.Any(e => e.Seq <= standBeiderNachfrage), Is.False,
                        "Bestätigte Stanzas liegen noch im Puffer.");

        }

        #endregion

        #region TheClientResumesInsteadOfBindingAnew()

        /// <summary>
        /// Nach einem Abriss nimmt der Client den Stream wieder auf, statt
        /// eine neue Resource zu binden.
        /// </summary>
        /// <remarks>
        /// Die Full-JID ist der sichtbare Beleg. Bei einem gewöhnlichen
        /// Neuaufbau vergibt der Server eine neue Resource, und für die
        /// Kontakte ist der Rückkehrer ein anderer als der Verschwundene -
        /// laufende Gespräche, die auf die volle Adresse zeigen, laufen ins
        /// Leere. Nach einer Wiederaufnahme ist es dieselbe Adresse, weil es
        /// derselbe Stream ist.
        /// </remarks>
        [Test]
        public async Task TheClientResumesInsteadOfBindingAnew()
        {

            var alice    = await ConnectClientAsync(reconnectDelay: TimeSpan.FromMilliseconds(200));
            var vorher   = alice.FullJid;
            var sitzung  = Server.SessionOf(vorher!)!;

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            var kennung = alice.StreamManagement!.ResumeId;

            // Auf den *abgeschlossenen* Aufbau warten, nicht auf das
            // Abholen des Streams: der Server raeumt ihn mitten in der
            // Aufbauphase des Clients aus seiner Liste, und wer nur darauf
            // wartet, prueft den Client in einem Zustand, den er gleich
            // wieder verlaesst. Genau daran ist die Mutation, die den
            // Manager bei jedem Aufbau neu erzeugt, zunaechst vorbeigekommen.
            var wiederVerbunden = 0;
            alice.OnStateChanged += (_, neu) =>
            {
                if (neu == ConnectionState.Connected)
                    Interlocked.Increment(ref wiederVerbunden);
            };

            await KillAndAwaitParked(sitzung);

            await WaitFor(() => wiederVerbunden > 0,
                          "die wiederaufgenommene Sitzung",
                          TimeSpan.FromSeconds(20));

            Assert.Multiple(() =>
            {

                Assert.That(alice.FullJid, Is.EqualTo(vorher),
                            "Der Client hat eine neue Resource gebunden statt wiederaufzunehmen.");

                // Die Full-JID allein reicht als Beleg nicht: die Resource ist
                // prozessfest, ein neuer Bind ergäbe dieselbe Adresse. Eine
                // unveränderte Kennung gibt es nur ohne neues <enabled/>.
                Assert.That(alice.StreamManagement.ResumeId, Is.EqualTo(kennung),
                            "Der Stream wurde neu ausgehandelt statt wieder aufgenommen.");

                Assert.That(Server.SessionOf(vorher!), Is.Not.Null);

            });

        }

        #endregion

        #region WhatArrivedDuringTheOutage_IsDeliveredAfterwards()

        /// <summary>
        /// Was während des Abrisses zugestellt wurde, kommt nach der
        /// Wiederaufnahme nach.
        /// </summary>
        /// <remarks>
        /// Der eigentliche Gewinn der ganzen Erweiterung, und der Grund für
        /// den Puffer aus R1. Ohne ihn wäre die Wiederaufnahme nur Kosmetik an
        /// der Full-JID: die Nachrichten, die der Server in eine tote
        /// Verbindung geschrieben hat, wären weg, und niemand erführe davon -
        /// weder der Absender noch der Empfänger.
        /// </remarks>
        [Test]
        public async Task WhatArrivedDuringTheOutage_IsDeliveredAfterwards()
        {

            MakeContacts("alice", "bob");

            var alice   = await ConnectClientAsync(reconnectDelay: TimeSpan.FromMilliseconds(500));
            var bob     = await ConnectClientAsync("bob", createAccount: false);
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            var angekommen = new List<String>();
            alice.OnMessage += m => { lock (angekommen) angekommen.Add(m.Body); };

            // Die Verbindung ist tot, der Server weiss es noch nicht: was er
            // jetzt schickt, geht in den Puffer.
            sitzung.Kill();

            await bob.SendMessageAsync($"alice@{Server.Domain}", "Im Dunkeln geschickt");

            await WaitFor(() => { lock (angekommen) return angekommen.Contains("Im Dunkeln geschickt"); },
                          "die nachgesendete Nachricht",
                          TimeSpan.FromSeconds(20));

        }

        #endregion

        #region AStolenId_DoesNotHandOverTheStream()

        /// <summary>
        /// Die Kennung allein reicht nicht - der Rückkehrer muss auf demselben
        /// Konto angemeldet sein.
        /// </summary>
        /// <remarks>
        /// Die schwerwiegendste Stelle der ganzen Erweiterung. Die Kennung
        /// wandert über die Leitung; wer sie in die Finger bekommt, hätte
        /// ohne diese Prüfung eine fremde Sitzung samt Full-JID, Roster und
        /// laufenden Gesprächen - ohne je das Passwort gesehen zu haben.
        ///
        /// Sie ist damit kein Ausweis, sondern nur eine Auswahl: <i>welcher</i>
        /// der aufgehobenen Streams dieses Kontos gemeint ist. Ausgewiesen hat
        /// sich der Client vorher, über SASL.
        /// </remarks>
        [Test]
        public async Task AStolenId_DoesNotHandOverTheStream()
        {

            var alice   = await ConnectClientAsync("alice", maxReconnectAttempts: 0);
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            var aliceKennung = alice.StreamManagement!.ResumeId;
            var aliceJid     = alice.FullJid;

            sitzung.Kill();
            await WaitFor(() => Server.ResumableStreamCount == 1, "den aufgehobenen Stream");

            // Mallory ist ordentlich angemeldet - nur eben als Mallory - und
            // legt Alices Kennung vor.
            var mallory = await ConnectClientAsync("mallory", maxReconnectAttempts: 0);

            await mallory.SendRawAsync(
                      $"<resume xmlns='urn:xmpp:sm:3' h='0' previd='{aliceKennung}'/>");

            var mallorySitzung = Server.SessionOf(mallory.FullJid!)!;

            await WaitFor(() => mallorySitzung.Sent.Any(f => f.StartsWith("<failed", StringComparison.Ordinal)),
                          "die Abweisung");

            Assert.Multiple(() =>
            {

                Assert.That(mallory.FullJid, Is.Not.EqualTo(aliceJid),
                            "Mallory hat Alices Adresse übernommen.");

                Assert.That(Server.ResumableStreamCount, Is.EqualTo(1),
                            "Alices Stream wurde herausgegeben.");

            });

        }

        #endregion

        #region TheResumedCountPreventsADoubleDelivery()

        /// <summary>
        /// Was der Server schon hatte, schickt der Client nach der
        /// Wiederaufnahme nicht noch einmal.
        /// </summary>
        /// <remarks>
        /// Der Client hält jede gesendete Stanza fest, bis ein <c>h</c> sie
        /// bestätigt. Nach einem Abriss hat er deshalb eine Warteschlange voll
        /// Stanzas, die der Server längst verarbeitet hat - er hatte nur nie
        /// Anlass, sie zu bestätigen. Sendete er sie stumpf alle nach, bekäme
        /// jeder Empfänger sie doppelt.
        ///
        /// Genau dagegen trägt das <c>h</c> im <c>&lt;resumed/&gt;</c>: es
        /// meldet, wie weit der Server gekommen ist, und räumt die
        /// Warteschlange bis dorthin ab. Erst was danach kommt, geht erneut
        /// hinaus.
        ///
        /// <b>Nicht abgedeckt</b> bleibt der umgekehrte Fall - eine Stanza,
        /// die der Client erfolgreich abschickt und die den Server nie
        /// erreicht. Im selben Prozess gibt es ihn nicht: ein abgerissener
        /// Socket lässt das Senden sofort und lautstark scheitern, und eine
        /// nicht gesendete Stanza wird gar nicht erst mitgezählt.
        /// </remarks>
        [Test]
        public async Task TheResumedCountPreventsADoubleDelivery()
        {

            MakeContacts("alice", "bob");

            var alice   = await ConnectClientAsync(reconnectDelay: TimeSpan.FromMilliseconds(200));
            var bob     = await ConnectClientAsync("bob", createAccount: false);
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            var angekommen = new List<String>();
            bob.OnMessage += m => { lock (angekommen) angekommen.Add(m.Body); };

            await alice.SendMessageAsync($"bob@{Server.Domain}", "Nur einmal");

            await WaitFor(() => { lock (angekommen) return angekommen.Count == 1; },
                          "die Nachricht bei Bob");

            Assert.That(alice.StreamManagement!.UnackedCount, Is.GreaterThan(0),
                        "Nichts offen - dann gäbe es beim Wiederaufnehmen auch nichts falsch zu machen.");

            var wiederVerbunden = 0;
            alice.OnStateChanged += (_, neu) =>
            {
                if (neu == ConnectionState.Connected)
                    Interlocked.Increment(ref wiederVerbunden);
            };

            await KillAndAwaitParked(sitzung);

            await WaitFor(() => wiederVerbunden > 0,
                          "die wiederaufgenommene Sitzung",
                          TimeSpan.FromSeconds(20));

            // Hier stand in D7 eine verlängerte Frist, weil dieser Test unter
            // Last gelegentlich rot wurde. Das war die falsche Erklärung: Es
            // ging nie um Wartezeit, sondern um eine Warteschlange, die von
            // selbst nicht leer wurde, solange das Nachsenden keine
            // Bestätigung anforderte (siehe D9). Die Frist steht wieder auf
            // dem Vorgabewert.
            await WaitFor(() => alice.StreamManagement.UnackedCount == 0,
                          "das Leeren der Warteschlange nach der Wiederaufnahme");

            await WaitAgainst(() => { lock (angekommen) return angekommen.Count > 1; },
                              "eine zweite Zustellung derselben Nachricht");

        }

        #endregion

        #region AnExpiredStream_FallsBackToAFreshBind()

        /// <summary>
        /// Ist die Frist abgelaufen, baut der Client normal auf.
        /// </summary>
        /// <remarks>
        /// Der Fehlerpfad, und ohne ihn wäre die Erweiterung gefährlicher als
        /// ihr Nutzen: ein Client, der auf ein <c>&lt;failed/&gt;</c> nicht
        /// zurückfallen kann, käme nach einer längeren Störung überhaupt nicht
        /// mehr online. Die neue Resource ist hier das Richtige - der alte
        /// Stream ist endgültig fort.
        /// </remarks>
        [Test]
        public async Task AnExpiredStream_FallsBackToAFreshBind()
        {

            Server.ResumptionTimeout = TimeSpan.FromMilliseconds(1);

            var alice   = await ConnectClientAsync(reconnectDelay: TimeSpan.FromSeconds(3));
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            // Die Kennung unterscheidet die beiden Fälle, nicht die Full-JID:
            // die Resource ist prozessfest (console-{ProcessId}), ein neuer
            // Bind ergibt also dieselbe Adresse. Eine Wiederaufnahme behält
            // ihre Kennung, ein neues <enabled/> bringt eine neue.
            var alteKennung = alice.StreamManagement!.ResumeId;

            sitzung.Kill();

            // Der Abräumer läuft im Sekundentakt, der Reconnect erst danach.
            await WaitFor(() => alice.IsConnected &&
                                alice.StreamManagement.ResumeId is not null &&
                                alice.StreamManagement.ResumeId != alteKennung,
                          "einen neuen Aufbau nach abgelaufener Frist",
                          TimeSpan.FromSeconds(30));

            Assert.That(Server.SessionOf(alice.FullJid!), Is.Not.Null,
                        "Der Client hält sich für verbunden, der Server kennt ihn nicht.");

        }

        #endregion

        #region StanzasLostInFlight_GoOutAgainAfterResumption()

        /// <summary>
        /// Was der Client erfolgreich abgeschickt hat und der Server nie
        /// verarbeitet hat, geht nach der Wiederaufnahme erneut hinaus.
        /// </summary>
        /// <remarks>
        /// Der Fall, für den der Puffer auf der Client-Seite überhaupt
        /// existiert - und der bis hierher ungeprüft blieb, weil er sich im
        /// selben Prozess nicht herstellen liess: ein abgerissener Socket
        /// lässt das Senden sofort und lautstark scheitern, und eine nicht
        /// gesendete Stanza wird gar nicht erst mitgezählt. Was fehlte, war
        /// eine Stanza, die die Leitung verlässt und trotzdem nicht ankommt.
        ///
        /// <c>SwallowClientStanzas</c> stellt genau das her: der Server nimmt
        /// den Rahmen entgegen und wirft ihn weg, bevor er ihn zählt oder
        /// weiterreicht. Für den Client sieht es aus wie ein geglücktes
        /// Senden, für den Server, als sei nie etwas gekommen - dasselbe Bild
        /// wie bei einer Verbindung, die zwischen Absenden und Verarbeiten
        /// zerfällt.
        ///
        /// Dass die Nachricht am Ende ankommt, hängt allein am Nachsenden:
        /// ohne es ist sie fort, und weder Absender noch Empfänger erführen
        /// davon.
        /// </remarks>
        [Test]
        public async Task StanzasLostInFlight_GoOutAgainAfterResumption()
        {

            MakeContacts("alice", "bob");

            var alice   = await ConnectClientAsync(reconnectDelay: TimeSpan.FromMilliseconds(200));
            var bob     = await ConnectClientAsync("bob", createAccount: false);
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            var angekommen = new List<String>();
            bob.OnMessage += m => { lock (angekommen) angekommen.Add(m.Body); };

            var offenVorher = alice.StreamManagement!.UnackedCount;

            // Ab hier verschluckt der Server, was Alice schickt.
            Server.SwallowClientStanzas = true;

            await alice.SendMessageAsync($"bob@{Server.Domain}", "Unterwegs verloren");

            await WaitFor(() => alice.StreamManagement.UnackedCount > offenVorher,
                          "die abgeschickte, aber unbestätigte Nachricht");

            await WaitAgainst(() => { lock (angekommen) return angekommen.Count > 0; },
                              "eine Zustellung, obwohl der Server verschluckt");

            Server.SwallowClientStanzas = false;

            var wiederVerbunden = 0;
            alice.OnStateChanged += (_, neu) =>
            {
                if (neu == ConnectionState.Connected)
                    Interlocked.Increment(ref wiederVerbunden);
            };

            await KillAndAwaitParked(sitzung);

            await WaitFor(() => wiederVerbunden > 0,
                          "die wiederaufgenommene Sitzung",
                          TimeSpan.FromSeconds(20));

            await WaitFor(() => { lock (angekommen) return angekommen.Contains("Unterwegs verloren"); },
                          "die nachgesendete Nachricht",
                          TimeSpan.FromSeconds(20));

            // Nachgesendet wird ohne erneutes Mitzählen: die Stanza trägt ihre
            // Sequenznummer bereits. Zählte der Client sie ein zweites Mal,
            // liefe sein Ausgangszähler dem Empfangszähler des Servers davon,
            // und ab da bestätigte jedes <a h='…'/> die falschen Stanzas.
            //
            // Hier stand einmal ein RequestAckAsync von Hand. Es war nötig,
            // weil das Nachsenden selbst nicht nachfragte - und es verdeckte
            // damit genau den Fehler, den es hätte zeigen können (siehe D9).
            await WaitFor(() => alice.StreamManagement.LastAcknowledged ==
                                alice.StreamManagement.OutboundCount,
                          "einen Ack über genau den eigenen Stand");

            Assert.That(angekommen.Count(b => b == "Unterwegs verloren"), Is.EqualTo(1),
                        "Die nachgesendete Nachricht kam mehrfach an.");

        }

        #endregion

        #region AnUnknownId_IsRefusedWithoutACount()

        /// <summary>
        /// Kennt der Server die Kennung nicht, nennt seine Abweisung auch
        /// keinen Stand.
        /// </summary>
        /// <remarks>
        /// Das <c>h</c> im <c>&lt;failed/&gt;</c> ist nach XEP-0198,
        /// Abschnitt 5, freiwillig („MAY also include") und meint eine
        /// Messung: wie viel der Server vom alten Stream verarbeitet hatte.
        /// Hier stand bisher fest <c>h='0'</c> - eine Zahl, die niemand
        /// gemessen hat, und die Behauptung „von allem, was du geschickt
        /// hast, ist nichts angekommen". Wer sie glaubt und daraufhin
        /// nachsendet, stellt alles ein zweites Mal zu.
        ///
        /// Nichts zu wissen ist hier der Normalfall: Der Server wurde neu
        /// gestartet, oder die Frist ist längst abgelaufen und der Abräumer
        /// war da. Dann gehört das Attribut weggelassen und nicht geraten.
        /// </remarks>
        [Test]
        public async Task AnUnknownId_IsRefusedWithoutACount()
        {

            var alice = await ConnectClientAsync("alice", maxReconnectAttempts: 0);

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            await alice.SendRawAsync(
                      "<resume xmlns='urn:xmpp:sm:3' h='0' previd='diese-kennung-gab-es-nie'/>");

            await WaitFor(() => Abweisung() is not null, "die Abweisung des Servers");

            var failed = Abweisung()!;

            Assert.Multiple(() =>
            {

                Assert.That(failed, Does.Contain("item-not-found"),
                            "XEP-0198, Abschnitt 5: eine unbekannte Kennung ist ein item-not-found.");

                Assert.That(failed, Does.Not.Contain(" h='"),
                            $"Der Server nennt einen Stand, den er nicht kennt: {failed}");

            });

        }

        #endregion

        #region AStolenId_IsNotEvenConfirmed()

        /// <summary>
        /// Wer eine fremde Kennung vorlegt, erfährt aus der Abweisung nichts
        /// über den fremden Stream.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zu <see cref="AnExpiredStream_IsRefusedWithTheCountItReached"/>:
        /// Dort ist der Stand eine Auskunft an den Eigentümer, hier wäre er
        /// eine an einen Fremden. Ein <c>h</c> verriete zweierlei - dass es
        /// diesen Stream überhaupt gibt, und wie viel über ihn gelaufen ist.
        /// Aus einem geratenen Versuch würde damit eine Sonde.
        ///
        /// Der Unterschied liegt nicht am Fundort der Kennung, sondern am
        /// Konto: Auskunft bekommt nur, wer ohnehin Zugriff auf den Stream
        /// hätte.
        /// </remarks>
        [Test]
        public async Task AStolenId_IsNotEvenConfirmed()
        {

            var alice   = await ConnectClientAsync("alice", maxReconnectAttempts: 0);
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            var aliceKennung = alice.StreamManagement!.ResumeId;

            sitzung.Kill();
            await WaitFor(() => Server.ResumableStreamCount == 1, "den aufgehobenen Stream");

            Assert.That(sitzung.StanzasReceivedFromClient, Is.GreaterThan(0u),
                        "Ohne verarbeitete Stanzas gäbe es auch nichts zu verraten.");

            // Mallory ist ordentlich angemeldet - nur eben als Mallory.
            var mallory = await ConnectClientAsync("mallory", maxReconnectAttempts: 0);

            await mallory.SendRawAsync(
                      $"<resume xmlns='urn:xmpp:sm:3' h='0' previd='{aliceKennung}'/>");

            await WaitFor(() => Abweisung() is not null, "die Abweisung des Servers");

            Assert.That(Abweisung(), Does.Not.Contain(" h='"),
                        $"Die Abweisung verrät den Stand eines fremden Streams: {Abweisung()}");

        }

        #endregion

        #region AnExpiredStream_IsRefusedWithTheCountItReached()

        /// <summary>
        /// Liegt der abgelaufene Stream noch da, nennt die Abweisung, wie weit
        /// der Server gekommen war.
        /// </summary>
        /// <remarks>
        /// Der Fall aus XEP-0198, Abschnitt 5: „If the server recognizes the
        /// 'previd' as an earlier session that has timed out the server MAY
        /// also include a 'h' attribute indicating the number of stanzas
        /// received before the timeout."
        ///
        /// Für den Client ist das die Grenze zwischen zwei Dingen, die er
        /// sonst nicht auseinanderhalten kann: Was der Server verarbeitet hat,
        /// ist zugestellt und darf nicht noch einmal hinausgehen; erst was
        /// darüber hinaus offen ist, ist wirklich verloren.
        /// </remarks>
        [Test]
        public async Task AnExpiredStream_IsRefusedWithTheCountItReached()
        {

            MakeContacts("alice", "bob");

            // Sonst ist dieser Fall nur im Wettlauf zu treffen: Der Abräumer
            // geht im Sekundentakt durch, und was er abgeräumt hat, weiss der
            // Server nicht mehr.
            Server.SweepResumableStreams = false;

            // Drei Sekunden, damit der Wettlauf auch nicht zufällig ausgeht:
            // Der Abräumer wäre in dieser Zeit zweimal vorbeigekommen. Mit
            // den 200 ms der anderen Tests bestünde dieser hier auch dann,
            // wenn der Schalter darüber wirkungslos wäre - der Rückkehrer
            // käme dem Abräumer schlicht zuvor.
            var alice   = await ConnectClientAsync(reconnectDelay: TimeSpan.FromSeconds(3));
            var bob     = await ConnectClientAsync("bob", createAccount: false);
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            var angekommen = new List<String>();
            bob.OnMessage += m => { lock (angekommen) angekommen.Add(m.Body); };

            await alice.SendMessageAsync($"bob@{Server.Domain}", "Verarbeitet");

            await WaitFor(() => { lock (angekommen) return angekommen.Count == 1; },
                          "die zugestellte Nachricht");

            // Die Frist ist abgelaufen, bevor sie zu laufen beginnt.
            Server.ResumptionTimeout = TimeSpan.Zero;

            await KillAndAwaitParked(sitzung);

            var stand = sitzung.StanzasReceivedFromClient;

            Assert.That(stand, Is.GreaterThan(0u),
                        "Ohne verarbeitete Stanzas wäre auch die Unwahrheit h='0'.");

            await WaitFor(() => Abweisung() is not null,
                          "die Abweisung des Servers",
                          TimeSpan.FromSeconds(20));

            Assert.That(Abweisung(), Does.Contain($" h='{stand}'"),
                        $"Der Server nennt nicht, was er verarbeitet hat: {Abweisung()}");

        }

        #endregion

        #region WhatTheServerHandled_IsNotReportedAsLost()

        /// <summary>
        /// Was der Server nach seiner eigenen Auskunft verarbeitet hat, gilt
        /// dem Client nach einer gescheiterten Wiederaufnahme nicht als
        /// verloren.
        /// </summary>
        /// <remarks>
        /// Die Verdrahtung, ohne die der Stand aus dem <c>&lt;failed/&gt;</c>
        /// blosse Zierde wäre: Der Client las ihn nicht und erklärte jede
        /// unbestätigte Stanza für verloren - auch die, die längst beim
        /// Empfänger lag. Bestätigt wird auf dem Weg über
        /// <c>ProcessAck</c> und damit in derselben Modulo-Arithmetik wie ein
        /// <c>&lt;a h='…'/&gt;</c>; ein eigener Vergleich hier wäre eine
        /// zweite Auffassung derselben Rechnung.
        ///
        /// Was den Fehler gefährlich macht, ist die naheliegende Reaktion
        /// darauf: Ein Client, der Verlorenes erneut schickt (Abschnitt 4
        /// empfiehlt es ausdrücklich), stellt damit alles ein zweites Mal zu.
        /// </remarks>
        [Test]
        public async Task WhatTheServerHandled_IsNotReportedAsLost()
        {

            MakeContacts("alice", "bob");

            // Beides wie im vorigen Test, und aus denselben Gründen: der
            // stillstehende Abräumer und eine Wartezeit, die ihm zweimal
            // Gelegenheit gäbe.
            Server.SweepResumableStreams = false;

            var alice   = await ConnectClientAsync(reconnectDelay: TimeSpan.FromSeconds(3));
            var bob     = await ConnectClientAsync("bob", createAccount: false);
            var sitzung = Server.SessionOf(alice.FullJid!)!;

            await WaitFor(() => alice.StreamManagement?.CanResume == true,
                          "eine zugesagte Wiederaufnahme");

            var alteKennung = alice.StreamManagement!.ResumeId;

            List<String>? verloren = null;
            alice.StreamManagement.OnStanzasLost += liste => verloren = liste;

            var angekommen = new List<String>();
            bob.OnMessage += m => { lock (angekommen) angekommen.Add(m.Body); };

            await alice.SendMessageAsync($"bob@{Server.Domain}", "Verarbeitet");

            await WaitFor(() => { lock (angekommen) return angekommen.Count == 1; },
                          "die zugestellte Nachricht");

            Assert.That(alice.StreamManagement.UnackedCount, Is.GreaterThan(0),
                        "Nichts offen - dann gäbe es auch nichts fälschlich für verloren zu erklären.");

            Server.ResumptionTimeout = TimeSpan.Zero;

            await KillAndAwaitParked(sitzung);

            // Auf den abgeschlossenen Neuaufbau warten, nicht auf die blosse
            // Verbindung: Zwischen der Abfuhr und dem neuen <enabled/> ist die
            // Kennung null, und ein „ungleich der alten" träfe schon dort zu.
            await WaitFor(() => alice.IsConnected &&
                                alice.StreamManagement.ResumeId is not null &&
                                alice.StreamManagement.ResumeId != alteKennung,
                          "den neuen Aufbau nach der Abfuhr",
                          TimeSpan.FromSeconds(20));

            Assert.That((verloren ?? []).Any(s => s.Contains("Verarbeitet")), Is.False,
                        "Der Client hält eine Nachricht für verloren, die der Server zugestellt hat.");

        }

        #endregion

    }

}
