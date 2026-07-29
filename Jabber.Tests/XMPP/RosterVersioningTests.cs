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
    /// Roster-Versionierung nach RFC 6121, Abschnitt 2.6.
    /// </summary>
    /// <remarks>
    /// Der Roster ist das Grösste, was beim Anmelden über die Leitung geht, und
    /// er ändert sich selten. Die Versionierung erspart ihn deshalb: Der Client
    /// nennt die Fassung, die er zwischengespeichert hat, und bekommt ein
    /// leeres Ergebnis, wenn sie noch stimmt.
    ///
    /// Der ganze Mechanismus hängt an einer Feinheit, die leicht falsch
    /// herauskommt: „unverändert" ist ein Ergebnis <b>ganz ohne</b>
    /// <c>&lt;query/&gt;</c>. Ein <c>&lt;query/&gt;</c> ohne Kinder heisst
    /// dagegen „dein Roster ist leer" - wer beides verwechselt, löscht dem
    /// Nutzer die Kontaktliste oder zeigt ihm eine veraltete.
    /// </remarks>
    [TestFixture]
    public class RosterVersioningTests : AXMPPTests
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

        /// <summary>Zählt die Anmeldungen dieses Clients.</summary>
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

        /// <summary>Alle Roster-Anfragen, die der Server je gesehen hat.</summary>
        private IEnumerable<String> RosterRequests
            => Server.AllReceived.Where(f => f.Contains("jabber:iq:roster", StringComparison.Ordinal) &&
                                             f.Contains("type='get'",       StringComparison.Ordinal));

        /// <summary>Die Roster-Ergebnisse der zuletzt geöffneten Sitzung.</summary>
        private IEnumerable<String> RosterResults
            => Server.Sessions.Last().Sent
                     .Where(f => f.Contains("id='roster1'", StringComparison.Ordinal));

        #endregion


        #region TheFirstRequestBringsAVersion()

        /// <summary>
        /// Die erste Anfrage nennt eine leere Fassung, das Ergebnis bringt eine
        /// mit.
        /// </summary>
        /// <remarks>
        /// Das leere <c>ver=''</c> ist kein Platzhalter, sondern die Ansage
        /// „ich kann Versionierung, habe aber noch nichts" (RFC 6121,
        /// Abschnitt 2.6.1). Ohne es wüsste der Server nicht, dass er eine
        /// Fassung mitschicken soll.
        /// </remarks>
        [Test]
        public async Task TheFirstRequestBringsAVersion()
        {

            SetServerRoster("alice", "bob", "both");

            var client = PlainClient();
            await client.ConnectAsync();

            Assert.Multiple(() =>
            {

                Assert.That(RosterRequests.Any(f => f.Contains("ver=''", StringComparison.Ordinal)),
                            Is.True,
                            "Die erste Anfrage muss ein leeres ver tragen.");

                Assert.That(client.Connection.Roster.Version, Is.Not.Null.And.Not.Empty,
                            "Der Client muss die Fassung aus dem Ergebnis übernehmen.");

                Assert.That(client.Connection.Roster.Version,
                            Is.EqualTo(Server.GetAccount(client.BareJid)!.RosterVersion));

                Assert.That(client.Connection.Roster.Items, Has.Count.EqualTo(1));

            });

        }

        #endregion

        #region AnUnchangedRoster_IsNotSentAgain()

        /// <summary>
        /// Der Kern: Kennt der Client die Fassung schon, kommt der Roster nicht
        /// noch einmal - und sein Zwischenspeicher bleibt trotzdem gefüllt.
        /// </summary>
        /// <remarks>
        /// Die zweite Zusicherung ist die wichtigere. Ein leeres Ergebnis
        /// falsch zu lesen hiesse, dem Nutzer bei jeder zweiten Anmeldung eine
        /// leere Kontaktliste zu zeigen.
        /// </remarks>
        [Test]
        public async Task AnUnchangedRoster_IsNotSentAgain()
        {

            SetServerRoster("alice", "bob", "both");

            var client = PlainClient();
            await client.ConnectAsync();

            var fassung     = client.Connection.Roster.Version;
            var anmeldungen = CountConnects(client);

            client.KillConnection();

            await WaitFor(() => anmeldungen() >= 1, "die zweite Anmeldung");

            Assert.Multiple(() =>
            {

                Assert.That(RosterRequests.Any(f => f.Contains($"ver='{fassung}'", StringComparison.Ordinal)),
                            Is.True,
                            "Die zweite Anfrage muss die bekannte Fassung nennen.");

                Assert.That(RosterResults.Any(f => f.Contains("jabber:iq:roster", StringComparison.Ordinal)),
                            Is.False,
                            "Auf eine bekannte Fassung darf kein <query/> mehr folgen.");

                Assert.That(client.Connection.Roster.Items, Has.Count.EqualTo(1),
                            "Der Zwischenspeicher muss den leeren Bescheid überleben.");

                Assert.That(client.Connection.Roster.Version, Is.EqualTo(fassung));

            });

        }

        #endregion

        #region AChangedRoster_ComesAgainWithANewVersion()

        /// <summary>
        /// Die Gegenprobe: Hat sich etwas geändert, kommt der volle Roster und
        /// eine neue Fassung.
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde die Sammlung auch dann, wenn der Server jede
        /// zweite Anfrage leer beantwortete - und der Client bekäme Änderungen
        /// nie zu sehen.
        /// </remarks>
        [Test]
        public async Task AChangedRoster_ComesAgainWithANewVersion()
        {

            SetServerRoster("alice", "bob", "both");

            var client = PlainClient();
            await client.ConnectAsync();

            var vorher      = client.Connection.Roster.Version;
            var anmeldungen = CountConnects(client);

            // Während der Client weg ist, kommt ein Kontakt dazu.
            //
            // Erst den Abriss abwarten: Sonst kann der Reconnect dem
            // SetServerRoster zuvorkommen, und dann fragt der Client mit der
            // alten Fassung nach einem Roster, der noch der alte ist - der Test
            // prüfte etwas anderes, als er soll, und schlüge gelegentlich fehl.
            client.KillConnection();

            await WaitFor(() => !Server.Sessions.Any(s => s.BareJid == client.BareJid),
                          "das Ende der ersten Sitzung");

            SetServerRoster("alice", "carol", "both");

            await WaitFor(() => anmeldungen() >= 1, "die zweite Anmeldung");
            await WaitFor(() => client.Connection.Roster.Items.Count == 2,
                          "den zweiten Kontakt im Roster");

            Assert.Multiple(() =>
            {

                Assert.That(client.Connection.Roster.Version, Is.Not.EqualTo(vorher),
                            "Eine Änderung muss eine neue Fassung ergeben.");

                Assert.That(client.Connection.Roster.Version,
                            Is.EqualTo(Server.GetAccount(client.BareJid)!.RosterVersion));

            });

        }

        #endregion

        #region ARosterPush_CarriesTheNewVersion()

        /// <summary>
        /// Auch der Push trägt die Fassung (RFC 6121, Abschnitt 2.6.3).
        /// </summary>
        /// <remarks>
        /// Ohne sie stünde der Client nach jeder Änderung wieder auf einer
        /// veralteten Fassung und holte beim nächsten Anmelden alles neu - die
        /// Ersparnis wäre genau bei denen weg, die ihren Roster pflegen.
        ///
        /// Gewartet wird auf die <i>Übereinstimmung</i> und nicht auf die erste
        /// Änderung. <c>AddContactAsync</c> ist zweierlei - ein Roster-Set und
        /// ein <c>subscribe</c> -, und beides ändert den Roster, also kommen
        /// zwei Pushes. Wer beim ersten stehenbleibt und dann gegen den
        /// Serverstand vergleicht, prüft gegen ein bewegliches Ziel und
        /// scheitert gelegentlich. Die Zusicherung, um die es geht, ist
        /// ohnehin die: Wenn es sich beruhigt hat, sind beide Seiten einig.
        /// </remarks>
        [Test]
        public async Task ARosterPush_CarriesTheNewVersion()
        {

            var client = PlainClient();
            await client.ConnectAsync();

            var vorher = client.Connection.Roster.Version;

            await client.Connection.AddContactAsync($"carol@{Server.Domain}", "Carol");

            // Beide Bedingungen zusammen, und das ist kein Zierrat: Am Anfang
            // stehen Client und Server beide beim leeren Roster, sind also
            // bereits einig. Eine Wartebedingung, die nur auf Übereinstimmung
            // sieht, wäre erfüllt, bevor irgendetwas geschehen ist.
            await WaitFor(() => client.Connection.Roster.Version != vorher &&
                                client.Connection.Roster.Version ==
                                    Server.GetAccount(client.BareJid)!.RosterVersion,
                          "die Fassung aus den Pushes");

            Assert.Multiple(() =>
            {

                Assert.That(client.Connection.Roster.Version, Is.Not.EqualTo(vorher),
                            "Der neue Kontakt muss eine neue Fassung ergeben.");

                Assert.That(client.Connection.Roster.GetItem($"carol@{Server.Domain}"),
                            Is.Not.Null);

            });

        }

        #endregion

        #region WithoutTheFeature_NothingIsVersioned()

        /// <summary>
        /// Kündigt der Server keine Versionierung an, fragt der Client auch
        /// nicht danach.
        /// </summary>
        /// <remarks>
        /// RFC 6121, Abschnitt 2.6.1 verlangt genau das. Der Grund liegt in der
        /// Gegenrichtung: Ein Client, der ungefragt ein <c>ver</c> schickt und
        /// dann ein leeres Ergebnis als „unverändert" liest, hielte bei einem
        /// Server ohne Versionierung irgendwann einen leeren Roster für den
        /// aktuellen Stand.
        /// </remarks>
        [Test]
        public async Task WithoutTheFeature_NothingIsVersioned()
        {

            Server.OfferRosterVersioning = false;

            SetServerRoster("alice", "bob", "both");

            var client = PlainClient();
            await client.ConnectAsync();

            Assert.Multiple(() =>
            {

                Assert.That(RosterRequests.Any(f => f.Contains("ver=", StringComparison.Ordinal)),
                            Is.False,
                            "Ohne Ankündigung darf keine Fassung angefragt werden.");

                Assert.That(client.Connection.Roster.Version, Is.Null);

                Assert.That(client.Connection.Roster.Items, Has.Count.EqualTo(1),
                            "Der Roster kommt trotzdem vollständig.");

            });

        }

        #endregion

        #region TheVersionFollowsTheContent()

        /// <summary>
        /// Die Fassung ändert sich mit jeder Änderung - und nur mit ihr.
        /// </summary>
        /// <remarks>
        /// Sie ist ein Streuwert über den Inhalt und kein Zähler. Daher die
        /// letzte Zusicherung: Geht der Roster nach A zurück, ist die Fassung
        /// wieder die alte. Das ist richtig so - der Zwischenstand eines
        /// Clients, der A gespeichert hat, stimmt ja wieder.
        /// </remarks>
        [Test]
        public void TheVersionFollowsTheContent()
        {

            var konto = Server.AddAccount("alice");

            var leer = konto.RosterVersion;

            konto.SetRosterEntry(new RosterEntry($"bob@{Server.Domain}", null, "both"));
            var mitBob = konto.RosterVersion;

            konto.SetRosterEntry(new RosterEntry($"bob@{Server.Domain}", "Robert", "both"));
            var umbenannt = konto.RosterVersion;

            konto.SetRosterEntry(new RosterEntry($"bob@{Server.Domain}", "Robert", "to"));
            var andereBerechtigung = konto.RosterVersion;

            konto.RemoveRosterEntry($"bob@{Server.Domain}");
            var wiederLeer = konto.RosterVersion;

            Assert.Multiple(() =>
            {

                Assert.That(mitBob,             Is.Not.EqualTo(leer),      "Ein neuer Kontakt.");
                Assert.That(umbenannt,          Is.Not.EqualTo(mitBob),    "Ein geänderter Name.");
                Assert.That(andereBerechtigung, Is.Not.EqualTo(umbenannt), "Eine geänderte Berechtigung.");

                Assert.That(wiederLeer, Is.EqualTo(leer),
                            "Derselbe Inhalt ergibt dieselbe Fassung.");

            });

        }

        #endregion

    }

}
