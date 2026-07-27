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
    /// XEP-0288 über echte Sockets - und zwar in der Lage, für die es die
    /// Erweiterung gibt: die Gegenstelle kann uns <b>nicht</b> anwählen.
    /// </summary>
    /// <remarks>
    /// Der Aufbau ist absichtlich einseitig. <c>links</c> kennt <c>rechts</c>,
    /// <c>rechts</c> kennt <c>links</c> nicht - kein Eintrag, kein Resolver,
    /// nichts. Damit steht und fällt die Antwort mit der Rückrichtung.
    ///
    /// Der übliche <see cref="TcpServerLinks.Connect"/> taugt hier nicht: er
    /// trägt beide Seiten gegenseitig ein, und dann käme die Antwort über eine
    /// eigene Verbindung an - der Test bestünde, ohne von Bidi je Gebrauch
    /// gemacht zu haben. Genau deshalb prüft er auch
    /// <see cref="TcpServerLinks.BidirectionalDeliveryCount"/> und nicht nur
    /// die Ankunft.
    /// </remarks>
    [TestFixture]
    public class BidirectionalFederationTests
    {

        #region Data

        private XMPPServer      _links   = null!;
        private XMPPServer      _rechts  = null!;
        private TcpServerLinks  _linksS  = null!;
        private TcpServerLinks  _rechtsS = null!;

        private readonly List<XMPPClient> _clients = [];

        #endregion

        #region SetUp / TearDown

        [TearDown]
        public async Task Abraeumen()
        {

            foreach (var client in _clients)
            {
                try { await client.DisposeAsync(); } catch { /* im Teardown egal */ }
            }

            _clients.Clear();

            if (_links  is not null) await _links.DisposeAsync();
            if (_rechts is not null) await _rechts.DisposeAsync();

        }

        #endregion

        #region Hilfsfunktionen

        /// <summary>
        /// Zwei Server, aber nur eine Richtung ist eingetragen.
        /// </summary>
        private void EinseitigVerbinden(Boolean bidi)
        {

            _links   = new XMPPServer("links.example");
            _rechts  = new XMPPServer("rechts.example");

            _links.Start();
            _rechts.Start();

            // SASL-EXTERNAL und nicht Dialback: dessen Rueckfrage ginge
            // ausgerechnet in die Richtung, die es hier nicht gibt. Rechts
            // muesste links anwaehlen, um den Schluessel pruefen zu lassen -
            // und koennte dann auch gleich die Antwort schicken. Der Nachweis
            // ueber das Zertifikat kommt ohne Rueckweg aus, und genau darum
            // geht es hier.
            _linksS   = new TcpServerLinks(_links) {
                            UseSaslExternal          = true,
                            UseBidirectionalStreams  = bidi
                        };

            _rechtsS  = new TcpServerLinks(_rechts) {
                            UseSaslExternal          = true,
                            UseBidirectionalStreams  = bidi
                        };

            // Nur links kennt rechts. Ausdrücklich die Adresse und nicht
            // "localhost": der Listener bindet IPv4-Loopback, und ein Name, der
            // zuerst nach IPv6 auflöst, kostet je Verbindung den Fallback ab.
            _linksS.AddPeer(_rechts.Domain,
                            System.Net.IPAddress.Loopback.ToString(),
                            _rechtsS.Port,
                            TcpTlsMode.StartTls,
                            _rechts.IsOwnCertificate);

        }

        private async Task<XMPPClient> ConnectAsync(XMPPServer server, String localPart)
        {

            if (server.GetAccount($"{localPart}@{server.Domain}") is null)
                server.AddAccount(localPart);

            var connection = new XMPPConnection($"{localPart}@{server.Domain}", "pw", server.Uri) {
                                 KeepaliveEnabled            = false,
                                 MaxReconnectAttempts        = 0,
                                 ServerCertificateValidator  = server.IsOwnCertificate
                             };

            var client = new XMPPClient(connection);
            _clients.Add(client);

            await client.ConnectAsync();

            return client;

        }

        private static async Task WarteAuf(Func<Boolean> bedingung, String was)
        {
            Assert.That(await XMPPServer.WaitUntilAsync(bedingung),
                        Is.True, $"Zeitüberschreitung beim Warten auf: {was}");
        }

        #endregion


        #region TheAnswerComesBackOverTheSameConnection()

        /// <summary>
        /// Der Kern von XEP-0288: die Antwort nimmt die Verbindung, über die
        /// die Frage kam.
        /// </summary>
        [Test]
        public async Task TheAnswerComesBackOverTheSameConnection()
        {

            EinseitigVerbinden(bidi: true);

            var alice  = await ConnectAsync(_links,  "alice");
            var juliet = await ConnectAsync(_rechts, "juliet");

            _links.GetAccount(alice.BareJid)!.SetRosterEntry(new RosterEntry(juliet.BareJid, null, "both"));
            _rechts.GetAccount(juliet.BareJid)!.SetRosterEntry(new RosterEntry(alice.BareJid, null, "both"));

            var beiJuliet = new List<String>();
            var beiAlice  = new List<String>();

            juliet.OnMessage += m => beiJuliet.Add(m.Body ?? "");
            alice.OnMessage  += m => beiAlice.Add(m.Body ?? "");

            await alice.SendMessageAsync(juliet.BareJid, "Hin");
            await WarteAuf(() => beiJuliet.Count > 0, "die Nachricht bei Juliet");

            // Und jetzt die Antwort - für die rechts keinen Weg zu links hat.
            await juliet.SendMessageAsync(alice.BareJid, "Zurück");
            await WarteAuf(() => beiAlice.Any(b => b == "Zurück"), "die Antwort bei Alice");

            Assert.That(_rechtsS.BidirectionalDeliveryCount, Is.GreaterThan(0),
                        "Die Antwort kam an, aber nicht über die Rückrichtung.");

        }

        #endregion

        #region WithoutBidi_TheAnswerIsLost()

        /// <summary>
        /// Die Gegenprobe. Ohne die Erweiterung gibt es keinen Rückweg, und
        /// die Antwort geht verloren.
        /// </summary>
        /// <remarks>
        /// Ohne diesen Test bewiese der vorige nichts: käme die Antwort auch
        /// ohne Bidi an, hätte sie einen anderen Weg genommen und die
        /// Erweiterung wäre unbeteiligt gewesen.
        ///
        /// Das stille Verschwinden ist dabei der eigentliche Schaden. Juliets
        /// Client hält die Nachricht für abgeschickt, ihr Server hat sie
        /// verworfen, und niemand erfährt davon - genau so verhielt sich
        /// Prosody im Lauf aus S8.
        /// </remarks>
        [Test]
        public async Task WithoutBidi_TheAnswerIsLost()
        {

            EinseitigVerbinden(bidi: false);

            var alice  = await ConnectAsync(_links,  "alice");
            var juliet = await ConnectAsync(_rechts, "juliet");

            _links.GetAccount(alice.BareJid)!.SetRosterEntry(new RosterEntry(juliet.BareJid, null, "both"));
            _rechts.GetAccount(juliet.BareJid)!.SetRosterEntry(new RosterEntry(alice.BareJid, null, "both"));

            var beiJuliet = new List<String>();
            var beiAlice  = new List<String>();

            juliet.OnMessage += m => beiJuliet.Add(m.Body ?? "");
            alice.OnMessage  += m => beiAlice.Add(m.Body ?? "");

            await alice.SendMessageAsync(juliet.BareJid, "Hin");
            await WarteAuf(() => beiJuliet.Count > 0, "die Nachricht bei Juliet");

            await juliet.SendMessageAsync(alice.BareJid, "Zurück");

            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.Multiple(() =>
            {
                Assert.That(beiAlice.Any(b => b == "Zurück"), Is.False,
                            "Ohne Bidi darf es keinen Rückweg geben.");
                Assert.That(_rechtsS.BidirectionalDeliveryCount, Is.Zero);
            });

        }

        #endregion

        #region TheOutgoingDirection_StillWorksWithoutAReturnPath()

        /// <summary>
        /// Bidi darf die gewöhnliche Richtung nicht antasten.
        /// </summary>
        /// <remarks>
        /// Die Rückrichtung greift nur, wo es eine ausgewiesene eingehende
        /// Verbindung derselben Domain gibt. Für alles andere bleibt es beim
        /// Anwählen - sonst hätte die Erweiterung die Föderation umgebaut
        /// statt sie zu ergänzen.
        /// </remarks>
        [Test]
        public async Task TheOutgoingDirection_StillWorksWithoutAReturnPath()
        {

            EinseitigVerbinden(bidi: true);

            var alice  = await ConnectAsync(_links,  "alice");
            var juliet = await ConnectAsync(_rechts, "juliet");

            _links.GetAccount(alice.BareJid)!.SetRosterEntry(new RosterEntry(juliet.BareJid, null, "both"));

            var beiJuliet = new List<String>();
            juliet.OnMessage += m => beiJuliet.Add(m.Body ?? "");

            await alice.SendMessageAsync(juliet.BareJid, "Hin");

            await WarteAuf(() => beiJuliet.Count > 0, "die Nachricht bei Juliet");

            Assert.That(_linksS.BidirectionalDeliveryCount, Is.Zero,
                        "Links hat angewählt und keine Rückrichtung benutzt.");

        }

        #endregion

        #region TheReturnPath_GoesToTheRightDomain()

        /// <summary>
        /// Hängen mehrere Gegenstellen mit Rückrichtung an, muss eine Stanza
        /// die Verbindung <b>ihrer</b> Domain nehmen.
        /// </summary>
        /// <remarks>
        /// Der Fall, den ein Aufbau mit nur einer Gegenstelle nie zeigt - und
        /// genau daran kam eine Mutation vorbei, die den Domainabgleich aus der
        /// Auswahl entfernt: bei einer einzigen Verbindung ändert das nichts.
        ///
        /// Im Betrieb wäre es ein Leck zwischen zwei fremden Servern. Die
        /// Stanza ginge an die falsche Gegenstelle, die sie zwar verwirft
        /// (falscher Empfänger), aber vorher gelesen hat - und der eigentliche
        /// Empfänger bekäme nie etwas, ohne dass irgendwo ein Fehler aufliefe.
        ///
        /// <c>ferner</c> baut zuerst auf: eine Auswahl ohne Abgleich nähme die
        /// erstbeste Verbindung, und das wäre dann die falsche.
        /// </remarks>
        [Test]
        public async Task TheReturnPath_GoesToTheRightDomain()
        {

            _links   = new XMPPServer("links.example");
            _rechts  = new XMPPServer("rechts.example");

            var ferner = new XMPPServer("ferner.example");

            _links.Start();
            _rechts.Start();
            ferner.Start();

            try
            {

                _linksS   = new TcpServerLinks(_links)  { UseSaslExternal = true, UseBidirectionalStreams = true };
                _rechtsS  = new TcpServerLinks(_rechts) { UseSaslExternal = true, UseBidirectionalStreams = true };

                var fernerS = new TcpServerLinks(ferner) { UseSaslExternal = true, UseBidirectionalStreams = true };

                // Beide wählen rechts an; rechts kennt keinen von beiden.
                fernerS.AddPeer(_rechts.Domain, System.Net.IPAddress.Loopback.ToString(),
                                _rechtsS.Port, TcpTlsMode.StartTls, _rechts.IsOwnCertificate);

                _linksS.AddPeer(_rechts.Domain, System.Net.IPAddress.Loopback.ToString(),
                                _rechtsS.Port, TcpTlsMode.StartTls, _rechts.IsOwnCertificate);

                var alice  = await ConnectAsync(_links,  "alice");
                var juliet = await ConnectAsync(_rechts, "juliet");
                var dritte = await ConnectAsync(ferner,  "dritte");

                _links.GetAccount(alice.BareJid)!.SetRosterEntry(new RosterEntry(juliet.BareJid, null, "both"));
                ferner.GetAccount(dritte.BareJid)!.SetRosterEntry(new RosterEntry(juliet.BareJid, null, "both"));
                _rechts.GetAccount(juliet.BareJid)!.SetRosterEntry(new RosterEntry(alice.BareJid, null, "both"));

                var beiJuliet = new List<String>();
                var beiAlice  = new List<String>();

                juliet.OnMessage += m => beiJuliet.Add(m.Body ?? "");
                alice.OnMessage  += m => beiAlice.Add(m.Body ?? "");

                // Erst ferner, dann links - die Reihenfolge ist der Kern des Tests.
                await dritte.SendMessageAsync(juliet.BareJid, "von ferner");
                await WarteAuf(() => beiJuliet.Count > 0, "die Nachricht von ferner");

                await alice.SendMessageAsync(juliet.BareJid, "von links");
                await WarteAuf(() => beiJuliet.Count > 1, "die Nachricht von links");

                // Jetzt hängen zwei Rückrichtungen an rechts.
                await juliet.SendMessageAsync(alice.BareJid, "Zurück an links");

                await WarteAuf(() => beiAlice.Any(b => b == "Zurück an links"),
                               "die Antwort bei Alice und nicht bei ferner");

                Assert.That(_rechtsS.BidirectionalDeliveryCount, Is.GreaterThan(0));

            }
            finally
            {
                await ferner.DisposeAsync();
            }

        }

        #endregion

    }

}
