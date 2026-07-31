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

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Der SASL-Downgrade: Der Server bietet plötzlich weniger an als beim
    /// letzten Mal.
    /// </summary>
    /// <remarks>
    /// Der Client nahm bisher, was angekündigt wurde. Das ist bequem und für
    /// den ehrlichen Server auch richtig - nur ist die Ankündigung nicht
    /// authentifiziert. Ein Zwischenmann streicht die SCRAM-Angebote aus den
    /// Features, übrig bleibt PLAIN, und der Client schickt das Passwort
    /// selbst statt eines Beweises, dass er es kennt.
    ///
    /// Der Angriff braucht dafür nicht den ersten Verbindungsaufbau: Der
    /// Client kommt nach jedem Abriss von allein wieder, und ein Abriss lässt
    /// sich erzwingen. Genau diese zweite Anmeldung ist es, die hier gedeckt
    /// wird - der Testserver spielt den Zwischenmann, indem er seine
    /// Mechanismen zwischen den beiden Verbindungen ändert.
    /// </remarks>
    [TestFixture]
    public class SaslDowngradeTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Zählt, wie oft die Verbindung seit dem Anmelden in
        /// <see cref="ConnectionState.Connected"/> gegangen ist.
        /// </summary>
        /// <remarks>
        /// Gezählt statt abgefragt: Ein <c>WaitFor(() =&gt; client.IsConnected)</c>
        /// wäre schon erfüllt, bevor der Abriss überhaupt bemerkt wurde, und
        /// bewiese dann nichts über den zweiten Aufbau.
        /// </remarks>
        private static Func<Int32> CountReconnects(XMPPClient client)
        {

            var count = 0;

            client.Connection.OnStateChanged += (alt, neu) =>
            {
                if (neu == ConnectionState.Connected)
                    Interlocked.Increment(ref count);
            };

            return () => Volatile.Read(ref count);

        }

        /// <summary>Alles, was der Server je an <c>&lt;auth/&gt;</c> gesehen hat.</summary>
        private Boolean SawAuthWith(String mechanism)

            => Server.AllReceived.Any(f => f.Contains($"mechanism='{mechanism}'", StringComparison.Ordinal));

        #endregion


        #region AWeakerServerOnTheSecondConnect_IsRefused()

        /// <summary>
        /// Der Kern: Was beim ersten Mal über SCRAM lief, darf beim zweiten
        /// nicht über PLAIN laufen.
        /// </summary>
        [Test]
        public async Task AWeakerServerOnTheSecondConnect_IsRefused()
        {

            var client = await ConnectClientAsync();

            Assert.That(client.Connection.PinnedSaslMechanism, Is.EqualTo("SCRAM-SHA-256"),
                        "Vorbedingung: die erste Anmeldung muss über SCRAM-SHA-256 gelaufen sein.");

            var errors = new List<String>();
            client.OnError += e => errors.Add(e);

            // Ab jetzt bietet der Server nur noch PLAIN an.
            Server.OfferedSaslMechanisms.Clear();
            Server.OfferedSaslMechanisms.Add("PLAIN");

            client.KillConnection();

            await WaitFor(() => errors.Count > 0, "die Ablehnung des Downgrades");

            Assert.Multiple(() =>
            {

                Assert.That(client.IsConnected, Is.False,
                            "Nach einem Downgrade darf keine Verbindung zustande kommen.");

                Assert.That(errors.Any(e => e.Contains("Downgrade", StringComparison.OrdinalIgnoreCase)),
                            Is.True,
                            $"Der Grund muss benannt werden. Gemeldet wurde: {String.Join(" | ", errors)}");

                Assert.That(SawAuthWith("PLAIN"), Is.False,
                            "Es darf gar kein <auth/> über PLAIN hinausgegangen sein.");

            });

        }

        #endregion

        #region TheRefusalHappensBeforeThePasswordGoesOut()

        /// <summary>
        /// Und zwar, bevor der erste Rahmen hinausgeht - bei PLAIN steht das
        /// Passwort in genau diesem <c>&lt;auth/&gt;</c>.
        /// </summary>
        /// <remarks>
        /// Eine Prüfung, die erst die Antwort des Servers ansieht, käme zu
        /// spät: Der Zwischenmann hätte, worauf er aus war, und die Anmeldung
        /// danach abzubrechen nähme es ihm nicht wieder ab.
        /// </remarks>
        [Test]
        public async Task TheRefusalHappensBeforeThePasswordGoesOut()
        {

            const String passwort = "Zwiebelfisch-Quastenflosser-42";

            Server.AddAccount("alice", passwort);

            var client = CreateClient("alice", password: passwort);
            await client.ConnectAsync();

            var errors = new List<String>();
            client.OnError += e => errors.Add(e);

            Server.OfferedSaslMechanisms.Clear();
            Server.OfferedSaslMechanisms.Add("PLAIN");

            client.KillConnection();

            await WaitFor(() => errors.Count > 0, "die Ablehnung des Downgrades");

            var base64 = Convert.ToBase64String(
                             System.Text.Encoding.UTF8.GetBytes($"\0alice\0{passwort}"));

            Assert.Multiple(() =>
            {

                Assert.That(Server.AllReceived.Any(f => f.Contains(passwort, StringComparison.Ordinal)),
                            Is.False,
                            "Das Passwort stand im Klartext in einem Frame.");

                Assert.That(Server.AllReceived.Any(f => f.Contains(base64, StringComparison.Ordinal)),
                            Is.False,
                            "Das Passwort ging als PLAIN-Nutzlast hinaus, bevor das Downgrade auffiel.");

            });

        }

        #endregion

        #region AnUnchangedServerOnTheSecondConnect_IsAccepted()

        /// <summary>
        /// Die Gegenprobe: Bleibt das Angebot gleich, kommt der Client ganz
        /// normal wieder.
        /// </summary>
        /// <remarks>
        /// Ohne sie bestünde die Sammlung auch dann, wenn die Untergrenze jeden
        /// zweiten Verbindungsaufbau abwiese.
        /// </remarks>
        [Test]
        public async Task AnUnchangedServerOnTheSecondConnect_IsAccepted()
        {

            var client       = await ConnectClientAsync();
            var reconnects   = CountReconnects(client);

            var errors = new List<String>();
            client.OnError += e => errors.Add(e);

            client.KillConnection();

            await WaitFor(() => reconnects() >= 1, "die zweite Anmeldung");

            Assert.Multiple(() =>
            {

                Assert.That(client.IsConnected, Is.True);

                Assert.That(client.Connection.PinnedSaslMechanism, Is.EqualTo("SCRAM-SHA-256"));

                Assert.That(errors, Is.Empty,
                            $"Gemeldet wurde: {String.Join(" | ", errors)}");

            });

        }

        #endregion

        #region AStrongerServerOnTheSecondConnect_IsAccepted()

        /// <summary>
        /// Nach oben ist die Untergrenze offen: Ein Server, der SCRAM-SHA-256
        /// nachrüstet, darf nicht daran scheitern, dass beim letzten Mal
        /// SCRAM-SHA-1 lief.
        /// </summary>
        /// <remarks>
        /// Eine Anheftung, die auf Gleichheit statt auf Stärke prüft, wäre
        /// kürzer zu schreiben und würde hier scheitern.
        /// </remarks>
        [Test]
        public async Task AStrongerServerOnTheSecondConnect_IsAccepted()
        {

            Server.OfferedSaslMechanisms.Clear();
            Server.OfferedSaslMechanisms.Add("SCRAM-SHA-1");

            var client       = await ConnectClientAsync();
            var reconnects   = CountReconnects(client);

            Assert.That(client.Connection.PinnedSaslMechanism, Is.EqualTo("SCRAM-SHA-1"),
                        "Vorbedingung: die erste Anmeldung muss über SCRAM-SHA-1 gelaufen sein.");

            Server.OfferedSaslMechanisms.Add("SCRAM-SHA-256");

            client.KillConnection();

            await WaitFor(() => reconnects() >= 1, "die zweite Anmeldung");

            Assert.Multiple(() =>
            {

                Assert.That(client.IsConnected, Is.True);

                Assert.That(client.Connection.PinnedSaslMechanism, Is.EqualTo("SCRAM-SHA-256"),
                            "Die Anheftung muss dem stärkeren Angebot folgen.");

                Assert.That(SawAuthWith("SCRAM-SHA-256"), Is.True);

            });

        }

        #endregion

        #region TheMinimumHoldsOnTheVeryFirstConnect()

        /// <summary>
        /// Die gesetzte Untergrenze wirkt ohne jede vorige Anmeldung.
        /// </summary>
        /// <remarks>
        /// Die Anheftung ist ein Trust-On-First-Use und schützt den ersten
        /// Verbindungsaufbau naturgemäss nicht. Wer weiss, was sein Server
        /// kann, sagt es - und braucht kein erstes Mal.
        /// </remarks>
        [Test]
        public async Task TheMinimumHoldsOnTheVeryFirstConnect()
        {

            Server.OfferedSaslMechanisms.Clear();
            Server.OfferedSaslMechanisms.Add("PLAIN");

            Server.AddAccount("alice");

            var client = CreateClient("alice");
            client.Connection.MaxReconnectAttempts   = 0;
            client.Connection.MinimumSaslMechanism   = "SCRAM-SHA-256";

            var errors = new List<String>();
            client.OnError += e => errors.Add(e);

            await FailingConnectAsync(client);

            Assert.Multiple(() =>
            {

                Assert.That(client.IsConnected, Is.False,
                            "Unter der verlangten Untergrenze darf keine Verbindung zustande kommen.");

                Assert.That(SawAuthWith("PLAIN"), Is.False);

                Assert.That(errors, Is.Not.Empty);

            });

        }

        #endregion

        #region TheMinimumIsMetByAStrongerServer()

        /// <summary>
        /// Und die Gegenprobe dazu: Erfüllt der Server sie, ändert sie nichts.
        /// </summary>
        [Test]
        public async Task TheMinimumIsMetByAStrongerServer()
        {

            Server.AddAccount("alice");

            var client = CreateClient("alice");
            client.Connection.MinimumSaslMechanism = "SCRAM-SHA-1";

            await client.ConnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.True);
                Assert.That(client.Connection.PinnedSaslMechanism, Is.EqualTo("SCRAM-SHA-256"));
            });

        }

        #endregion

        #region AnUnknownMinimum_IsRefusedAtTheSetter()

        /// <summary>
        /// Ein Mechanismusname, den der Client nicht kennt, wird beim Setzen
        /// abgewiesen.
        /// </summary>
        /// <remarks>
        /// Sonst wäre der Tippfehler die gefährlichste Eingabe: Ein unbekannter
        /// Name hat die Stärke 0, und eine Untergrenze von 0 verlangt gar
        /// nichts. Der Aufrufer bekäme lautlos das Gegenteil dessen, was er
        /// hinschrieb.
        /// </remarks>
        [Test]
        public void AnUnknownMinimum_IsRefusedAtTheSetter()
        {

            var client = CreateClient("alice");

            Assert.That(() => client.Connection.MinimumSaslMechanism = "SCRAM-SHA-512",
                        Throws.TypeOf<ArgumentException>());

        }

        #endregion

    }

}
