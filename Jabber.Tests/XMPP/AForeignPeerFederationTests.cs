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

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Basis für Föderationsläufe gegen eine fremde, ausgewachsene Gegenstelle.
    /// </summary>
    /// <remarks>
    /// Alles andere in dieser Testsammlung prüft unseren Server gegen unseren
    /// Server. Das beweist, dass beide Seiten dieselbe Auffassung vom Protokoll
    /// haben - nicht, dass diese Auffassung stimmt. Wo unsere beiden Seiten
    /// denselben Fehler machen, fällt er nicht auf.
    ///
    /// Der Aufbau ist für jede Gegenstelle derselbe: sie wird ohne root in WSL
    /// ausgepackt, bekommt ein von einer Test-CA signiertes Zertifikat und
    /// horcht auf der Rückschleife. Dieselbe CA signiert unser Zertifikat -
    /// damit trägt SASL-EXTERNAL (XEP-0178). Was sich unterscheidet, sind
    /// Domain, Ports und die Umgebungsvariable, über die die Testsammlung die
    /// CA findet; genau das legen die Ableitungen fest.
    /// </remarks>
    public abstract class AForeignPeerFederationTests
    {

        #region Was die Gegenstelle ausmacht

        /// <summary>Name der Gegenstelle - nur für Fehlermeldungen.</summary>
        protected abstract String  PeerName      { get; }

        /// <summary>Die Domain, die die Gegenstelle bedient.</summary>
        protected abstract String  PeerDomain    { get; }

        /// <summary>Der Port, auf dem sie S2S-Verbindungen annimmt.</summary>
        protected abstract Int32   PeerPort      { get; }

        /// <summary>
        /// Der Port, auf dem <b>wir</b> im eingehenden Lauf horchen - also der,
        /// auf den die Gegenstelle ohne SRV-Eintrag zurückfällt.
        /// </summary>
        protected abstract Int32   InboundPort   { get; }

        /// <summary>
        /// Die Umgebungsvariable, die auf das Zertifikatsverzeichnis des
        /// Aufbaus zeigt.
        /// </summary>
        protected abstract String  CertVariable  { get; }

        #endregion

        #region Data

        /// <summary>Unsere Domain im ausgehenden Lauf.</summary>
        protected const String LocalDomain    = "jabber.test";

        /// <summary>
        /// Unsere Domain im eingehenden Lauf - und der Grund, warum es zwei
        /// gibt.
        /// </summary>
        /// <remarks>
        /// Damit die <b>Gegenstelle</b> uns anwählen kann, muss sie unsere
        /// Domain auflösen können. Ein Eintrag in <c>/etc/hosts</c> bräuchte
        /// root; <c>localhost</c> steht dort ohnehin und zeigt auf die
        /// Rückschleife. Deshalb bedient der Testserver in diesem Fall diese
        /// Domain.
        /// </remarks>
        protected const String InboundDomain  = "localhost";

        private   XMPPClient?       _client;
        private   X509Certificate2  _ca       = null!;
        private   X509Certificate2  _ourCert  = null!;

        /// <summary>Unser Server im laufenden Test.</summary>
        protected XMPPServer?      Server  { get; private set; }

        /// <summary>Unser S2S-Zweig im laufenden Test.</summary>
        protected TcpServerLinks?  Links   { get; private set; }

        #endregion

        #region Aufbau / Abbau

        /// <summary>
        /// Wo die Test-CA und unser Zertifikat liegen. Ohne sie oder ohne
        /// laufende Gegenstelle hat der Test nichts zu prüfen.
        /// </summary>
        private String CertDirectory
            => Environment.GetEnvironmentVariable(CertVariable) ?? "";

        /// <summary>
        /// Baut Server und S2S-Zweig auf, oder überspringt den Test, wenn die
        /// Gegenstelle nicht bereitsteht.
        /// </summary>
        /// <param name="bidi">
        /// XEP-0288 in beide Richtungen: anbieten und erbitten.
        /// </param>
        /// <param name="nurAnbieten">
        /// XEP-0288 nur auf eingehenden Verbindungen anbieten, auf ausgehenden
        /// nicht erbitten.
        /// </param>
        /// <param name="erreichbar">
        /// Unter einer Domain und einem Port aufbauen, unter denen die
        /// Gegenstelle uns von sich aus anwählen kann.
        /// </param>
        /// <param name="dialback">
        /// Ohne SASL-EXTERNAL aufbauen, sodass beide Seiten auf die
        /// Dialback-Rückfrage zurückfallen.
        /// </param>
        protected void Aufbau(Boolean bidi         = false,
                              Boolean nurAnbieten  = false,
                              Boolean erreichbar   = false,
                              Boolean dialback     = false)
        {

            var verzeichnis = CertDirectory;

            if (verzeichnis.Length == 0 || !File.Exists(Path.Combine(verzeichnis, "ca.crt")))
                Assert.Ignore($"Kein {PeerName}-Aufbau: {CertVariable} zeigt auf keine Test-CA.");

            if (!PortAntwortet())
                Assert.Ignore($"Auf 127.0.0.1:{PeerPort} antwortet kein {PeerName}.");

            var domain = erreichbar ? InboundDomain : LocalDomain;

            _ca       = X509CertificateLoader.LoadCertificateFromFile(Path.Combine(verzeichnis, "ca.crt"));
            _ourCert  = X509CertificateLoader.LoadPkcs12FromFile(
                            Path.Combine(verzeichnis, $"{domain}.pfx"), null);

            Server    = new XMPPServer(domain, certificate: _ourCert);
            Server.Start();
            Server.AddAccount("alice");

            Links     = new TcpServerLinks(Server,
                                           port: erreichbar ? InboundPort : 0,
                                           mode: TcpTlsMode.StartTls) {

                            // Ohne SASL-EXTERNAL legen wir kein Klientzertifikat
                            // vor. Die Gegenstelle hat dann nichts zu prüfen und
                            // bietet EXTERNAL gar nicht erst an - der Weg fällt
                            // damit von selbst auf Dialback zurück, ohne dass
                            // eine Seite es erzwingen müsste.
                            UseSaslExternal          = !dialback,
                            OfferBidirectionalStreams    = bidi || nurAnbieten,
                            RequestBidirectionalStreams  = bidi
                        };

            Links.AddPeer(PeerDomain, "127.0.0.1", PeerPort, TcpTlsMode.StartTls, TrautDerTestCA);

        }

        [TearDown]
        public async Task Abbau()
        {

            if (_client is not null)
            {
                try { await _client.DisposeAsync(); } catch { /* im Teardown egal */ }
                _client = null;
            }

            // Ausdrücklich vor dem Server: der S2S-Zweig hält im erreichbaren
            // Aufbau einen festen Port, und den bekommt der nächste Test sonst
            // nicht mehr. Das kostete beim Prosody-Aufbau zwei Testläufe, weil
            // ein gescheiterter Bind wie ein Protokollfehler aussieht.
            if (Links is not null)
            {
                try { await Links.DisposeAsync(); } catch { /* im Teardown egal */ }
                Links = null;
            }

            if (Server is not null)
            {
                await Server.DisposeAsync();
                Server = null;
            }

        }

        #endregion

        #region Hilfsfunktionen

        private Boolean PortAntwortet()
        {
            try
            {
                using var s = new TcpClient();
                return s.ConnectAsync("127.0.0.1", PeerPort).Wait(TimeSpan.FromSeconds(2));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Nimmt genau die Zertifikate an, die von der Test-CA signiert sind.
        /// </summary>
        /// <remarks>
        /// Nicht "alles annehmen": eine Prüfung, die jedes Zertifikat
        /// durchlässt, bestünde auch gegen eine beliebige fremde Gegenstelle
        /// und sagte über den Handshake nichts aus. Der Betriebssystemspeicher
        /// hilft hier nicht - die Test-CA steht dort nicht und soll es auch
        /// nicht.
        /// </remarks>
        private Boolean TrautDerTestCA(Object            sender,
                                       X509Certificate?  certificate,
                                       X509Chain?        chain,
                                       SslPolicyErrors   errors)
        {

            if (certificate is null)
                return false;

            var zertifikat = certificate as X509Certificate2
                                 ?? X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());

            using var pruefung = new X509Chain();

            pruefung.ChainPolicy.TrustMode       = X509ChainTrustMode.CustomRootTrust;
            pruefung.ChainPolicy.RevocationMode  = X509RevocationMode.NoCheck;
            pruefung.ChainPolicy.CustomTrustStore.Add(_ca);

            return pruefung.Build(zertifikat);

        }

        /// <summary>
        /// Verbindet einen echten Client mit unserem Testserver.
        /// </summary>
        protected async Task<XMPPClient> AliceAsync()
        {

            var connection = new XMPPConnection($"alice@{Server!.Domain}", "pw", Server.Uri) {
                                 KeepaliveEnabled            = false,
                                 MaxReconnectAttempts        = 0,
                                 ServerCertificateValidator  = (_, c, _, _) =>
                                     c is not null &&
                                     c.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256)
                                      .Equals(_ourCert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256),
                                              StringComparison.OrdinalIgnoreCase)
                             };

            _client = new XMPPClient(connection);
            await _client.ConnectAsync();

            return _client;

        }

        #endregion


        #region ThePeerTakesTheReturnPathWeOffered()

        /// <summary>
        /// Die Gegenstelle nimmt unsere XEP-0288-Ankündigung an, wenn <b>sie</b>
        /// uns anwählt.
        /// </summary>
        /// <remarks>
        /// Die Richtung, die bis zuletzt nur aus fremdem Quelltext geschlossen
        /// und nie beobachtet war - und sie war es aus einem hausgemachten
        /// Grund: ein einziger Schalter steuerte Anbieten und Erbitten
        /// zugleich. Solange unsere ausgehende Verbindung die Rückrichtung
        /// nutzt, antwortet die Gegenstelle darüber und wählt uns gar nicht
        /// erst an. Es gab also keinen Zustand, in dem sich unsere
        /// Ankündigung zeigen konnte.
        ///
        /// Mit getrennten Schaltern gibt es ihn: wir bieten an, erbitten aber
        /// nicht. Der Ablauf ist dann
        ///
        /// <list type="number">
        ///   <item>
        ///     Alice pingt. Wir wählen an - ohne <c>&lt;bidi/&gt;</c> -, die
        ///     Gegenstelle beantwortet den Ping über eine <b>eigene</b>
        ///     Verbindung zu uns (RFC 6120, Abschnitt 4.1).
        ///   </item>
        ///   <item>
        ///     Auf dieser eingehenden Verbindung kündigen wir die Rückrichtung
        ///     an. Nimmt die Gegenstelle sie an, ist der Stream bei uns
        ///     freigeschaltet.
        ///   </item>
        ///   <item>
        ///     Der zweite Ping geht dann über genau diesen Stream hinaus statt
        ///     über eine neue Verbindung - und das zählt
        ///     <c>BidirectionalDeliveryCount</c>.
        ///   </item>
        /// </list>
        ///
        /// Zwei Pings, nicht einer: beim ersten gibt es die eingehende
        /// Verbindung noch nicht.
        ///
        /// <b>Nur innerhalb von WSL.</b> Von Windows aus erreicht uns die
        /// Gegenstelle nicht.
        /// </remarks>
        [Test]
        public async Task ThePeerTakesTheReturnPathWeOffered()
        {

            if (!OperatingSystem.IsLinux())
                Assert.Ignore($"Nur innerhalb von WSL: von Windows aus erreicht {PeerName} diesen Server nicht.");

            Aufbau(nurAnbieten: true, erreichbar: true);

            var alice = await AliceAsync();

            Assert.That(await alice.PingAsync(PeerDomain), Is.Not.Null,
                        "Schon der erste Ping kam nicht zurück.");

            Assert.That(await XMPPServer.WaitUntilAsync(() => Links!.InboundConnectionCount > 0,
                                                       TimeSpan.FromSeconds(10)),
                        Is.True,
                        $"Keine eingehende Verbindung von {PeerName}.");

            var dauer = await alice.PingAsync(PeerDomain);

            Assert.Multiple(() =>
            {

                Assert.That(dauer, Is.Not.Null,
                            "Der zweite Ping kam nicht zurück.");

                Assert.That(Links!.BidirectionalDeliveryCount, Is.GreaterThan(0),
                            $"{PeerName} hat unsere Ankündigung nicht angenommen - " +
                            "die zweite Stanza ging über eine eigene Verbindung hinaus.");

            });

        }

        #endregion

    }

}
