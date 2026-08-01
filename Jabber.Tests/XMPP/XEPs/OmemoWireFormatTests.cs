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

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Das Drahtformat von OMEMO: die drei Protobuf-Nachrichten, das
    /// <c>&lt;encrypted/&gt;</c>-Element und die SCE-Hülle (XEP-0420).
    /// </summary>
    /// <remarks>
    /// <b>Hier zählt jedes Byte, und zwar gegen die Spezifikation und nicht
    /// gegen sich selbst.</b> Ein Format, das sich selbst lesen kann, ist noch
    /// keines - das ist die Lehre aus D62 bis D64. Die erwarteten Bytes stehen
    /// deshalb ausgeschrieben da, wo es geht.
    /// </remarks>
    [TestFixture]
    public class OmemoWireFormatTests
    {

        #region Hilfsfunktionen

        private static String Hex(Byte[] bytes)
            => Convert.ToHexString(bytes).ToLowerInvariant();

        private static Byte[] Muster(Int32 laenge, Byte start = 0)
        {

            var b = new Byte[laenge];

            for (var i = 0; i < laenge; i++)
                b[i] = (Byte) (start + i);

            return b;

        }

        #endregion


        #region TheMessage_IsEncodedFieldByField()

        /// <summary>
        /// <c>OMEMOMessage.proto</c> - Feld für Feld nachgerechnet.
        /// </summary>
        /// <remarks>
        /// <c>08</c> ist Feld 1 als Varint, <c>10</c> Feld 2, <c>1a</c> Feld 3
        /// als längenbegrenzt, <c>22</c> Feld 4. Der Geheimtext steht also
        /// <b>im</b> Protobuf mit Kennung und Längenangabe - und genau darüber
        /// läuft der HMAC.
        /// </remarks>
        [Test]
        public void TheMessage_IsEncodedFieldByField()
        {

            var dh      = Muster(32);
            var geheim  = Muster(16, 100);

            var kopf = new RatchetHeader(dh, 2, 1);

            Assert.Multiple(() =>
            {

                Assert.That(Hex(kopf.Encode()),
                            Is.EqualTo("0801" + "1002" + "1a20" + Hex(dh)),
                            "Der Kopf ohne Geheimtext");

                Assert.That(Hex(kopf.Encode(geheim)),
                            Is.EqualTo("0801" + "1002" + "1a20" + Hex(dh) + "2210" + Hex(geheim)),
                            "Der Kopf mit Geheimtext");

            });

        }

        #endregion

        #region TheMac_CoversTheEncodedMessage()

        /// <summary>
        /// Der HMAC läuft über <c>ad ‖ OMEMOMessage.proto</c> - mit dem
        /// Geheimtext im Protobuf und nicht dahinter.
        /// </summary>
        /// <remarks>
        /// <b>Dieser Test kam durch einen Fund beim Lesen der Spezifikation
        /// zustande, nicht durch eine Mutation.</b> In D64 hing der Geheimtext
        /// roh hinter dem Kopf; die Spezifikation verlangt ihn als Feld 4 der
        /// kodierten Nachricht. Der Unterschied sind drei Byte - Kennung und
        /// Längenangabe -, und beide Seiten dieses Hauses hätten ihn nie
        /// bemerkt. Gegen einen fremden Client hätte keine einzige Prüfsumme
        /// gestimmt.
        ///
        /// Deshalb steht die Rechnung hier von Hand daneben.
        /// </remarks>
        [Test]
        public void TheMac_CoversTheEncodedMessage()
        {

            var authKey  = Muster(32);
            var beigabe  = Encoding.UTF8.GetBytes("AD");
            var kopf     = new RatchetHeader(Muster(32), 7, 3);
            var geheim   = Muster(48, 200);

            Byte[] erwartet = [.. beigabe, .. kopf.Encode(geheim)];

            Assert.That(Hex(DoubleRatchet.Mac(authKey, beigabe, kopf, geheim)),
                        Is.EqualTo(Hex(HMACSHA256.HashData(authKey, erwartet)[..16])));

        }

        #endregion

        #region ARatchetMessage_SurvivesTheWire()

        /// <summary>
        /// Eine Ratchet-Nachricht überlebt die Kodierung als
        /// <c>OMEMOAuthenticatedMessage</c> - und lässt sich danach noch
        /// entschlüsseln.
        /// </summary>
        /// <remarks>
        /// Der letzte Teil ist der wichtige: Eine Kodierung, die sich lesen
        /// lässt, aber den HMAC ungültig macht, fiele an einem blossen
        /// Vergleich der Felder nicht auf.
        /// </remarks>
        [Test]
        public void ARatchetMessage_SurvivesTheWire()
        {

            var geheimnis  = RandomNumberGenerator.GetBytes(32);
            var bobsKey    = Curve25519.GenerateKeyPair();
            var beigabe    = Encoding.UTF8.GetBytes("AD");

            var alice = DoubleRatchet.InitiateAsSender(geheimnis, bobsKey.PublicKey);
            var bob   = DoubleRatchet.InitiateAsReceiver(geheimnis, bobsKey);

            var nachricht = alice.Encrypt(Encoding.UTF8.GetBytes("durch den Draht"), beigabe);

            var draht     = OmemoWireFormat.Encode(nachricht);
            var zurueck   = OmemoWireFormat.Decode(draht);

            Assert.Multiple(() =>
            {

                Assert.That(zurueck.Header.MessageNumber,       Is.EqualTo(nachricht.Header.MessageNumber));
                Assert.That(zurueck.Header.PreviousChainLength, Is.EqualTo(nachricht.Header.PreviousChainLength));
                Assert.That(Hex(zurueck.Header.DhPublicKey),    Is.EqualTo(Hex(nachricht.Header.DhPublicKey)));
                Assert.That(Hex(zurueck.Ciphertext),            Is.EqualTo(Hex(nachricht.Ciphertext)));
                Assert.That(Hex(zurueck.Mac),                   Is.EqualTo(Hex(nachricht.Mac)));

                Assert.That(bob.Decrypt(zurueck, beigabe),
                            Is.EqualTo(Encoding.UTF8.GetBytes("durch den Draht")));

            });

        }

        #endregion

        #region AMissingField_IsAnError()

        /// <summary>
        /// Ein fehlendes Pflichtfeld ist ein Formatfehler und kein
        /// Vorgabewert.
        /// </summary>
        /// <remarks>
        /// Protocol Buffers kennt für <c>uint32</c> die Null und für
        /// <c>bytes</c> das leere Feld. Beides liesse sich stillschweigend
        /// einsetzen - die Nachricht sähe dann aus wie die erste einer Kette
        /// mit leerem Ratchet-Schlüssel, liesse sich nicht entschlüsseln, und
        /// niemand wüsste, dass ein Feld fehlte.
        /// </remarks>
        [Test]
        public void AMissingField_IsAnError()
        {

            Assert.Multiple(() =>
            {

                // Eine beglaubigte Nachricht ohne MAC.
                var ohneMac = new List<Byte>();
                Protobuf.WriteBytes(ohneMac, 2, Muster(20));

                Assert.That(() => OmemoWireFormat.Decode([.. ohneMac]),
                            Throws.TypeOf<FormatException>(), "MAC fehlt");

                // Ein MAC der falschen Länge - und zwar um eine sonst
                // einwandfreie Nachricht herum.
                //
                // Die frühere Fassung packte hier zufällige Bytes als innere
                // Nachricht ein. Die scheiterten schon beim Protobuf-Lesen,
                // und der Test bestand deshalb auch dann, wenn die
                // Längenprüfung fehlte - er prüfte den falschen Grund. Die
                // Mutation, die genau diese Prüfung entfernt, hat ihn
                // überlebt.
                var innereNachricht = new RatchetHeader(Muster(32), 0, 0).Encode(Muster(16));

                var kurzerMac = new List<Byte>();
                Protobuf.WriteBytes(kurzerMac, 1, Muster(8));
                Protobuf.WriteBytes(kurzerMac, 2, innereNachricht);

                Assert.That(() => OmemoWireFormat.Decode([.. kurzerMac]),
                            Throws.TypeOf<FormatException>(), "MAC zu kurz");

                // Zur Gegenprobe: mit 16 Byte MAC geht dieselbe Nachricht durch.
                var richtig = new List<Byte>();
                Protobuf.WriteBytes(richtig, 1, Muster(16));
                Protobuf.WriteBytes(richtig, 2, innereNachricht);

                Assert.That(() => OmemoWireFormat.Decode([.. richtig]),
                            Throws.Nothing, "Die Gegenprobe scheitert - dann prüft der Test etwas anderes.");

                // Eine Nachricht ohne Ratchet-Schlüssel.
                var innen = new List<Byte>();
                Protobuf.WriteUInt32(innen, 1, 0);
                Protobuf.WriteUInt32(innen, 2, 0);
                Protobuf.WriteBytes (innen, 4, Muster(16));

                var aussen = new List<Byte>();
                Protobuf.WriteBytes(aussen, 1, Muster(16));
                Protobuf.WriteBytes(aussen, 2, [.. innen]);

                Assert.That(() => OmemoWireFormat.Decode([.. aussen]),
                            Throws.TypeOf<FormatException>(), "Ratchet-Schlüssel fehlt");

            });

        }

        #endregion

        #region TheKeyExchange_RoundTrips()

        /// <summary>
        /// <c>OMEMOKeyExchange.proto</c> - hin und zurück, und die Feldnummern
        /// nachgerechnet.
        /// </summary>
        [Test]
        public void TheKeyExchange_RoundTrips()
        {

            var austausch = new OmemoKeyExchange(31, 2, Muster(32), Muster(32, 50), Muster(70, 90));

            var kodiert = austausch.Encode();
            var gelesen = OmemoKeyExchange.Decode(kodiert);

            Assert.Multiple(() =>
            {

                Assert.That(gelesen.PreKeyId,        Is.EqualTo(31u));
                Assert.That(gelesen.SignedPreKeyId,  Is.EqualTo(2u));
                Assert.That(Hex(gelesen.IdentityKey),   Is.EqualTo(Hex(austausch.IdentityKey)));
                Assert.That(Hex(gelesen.EphemeralKey),  Is.EqualTo(Hex(austausch.EphemeralKey)));
                Assert.That(Hex(gelesen.Message),       Is.EqualTo(Hex(austausch.Message)));

                // pk_id = 1, spk_id = 2, ik = 3, ek = 4, message = 5
                Assert.That(Hex(kodiert), Does.StartWith("081f" + "1002" + "1a20"));

            });

        }

        #endregion

        #region TheEncryptedElement_RoundTrips()

        /// <summary>
        /// Das <c>&lt;encrypted/&gt;</c>-Element: gebaut, gelesen, und die
        /// Gestalt geprüft.
        /// </summary>
        [Test]
        public void TheEncryptedElement_RoundTrips()
        {

            var element = new OmemoEncryptedElement(
                              12345,
                              new Dictionary<String, IReadOnlyList<OmemoKey>> {
                                  ["bob@example.org"]    = [new OmemoKey(1, Muster(20), false),
                                                            new OmemoKey(2, Muster(20, 40), true)],
                                  ["alice@example.org"]  = [new OmemoKey(9, Muster(20, 80), false)]
                              },
                              Muster(64));

            var xml = element.ToXml();

            // In eine Nachricht einpacken, wie es auf der Leitung wäre.
            var stanza = XElement.Parse(
                             $"<message xmlns='jabber:client' from='bob@example.org/x' " +
                             $"to='alice@example.org/y' type='chat'>{xml}</message>");

            Assert.That(OmemoEncryptedElement.TryRead(stanza, out var gelesen), Is.True);

            Assert.Multiple(() =>
            {

                Assert.That(gelesen!.SenderDeviceId, Is.EqualTo(12345u));
                Assert.That(gelesen.Keys, Has.Count.EqualTo(2));

                Assert.That(Hex(gelesen.Payload!), Is.EqualTo(Hex(Muster(64))));

                var fuerBob2 = gelesen.KeyFor("bob@example.org", 2);
                Assert.That(fuerBob2,                Is.Not.Null);
                Assert.That(fuerBob2!.IsKeyExchange, Is.True);
                Assert.That(Hex(fuerBob2.Data),      Is.EqualTo(Hex(Muster(20, 40))));

                var fuerBob1 = gelesen.KeyFor("bob@example.org", 1);
                Assert.That(fuerBob1!.IsKeyExchange, Is.False,
                            "Ohne kex-Attribut gilt 'false' (Abschnitt 4.5).");

                // Die Gerätekennung allein reicht nicht - sie gehört zu einem
                // JID. Gerät 1 gibt es bei Bob, nicht bei Alice.
                Assert.That(gelesen.KeyFor("alice@example.org", 1), Is.Null);
                Assert.That(gelesen.KeyFor("carol@example.org", 1), Is.Null);

                // Der Vorgabewert steht nicht in der Stanza.
                //
                // Hier stand eine Suche nach der Zeichenfolge "kex='false'" im
                // ausgegebenen XML - und die konnte nie zutreffen:
                // XElement.ToString schreibt Attribute mit doppelten
                // Anführungszeichen. Der Test bestand also immer, auch als die
                // Mutation den Vorgabewert ausschrieb. Gefragt wird jetzt das
                // Attribut selbst.
                XNamespace ns = OmemoEncryptedElement.Namespace;

                var ohneKex = xml.Descendants(ns + "key")
                                 .Where(k => k.Attr("rid") is "1" or "9")
                                 .ToList();

                Assert.That(ohneKex, Has.Count.EqualTo(2), "Die Schlüssel ohne kex fehlen.");

                foreach (var k in ohneKex)
                    Assert.That(k.Attribute("kex"), Is.Null,
                                $"Bei rid={k.Attr("rid")} steht ein ausgeschriebener Vorgabewert.");

            });

        }

        #endregion

        #region AMessageWithoutPayload_IsValid()

        /// <summary>
        /// Eine Nachricht ohne <c>&lt;payload/&gt;</c> ist keine kaputte,
        /// sondern eine ohne Inhalt.
        /// </summary>
        /// <remarks>
        /// Sie heisst „ich habe die Sitzung neu aufgebaut" und trägt nur den
        /// Schlüsselaustausch. So bekommt eine Gegenstelle eine Sitzung, ohne
        /// dass ein Mensch etwas schreiben müsste.
        /// </remarks>
        [Test]
        public void AMessageWithoutPayload_IsValid()
        {

            var element = new OmemoEncryptedElement(
                              7,
                              new Dictionary<String, IReadOnlyList<OmemoKey>> {
                                  ["bob@example.org"] = [new OmemoKey(1, Muster(20), true)]
                              },
                              null);

            var stanza = XElement.Parse(
                             $"<message xmlns='jabber:client' from='a@b/c' type='chat'>{element.ToXml()}</message>");

            Assert.That(OmemoEncryptedElement.TryRead(stanza, out var gelesen), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(gelesen!.Payload, Is.Null);
                Assert.That(gelesen.KeyFor("bob@example.org", 1)!.IsKeyExchange, Is.True);
            });

        }

        #endregion

        #region AnEncryptedElementInsideACarbon_IsNotTheOuterOne()

        /// <summary>
        /// Die Verschlüsselung einer eingepackten Nachricht gehört nicht der
        /// äusseren.
        /// </summary>
        /// <remarks>
        /// Dieselbe Falle wie beim Verzugsstempel (D59) und beim
        /// Korrekturvermerk (D60): Ein Carbon bringt in seinem
        /// <c>&lt;forwarded/&gt;</c> eine vollständige eigene Nachricht mit.
        /// Wer die ganze Stanza durchsucht, hält die äussere für verschlüsselt
        /// und entschlüsselt eine Nutzlast, die zu einer anderen Sitzung
        /// gehört.
        /// </remarks>
        [Test]
        public void AnEncryptedElementInsideACarbon_IsNotTheOuterOne()
        {

            var innen = new OmemoEncryptedElement(
                            7,
                            new Dictionary<String, IReadOnlyList<OmemoKey>> {
                                ["bob@example.org"] = [new OmemoKey(1, Muster(20), false)]
                            },
                            Muster(32)).ToXml();

            var carbon = XElement.Parse(
                             "<message xmlns='jabber:client' from='alice@example.org' type='chat'>" +
                             "<received xmlns='urn:xmpp:carbons:2'>" +
                             "<forwarded xmlns='urn:xmpp:forward:0'>" +
                             $"<message xmlns='jabber:client'>{innen}</message>" +
                             "</forwarded></received></message>");

            Assert.That(OmemoEncryptedElement.TryRead(carbon, out _), Is.False,
                        "Die äussere Nachricht gilt als verschlüsselt.");

        }

        #endregion

        #region ABrokenElement_IsRefusedWithoutThrowing()

        /// <summary>
        /// Was sich nicht lesen lässt, ergibt <c>false</c> - und keine
        /// Ausnahme.
        /// </summary>
        /// <remarks>
        /// Eine unverständliche Nachricht ist für den Empfänger dasselbe wie
        /// keine. Ein Absturz wäre die schlechtere Antwort: Er liesse sich von
        /// jedem auslösen, der ein <c>&lt;key/&gt;</c> mit krummem Base64
        /// schickt.
        /// </remarks>
        [Test]
        public void ABrokenElement_IsRefusedWithoutThrowing()
        {

            String[] kaputt = [

                // Keine Gerätekennung
                "<encrypted xmlns='urn:xmpp:omemo:2'><header/></encrypted>",

                // Kennung ist keine Zahl
                "<encrypted xmlns='urn:xmpp:omemo:2'><header sid='keine-zahl'/></encrypted>",

                // Krummes Base64 im Schlüssel
                "<encrypted xmlns='urn:xmpp:omemo:2'><header sid='1'>" +
                "<keys jid='bob@example.org'><key rid='1'>!!!kein base64!!!</key></keys>" +
                "</header></encrypted>",

                // Ein Schlüssel ohne rid
                "<encrypted xmlns='urn:xmpp:omemo:2'><header sid='1'>" +
                "<keys jid='bob@example.org'><key>AAAA</key></keys></header></encrypted>",

                // Keys ohne jid
                "<encrypted xmlns='urn:xmpp:omemo:2'><header sid='1'>" +
                "<keys><key rid='1'>AAAA</key></keys></header></encrypted>",

                // Krummes Base64 in der Nutzlast
                "<encrypted xmlns='urn:xmpp:omemo:2'><header sid='1'/>" +
                "<payload>!!!</payload></encrypted>"

            ];

            Assert.Multiple(() =>
            {

                foreach (var text in kaputt)
                {

                    var stanza = XElement.Parse(
                                     $"<message xmlns='jabber:client' from='a@b/c'>{text}</message>");

                    Assert.That(OmemoEncryptedElement.TryRead(stanza, out _), Is.False, text);

                }

            });

        }

        #endregion

        #region TheEnvelope_CarriesContentAndAffixes()

        /// <summary>
        /// Die SCE-Hülle (XEP-0420): Inhalt, Absender, Zeit und Polsterung.
        /// </summary>
        [Test]
        public void TheEnvelope_CarriesContentAndAffixes()
        {

            XNamespace client = "jabber:client";

            var huelle = new SceEnvelope([new XElement(client + "body", "Treffen wir uns um acht?")],
                                         From: "alice@example.org",
                                         Time: new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));

            var xml = huelle.ToXml();

            Assert.Multiple(() =>
            {

                Assert.That(xml.Child(SceEnvelope.Namespace, "content"), Is.Not.Null);
                Assert.That(xml.Child(SceEnvelope.Namespace, "rpad"),    Is.Not.Null,
                            "XEP-0420 verlangt die Polsterung.");

                Assert.That(xml.Child(SceEnvelope.Namespace, "time")?.Attr("stamp"),
                            Is.EqualTo("2026-08-01T12:00:00Z"));

                Assert.That(SceEnvelope.TryRead(xml, out var gelesen, "alice@example.org/handy"),
                            Is.True,
                            "Der Absender aus der Stanza gehört zu demselben Menschen.");

                Assert.That(gelesen!.Content.Single().Value, Is.EqualTo("Treffen wir uns um acht?"));
                Assert.That(gelesen.From, Is.EqualTo("alice@example.org"));

            });

        }

        #endregion

        #region AForwardedEnvelope_IsRefused()

        /// <summary>
        /// Nennt die Hülle einen anderen Absender als die Stanza, wird sie
        /// abgewiesen.
        /// </summary>
        /// <remarks>
        /// <b>Das ist der Angriff, gegen den die Beigabe steht:</b> Jemand
        /// fängt einen Geheimtext ab und schickt ihn unter eigenem Namen
        /// weiter. Die Verschlüsselung bleibt gültig - sie wurde ja nicht
        /// angetastet -, und ohne diesen Abgleich sähe der Empfänger eine
        /// Nachricht, die nie an ihn gerichtet war, mit einem Absender, der
        /// sie nie geschrieben hat.
        /// </remarks>
        [Test]
        public void AForwardedEnvelope_IsRefused()
        {

            XNamespace client = "jabber:client";

            var huelle = new SceEnvelope([new XElement(client + "body", "vertraulich")],
                                         From: "alice@example.org").ToXml();

            Assert.Multiple(() =>
            {

                Assert.That(SceEnvelope.TryRead(huelle, out _, "mallory@example.org/x"), Is.False,
                            "Eine weitergereichte Hülle wurde angenommen.");

                // Ohne Erwartung wird nicht abgeglichen - der Aufrufer weiss
                // dann selbst, was er tut.
                Assert.That(SceEnvelope.TryRead(huelle, out _), Is.True);

            });

        }

        #endregion

        #region ThePadding_IsRandomEveryTime()

        /// <summary>
        /// Die Polsterung ist bei jedem Aufruf eine andere.
        /// </summary>
        /// <remarks>
        /// Ohne sie verriete die Länge des Geheimtextes die Länge der
        /// Nachricht - bei „ja" und „nein" ist das der ganze Inhalt. Wäre sie
        /// bei gleichem Inhalt gleich, wären zwei gleiche Nachrichten wieder
        /// gleich lang, und die Massnahme wäre genau so weit wirkungslos, wie
        /// sie gedacht war.
        /// </remarks>
        [Test]
        public void ThePadding_IsRandomEveryTime()
        {

            XNamespace client = "jabber:client";

            var huelle = new SceEnvelope([new XElement(client + "body", "ja")]);

            var laengen = new HashSet<Int32>();

            for (var i = 0; i < 30; i++)
                laengen.Add(huelle.ToXml().Child(SceEnvelope.Namespace, "rpad")!.Value.Length);

            Assert.Multiple(() =>
            {

                Assert.That(laengen, Has.Count.GreaterThan(1),
                            "Die Polsterung hat immer dieselbe Länge.");

                Assert.That(laengen.Max(), Is.LessThanOrEqualTo(SceEnvelope.MaxPadding));

            });

        }

        #endregion

    }

}
