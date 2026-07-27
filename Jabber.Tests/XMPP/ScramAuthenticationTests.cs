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
    /// RFC geprüft, nie im Gespräch. Insbesondere die zweite Hälfte, in der der
    /// Client die Signatur des Servers prüft, hatte keinen einzigen Test, der
    /// sie beim Versagen erwischt hätte.
    ///
    /// Jetzt spricht der Server SCRAM, und weil der Client von sich aus den
    /// stärksten angebotenen Mechanismus wählt, läuft die gesamte übrige Suite
    /// ebenfalls darüber.
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
        /// das Passwort selbst geht nie über die Leitung, nur ein Beweis, dass
        /// der Client es kennt.
        /// </remarks>
        [Test]
        public async Task WrongPassword_IsRejected()
        {

            Server.AddAccount("alice");

            var client  = SingleAttemptClient(password: "falsch");
            var errors  = new List<String>();

            client.OnError += e => errors.Add(e);

            await client.ConnectAsync();

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

            await client.ConnectAsync();

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
        /// prüfen.
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
        /// Wer sie nicht prüft, hat sich einseitig statt gegenseitig
        /// authentifiziert.
        /// </remarks>
        [Test]
        public async Task CorruptedServerSignature_IsRefused()
        {

            Server.CorruptScramSignature = true;

            var client  = SingleAttemptClient();
            var errors  = new List<String>();

            client.OnError += e => errors.Add(e);

            await client.ConnectAsync();

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

            await client.ConnectAsync();

            Assert.Multiple(() =>
            {
                Assert.That(client.IsConnected, Is.False,
                            "Ohne Serversignatur darf keine Verbindung zustande kommen.");

                Assert.That(errors, Is.Not.Empty);
            });

        }

        #endregion

        #region ThePasswordNeverGoesOverTheWire()

        /// <summary>
        /// Die Zusage, für die SCRAM überhaupt da ist: das Passwort taucht in
        /// keinem gesendeten Frame auf.
        /// </summary>
        /// <remarks>
        /// Geprüft wird gegen ein auffälliges Passwort, damit ein zufälliges
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

            // Und dasselbe für die Base64-Fassung, wie PLAIN sie schicken würde.
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
