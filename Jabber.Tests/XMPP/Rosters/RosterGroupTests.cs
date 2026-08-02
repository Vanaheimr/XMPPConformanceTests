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
    /// Roster-Gruppen nach RFC 6121, Abschnitt 2.1.2.4.
    /// </summary>
    /// <remarks>
    /// <b>Der Client konnte sie von jeher, der Server nahm sie nie an.</b>
    /// <c>RosterStanzaBuilder.SetItem</c> schickt <c>&lt;group/&gt;</c> mit,
    /// <c>RosterItem.Groups</c> führt sie, die Konsole zeigt danach sortiert an
    /// - und der Server las das <c>&lt;item/&gt;</c> nur bis zu seinen
    /// Attributen. Die Gruppe kam an, wurde still verworfen, und der Push
    /// brachte denselben Eintrag ohne sie zurück. Da ein Push die Gruppen eines
    /// Eintrags <i>ersetzt</i>, verschwand sie damit auch beim Client: Was der
    /// Mensch eingestellt hatte, war einen Wimpernschlag später weg, ohne dass
    /// irgendetwas nach einem Fehler aussah.
    ///
    /// Der Kommentar in der Roster-Behandlung behauptete die ganze Zeit, ein
    /// Set ändere „Name und Gruppen".
    /// </remarks>
    [TestFixture]
    public class RosterGroupTests : AXMPPTests
    {

        #region Hilfsfunktionen

        // Beide dürfen fragen, bevor es den Eintrag gibt: Sie stehen in
        // WaitFor-Bedingungen, und eine Bedingung, die wirft statt falsch zu
        // sein, wartet nicht - sie scheitert sofort.

        /// <summary>Die Gruppen, die der Server zu einem Kontakt führt.</summary>
        private IReadOnlyList<String> ServerGroupsOf(XMPPClient client, String contact)
            => Server.GetAccount(client.BareJid)
                    ?.Roster.FirstOrDefault(e => e.Jid == $"{contact}@{Server.Domain}")
                    ?.Groups ?? [];

        /// <summary>Die Gruppen, die der Client zu einem Kontakt führt.</summary>
        private static IReadOnlyList<String> ClientGroupsOf(XMPPClient client, String jid)
            => client.Connection.Roster.Items.FirstOrDefault(i => i.Jid == jid)?.Groups ?? [];

        #endregion


        #region AGroupSurvivesTheRoundTrip()

        /// <summary>
        /// Eine Gruppe, die der Client setzt, steht danach beim Server - und
        /// kommt im Push zurück.
        /// </summary>
        [Test]
        public async Task AGroupSurvivesTheRoundTrip()
        {

            var alice = await ConnectClientAsync("alice");

            await alice.AddContactAsync($"bob@{Server.Domain}", "Bob", ["Freunde"]);

            await WaitFor(() => ServerGroupsOf(alice, "bob").Count > 0,
                          "die Gruppe beim Server");

            await WaitFor(() => ClientGroupsOf(alice, $"bob@{Server.Domain}").Count > 0,
                          "die Gruppe im Push");

            Assert.Multiple(() =>
            {
                Assert.That(ServerGroupsOf(alice, "bob"),                       Is.EqualTo(new[] { "Freunde" }));
                Assert.That(ClientGroupsOf(alice, $"bob@{Server.Domain}"),      Is.EqualTo(new[] { "Freunde" }));
            });

        }

        #endregion

        #region TwoGroups_BothSurvive()

        /// <summary>
        /// Ein Kontakt darf in mehreren Gruppen stehen.
        /// </summary>
        [Test]
        public async Task TwoGroups_BothSurvive()
        {

            var alice = await ConnectClientAsync("alice");

            await alice.AddContactAsync($"bob@{Server.Domain}", "Bob", ["Freunde", "Arbeit"]);

            await WaitFor(() => ServerGroupsOf(alice, "bob").Count > 1, "beide Gruppen");

            Assert.That(ServerGroupsOf(alice, "bob"), Is.EquivalentTo(new[] { "Freunde", "Arbeit" }));

        }

        #endregion

        #region ASetWithoutGroups_TakesThemAway()

        /// <summary>
        /// RFC 6121, Abschnitt 2.3.2: Die Gruppen eines Sets ersetzen die
        /// bisherigen vollständig.
        /// </summary>
        /// <remarks>
        /// Ein Set ohne <c>&lt;group/&gt;</c> ist deshalb keine Auslassung,
        /// sondern die Anweisung, dass der Kontakt in keiner Gruppe mehr steht.
        /// Wer das als „nichts angegeben, also nichts geändert" läse, könnte
        /// eine Gruppe nie wieder loswerden.
        /// </remarks>
        [Test]
        public async Task ASetWithoutGroups_TakesThemAway()
        {

            var alice = await ConnectClientAsync("alice");

            await alice.AddContactAsync($"bob@{Server.Domain}", "Bob", ["Freunde"]);

            await WaitFor(() => ServerGroupsOf(alice, "bob").Count > 0, "die Gruppe");

            await alice.AddContactAsync($"bob@{Server.Domain}", "Bob");

            await WaitFor(() => ServerGroupsOf(alice, "bob").Count == 0 &&
                                ClientGroupsOf(alice, $"bob@{Server.Domain}").Count == 0,
                          "die geleerte Gruppenliste auf beiden Seiten");

            Assert.That(ClientGroupsOf(alice, $"bob@{Server.Domain}"), Is.Empty,
                        "Und der Client hört dasselbe.");

        }

        #endregion

        #region AGroupChange_ChangesTheRosterVersion()

        /// <summary>
        /// Ein Umgruppieren ändert die Fassung des Rosters.
        /// </summary>
        /// <remarks>
        /// <b>Das ist der Teil, an dem sonst nichts auffiele.</b> Bliebe die
        /// Fassung dieselbe, bekäme ein Client, der sie zwischengespeichert
        /// hat, beim nächsten Anmelden ein leeres Ergebnis - und behielte die
        /// alte Einteilung für immer. Der Fehler zeigte sich erst Tage später
        /// und an einem anderen Gerät.
        /// </remarks>
        [Test]
        public async Task AGroupChange_ChangesTheRosterVersion()
        {

            var alice = await ConnectClientAsync("alice");
            var konto = Server.GetAccount(alice.BareJid)!;

            await alice.AddContactAsync($"bob@{Server.Domain}", "Bob", ["Freunde"]);

            await WaitFor(() => ServerGroupsOf(alice, "bob").Count > 0, "die erste Gruppe");

            var vorher = konto.RosterVersion;

            await alice.AddContactAsync($"bob@{Server.Domain}", "Bob", ["Arbeit"]);

            await WaitFor(() => ServerGroupsOf(alice, "bob").Contains("Arbeit"), "die zweite Gruppe");

            Assert.That(konto.RosterVersion, Is.Not.EqualTo(vorher),
                        "Eine Änderung, die die Fassung nicht ändert, erreicht den Client nie wieder.");

        }

        #endregion

        #region TheRosterRequest_BringsTheGroups()

        /// <summary>
        /// Auch der Abruf trägt die Gruppen und nicht nur der Push.
        /// </summary>
        /// <remarks>
        /// Beides baut jetzt dieselbe Stelle. Zwei Auskünfte über denselben
        /// Eintrag laufen sonst auseinander, und die Versionierung macht daraus
        /// eine dauerhafte: Der Client hält den Stand aus dem Push für den
        /// ganzen und fragt nicht nach.
        /// </remarks>
        [Test]
        public async Task TheRosterRequest_BringsTheGroups()
        {

            Server.AddAccount("alice").SetRosterEntry(
                new RosterEntry($"bob@{Server.Domain}", "Bob", "both", null, false, ["Freunde"]));

            var alice = await ConnectClientAsync("alice", createAccount: false);

            await WaitFor(() => alice.Connection.Roster.Items.Count > 0, "den Roster");

            Assert.That(ClientGroupsOf(alice, $"bob@{Server.Domain}"), Is.EqualTo(new[] { "Freunde" }));

        }

        #endregion

        #region AGroupWithSpecialCharacters_ArrivesAsItWasWritten()

        /// <summary>
        /// Ein Gruppenname mit XML-Sonderzeichen übersteht beide Richtungen.
        /// </summary>
        /// <remarks>
        /// Der Server liest den Rahmen hier mit einem Muster und nicht mit
        /// einem XML-Leser - dann muss er das Entschärfen selbst rückgängig
        /// machen. <b>Das kaufmännische Und zuletzt:</b> Wer es zuerst ersetzt,
        /// macht aus einem Text, der von einem Zeichen handelt, das Zeichen
        /// selbst.
        /// </remarks>
        [Test]
        public async Task AGroupWithSpecialCharacters_ArrivesAsItWasWritten()
        {

            var alice = await ConnectClientAsync("alice");

            // Der zweite Name ist der eigentliche Prüfstein: Er enthält den
            // Text „&lt;" und meint ihn wörtlich. Wer beim Entschärfen das
            // kaufmännische Und zuerst ersetzt, macht daraus ein „<" - aus
            // einem Text, der von einem Zeichen handelt, wird das Zeichen.
            await alice.AddContactAsync($"bob@{Server.Domain}", "Bob",
                                        ["Tom & Jerry <alt>", "A&lt;B"]);

            // Auf beides warten und nicht nur auf das erste: Der Push kommt
            // nach dem Speichern, und ein Test, der ihn nicht abwartet, misst
            // die Geschwindigkeit der Maschine.
            await WaitFor(() => ServerGroupsOf(alice, "bob").Count > 1 &&
                                ClientGroupsOf(alice, $"bob@{Server.Domain}").Count > 1,
                          "beide Gruppen auf beiden Seiten");

            Assert.Multiple(() =>
            {

                Assert.That(ServerGroupsOf(alice, "bob"),
                            Is.EqualTo(new[] { "Tom & Jerry <alt>", "A&lt;B" }));

                Assert.That(ClientGroupsOf(alice, $"bob@{Server.Domain}"),
                            Is.EqualTo(new[] { "Tom & Jerry <alt>", "A&lt;B" }),
                            "Und der Weg zurück entschärft sie ebenso.");

            });

        }

        #endregion

    }

}
