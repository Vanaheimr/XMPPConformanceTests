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
    /// Das Ergebnis einer Roster-Anfrage ist der vollständige Roster und keine
    /// Ergänzung (RFC 6121, Abschnitt 2.1.4).
    /// </summary>
    /// <remarks>
    /// Der Unterschied wird an genau einer Stelle sichtbar, und die ist im
    /// Alltag häufig: Ein Kontakt wird an einem anderen Gerät gelöscht,
    /// während dieses hier abgemeldet ist. Beim nächsten Anmelden schickt der
    /// Server ihn nicht mehr - aber wer das Ergebnis nur einarbeitet, nimmt ihn
    /// auch nicht heraus. Der Kontakt kommt zurück und lässt sich von diesem
    /// Gerät aus nicht mehr loswerden.
    ///
    /// Im laufenden Betrieb fällt das nie auf, weil dann ein Push mit
    /// <c>subscription='remove'</c> kommt und der Eintrag ordentlich
    /// verschwindet.
    /// </remarks>
    [TestFixture]
    public class RosterReplacementTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Ein Client ohne Stream Management - sonst nähme ein Reconnect den
        /// alten Stream wieder auf und fragte den Roster gar nicht erst neu ab.
        /// </summary>
        private XMPPClient PlainClient(String localPart = "alice")
        {

            if (Server.GetAccount($"{localPart}@{Server.Domain}") is null)
                Server.AddAccount(localPart);

            return CreateClient(localPart, streamManagement: false);

        }

        private static Func<Int32> CountConnects(XMPPClient client)
        {

            var count = 0;

            client.Connection.OnStateChanged += (alt, neu) =>
            {
                if (neu == ConnectionState.Connected)
                    Interlocked.Increment(ref count);
            };

            return () => Volatile.Read(ref count);

        }

        /// <summary>
        /// Reisst die Verbindung ab, wartet das Ende der Sitzung ab, führt die
        /// Änderung aus und wartet auf die neue Anmeldung.
        /// </summary>
        private async Task ReconnectAround(XMPPClient client, Action aenderung)
        {

            var anmeldungen = CountConnects(client);

            client.KillConnection();

            // Erst das Ende abwarten: Sonst kann der Reconnect der Änderung
            // zuvorkommen, und der Test prüfte etwas anderes, als er soll.
            await WaitFor(() => !Server.Sessions.Any(s => s.BareJid == client.BareJid),
                          "das Ende der ersten Sitzung");

            aenderung();

            await WaitFor(() => anmeldungen() >= 1, "die zweite Anmeldung");

        }

        #endregion


        #region AContactRemovedWhileOffline_IsGoneAfterReconnect()

        /// <summary>
        /// Der Kern: Was der Server nicht mehr führt, verschwindet auch beim
        /// Client.
        /// </summary>
        [Test]
        public async Task AContactRemovedWhileOffline_IsGoneAfterReconnect()
        {

            SetServerRoster("alice", "bob",   "both");
            SetServerRoster("alice", "carol", "both");

            var client = PlainClient();
            await client.ConnectAsync();

            Assert.That(client.Connection.Roster.Items, Has.Count.EqualTo(2),
                        "Vorbedingung: beide Kontakte sind da.");

            var entfernt = new List<String>();
            client.Connection.Roster.OnItemRemoved += jid => entfernt.Add(jid);

            await ReconnectAround(client,
                                  () => Server.GetAccount(client.BareJid)!
                                              .RemoveRosterEntry($"bob@{Server.Domain}"));

            await WaitFor(() => client.Connection.Roster.Items.Count == 1,
                          "das Verschwinden des gelöschten Kontakts");

            Assert.Multiple(() =>
            {

                Assert.That(client.Connection.Roster.GetItem($"bob@{Server.Domain}"), Is.Null,
                            "Der gelöschte Kontakt darf nicht zurückkommen.");

                Assert.That(client.Connection.Roster.GetItem($"carol@{Server.Domain}"), Is.Not.Null,
                            "Der verbliebene muss bleiben.");

                Assert.That(entfernt, Does.Contain($"bob@{Server.Domain}"),
                            "Wer eine Anzeige führt, muss vom Wegfall erfahren.");

            });

        }

        #endregion

        #region AnUnchangedContact_SurvivesTheReconnect()

        /// <summary>
        /// Die Gegenprobe: Ohne Änderung bleibt alles stehen.
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde die Sammlung auch dann, wenn beim Anmelden schlicht
        /// alles gelöscht würde.
        /// </remarks>
        [Test]
        public async Task AnUnchangedContact_SurvivesTheReconnect()
        {

            SetServerRoster("alice", "bob", "both");

            var client = PlainClient();
            await client.ConnectAsync();

            // Eine Änderung, die den Roster berührt, damit das Ergebnis
            // wirklich noch einmal kommt statt als „unverändert" abgetan zu
            // werden - sonst prüfte der Test nur die Versionierung.
            await ReconnectAround(client,
                                  () => SetServerRoster("alice", "carol", "both"));

            await WaitFor(() => client.Connection.Roster.Items.Count == 2,
                          "den zweiten Kontakt");

            Assert.That(client.Connection.Roster.GetItem($"bob@{Server.Domain}"), Is.Not.Null,
                        "Der unveränderte Kontakt darf dabei nicht verlorengehen.");

        }

        #endregion

        #region ARosterPush_DoesNotReplaceTheWholeRoster()

        /// <summary>
        /// Ein Push trägt nur die geänderten Einträge und darf den übrigen
        /// Roster nicht anfassen.
        /// </summary>
        /// <remarks>
        /// Das ist die Gegenprobe zum Ersetzen, und sie ist die schärfere: Wer
        /// den Push mit demselben Verfahren behandelt wie das Ergebnis, löscht
        /// bei jeder einzelnen Änderung den gesamten übrigen Roster. Der
        /// Fehler wäre eine naheliegende Vereinfachung - beides sieht auf dem
        /// Draht gleich aus, ein <c>&lt;query/&gt;</c> mit <c>&lt;item/&gt;</c>.
        /// </remarks>
        [Test]
        public async Task ARosterPush_DoesNotReplaceTheWholeRoster()
        {

            SetServerRoster("alice", "bob",   "both");
            SetServerRoster("alice", "carol", "both");

            var client = PlainClient();
            await client.ConnectAsync();

            Assert.That(client.Connection.Roster.Items, Has.Count.EqualTo(2),
                        "Vorbedingung: beide Kontakte sind da.");

            // Ein einzelner Eintrag ändert sich - der Server antwortet mit
            // einem Push, der genau dieses eine Element trägt.
            //
            // Die Änderung muss vom Client kommen: Ein Eingriff am Konto
            // vorbei löst keinen Push aus, und der Test prüfte dann nur seine
            // eigene Geduld.
            await client.Connection.SendRawAsync(
                      RosterStanzaBuilder.SetItem($"bob@{Server.Domain}", "Robert"));

            await WaitFor(() => client.Connection.Roster.GetItem($"bob@{Server.Domain}")?.Name == "Robert",
                          "den umbenannten Kontakt");

            Assert.That(client.Connection.Roster.GetItem($"carol@{Server.Domain}"), Is.Not.Null,
                        "Ein Push über Bob darf Carol nicht löschen.");

        }

        #endregion

        #region ReplaceAll_UpdatesKeepsAndRemoves()

        /// <summary>
        /// Die drei Fälle einzeln, ohne Server: übernehmen, behalten,
        /// entfernen.
        /// </summary>
        [Test]
        public void ReplaceAll_UpdatesKeepsAndRemoves()
        {

            var roster = new Roster();

            roster.ProcessRosterItem(new RosterItem("bob@example.com")   { Name = "Bob"   });
            roster.ProcessRosterItem(new RosterItem("carol@example.com") { Name = "Carol" });

            var entfernt   = new List<String>();
            var ergaenzt   = new List<String>();
            var geaendert  = new List<String>();

            roster.OnItemRemoved += jid  => entfernt.Add(jid);
            roster.OnItemAdded   += item => ergaenzt.Add(item.Jid);
            roster.OnItemUpdated += item => geaendert.Add(item.Jid);

            // Bob bleibt (mit neuem Namen), Carol fällt weg, Dave kommt dazu.
            roster.ReplaceAll([
                new RosterItem("bob@example.com")  { Name = "Robert" },
                new RosterItem("dave@example.com") { Name = "Dave"   }
            ]);

            Assert.Multiple(() =>
            {

                Assert.That(roster.GetItem("bob@example.com")?.Name, Is.EqualTo("Robert"));
                Assert.That(roster.GetItem("dave@example.com"),      Is.Not.Null);
                Assert.That(roster.GetItem("carol@example.com"),     Is.Null);

                Assert.That(roster.Items, Has.Count.EqualTo(2));

                Assert.That(entfernt,  Is.EqualTo(new[] { "carol@example.com" }));
                Assert.That(ergaenzt,  Is.EqualTo(new[] { "dave@example.com"  }));
                Assert.That(geaendert, Is.EqualTo(new[] { "bob@example.com"   }));

            });

        }

        #endregion

        #region ReplaceAll_WithAnEmptyListClearsTheRoster()

        /// <summary>
        /// Ein wirklich leerer Roster leert auch den Zwischenspeicher.
        /// </summary>
        /// <remarks>
        /// Nicht zu verwechseln mit dem leeren Ergebnis der Versionierung: Das
        /// kommt ganz ohne <c>&lt;query/&gt;</c> und erreicht diese Stelle nie.
        /// Ein <c>&lt;query/&gt;</c> <i>ohne Kinder</i> heisst dagegen
        /// tatsächlich „du hast keine Kontakte mehr", und dann müssen sie weg.
        /// </remarks>
        [Test]
        public void ReplaceAll_WithAnEmptyListClearsTheRoster()
        {

            var roster = new Roster();

            roster.ProcessRosterItem(new RosterItem("bob@example.com"));

            roster.ReplaceAll([]);

            Assert.That(roster.Items, Is.Empty);

        }

        #endregion

    }

}
