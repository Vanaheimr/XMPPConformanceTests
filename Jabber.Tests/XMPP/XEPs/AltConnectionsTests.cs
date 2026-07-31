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
    /// XEP-0156: den WebSocket-Endpunkt einer Domain aus ihrem
    /// <c>host-meta</c> lesen - ohne Netz, mit eingesetztem Abrufer.
    /// </summary>
    /// <remarks>
    /// Zwei Regeln des XEPs sind Sicherheitsregeln und stehen deshalb im
    /// Mittelpunkt: „host-meta files MUST be fetched only over HTTPS, and MUST
    /// only use connection URLs starting with 'https://' or 'wss://'."
    ///
    /// Beides ist derselbe Gedanke. Wer die Auskunft im Klartext holt, lässt
    /// jeden Zwischenmann bestimmen, wohin sich der Client anmeldet; wer einer
    /// so geholten Auskunft ein <c>ws://</c> abnimmt, gibt hinterher Benutzer
    /// und Passwort dorthin. Das eine ist ohne das andere wertlos.
    ///
    /// Der DNS-Weg über <c>_xmppconnect</c>-TXT-Einträge, den frühere Fassungen
    /// des XEPs kannten, ist deshalb gar nicht erst umgesetzt: Er wurde aus dem
    /// Dokument entfernt - „this was insecure and has been removed".
    /// </remarks>
    [TestFixture]
    public class AltConnectionsTests
    {

        #region Beispiele aus dem XEP

        private const String Xrd =
            "<?xml version='1.0' encoding='utf-8'?>" +
            "<XRD xmlns='http://docs.oasis-open.org/ns/xri/xrd-1.0'>" +
            "<Link rel='urn:xmpp:alt-connections:xbosh' href='https://web.example.com:5280/bosh'/>" +
            "<Link rel='urn:xmpp:alt-connections:websocket' href='wss://web.example.com:443/ws'/>" +
            "</XRD>";

        private const String Jrd =
            "{ \"links\": [" +
            "{ \"rel\": \"urn:xmpp:alt-connections:xbosh\", \"href\": \"https://web.example.com:5280/bosh\" }," +
            "{ \"rel\": \"urn:xmpp:alt-connections:websocket\", \"href\": \"wss://web.example.com:443/ws\" }" +
            "] }";

        #endregion

        #region Hilfsfunktionen

        /// <summary>
        /// Ein Abrufer, der die angefragten Adressen mitschreibt und je nach
        /// Adresse eine hinterlegte Antwort gibt.
        /// </summary>
        private static AltConnectionsResolver Resolver(List<String>                 abgefragt,
                                                       IDictionary<String, String>  antworten)

            => new ((uri, ct) =>
               {
                   abgefragt.Add(uri);
                   return Task.FromResult(antworten.TryGetValue(uri, out var antwort) ? antwort : null);
               });

        #endregion


        #region TheWebsocketLinkIsTakenFromTheXrd()

        /// <summary>
        /// Das XRD-Beispiel aus dem XEP: Der websocket-Link wird gefunden, der
        /// BOSH-Link nicht mitgenommen.
        /// </summary>
        [Test]
        public void TheWebsocketLinkIsTakenFromTheXrd()
        {

            var endpunkte = AltConnectionsResolver.WebSocketEndpointsFromXrd(Xrd);

            Assert.Multiple(() =>
            {
                Assert.That(endpunkte, Has.Count.EqualTo(1), $"Gefunden: {String.Join(", ", endpunkte)}");
                Assert.That(endpunkte[0], Is.EqualTo("wss://web.example.com:443/ws"));
            });

        }

        #endregion

        #region TheWebsocketLinkIsTakenFromTheJrd()

        /// <summary>Dasselbe für die JSON-Fassung.</summary>
        [Test]
        public void TheWebsocketLinkIsTakenFromTheJrd()
        {

            var endpunkte = AltConnectionsResolver.WebSocketEndpointsFromJrd(Jrd);

            Assert.Multiple(() =>
            {
                Assert.That(endpunkte, Has.Count.EqualTo(1), $"Gefunden: {String.Join(", ", endpunkte)}");
                Assert.That(endpunkte[0], Is.EqualTo("wss://web.example.com:443/ws"));
            });

        }

        #endregion

        #region APlaintextEndpoint_IsRejected()

        /// <summary>
        /// Ein <c>ws://</c>-Endpunkt wird nicht genommen - in beiden Formaten.
        /// </summary>
        /// <remarks>
        /// Das ist keine Formsache: Der Endpunkt bestimmt, wohin gleich Benutzer
        /// und Passwort gehen. Eine Auskunft, die über TLS kommt und dann ins
        /// Klartext-Netz zeigt, hebt die Absicherung wieder auf.
        /// </remarks>
        [Test]
        public void APlaintextEndpoint_IsRejected()
        {

            var xrd = "<XRD xmlns='http://docs.oasis-open.org/ns/xri/xrd-1.0'>" +
                      "<Link rel='urn:xmpp:alt-connections:websocket' href='ws://web.example.com:5280/ws'/>" +
                      "</XRD>";

            var jrd = "{ \"links\": [ { \"rel\": \"urn:xmpp:alt-connections:websocket\"," +
                      " \"href\": \"ws://web.example.com:5280/ws\" } ] }";

            Assert.Multiple(() =>
            {
                Assert.That(AltConnectionsResolver.WebSocketEndpointsFromXrd(xrd), Is.Empty);
                Assert.That(AltConnectionsResolver.WebSocketEndpointsFromJrd(jrd), Is.Empty);
            });

        }

        #endregion

        #region AnotherRelation_IsNotAnEndpoint()

        /// <summary>
        /// Der Link-Typ entscheidet, nicht das Schema: Ein <c>wss://</c> unter
        /// einem anderen <c>rel</c> ist kein WebSocket-Endpunkt.
        /// </summary>
        /// <remarks>
        /// Ein <c>host-meta</c> ist nicht für XMPP gemacht - dort stehen
        /// <c>lrdd</c>, <c>webfinger</c> und was der Betreiber sonst
        /// veröffentlicht. Wer nur auf das Schema sieht, nimmt den erstbesten
        /// Eintrag, der zufällig verschlüsselt ist.
        /// </remarks>
        [Test]
        public void AnotherRelation_IsNotAnEndpoint()
        {

            var xrd = "<XRD xmlns='http://docs.oasis-open.org/ns/xri/xrd-1.0'>" +
                      "<Link rel='urn:xmpp:alt-connections:xbosh' href='wss://web.example.com:443/bosh'/>" +
                      "</XRD>";

            var jrd = "{ \"links\": [ { \"rel\": \"urn:xmpp:alt-connections:xbosh\"," +
                      " \"href\": \"wss://web.example.com:443/bosh\" } ] }";

            Assert.Multiple(() =>
            {
                Assert.That(AltConnectionsResolver.WebSocketEndpointsFromXrd(xrd), Is.Empty);
                Assert.That(AltConnectionsResolver.WebSocketEndpointsFromJrd(jrd), Is.Empty);
            });

        }

        #endregion

        #region TheFirstEndpointWins()

        /// <summary>
        /// Nennt eine Domain mehrere Endpunkte, gilt der erste - und die
        /// Reihenfolge des Dokuments bleibt erhalten.
        /// </summary>
        [Test]
        public async Task TheFirstEndpointWins()
        {

            var jrd = "{ \"links\": [" +
                      "{ \"rel\": \"urn:xmpp:alt-connections:websocket\", \"href\": \"wss://erst.example/ws\" }," +
                      "{ \"rel\": \"urn:xmpp:alt-connections:websocket\", \"href\": \"wss://dann.example/ws\" }" +
                      "] }";

            var resolver = Resolver([],
                               new Dictionary<String, String> {
                                   ["https://example.test/.well-known/host-meta.json"] = jrd
                               });

            Assert.Multiple(async () =>
            {

                Assert.That(AltConnectionsResolver.WebSocketEndpointsFromJrd(jrd),
                            Is.EqualTo(new[] { "wss://erst.example/ws", "wss://dann.example/ws" }));

                Assert.That(await resolver.DiscoverWebSocketAsync("example.test"),
                            Is.EqualTo("wss://erst.example/ws"));

            });

        }

        #endregion

        #region Garbage_IsNotAnException()

        /// <summary>
        /// Was keine gültige Datei ist, ergibt keine Endpunkte - und keinen
        /// Fehler.
        /// </summary>
        /// <remarks>
        /// Der Inhalt kommt von einem fremden Webserver, der irgendetwas
        /// ausliefern kann: eine Fehlerseite, eine Weiterleitung als HTML, eine
        /// halbe Datei. Fliegt hier eine Ausnahme, scheitert der
        /// Verbindungsaufbau an der Discovery statt ohne sie weiterzugehen.
        /// </remarks>
        [Test]
        public void Garbage_IsNotAnException()
        {

            Assert.Multiple(() =>
            {
                Assert.That(AltConnectionsResolver.WebSocketEndpointsFromXrd("<html><body>404"), Is.Empty);
                Assert.That(AltConnectionsResolver.WebSocketEndpointsFromJrd("<html><body>404"), Is.Empty);
                Assert.That(AltConnectionsResolver.WebSocketEndpointsFromXrd(""),                Is.Empty);
                Assert.That(AltConnectionsResolver.WebSocketEndpointsFromJrd("{ \"links\": 5 }"), Is.Empty);
            });

        }

        #endregion

        #region TheJsonFormIsTriedFirst()

        /// <summary>
        /// Gefragt wird über HTTPS nach <c>/.well-known/host-meta.json</c>;
        /// findet sich dort ein Endpunkt, bleibt es bei dieser einen Abfrage.
        /// </summary>
        [Test]
        public async Task TheJsonFormIsTriedFirst()
        {

            var abgefragt = new List<String>();

            var resolver = Resolver(abgefragt,
                               new Dictionary<String, String> {
                                   ["https://example.test/.well-known/host-meta.json"] = Jrd,
                                   ["https://example.test/.well-known/host-meta"]      = Xrd
                               });

            var endpunkt = await resolver.DiscoverWebSocketAsync("example.test");

            Assert.Multiple(() =>
            {
                Assert.That(endpunkt, Is.EqualTo("wss://web.example.com:443/ws"));
                Assert.That(abgefragt, Is.EqualTo(new[] { "https://example.test/.well-known/host-meta.json" }),
                            $"Abgefragt wurde: {String.Join(", ", abgefragt)}");
            });

        }

        #endregion

        #region WithoutTheJsonForm_TheXrdIsUsed()

        /// <summary>
        /// Liefert die JSON-Fassung nichts, wird die XML-Fassung nachgefragt.
        /// </summary>
        [Test]
        public async Task WithoutTheJsonForm_TheXrdIsUsed()
        {

            var abgefragt = new List<String>();

            var resolver = Resolver(abgefragt,
                               new Dictionary<String, String> {
                                   ["https://example.test/.well-known/host-meta"] = Xrd
                               });

            var endpunkt = await resolver.DiscoverWebSocketAsync("example.test");

            Assert.Multiple(() =>
            {
                Assert.That(endpunkt, Is.EqualTo("wss://web.example.com:443/ws"));
                Assert.That(abgefragt, Is.EqualTo(new[] {
                                "https://example.test/.well-known/host-meta.json",
                                "https://example.test/.well-known/host-meta"
                            }),
                            $"Abgefragt wurde: {String.Join(", ", abgefragt)}");
            });

        }

        #endregion

        #region WithoutAnyHostMeta_NothingIsDiscovered()

        /// <summary>
        /// Eine Domain ohne <c>host-meta</c> ergibt keinen Endpunkt - und keinen
        /// Fehler. Der Aufrufer bleibt dann bei seiner Vorgabe.
        /// </summary>
        [Test]
        public async Task WithoutAnyHostMeta_NothingIsDiscovered()
        {

            var abgefragt = new List<String>();
            var resolver  = Resolver(abgefragt, new Dictionary<String, String>());

            var endpunkt  = await resolver.DiscoverWebSocketAsync("example.test");

            Assert.That(endpunkt, Is.Null);

        }

        #endregion

    }

}
