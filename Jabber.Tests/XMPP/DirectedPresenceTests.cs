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
    /// RFC 6121, Abschnitt 4.6.3, Regel 2: Wird eine Resource unverfügbar, geht
    /// die Abmeldung auch an die Empfänger ihrer gerichteten Presence.
    /// </summary>
    /// <remarks>
    /// Die Regel schliesst eine Lücke, die sonst niemandem auffällt. Wer einem
    /// Fremden seine Anwesenheit zeigt, steht deswegen nicht in dessen Roster —
    /// und bekäme ohne diesen Weg nie ein Ende. Der Fremde führte die Resource
    /// für immer als anwesend.
    ///
    /// Der Fall ist der Regelfall und nicht die Ausnahme: Ein Gespräch mit
    /// jemandem, der nicht im Roster steht, beginnt nach Abschnitt 5.1 genau
    /// damit, dass man ihm gerichtete Presence schickt. Seit D17 hängt daran
    /// ausserdem, wer diese Resource überhaupt etwas fragen darf
    /// (Abschnitt 8.5.3.1) — eine Zusage, die nie endet, wäre damit doppelt
    /// unangenehm.
    ///
    /// Zwei Wege führen in die Unverfügbarkeit, und beide stehen hier: die
    /// eigene Abmeldung des Clients und der Verbindungsabriss, bei dem der
    /// Server sie in seinem Namen erzeugt (Abschnitt 4.5.2).
    /// </remarks>
    [TestFixture]
    public class DirectedPresenceTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Sammelt die Presence-Meldungen eines Clients.
        /// </summary>
        private static ConcurrentQueue<(String From, String? Type)> Presenzkorb(XMPPClient client)
        {

            var korb = new ConcurrentQueue<(String, String?)>();
            client.Connection.OnPresence += (from, type) => korb.Enqueue((from, type));

            return korb;

        }

        /// <summary>Nur die Abmeldungen daraus.</summary>
        private static Int32 Abmeldungen(ConcurrentQueue<(String From, String? Type)> korb)
            => korb.Count(p => p.Type == "unavailable");

        #endregion


        #region GoingUnavailable_TellsTheDirectedTarget()

        /// <summary>
        /// Der Kern: Bob zeigt Alice seine Anwesenheit, meldet sich ab — und
        /// Alice erfährt es, obwohl sie in keinem Roster steht.
        /// </summary>
        /// <remarks>
        /// Die erste Hälfte belegt, dass Alice überhaupt nur wegen der
        /// gerichteten Presence etwas hört. Ohne sie wäre jede Presence zwischen
        /// den beiden eine unter Fremden, und die darf es nicht geben — der Test
        /// bestünde dann auch bei einem Server, der Presence an alle verteilt.
        /// </remarks>
        [Test]
        public async Task GoingUnavailable_TellsTheDirectedTarget()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var beiAlice = Presenzkorb(alice);

            // Bob zeigt Alice seine Anwesenheit - und nur ihr.
            await bob.SendRawAsync($"<presence to='{alice.BareJid}'/>");

            await WaitFor(() => beiAlice.Any(p => p.Type != "unavailable"),
                          "die gerichtete Presence bei Alice");

            // Und nun meldet er sich ab.
            await bob.SendRawAsync("<presence type='unavailable'/>");

            await WaitFor(() => Abmeldungen(beiAlice) > 0, "die Abmeldung bei Alice");

            Assert.That(Server.SessionOf(bob.FullJid!)!.DirectedPresenceTargets, Is.Empty,
                        "Und die Zusage ist damit erledigt (Abschnitt 4.6.1).");

        }

        #endregion

        #region ATornConnection_AlsoTellsTheDirectedTarget()

        /// <summary>
        /// Dasselbe, wenn die Verbindung abreisst statt sich abzumelden.
        /// </summary>
        /// <remarks>
        /// Der wichtigere der beiden Wege, weil er der häufigere ist: Ein Client
        /// verschwindet meist, ohne sich zu verabschieden. Die Abmeldung erzeugt
        /// dann der Server in seinem Namen (Abschnitt 4.5.2) — und ginge sie nur
        /// an den Roster, bliebe der Fremde mit einer Anwesenheit zurück, die
        /// nie endet.
        ///
        /// Ohne Stream Management, denn ein Stream mit zugesagter Wiederaufnahme
        /// wird aufgehoben und nicht abgemeldet (XEP-0198, Abschnitt 5). Dann
        /// gibt es zu Recht keine Abmeldung, und der Test prüfte nichts.
        /// </remarks>
        [Test]
        public async Task ATornConnection_AlsoTellsTheDirectedTarget()
        {

            var alice = await ConnectClientAsync("alice");

            Server.AddAccount("bob");

            var bob = CreateClient("bob", streamManagement: false);
            await bob.ConnectAsync();

            var beiAlice = Presenzkorb(alice);

            await bob.SendRawAsync($"<presence to='{alice.BareJid}'/>");

            await WaitFor(() => beiAlice.Any(p => p.Type != "unavailable"),
                          "die gerichtete Presence bei Alice");

            Server.KillSessionsOf(bob.BareJid);

            await WaitFor(() => Abmeldungen(beiAlice) > 0,
                          "die Abmeldung bei Alice nach dem Abriss");

            Assert.Pass();

        }

        #endregion

        #region AStatusChange_DoesNotEndTheDirectedPromise()

        /// <summary>
        /// Eine Statusänderung mitten in der Sitzung beendet die Zusage nicht.
        /// </summary>
        /// <remarks>
        /// Regel 2 gilt „after having sent initial presence and before sending
        /// unavailable presence broadcast (i.e., during the user's presence
        /// session)". Eine gewöhnliche Presence — „abwesend", „beschäftigt" —
        /// beendet diese Sitzung nicht; nur die Abmeldung tut das.
        ///
        /// Im Betrieb ist das der häufigste Zwischenfall überhaupt: Ein Client
        /// schickt bei jedem Wechsel eine neue Presence, und wer die Liste dabei
        /// leert, nimmt dem Gegenüber zweierlei — die Abmeldung am Ende und, seit
        /// D17, das Recht, überhaupt etwas zu fragen (Abschnitt 8.5.3.1). Beides
        /// mitten im Gespräch und ohne dass jemand es merkt.
        ///
        /// Der Test hat eine Mutation aufgedeckt, die jeder andere überlebt hat:
        /// die Liste bei <b>jeder</b> Presence abzuholen statt nur bei der
        /// Abmeldung. Kein anderer Test schickte nach der gerichteten Presence
        /// noch eine gewöhnliche — die Reihenfolge, die im Betrieb die Regel ist.
        /// </remarks>
        [Test]
        public async Task AStatusChange_DoesNotEndTheDirectedPromise()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var beiAlice = Presenzkorb(alice);

            await bob.SendRawAsync($"<presence to='{alice.BareJid}'/>");

            await WaitFor(() => Server.SessionOf(bob.FullJid!)!
                                      .HasDirectedPresenceTo(alice.BareJid),
                          "den Vermerk der gerichteten Presence");

            // Bob wechselt auf "abwesend" - die Anwesenheitssitzung läuft weiter.
            await bob.SetPresenceAsync("away", "Mittagspause");

            Assert.That(Server.SessionOf(bob.FullJid!)!.HasDirectedPresenceTo(alice.BareJid),
                        Is.True,
                        "Eine Statusänderung beendet die Anwesenheitssitzung nicht.");

            // Und die Abmeldung findet Alice weiterhin.
            await bob.SendRawAsync("<presence type='unavailable'/>");

            await WaitFor(() => Abmeldungen(beiAlice) > 0,
                          "die Abmeldung bei Alice nach der Statusänderung");

        }

        #endregion

        #region ARosterContact_IsNotToldTwice()

        /// <summary>
        /// Wer im Roster mit <c>from</c> steht, bekommt die Abmeldung
        /// <b>einmal</b> — nicht zusätzlich über den gerichteten Weg.
        /// </summary>
        /// <remarks>
        /// Der RFC grenzt Regel 2 ausdrücklich auf Entitäten ein, die
        /// <b>nicht</b> mit <c>from</c> oder <c>both</c> im Roster stehen, und
        /// das ist keine Formsache: Ein Kontakt bekommt die Abmeldung schon über
        /// die gewöhnliche Verteilung. Käme sie zweimal, käme ein Client
        /// durcheinander, der Presence zählt statt sie zu ersetzen.
        ///
        /// Der Aufbau ist der heikle Teil: Bob schickt die gerichtete Presence,
        /// <i>bevor</i> Alice seinen Zustand sehen darf. Danach steht sie in
        /// beiden Töpfen, und nur die Einschränkung im Server verhindert die
        /// zweite Zustellung. Andersherum — Roster zuerst — wäre der Vermerk
        /// nach Regel 1 gar nicht nötig, und der Test prüfte den falschen Fall.
        /// </remarks>
        [Test]
        public async Task ARosterContact_IsNotToldTwice()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var beiAlice = Presenzkorb(alice);

            // Erst die gerichtete Presence ...
            await bob.SendRawAsync($"<presence to='{alice.BareJid}'/>");

            await WaitFor(() => Server.SessionOf(bob.FullJid!)!
                                      .HasDirectedPresenceTo(alice.BareJid),
                          "den Vermerk der gerichteten Presence");

            // ... und erst danach der Roster-Eintrag: Alice darf Bobs Zustand sehen.
            SetServerRoster("bob", "alice", "from");

            await bob.SendRawAsync("<presence type='unavailable'/>");

            await WaitFor(() => Abmeldungen(beiAlice) > 0, "die Abmeldung");

            // Eine zweite hätte inzwischen ankommen können.
            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.That(Abmeldungen(beiAlice), Is.EqualTo(1),
                        "Ein Kontakt bekommt die Abmeldung einmal, nicht zweimal.");

        }

        #endregion

        #region AWithdrawnDirectedPresence_GetsNoSecondUnavailable()

        /// <summary>
        /// Der Klammerzusatz der Regel: „if the user has not yet sent directed
        /// unavailable presence to that entity".
        /// </summary>
        /// <remarks>
        /// Wer seine Anwesenheit gegenüber einem Fremden schon ausdrücklich
        /// zurückgenommen hat, bekommt beim Abmelden keine zweite Abmeldung.
        ///
        /// Die Klammer fällt mit der Liste zusammen: Eine gerichtete Abmeldung
        /// nimmt den Empfänger heraus (Abschnitt 4.6.1), und was nicht darin
        /// steht, wird auch nicht benachrichtigt. Zwei Vorschriften, eine
        /// Umsetzung — und deshalb ein Test, der beide zugleich hält.
        /// </remarks>
        [Test]
        public async Task AWithdrawnDirectedPresence_GetsNoSecondUnavailable()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var beiAlice = Presenzkorb(alice);

            await bob.SendRawAsync($"<presence to='{alice.BareJid}'/>");

            await WaitFor(() => Server.SessionOf(bob.FullJid!)!
                                      .HasDirectedPresenceTo(alice.BareJid),
                          "den Vermerk der gerichteten Presence");

            // Bob nimmt seine Anwesenheit gegenüber Alice zurück.
            await bob.SendRawAsync($"<presence to='{alice.BareJid}' type='unavailable'/>");

            await WaitFor(() => Abmeldungen(beiAlice) > 0, "die gerichtete Abmeldung");

            // Und meldet sich danach ganz ab.
            await bob.SendRawAsync("<presence type='unavailable'/>");

            await Task.Delay(TimeSpan.FromSeconds(1));

            Assert.That(Abmeldungen(beiAlice), Is.EqualTo(1),
                        "Die zurückgenommene Zusage bringt keine zweite Abmeldung.");

        }

        #endregion

        #region WhoLeaves_LosesTheirPlaceInTheList()

        /// <summary>
        /// Abschnitt 4.6.1, SOLL-Teil: Wer dem Nutzer eine Abmeldung schickt,
        /// verschwindet aus dessen Liste gerichteter Presence.
        /// </summary>
        /// <remarks>
        /// Die beiden Hälften des Satzes sehen ähnlich aus und meinen
        /// Gegenteiliges. Das MUSS betrifft den <b>eigenen</b> Widerruf — „any
        /// entity to which the user sends directed unavailable presence" —, das
        /// SOLL die Gegenrichtung: „any entity that <i>sends</i> unavailable
        /// presence <i>to</i> the user". Der andere geht, und damit ist die
        /// vorübergehende Beziehung ebenfalls zu Ende.
        ///
        /// Sichtbar wird das erst über Abschnitt 8.5.3.1: Solange Alice in Bobs
        /// Liste steht, darf sie seine Resource befragen. Geht sie und kommt
        /// wieder, hätte sie ihr Fragerecht behalten, obwohl Bob ihr nichts mehr
        /// gezeigt hat — eine Erlaubnis, die den Anlass überlebt.
        ///
        /// Kein Roster-Eintrag zwischen den beiden, und das ist der Kern des
        /// Aufbaus: Wäre Alice Bobs Kontakt, käme ihr Fragerecht über den Roster
        /// und die Liste wäre gleichgültig. Der Fall ist nur ohne Roster
        /// beobachtbar.
        /// </remarks>
        [Test]
        public async Task WhoLeaves_LosesTheirPlaceInTheList()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            // Bob zeigt Alice seine Anwesenheit - sie darf ihn jetzt fragen.
            await bob.SendRawAsync($"<presence to='{alice.BareJid}'/>");

            await WaitFor(() => Server.SessionOf(bob.FullJid!)!
                                      .HasDirectedPresenceTo(alice.BareJid),
                          "den Vermerk der gerichteten Presence");

            // Alice meldet sich bei Bob ab.
            await alice.SendRawAsync($"<presence to='{bob.BareJid}' type='unavailable'/>");

            await WaitFor(() => !Server.SessionOf(bob.FullJid!)!
                                       .HasDirectedPresenceTo(alice.BareJid),
                          "das Vergessen des Absenders");

            // Und damit ist ihr Fragerecht erloschen.
            var fehler = new ConcurrentQueue<StanzaError>();
            alice.Connection.OnStanzaError += (from, e) => fehler.Enqueue(e);

            await alice.SendRawAsync(
                      $"<iq type='get' id='nach-der-abmeldung' to='{bob.FullJid}'>" +
                      "<ping xmlns='urn:xmpp:ping'/></iq>");

            await WaitFor(() => !fehler.IsEmpty,
                          "die Abweisung der Anfrage nach der Abmeldung");

            fehler.TryDequeue(out var abgelehnt);

            Assert.That(abgelehnt!.Condition, Is.EqualTo("service-unavailable"));

        }

        #endregion

        #region AnAvailablePresence_DoesNotRemoveTheSender()

        /// <summary>
        /// Die Gegenprobe: Nur eine <b>Abmeldung</b> nimmt den Absender heraus,
        /// keine gewöhnliche Presence.
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde die Sammlung auch dann, wenn jede eingehende
        /// Presence die Liste leerte — und dann wäre die Zusage schon beim ersten
        /// Lebenszeichen des Gegenübers dahin. Alice zeigt Bob ihre Anwesenheit,
        /// und genau das darf ihr Fragerecht nicht kosten.
        /// </remarks>
        [Test]
        public async Task AnAvailablePresence_DoesNotRemoveTheSender()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            await bob.SendRawAsync($"<presence to='{alice.BareJid}'/>");

            await WaitFor(() => Server.SessionOf(bob.FullJid!)!
                                      .HasDirectedPresenceTo(alice.BareJid),
                          "den Vermerk der gerichteten Presence");

            // Alice zeigt Bob ihre Anwesenheit - keine Abmeldung.
            await alice.SendRawAsync($"<presence to='{bob.BareJid}'/>");

            await WaitAgainst(() => !Server.SessionOf(bob.FullJid!)!
                                          .HasDirectedPresenceTo(alice.BareJid),
                              "das Vergessen bei einer gewöhnlichen Presence");

        }

        #endregion

        #region TheTwoRulesCompose_ALeavingTargetIsForgottenThroughRule2()

        /// <summary>
        /// Die beiden Regeln greifen ineinander: Alices Abmeldung erreicht Bob
        /// über Regel 2 — und ebendarum vergisst er sie.
        /// </summary>
        /// <remarks>
        /// Ohne Roster hört Bob von Alices Abmeldung nur, wenn <b>er</b> in
        /// <i>ihrer</i> Liste steht. Genau das stellt Abschnitt 4.6.3, Regel 2
        /// her (D20). Und weil die Abmeldung dann bei ihm ankommt, greift der
        /// SOLL-Teil aus 4.6.1 — ohne dass eine der beiden Regeln von der anderen
        /// wüsste.
        ///
        /// Der Test hält diese Verzahnung fest, weil sie leicht zerbricht: Wer
        /// eine der beiden Regeln auf „nur an Kontakte" einschränkt, macht die
        /// andere unerreichbar, und beide bestünden weiterhin für sich.
        /// </remarks>
        [Test]
        public async Task TheTwoRulesCompose_ALeavingTargetIsForgottenThroughRule2()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            // Beide zeigen einander ihre Anwesenheit - kein Roster im Spiel.
            await bob.SendRawAsync($"<presence to='{alice.BareJid}'/>");
            await alice.SendRawAsync($"<presence to='{bob.BareJid}'/>");

            await WaitFor(() => Server.SessionOf(bob.FullJid!)!
                                      .HasDirectedPresenceTo(alice.BareJid) &&
                                Server.SessionOf(alice.FullJid!)!
                                      .HasDirectedPresenceTo(bob.BareJid),
                          "die Vermerke auf beiden Seiten");

            // Alice meldet sich ganz ab - nicht gerichtet an Bob.
            await alice.SendRawAsync("<presence type='unavailable'/>");

            await WaitFor(() => !Server.SessionOf(bob.FullJid!)!
                                       .HasDirectedPresenceTo(alice.BareJid),
                          "das Vergessen über den Weg aus Regel 2");

        }

        #endregion

        #region AOneSidedRoster_StillForgetsTheLeavingSender()

        /// <summary>
        /// Auch über den Broadcast-Weg wird vergessen — und dort ist es
        /// beobachtbar, obwohl es zuerst nicht so aussieht.
        /// </summary>
        /// <remarks>
        /// Die beiden Roster-Hälften sind hier leicht zu verwechseln, und die
        /// Verwechslung führt zu genau dem falschen Schluss, dieser Weg sei
        /// gleichgültig:
        ///
        /// <list type="bullet">
        ///   <item>
        ///     Dass Alices Abmeldung Bob über die gewöhnliche Verteilung
        ///     erreicht, entscheidet <b>Alices</b> Roster: Dort steht Bob mit
        ///     <c>from</c> — er darf sie sehen.
        ///   </item>
        ///   <item>
        ///     Ob Alice Bob etwas fragen darf, entscheidet <b>Bobs</b> Roster.
        ///     Der ist hier leer, und damit hängt ihr Fragerecht allein an der
        ///     Liste gerichteter Presence.
        ///   </item>
        /// </list>
        ///
        /// Beides zusammen macht den Fall aus: Die Abmeldung kommt an, ohne dass
        /// Bob Alice im Roster hätte — und ohne das Vergessen behielte sie ihr
        /// Fragerecht über ihre eigene Abmeldung hinaus. Ein Mutant, der diese
        /// Zeile entfernt, überlebte jeden anderen Test dieser Sammlung.
        /// </remarks>
        [Test]
        public async Task AOneSidedRoster_StillForgetsTheLeavingSender()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            // Nur Alices Hälfte: Bob darf ihren Zustand sehen. Bobs Roster
            // bleibt leer.
            SetServerRoster("alice", "bob", "from");

            await bob.SendRawAsync($"<presence to='{alice.BareJid}'/>");

            await WaitFor(() => Server.SessionOf(bob.FullJid!)!
                                      .HasDirectedPresenceTo(alice.BareJid),
                          "den Vermerk der gerichteten Presence");

            // Alices Abmeldung geht über die gewöhnliche Verteilung an Bob.
            await alice.SendRawAsync("<presence type='unavailable'/>");

            await WaitFor(() => !Server.SessionOf(bob.FullJid!)!
                                       .HasDirectedPresenceTo(alice.BareJid),
                          "das Vergessen über den Broadcast-Weg");

        }

        #endregion

        #region AOneSidedRoster_ForgetsAlsoAfterATornConnection()

        /// <summary>
        /// Dasselbe, wenn Alices Verbindung abreisst statt sich abzumelden.
        /// </summary>
        /// <remarks>
        /// Der zweite Broadcast-Weg: Die Abmeldung erzeugt dann der Server in
        /// Alices Namen (Abschnitt 4.5.2). Er ist eine eigene Stelle im Code und
        /// braucht deshalb einen eigenen Test — ohne ihn behielte Alice ihr
        /// Fragerecht ausgerechnet in dem Fall, der im Betrieb der häufigere ist.
        ///
        /// Ohne Stream Management, denn ein aufgehobener Stream wird nicht
        /// abgemeldet (XEP-0198, Abschnitt 5).
        /// </remarks>
        [Test]
        public async Task AOneSidedRoster_ForgetsAlsoAfterATornConnection()
        {

            Server.AddAccount("alice");

            var alice = CreateClient("alice", streamManagement: false);
            await alice.ConnectAsync();

            var bob = await ConnectClientAsync("bob");

            SetServerRoster("alice", "bob", "from");

            await bob.SendRawAsync($"<presence to='{alice.BareJid}'/>");

            await WaitFor(() => Server.SessionOf(bob.FullJid!)!
                                      .HasDirectedPresenceTo(alice.BareJid),
                          "den Vermerk der gerichteten Presence");

            Server.KillSessionsOf(alice.BareJid);

            await WaitFor(() => !Server.SessionOf(bob.FullJid!)!
                                       .HasDirectedPresenceTo(alice.BareJid),
                          "das Vergessen nach dem Abriss");

        }

        #endregion

        #region AStranger_HearsNothing()

        /// <summary>
        /// Die Gegenprobe: Ohne gerichtete Presence und ohne Roster-Eintrag
        /// erfährt niemand etwas.
        /// </summary>
        /// <remarks>
        /// Sie ist die Grenze der ganzen Regel. Ohne sie bestünde die Sammlung
        /// auch dann, wenn der Server die Abmeldung an jede angemeldete Sitzung
        /// schickte — und das wäre kein Nachreichen, sondern ein Presence-Leck:
        /// Wer nie gefragt und nie etwas gezeigt bekommen hat, hat kein Recht zu
        /// erfahren, wann jemand geht.
        /// </remarks>
        [Test]
        public async Task AStranger_HearsNothing()
        {

            var alice = await ConnectClientAsync("alice");
            var bob   = await ConnectClientAsync("bob");

            var beiAlice = Presenzkorb(alice);

            await bob.SendRawAsync("<presence type='unavailable'/>");

            await WaitAgainst(() => Abmeldungen(beiAlice) > 0,
                              "eine Abmeldung an jemanden, der nichts von Bob weiss");

        }

        #endregion

    }

}
