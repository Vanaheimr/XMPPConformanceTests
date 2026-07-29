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
    /// Konten und Roster, die einen Serverstart überdauern.
    ///
    /// Sie lebten im Speicher einer <c>XMPPServer</c>-Instanz und waren beim
    /// Beenden weg. Damit war der Server auf Tests festgelegt - ein Betrieb
    /// hätte nach jedem Neustart eine leere Kontenliste gehabt.
    /// </summary>
    [TestFixture]
    public class AccountStoreTests
    {

        #region Data

        private String _verzeichnis = null!;
        private String _datei = null!;

        #endregion

        #region SetUp / TearDown

        [SetUp]
        public void EigenesVerzeichnis()
        {

            _verzeichnis = Path.Combine(Path.GetTempPath(),
                                        $"jabber-konten-{Guid.NewGuid():N}");

            _datei = Path.Combine(_verzeichnis, "konten.json");

        }

        [TearDown]
        public void Aufraeumen()
        {
            try { Directory.Delete(_verzeichnis, recursive: true); }
            catch { /* im Teardown egal */ }
        }

        #endregion


        #region MissingFile_IsAnEmptyStore()

        /// <summary>
        /// Ein Speicher auf eine Datei, die es noch nicht gibt, ist leer und
        /// kein Fehler - sonst müsste jeder erste Start von Hand vorbereitet
        /// werden.
        /// </summary>
        [Test]
        public void MissingFile_IsAnEmptyStore()
        {

            var store = new FileAccountStore(_datei);

            Assert.Multiple(() =>
            {
                Assert.That(store.Load(),          Is.Empty);
                Assert.That(File.Exists(_datei),   Is.False,
                            "Blosses Lesen darf die Datei nicht anlegen.");
            });

        }

        #endregion

        #region SavedAccount_ComesBack()

        /// <summary>
        /// Der Kern: ein gespeichertes Konto lässt sich wieder einlesen, und
        /// die Anmeldung funktioniert danach noch.
        /// </summary>
        [Test]
        public void SavedAccount_ComesBack()
        {

            new FileAccountStore(_datei).Save(new XMPPAccount("alice@localhost", "geheim"));

            var gelesen = new FileAccountStore(_datei).Load().ToList();

            Assert.That(gelesen, Has.Count.EqualTo(1));

            Assert.Multiple(() =>
            {
                Assert.That(gelesen[0].BareJid,                       Is.EqualTo("alice@localhost"));
                Assert.That(gelesen[0].Credentials.Verify("geheim"),  Is.True,
                            "Nach dem Einlesen muss die Anmeldung noch gehen.");
                Assert.That(gelesen[0].Credentials.Verify("falsch"),  Is.False);
            });

        }

        #endregion

        #region ScramKeys_SurviveTheRoundTrip()

        /// <summary>
        /// Auch die SCRAM-Schlüssel müssen unverändert zurückkommen - sonst
        /// ginge zwar PLAIN weiter, aber jede SCRAM-Anmeldung schlüge nach
        /// einem Neustart fehl.
        /// </summary>
        /// <remarks>
        /// Der Fall wäre leicht zu übersehen: die Suite prüft Anmeldungen
        /// überwiegend gegen frisch angelegte Konten, und das
        /// Salt-Roundtripping allein genügt nicht, weil die Schlüssel
        /// gespeichert und nicht neu abgeleitet werden.
        /// </remarks>
        [Test]
        public void ScramKeys_SurviveTheRoundTrip()
        {

            var original = new XMPPAccount("alice@localhost", "geheim");

            new FileAccountStore(_datei).Save(original);

            var gelesen = new FileAccountStore(_datei).Load().Single();

            Assert.Multiple(() =>
            {

                Assert.That(gelesen.Credentials.Salt,            Is.EqualTo(original.Credentials.Salt));
                Assert.That(gelesen.Credentials.IterationCount,  Is.EqualTo(original.Credentials.IterationCount));

                foreach (var mechanismus in Enum.GetValues<SCRAMMechanism>())
                {
                    Assert.That(gelesen.Credentials.KeysOf(mechanismus).StoredKey,
                                Is.EqualTo(original.Credentials.KeysOf(mechanismus).StoredKey),
                                $"StoredKey für {mechanismus}.");
                    Assert.That(gelesen.Credentials.KeysOf(mechanismus).ServerKey,
                                Is.EqualTo(original.Credentials.KeysOf(mechanismus).ServerKey),
                                $"ServerKey für {mechanismus}.");
                }

            });

        }

        #endregion

        #region Roster_SurvivesTheRoundTrip()

        /// <summary>
        /// Der Roster gehört zum Konto und muss mit - samt
        /// Subscription-Zustand und offener Anfrage.
        /// </summary>
        [Test]
        public void Roster_SurvivesTheRoundTrip()
        {

            var account = new XMPPAccount("alice@localhost", "geheim");

            account.SetRosterEntry(new RosterEntry("bob@localhost",     "Bob",  "both"));
            account.SetRosterEntry(new RosterEntry("carol@localhost",   null,   "none", "subscribe"));

            new FileAccountStore(_datei).Save(account);

            var gelesen = new FileAccountStore(_datei).Load().Single();

            Assert.Multiple(() =>
            {

                Assert.That(gelesen.Roster, Has.Count.EqualTo(2));

                var bob = gelesen.Roster.Single(e => e.Jid == "bob@localhost");
                Assert.That(bob.Name,          Is.EqualTo("Bob"));
                Assert.That(bob.Subscription,  Is.EqualTo("both"));
                Assert.That(bob.Ask,           Is.Null);

                var carol = gelesen.Roster.Single(e => e.Jid == "carol@localhost");
                Assert.That(carol.Name,          Is.Null);
                Assert.That(carol.Subscription,  Is.EqualTo("none"));
                Assert.That(carol.Ask,           Is.EqualTo("subscribe"),
                            "Eine offene Anfrage darf beim Neustart nicht verlorengehen.");

            });

        }

        #endregion

        #region PendingRequestsAndPreApprovals_SurviveTheRoundTrip()

        /// <summary>
        /// Aufbewahrte Anfragen (RFC 6121, Abschnitt 3.1.3) und Vormerkungen
        /// (Abschnitt 3.4) gehören ebenso zum Konto.
        /// </summary>
        /// <remarks>
        /// Der Abschnitt verlangt, eine Anfrage zuzustellen, sobald der Kontakt
        /// sich das nächste Mal anmeldet - ohne dass ein Serverneustart
        /// dazwischen etwas ändern dürfte. Ginge sie dabei verloren, hiesse
        /// "aufbewahrt" nur "bis zum nächsten Neustart", und der Antragsteller
        /// wartete weiter auf eine Antwort, die niemand mehr geben kann.
        ///
        /// Aufbewahrt wird die vollständige Stanza, nicht nur der Absender -
        /// deshalb wird hier eine mit erweitertem Inhalt geschrieben.
        /// </remarks>
        [Test]
        public void PendingRequestsAndPreApprovals_SurviveTheRoundTrip()
        {

            var account = new XMPPAccount("alice@localhost", "geheim");

            account.SetRosterEntry(new RosterEntry("dave@localhost", null, "none",
                                                   Approved: true));

            account.RememberSubscriptionRequest(
                "carol@localhost",
                "<presence from='carol@localhost' to='alice@localhost' type='subscribe'>" +
                "<status>Wir kennen uns vom Bahnsteig</status></presence>");

            new FileAccountStore(_datei).Save(account);

            var gelesen = new FileAccountStore(_datei).Load().Single();

            Assert.Multiple(() =>
            {

                Assert.That(gelesen.Roster.Single(e => e.Jid == "dave@localhost").Approved,
                            Is.True,
                            "Eine Vormerkung darf beim Neustart nicht verlorengehen.");

                Assert.That(gelesen.PendingSubscriptionRequests.Keys,
                            Is.EquivalentTo(new[] { "carol@localhost" }));

                Assert.That(gelesen.PendingSubscriptionRequests["carol@localhost"],
                            Does.Contain("Wir kennen uns vom Bahnsteig"),
                            "Aufbewahrt wird die vollständige Stanza samt erweitertem Inhalt.");

            });

        }

        #endregion

        #region TheOfflineStore_SurvivesTheRoundTrip()

        /// <summary>
        /// Die Offline-Ablage (RFC 6121, Abschnitt 8.5.2.2.1) gehört ebenso zum
        /// Konto - samt Reihenfolge und Eingangszeitpunkt.
        /// </summary>
        /// <remarks>
        /// Ein Absender, dessen Nachricht der Server angenommen hat, statt sie
        /// mit <c>&lt;service-unavailable/&gt;</c> abzuweisen, darf sich darauf
        /// verlassen, dass sie ankommt. Ginge sie beim Neustart verloren, wäre
        /// die Annahme ein leeres Versprechen - und niemand könnte den Verlust
        /// bemerken, denn der Absender hat seine Bestätigung schon.
        ///
        /// Der Zeitpunkt gehört dazu: Ohne ihn trüge die nachgereichte
        /// Nachricht nach einem Neustart einen falschen oder keinen
        /// XEP-0203-Stempel und behauptete damit, sie sei von jetzt.
        /// </remarks>
        [Test]
        public void TheOfflineStore_SurvivesTheRoundTrip()
        {

            var account    = new XMPPAccount("alice@localhost", "geheim");
            var zeitpunkt  = new DateTimeOffset(2026, 7, 29, 14, 5, 9, TimeSpan.Zero);

            account.StoreOfflineMessage("<message from='bob@localhost' to='alice@localhost' type='chat'>" +
                                        "<body>Erste</body></message>",
                                        zeitpunkt);

            account.StoreOfflineMessage("<message from='bob@localhost' to='alice@localhost' type='chat'>" +
                                        "<body>Zweite</body></message>",
                                        zeitpunkt.AddMinutes(3));

            new FileAccountStore(_datei).Save(account);

            var gelesen = new FileAccountStore(_datei).Load().Single().OfflineMessages;

            Assert.Multiple(() =>
            {

                Assert.That(gelesen,                Has.Count.EqualTo(2));
                Assert.That(gelesen[0].Stanza,      Does.Contain("Erste"));
                Assert.That(gelesen[1].Stanza,      Does.Contain("Zweite"),
                            "Die Reihenfolge des Eingangs übersteht den Neustart.");
                Assert.That(gelesen[0].StoredAt,    Is.EqualTo(zeitpunkt));

            });

        }

        #endregion

        #region SavingTwice_DoesNotDuplicate()

        /// <summary>
        /// Zweimal dasselbe Konto speichern ergibt einen Eintrag, keinen
        /// zweiten - <c>Save</c> heisst anlegen <b>oder</b> fortschreiben.
        /// </summary>
        [Test]
        public void SavingTwice_DoesNotDuplicate()
        {

            var store    = new FileAccountStore(_datei);
            var account  = new XMPPAccount("alice@localhost", "geheim");

            store.Save(account);

            account.SetRosterEntry(new RosterEntry("bob@localhost"));
            store.Save(account);

            var gelesen = store.Load().ToList();

            Assert.Multiple(() =>
            {
                Assert.That(gelesen,             Has.Count.EqualTo(1));
                Assert.That(gelesen[0].Roster,   Has.Count.EqualTo(1));
            });

        }

        #endregion

        #region DeletedAccount_IsGone()

        /// <summary>
        /// Löschen entfernt genau ein Konto, und ein unbekannter JID ist kein
        /// Fehler.
        /// </summary>
        [Test]
        public void DeletedAccount_IsGone()
        {

            var store = new FileAccountStore(_datei);

            store.Save(new XMPPAccount("alice@localhost", "geheim"));
            store.Save(new XMPPAccount("bob@localhost",   "geheim"));

            store.Delete("alice@localhost");
            store.Delete("gibtesnicht@localhost");

            Assert.That(store.Load().Select(a => a.BareJid),
                        Is.EqualTo(new[] { "bob@localhost" }));

        }

        #endregion

        #region TheFile_ContainsNoPassword()

        /// <summary>
        /// Die Zusage, an der alles hängt: in der Datei steht das Passwort
        /// nicht - weder im Klartext noch als Base64.
        /// </summary>
        [Test]
        public void TheFile_ContainsNoPassword()
        {

            const String passwort = "Zwiebelfisch-Quastenflosser-42";

            new FileAccountStore(_datei).Save(new XMPPAccount("alice@localhost", passwort));

            var inhalt = File.ReadAllText(_datei);

            var base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(passwort));

            Assert.Multiple(() =>
            {
                Assert.That(inhalt, Does.Not.Contain(passwort), "Passwort im Klartext in der Datei.");
                Assert.That(inhalt, Does.Not.Contain(base64),   "Passwort als Base64 in der Datei.");
            });

        }

        #endregion

        #region Server_LoadsExistingAccountsOnStart()

        /// <summary>
        /// Und der Server benutzt das auch: ein Konto aus einer früheren
        /// Instanz ist nach dem Neustart wieder da.
        /// </summary>
        [Test]
        public async Task Server_LoadsExistingAccountsOnStart()
        {

            await using (var erster = new XMPPServer(accountStore: new FileAccountStore(_datei),
                                                     useTLS:       false))
            {
                erster.AddAccount("alice", "geheim");
            }

            await using var zweiter = new XMPPServer(accountStore: new FileAccountStore(_datei),
                                                     useTLS:       false);

            var account = zweiter.GetAccount("alice@localhost");

            Assert.Multiple(() =>
            {
                Assert.That(account,                               Is.Not.Null);
                Assert.That(account!.Credentials.Verify("geheim"), Is.True);
            });

        }

        #endregion

        #region Server_PersistsRosterChanges()

        /// <summary>
        /// Roster-Änderungen am laufenden Server landen im Speicher, ohne dass
        /// jemand sie ausdrücklich sichern müsste.
        /// </summary>
        /// <remarks>
        /// Das ist die eigentliche Fehlerquelle bei so einer Umstellung: das
        /// Anlegen eines Kontos vergisst niemand zu speichern, eine
        /// Roster-Änderung mitten im Subscription-Handshake schon.
        /// </remarks>
        [Test]
        public async Task Server_PersistsRosterChanges()
        {

            await using var server = new XMPPServer(accountStore: new FileAccountStore(_datei),
                                                    useTLS:       false);

            var account = server.AddAccount("alice", "geheim");

            account.SetRosterEntry(new RosterEntry("bob@localhost", "Bob", "both"));

            var gelesen = new FileAccountStore(_datei).Load().Single();

            Assert.That(gelesen.Roster.Select(e => e.Jid),
                        Is.EqualTo(new[] { "bob@localhost" }));

        }

        #endregion

        #region InMemoryStore_IsTheDefault()

        /// <summary>
        /// Ohne Angabe bleibt alles wie bisher: im Speicher, und beim Beenden
        /// weg.
        /// </summary>
        [Test]
        public async Task InMemoryStore_IsTheDefault()
        {

            await using var erster = new XMPPServer(useTLS: false);
            erster.AddAccount("alice", "geheim");

            await using var zweiter = new XMPPServer(useTLS: false);

            Assert.Multiple(() =>
            {
                Assert.That(erster.GetAccount("alice@localhost"),  Is.Not.Null);
                Assert.That(zweiter.GetAccount("alice@localhost"), Is.Null,
                            "Ein zweiter Server darf die Konten des ersten nicht sehen.");
            });

        }

        #endregion

    }

}
