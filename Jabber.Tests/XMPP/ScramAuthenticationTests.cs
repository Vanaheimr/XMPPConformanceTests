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
    /// SCRAM zwischen echtem Client und echtem Server (RFC 5802, RFC 7677).
    ///
    /// Der Client beherrschte SCRAM von Anfang an, aber der Testserver bot nur
    /// PLAIN an - der ganze Pfad war deshalb nur gegen die Testvektoren aus dem
    /// RFC gepr\u00FCft, nie im Gespräch. Insbesondere die zweite Hälfte, in der der
    /// Client die Signatur des Servers pr\u00FCft, hatte keinen einzigen Test, der
    /// sie beim Versagen erwischt hätte.
    ///
    /// Jetzt spricht der Server SCRAM, und weil der Client von sich aus den
    /// stärksten angebotenen Mechanismus wählt, läuft die gesamte \u00FCbrige Suite
    /// ebenfalls dar\u00FCber.
    /// </summary>
    [TestFixture]
    public class ScramAuthenticationTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Ein Client, der nach einem Fehlschlag nicht zwanzigmal neu
        /// aufbaut - die Frage ist beim ersten Versuch beantwortet.
        /// </summary>
        private XMPPClient SingleAttemptClient(String localPart = "alice",
                                               String password  = "pw")
        {

            if (Server.GetAccount($"{localPart}@{Server.Domain}") is null)
                Server.AddAccount(localPart);

            var client = CreateClient(localPart, password: password);
            client.Connection.MaxReconnectAttempts = 0;

            return client;

        }

        #endregion


        #region Client_ChoosesScramSha256()

        /// <summary>
        /// Bietet der Server alles an, nimmt der Client den stärksten
        /// Mechanismus - und schickt insbesondere kein Passwort mehr.
        /// </summary>
        [Test]
        public async Task Client_ChoosesScramSha256()
        {

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            Assert.Multiple(() =>
            {

                Assert.That(session.Received.Any(f => f.Contains("mechanism='SCRAM-SHA-256'", StringComparison.Ordinal)),
                            Is.True,
                            "Der Client muss SCRAM-SHA-256 wählen, wenn es angeboten wird.");

                Assert.That(session.Received.Any(f => f.Contains("mechanism='PLAIN'", StringComparison.Ordinal)),
                            Is.False,
                            "Neben SCRAM darf PLAIN nicht mehr vorkommen.");

            });

        }

        #endregion

        #region ScramSha1_IsUsedWhenItIsAllThereIs()

        /// <summary>
        /// Der schwächere Mechanismus muss ebenfalls funktionieren - ein
        /// Server, der nur SCRAM-SHA-1 kann, ist der Normalfall im Bestand.
        /// </summary>
        [Test]
        public async Task ScramSha1_IsUsedWhenItIsAllThereIs()
        {

            Server.OfferedSaslMechanisms.Clear();
            Server.OfferedSaslMechanisms.Add("SCRAM-SHA-1");

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.True);
                Assert.That(session.Received.Any(f => f.Contains("mechanism='SCRAM-SHA-1'", StringComparison.Ordinal)),
                            Is.True);
            });

        }

        #endregion

        #region PlainOnly_StillWorks()

        /// <summary>
        /// Und PLAIN auch weiterhin - der Pfad ist jetzt der Ausnahmefall und
        /// bliebe sonst ungetestet.
        /// </summary>
        [Test]
        public async Task PlainOnly_StillWorks()
        {

            Server.OfferedSaslMechanisms.Clear();
            Server.OfferedSaslMechanisms.Add("PLAIN");

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.True);
                Assert.That(session.Received.Any(f => f.Contains("mechanism='PLAIN'", StringComparison.Ordinal)),
                            Is.True);
            });

        }

        #endregion

        #region WrongPassword_IsRejected()

        /// <summary>
        /// Die Gegenprobe zur Anmeldung: mit falschem Passwort kommt der
        /// Client nicht durch.
        /// </summary>
        /// <remarks>
        /// Bei SCRAM merkt das der Server erst an der client-final-message -
        /// das Passwort selbst geht nie \u00FCber die Leitung, nur ein Beweis, dass
        /// der Client es kennt.
        /// </remarks>
        [Test]
        public async Task WrongPassword_IsRejected()
        {

            Server.AddAccount("alice");

            var client  = SingleAttemptClient(password: "falsch");
            var errors  = new List<String>();

            client.OnError += e => errors.Add(e);

            await FailingConnectAsync(client);

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.False);
                Assert.That(errors,             Is.Not.Empty);
            });

        }

        #endregion

        #region UnknownAccount_IsRejected()

        /// <summary>
        /// Ein Konto, das es nicht gibt, ebenso.
        /// </summary>
        [Test]
        public async Task UnknownAccount_IsRejected()
        {

            var client = CreateClient("niemand");
            client.Connection.MaxReconnectAttempts = 0;

            var errors = new List<String>();
            client.OnError += e => errors.Add(e);

            await FailingConnectAsync(client);

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.False);
                Assert.That(errors,             Is.Not.Empty);
            });

        }

        #endregion

        #region Success_CarriesTheServerSignature()

        /// <summary>
        /// Das <c>&lt;success/&gt;</c> trägt die server-final-message
        /// (RFC 5802, Abschnitt 3) - ohne sie hätte der Client nichts zu
        /// pr\u00FCfen.
        /// </summary>
        [Test]
        public async Task Success_CarriesTheServerSignature()
        {

            var client   = await ConnectClientAsync();
            var session  = Server.SessionOf(client.FullJid)!;

            var success = session.Sent.FirstOrDefault(f => f.StartsWith("<success", StringComparison.Ordinal));

            Assert.That(success, Is.Not.Null, "Kein <success/> gefunden.");

            var payload = success!.Replace("<success xmlns='urn:ietf:params:xml:ns:xmpp-sasl'>", "")
                                  .Replace("</success>", "");

            Assert.That(payload, Is.Not.Empty, "Das <success/> kam ohne server-final-message.");

            var entpackt = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));

            Assert.That(entpackt, Does.StartWith("v="),
                        $"Die server-final-message muss mit v= beginnen, war aber: {entpackt}");

        }

        #endregion

        #region CorruptedServerSignature_IsRefused()

        /// <summary>
        /// Der Kern: eine falsche Serversignatur muss den Client die Anmeldung
        /// verweigern lassen.
        /// </summary>
        /// <remarks>
        /// Genau das ist die zweite Hälfte von SCRAM. Ein Zwischenmann, der
        /// das Passwort nicht kennt, kann den Client zwar zu einer
        /// client-final-message bewegen, aber diese Signatur nicht erzeugen.
        /// Wer sie nicht pr\u00FCft, hat sich einseitig statt gegenseitig
        /// authentifiziert.
        /// </remarks>
        [Test]
        public async Task CorruptedServerSignature_IsRefused()
        {

            Server.CorruptScramSignature = true;

            var client  = SingleAttemptClient();
            var errors  = new List<String>();

            client.OnError += e => errors.Add(e);

            await FailingConnectAsync(client);

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.False,
                            "Bei falscher Serversignatur darf keine Verbindung zustande kommen.");

                Assert.That(errors.Any(e => e.Contains("Signatur", StringComparison.OrdinalIgnoreCase)),
                            Is.True,
                            $"Der Grund muss benannt werden. Gemeldet wurde: {String.Join(" | ", errors)}");
            });

        }

        #endregion

        #region MissingServerSignature_IsRefused()

        /// <summary>
        /// Und eine fehlende ebenso - der verlockendere Fehler, weil ein
        /// leeres <c>&lt;success/&gt;</c> wie ein Erfolg aussieht.
        /// </summary>
        [Test]
        public async Task MissingServerSignature_IsRefused()
        {

            Server.OmitScramSignature = true;

            var client  = SingleAttemptClient();
            var errors  = new List<String>();

            client.OnError += e => errors.Add(e);

            await FailingConnectAsync(client);

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.False,
                            "Ohne Serversignatur darf keine Verbindung zustande kommen.");

                Assert.That(errors, Is.Not.Empty);
            });

        }

        #endregion

        #region ADifferentlyComposedPassword_StillMatches()

        /// <summary>
        /// Dasselbe Passwort, anders zusammengesetzt, muss dieselbe Anmeldung
        /// ergeben — \u00FCber SCRAM wie \u00FCber PLAIN.
        /// </summary>
        /// <remarks>
        /// Ein <c>\u00FC</c> kommt je nach Tastatur und Betriebssystem als ein
        /// Zeichen an oder als <c>u</c> mit angehängten zwei Punkten. F\u00FCr den
        /// Menschen davor ist das dasselbe Passwort; f\u00FCr einen Byte-Vergleich
        /// nicht. Genau daf\u00FCr steht SASLprep vor der Schl\u00FCsselableitung — und
        /// solange es nur aus einem NFKC bestand, hing es zusätzlich am
        /// Mechanismus: SCRAM normalisierte, PLAIN gar nicht.
        /// </remarks>
        [Test]
        public async Task ADifferentlyComposedPassword_StillMatches()
        {

            // Einmal zusammengesetzt, einmal zerlegt (u + kombinierendes Trema).
            const String zusammengesetzt  = "Gr\u00FCße-42";
            const String zerlegt          = "Gru\u0308ße-42";

            Server.AddAccount("alice", zusammengesetzt);

            var ueberScram = CreateClient("alice", password: zerlegt);
            await ueberScram.ConnectAsync();

            Assert.That(ueberScram.IsConnected, Is.True,
                        "Über SCRAM muss die zerlegte Schreibweise passen.");

            // Und dasselbe noch einmal, wenn der Server nur PLAIN anbietet.
            Server.OfferedSaslMechanisms.Clear();
            Server.OfferedSaslMechanisms.Add("PLAIN");

            var ueberPlain = CreateClient("alice", password: zerlegt);
            ueberPlain.Connection.Resource = "zweite";
            await ueberPlain.ConnectAsync();

            Assert.That(ueberPlain.IsConnected, Is.True,
                        "Über PLAIN ebenso - sonst hinge es am Mechanismus.");

            // Und hinausgegangen ist die vorbereitete Fassung.
            //
            // Dass die Anmeldung gelingt, belegt das nämlich nicht: Der Server
            // bereitet vor, was bei ihm ankommt, und käme deshalb auch mit der
            // zerlegten Fassung zurecht. Geprüft werden muss, was auf der
            // Leitung steht - sonst bliebe die Client-Hälfte ungedeckt, und ein
            // Server, der selbst nicht vorbereitet, liesse uns nicht mehr herein.
            var sitzung   = Server.SessionOf(ueberPlain.FullJid)!;
            var erwartet  = Convert.ToBase64String(
                                System.Text.Encoding.UTF8.GetBytes($"\0alice\0{zusammengesetzt}"));

            Assert.That(sitzung.Received.Any(f => f.Contains(erwartet, StringComparison.Ordinal)),
                        Is.True,
                        "Das <auth/> muss das nach SASLprep vorbereitete Passwort tragen.");

        }

        #endregion

        #region AnUnusablePassword_IsRejectedAndDoesNotThrow()

        /// <summary>
        /// Ein Passwort, das sich nicht nach SASLprep vorbereiten lässt, ist
        /// ein Fehlversuch — und kein Serverfehler.
        /// </summary>
        /// <remarks>
        /// Der Weg dahin f\u00FChrt \u00FCber die Leitung: Was in einem
        /// PLAIN-<c>&lt;auth/&gt;</c> steht, bestimmt die Gegenstelle, und ein
        /// Steuerzeichen darin darf den Server nicht umwerfen. Die Pr\u00FCfung
        /// wandert deshalb bewusst in ein <c>false</c> statt in eine Ausnahme.
        /// </remarks>
        [Test]
        public async Task AnUnusablePassword_IsRejectedAndDoesNotThrow()
        {

            Server.AddAccount("alice");
            Server.OfferedSaslMechanisms.Clear();
            Server.OfferedSaslMechanisms.Add("PLAIN");

            var konto = Server.GetAccount($"alice@{Server.Domain}")!;

            Assert.Multiple(() =>
            {

                Assert.That(() => konto.Credentials.Verify("pw\u0007"), Throws.Nothing,
                            "Ein unbrauchbares Passwort darf keine Ausnahme auslösen.");

                Assert.That(konto.Credentials.Verify("pw\u0007"), Is.False);

                // Das richtige Passwort geht weiterhin durch.
                Assert.That(konto.Credentials.Verify("pw"), Is.True);

            });

            await Task.CompletedTask;

        }

        #endregion

        #region ThePasswordNeverGoesOverTheWire()

        /// <summary>
        /// Die Zusage, f\u00FCr die SCRAM \u00FCberhaupt da ist: das Passwort taucht in
        /// keinem gesendeten Frame auf.
        /// </summary>
        /// <remarks>
        /// Gepr\u00FCft wird gegen ein auffälliges Passwort, damit ein zufälliges
        /// Vorkommen in einem Base64-Block ausgeschlossen ist.
        /// </remarks>
        [Test]
        public async Task ThePasswordNeverGoesOverTheWire()
        {

            const String passwort = "Zwiebelfisch-Quastenflosser-42";

            Server.AddAccount("alice", passwort);

            var client = CreateClient("alice", password: passwort);
            await client.ConnectAsync();

            var session = Server.SessionOf(client.FullJid)!;

            var imKlartext = session.Received.Where(f => f.Contains(passwort, StringComparison.Ordinal)).ToList();

            // Und dasselbe f\u00FCr die Base64-Fassung, wie PLAIN sie schicken w\u00FCrde.
            var base64 = Convert.ToBase64String(
                             System.Text.Encoding.UTF8.GetBytes($"\0alice\0{passwort}"));

            var kodiert = session.Received.Where(f => f.Contains(base64, StringComparison.Ordinal)).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(imKlartext, Is.Empty, "Das Passwort stand im Klartext in einem Frame.");
                Assert.That(kodiert,    Is.Empty, "Das Passwort stand als PLAIN-Nutzlast in einem Frame.");
            });

        }

        #endregion

    }

}
