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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Der Double Ratchet (XEP-0384, Abschnitt 4.3).
    /// </summary>
    /// <remarks>
    /// <b>Hier sind Fehler still, und deshalb sehen diese Tests anders aus als
    /// die übrigen.</b> Eine Ratsche, die nicht weiterläuft, verschlüsselt
    /// weiterhin einwandfrei - sie tut es nur immer wieder mit demselben
    /// Schlüssel. Ein Test, der nur „hin und zurück ergibt den Klartext"
    /// prüft, bestünde auch dann. Geprüft wird deshalb zusätzlich, dass sich
    /// die Geheimtexte <i>unterscheiden</i>, dass Schlüssel <i>verschwinden</i>
    /// und dass eine Nachricht an falscher Stelle <i>abgewiesen</i> wird.
    /// </remarks>
    [TestFixture]
    public class DoubleRatchetTests
    {

        #region Hilfsfunktionen

        private static readonly Byte[] Beigabe = Encoding.UTF8.GetBytes("AD");

        private static String Hex(Byte[] bytes)
            => Convert.ToHexString(bytes).ToLowerInvariant();

        private static Byte[] Text(String s)
            => Encoding.UTF8.GetBytes(s);

        /// <summary>
        /// Ein Paar Ratschen, wie es nach X3DH entsteht: Alice ruft an, Bob
        /// hat seinen Signed PreKey.
        /// </summary>
        private static (DoubleRatchet Alice, DoubleRatchet Bob) Paar()
        {

            var geheimnis  = RandomNumberGenerator.GetBytes(32);
            var bobsKey    = Curve25519.GenerateKeyPair();

            return (DoubleRatchet.InitiateAsSender(geheimnis, bobsKey.PublicKey),
                    DoubleRatchet.InitiateAsReceiver(geheimnis, bobsKey));

        }

        #endregion


        #region TheFirstMessage_Arrives()

        /// <summary>
        /// Der einfachste Fall - und zugleich der, in dem die angerufene Seite
        /// ihre Ketten überhaupt erst bekommt.
        /// </summary>
        [Test]
        public void TheFirstMessage_Arrives()
        {

            var (alice, bob) = Paar();

            Assert.Multiple(() =>
            {

                Assert.That(alice.CanSend, Is.True,  "Die anrufende Seite kann sofort senden.");
                Assert.That(bob.CanSend,   Is.False, "Die angerufene Seite kann noch nicht senden.");

            });

            var nachricht = alice.Encrypt(Text("Hallo Bob"), Beigabe);

            Assert.Multiple(() =>
            {

                Assert.That(bob.Decrypt(nachricht, Beigabe), Is.EqualTo(Text("Hallo Bob")));

                Assert.That(bob.CanSend, Is.True,
                            "Nach der ersten Nachricht muss die angerufene Seite antworten können.");

            });

        }

        #endregion

        #region EveryMessage_HasItsOwnKey()

        /// <summary>
        /// Zweimal derselbe Klartext ergibt zwei verschiedene Geheimtexte.
        /// </summary>
        /// <remarks>
        /// <b>Der Test, der eine stehengebliebene Ratsche findet.</b> Läuft die
        /// symmetrische Kette nicht weiter, entschlüsselt trotzdem alles
        /// richtig - nur mit demselben Schlüssel, demselben IV und damit
        /// demselben Geheimtext. Wer denselben Text zweimal schreibt, verrät
        /// es dann jedem Mitlesenden.
        /// </remarks>
        [Test]
        public void EveryMessage_HasItsOwnKey()
        {

            var (alice, bob) = Paar();

            var erste   = alice.Encrypt(Text("dasselbe"), Beigabe);
            var zweite  = alice.Encrypt(Text("dasselbe"), Beigabe);

            Assert.Multiple(() =>
            {

                Assert.That(Hex(zweite.Ciphertext), Is.Not.EqualTo(Hex(erste.Ciphertext)),
                            "Zweimal derselbe Geheimtext - die Kette steht.");

                Assert.That(erste.Header.MessageNumber,  Is.EqualTo(0u));
                Assert.That(zweite.Header.MessageNumber, Is.EqualTo(1u));

                Assert.That(bob.Decrypt(erste,  Beigabe), Is.EqualTo(Text("dasselbe")));
                Assert.That(bob.Decrypt(zweite, Beigabe), Is.EqualTo(Text("dasselbe")));

            });

        }

        #endregion

        #region AConversation_TurnsTheDhRatchet()

        /// <summary>
        /// Ein Hin und Her über mehrere Runden - und der Ratchet-Schlüssel
        /// wechselt dabei.
        /// </summary>
        /// <remarks>
        /// Das ist die zweite Ratsche und der Grund für „Break-in Recovery":
        /// Wer den Zustand einmal gestohlen hat, verliert ihn wieder, sobald
        /// die beiden einmal in beide Richtungen geschrieben haben. Wechselte
        /// der Schlüssel nicht, bliebe der Dieb für immer dabei.
        /// </remarks>
        [Test]
        public void AConversation_TurnsTheDhRatchet()
        {

            var (alice, bob) = Paar();

            var ersterSchluessel = alice.Encrypt(Text("1"), Beigabe);
            bob.Decrypt(ersterSchluessel, Beigabe);

            var bobsAntwort = bob.Encrypt(Text("2"), Beigabe);

            Assert.That(Hex(bobsAntwort.Header.DhPublicKey),
                        Is.Not.EqualTo(Hex(ersterSchluessel.Header.DhPublicKey)),
                        "Beide Seiten benutzen denselben Ratchet-Schlüssel.");

            Assert.That(alice.Decrypt(bobsAntwort, Beigabe), Is.EqualTo(Text("2")));

            // Und weiter, über mehrere Runden.
            var schluessel = new List<String>();

            for (var i = 0; i < 5; i++)
            {

                var hin = alice.Encrypt(Text($"A{i}"), Beigabe);
                Assert.That(bob.Decrypt(hin, Beigabe), Is.EqualTo(Text($"A{i}")));

                var zurueck = bob.Encrypt(Text($"B{i}"), Beigabe);
                Assert.That(alice.Decrypt(zurueck, Beigabe), Is.EqualTo(Text($"B{i}")));

                schluessel.Add(Hex(hin.Header.DhPublicKey));
                schluessel.Add(Hex(zurueck.Header.DhPublicKey));

            }

            Assert.That(schluessel.Distinct().Count(), Is.EqualTo(schluessel.Count),
                        "Ein Ratchet-Schlüssel kam zweimal vor.");

        }

        #endregion

        #region MessagesOutOfOrder_StillArrive()

        /// <summary>
        /// Nachrichten, die überholt wurden, lassen sich später noch lesen.
        /// </summary>
        /// <remarks>
        /// Der Fall ist nicht ausgedacht: XMPP stellt über verschiedene Wege
        /// zu, und eine Nachricht kann hinter einer späteren ankommen. Ohne
        /// die beiseitegelegten Schlüssel wäre sie verloren - und zwar
        /// endgültig, weil ihr Schlüssel beim Vorspulen vergessen worden wäre.
        /// </remarks>
        [Test]
        public void MessagesOutOfOrder_StillArrive()
        {

            var (alice, bob) = Paar();

            var eins  = alice.Encrypt(Text("eins"),  Beigabe);
            var zwei  = alice.Encrypt(Text("zwei"),  Beigabe);
            var drei  = alice.Encrypt(Text("drei"),  Beigabe);

            // Die dritte kommt zuerst.
            Assert.That(bob.Decrypt(drei, Beigabe), Is.EqualTo(Text("drei")));

            Assert.That(bob.SkippedKeys, Is.EqualTo(2),
                        "Die beiden übersprungenen Schlüssel wurden nicht aufgehoben.");

            Assert.Multiple(() =>
            {

                Assert.That(bob.Decrypt(eins, Beigabe), Is.EqualTo(Text("eins")));
                Assert.That(bob.Decrypt(zwei, Beigabe), Is.EqualTo(Text("zwei")));

            });

            Assert.That(bob.SkippedKeys, Is.EqualTo(0),
                        "Ein benutzter Schlüssel wurde nicht weggeräumt.");

        }

        #endregion

        #region AMessageFromAPreviousChain_StillArrives()

        /// <summary>
        /// Eine Nachricht aus der <b>vorigen</b> Kette kommt an, auch wenn die
        /// Ratsche sich inzwischen gedreht hat.
        /// </summary>
        /// <remarks>
        /// Dafür steht das <c>pn</c> im Kopf: Es sagt der Gegenseite, wie lang
        /// die vorige Kette war, damit sie deren Rest ausrechnen und
        /// beiseitelegen kann, bevor sie die neue beginnt. Ohne dieses Feld
        /// wäre jede Nachricht verloren, die während eines Richtungswechsels
        /// unterwegs war - und Richtungswechsel sind der Normalfall eines
        /// Gesprächs.
        /// </remarks>
        [Test]
        public void AMessageFromAPreviousChain_StillArrives()
        {

            var (alice, bob) = Paar();

            // Alice schreibt zweimal; die zweite bleibt unterwegs liegen.
            var erste     = alice.Encrypt(Text("erste"), Beigabe);
            var verspaetet = alice.Encrypt(Text("verspätet"), Beigabe);

            bob.Decrypt(erste, Beigabe);

            // Bob antwortet - damit dreht sich die Ratsche.
            var antwort = bob.Encrypt(Text("Antwort"), Beigabe);
            alice.Decrypt(antwort, Beigabe);

            // Alice schreibt in der neuen Kette.
            var neue = alice.Encrypt(Text("neue Kette"), Beigabe);

            Assert.That(neue.Header.PreviousChainLength, Is.EqualTo(2u),
                        "Die Länge der vorigen Kette steht nicht im Kopf.");

            Assert.Multiple(() =>
            {

                Assert.That(bob.Decrypt(neue, Beigabe), Is.EqualTo(Text("neue Kette")));

                // Und jetzt die liegengebliebene aus der alten Kette.
                Assert.That(bob.Decrypt(verspaetet, Beigabe), Is.EqualTo(Text("verspätet")),
                            "Die Nachricht aus der vorigen Kette ist verloren.");

            });

        }

        #endregion

        #region AReplayedMessage_IsRefused()

        /// <summary>
        /// Dieselbe Nachricht ein zweites Mal wird abgewiesen.
        /// </summary>
        /// <remarks>
        /// Ihr Schlüssel ist nach dem ersten Mal fort - entweder verbraucht
        /// oder aus dem Vorrat der übersprungenen entfernt. <b>Das ist keine
        /// Nebenwirkung, sondern der Zweck:</b> Ohne sie liesse sich eine alte
        /// Nachricht beliebig oft einspielen, und der Empfänger zeigte sie
        /// jedesmal als neu an.
        /// </remarks>
        [Test]
        public void AReplayedMessage_IsRefused()
        {

            var (alice, bob) = Paar();

            var nachricht = alice.Encrypt(Text("nur einmal"), Beigabe);

            Assert.That(bob.Decrypt(nachricht, Beigabe), Is.EqualTo(Text("nur einmal")));

            Assert.That(() => bob.Decrypt(nachricht, Beigabe),
                        Throws.InstanceOf<Exception>(),
                        "Dieselbe Nachricht liess sich ein zweites Mal lesen.");

        }

        #endregion

        #region ATamperedMessage_IsRefused()

        /// <summary>
        /// Verändertes verrät sich - im Geheimtext wie im Kopf.
        /// </summary>
        /// <remarks>
        /// Der Kopf ist mitgeprüft, weil er in die Beigabe eingeht
        /// (<c>ad ‖ OMEMOMessage.proto(header)</c>). Ohne das liesse sich eine
        /// gültige Nachricht an eine andere Stelle der Kette verschieben: Der
        /// Empfänger nähme dann einen anderen Schlüssel, und was er
        /// entschlüsselt, wäre Zufall - aber die Herkunft sähe unversehrt aus.
        /// </remarks>
        [Test]
        public void ATamperedMessage_IsRefused()
        {

            // Jeder Fall bekommt ein frisches Paar, und das ist keine
            // Umständlichkeit: Eine abgewiesene Nachricht verändert den
            // Zustand der Ratsche trotzdem - ein Vorspulen hat stattgefunden,
            // ein Schlüssel ist verbraucht. Standen die Fälle hintereinander
            // auf demselben Paar, scheiterte der zweite an den Folgen des
            // ersten statt an seinem eigenen Grund.
            //
            // Genau daran ist die Mutation „der HMAC wird nicht geprüft"
            // vorbeigekommen: Der dritte Fall - die fremde Beigabe - hätte sie
            // erschlagen, prüfte aber auf einer Ratsche, die durch die beiden
            // Fälle davor schon weitergelaufen war, und warf deshalb aus einem
            // ganz anderen Grund.

            Assert.Multiple(() =>
            {

                {
                    var (alice, bob) = Paar();
                    var nachricht    = alice.Encrypt(Text("unverändert"), Beigabe);

                    var geheim = (Byte[]) nachricht.Ciphertext.Clone();
                    geheim[0] ^= 0x01;

                    Assert.That(() => bob.Decrypt(nachricht with { Ciphertext = geheim }, Beigabe),
                                Throws.TypeOf<CryptographicException>(),
                                "Ein verändertes Byte im Geheimtext kam durch.");
                }

                {
                    var (alice, bob) = Paar();
                    var nachricht    = alice.Encrypt(Text("unverändert"), Beigabe);

                    Assert.That(() => bob.Decrypt(
                                    nachricht with { Header = nachricht.Header with { MessageNumber = 7 } },
                                    Beigabe),
                                Throws.InstanceOf<Exception>(),
                                "Eine verschobene Nummer kam durch.");
                }

                {
                    // Der schärfste der drei: Am Geheimtext ist nichts
                    // verändert, und der Nachrichtenschlüssel stimmt. Allein
                    // die Beigabe ist eine andere - wird sie nicht geprüft,
                    // entschlüsselt diese Nachricht anstandslos, und eine
                    // gültige Nachricht liesse sich in eine fremde Sitzung
                    // verschieben.
                    var (alice, bob) = Paar();
                    var nachricht    = alice.Encrypt(Text("unverändert"), Beigabe);

                    Assert.That(() => bob.Decrypt(nachricht, Text("andere Beigabe")),
                                Throws.TypeOf<CryptographicException>(),
                                "Eine fremde Beigabe kam durch - die Sitzung ist nicht gebunden.");
                }

            });

        }

        #endregion

        #region ARidiculousMessageNumber_IsRefused()

        /// <summary>
        /// Eine Nachricht mit einer sehr grossen Nummer wird abgewiesen, statt
        /// den Empfänger rechnen zu lassen.
        /// </summary>
        /// <remarks>
        /// <b>Das ist eine Abwehr, keine Ordnungsfrage.</b> Ohne Obergrenze
        /// genügt eine einzige Nachricht mit <c>n = 4000000000</c>, und der
        /// Empfänger rechnet vier Milliarden Schlüssel aus, bevor er merkt,
        /// dass sie nicht stimmt. Ein Angreifer braucht dafür weder Schlüssel
        /// noch Zugang - er braucht nur diese eine Zahl.
        ///
        /// Die Prüfung steht deshalb <b>vor</b> der Schleife: Eine, die in ihr
        /// stünde, käme zu spät.
        /// </remarks>
        [Test]
        public void ARidiculousMessageNumber_IsRefused()
        {

            var (alice, bob) = Paar();

            var erste = alice.Encrypt(Text("eins"), Beigabe);
            bob.Decrypt(erste, Beigabe);

            var boshaft = alice.Encrypt(Text("zwei"), Beigabe);

            var stoppuhr = System.Diagnostics.Stopwatch.StartNew();

            Assert.That(() => bob.Decrypt(
                            boshaft with { Header = boshaft.Header with { MessageNumber = 4_000_000_000 } },
                            Beigabe),
                        Throws.TypeOf<CryptographicException>(),
                        "Die unsinnige Nummer wurde angenommen.");

            stoppuhr.Stop();

            Assert.Multiple(() =>
            {

                Assert.That(stoppuhr.ElapsedMilliseconds, Is.LessThan(1000),
                            "Die Abweisung hat gerechnet, statt zu prüfen.");

                Assert.That(bob.SkippedKeys, Is.LessThanOrEqualTo(DoubleRatchet.MaxSkip),
                            "Es wurden mehr Schlüssel aufgehoben als erlaubt.");

            });

        }

        #endregion

        #region TheChainStep_MatchesTheSpecificationLiterally()

        /// <summary>
        /// Nachrichtenschlüssel aus <c>HMAC(ck, 0x01)</c>, nächster
        /// Kettenschlüssel aus <c>HMAC(ck, 0x02)</c> - und zwar in dieser
        /// Zuordnung.
        /// </summary>
        /// <remarks>
        /// <b>Hier stand ein Test, der nichts prüfte.</b> Er rechnete die
        /// beiden Konstanten im Test selbst nach und stellte fest, dass sie
        /// verschiedene Ergebnisse liefern - über den Quelltext sagte er
        /// nichts. Die Mutation, die beide auf <c>0x01</c> setzt, überlebte
        /// ihn folgerichtig.
        ///
        /// <b>Und das wäre die schwerste Lücke dieser ganzen Etappe
        /// gewesen:</b> Wären Nachrichten- und Kettenschlüssel dieselben
        /// Bytes, könnte jeder, der eine einzige Nachricht mitliest, die ganze
        /// weitere Kette ausrechnen. Aus Forward Secrecy würde ihr Gegenteil -
        /// und nichts sähe anders aus, denn beide Seiten rechnen ja gleich
        /// falsch.
        /// </remarks>
        [Test]
        public void TheChainStep_MatchesTheSpecificationLiterally()
        {

            var kette = RandomNumberGenerator.GetBytes(32);

            var (mk, ck) = DoubleRatchet.AdvanceChain(kette);

            Assert.Multiple(() =>
            {

                Assert.That(Hex(mk),
                            Is.EqualTo(Hex(HMACSHA256.HashData(kette, new Byte[] { 0x01 }))),
                            "Der Nachrichtenschlüssel entsteht nicht aus 0x01.");

                Assert.That(Hex(ck),
                            Is.EqualTo(Hex(HMACSHA256.HashData(kette, new Byte[] { 0x02 }))),
                            "Der nächste Kettenschlüssel entsteht nicht aus 0x02.");

                Assert.That(Hex(mk), Is.Not.EqualTo(Hex(ck)));

            });

        }

        #endregion

        #region TheRootChain_MatchesTheSpecificationLiterally()

        /// <summary>
        /// Die Wurzelkette: der Wurzelschlüssel ist das <b>Salz</b>, der
        /// Diffie-Hellman-Wert das Eingabematerial, „OMEMO Root Chain" der
        /// Info-String, und die 64 Byte teilen sich in neue Wurzel und neue
        /// Kette.
        /// </summary>
        /// <remarks>
        /// <b>Zum vierten Mal dieselbe Lehre, und diesmal die teuerste.</b>
        /// Ohne diesen Test überlebten vier Mutationen: Salz und
        /// Eingabematerial vertauscht, Info-String weg, und - am schlimmsten -
        /// beide Hälften aus derselben. Die letzte macht Wurzel- und
        /// Kettenschlüssel zu denselben Bytes; aus einer mitgelesenen
        /// Nachricht liesse sich damit die Wurzel und daraus die ganze Sitzung
        /// aufrollen.
        ///
        /// Keine davon fiel auf, weil <b>beide Seiten dieselbe Funktion
        /// benutzen und deshalb weiterhin übereinkamen</b>. Ein Test, der
        /// „beide bekommen dasselbe heraus" prüft, kann eine falsche
        /// Ableitung nicht von einer richtigen unterscheiden - er prüft nur,
        /// dass sie auf beiden Seiten gleich ist.
        ///
        /// Also wieder: die Vorschrift ein zweites Mal wörtlich, mit einem
        /// zweiten HKDF nachgerechnet.
        /// </remarks>
        [Test]
        public void TheRootChain_MatchesTheSpecificationLiterally()
        {

            var wurzel   = RandomNumberGenerator.GetBytes(32);
            var dhWert   = RandomNumberGenerator.GetBytes(32);

            var hkdf = new Org.BouncyCastle.Crypto.Generators.HkdfBytesGenerator(
                           new Org.BouncyCastle.Crypto.Digests.Sha256Digest());

            hkdf.Init(new Org.BouncyCastle.Crypto.Parameters.HkdfParameters(
                          dhWert,                                        // Eingabematerial
                          wurzel,                                        // Salz
                          Encoding.UTF8.GetBytes("OMEMO Root Chain")));  // Info

            var erwartet = new Byte[64];
            hkdf.GenerateBytes(erwartet, 0, erwartet.Length);

            var (neueWurzel, neueKette) = DoubleRatchet.DeriveRootChain(wurzel, dhWert);

            Assert.Multiple(() =>
            {

                Assert.That(Hex(neueWurzel), Is.EqualTo(Hex(erwartet[..32])),
                            "Die neue Wurzel ist nicht die erste Hälfte.");

                Assert.That(Hex(neueKette), Is.EqualTo(Hex(erwartet[32..])),
                            "Die neue Kette ist nicht die zweite Hälfte.");

                Assert.That(Hex(neueWurzel), Is.Not.EqualTo(Hex(neueKette)),
                            "Wurzel und Kette sind dieselben Bytes - die Sitzung liesse sich aufrollen.");

            });

        }

        #endregion

        #region TheMessageKeyMaterial_MatchesTheSpecificationLiterally()

        /// <summary>
        /// Das Material eines Nachrichtenschlüssels: 80 Byte, Salz aus 32
        /// Nullbyte, Info „OMEMO Message Key Material".
        /// </summary>
        [Test]
        public void TheMessageKeyMaterial_MatchesTheSpecificationLiterally()
        {

            var mk = RandomNumberGenerator.GetBytes(32);

            var hkdf = new Org.BouncyCastle.Crypto.Generators.HkdfBytesGenerator(
                           new Org.BouncyCastle.Crypto.Digests.Sha256Digest());

            hkdf.Init(new Org.BouncyCastle.Crypto.Parameters.HkdfParameters(
                          mk,
                          new Byte[32],
                          Encoding.UTF8.GetBytes("OMEMO Message Key Material")));

            var erwartet = new Byte[80];
            hkdf.GenerateBytes(erwartet, 0, erwartet.Length);

            var (key, authKey, iv) = DoubleRatchet.Material(mk);

            Assert.Multiple(() =>
            {
                Assert.That(Hex(key),      Is.EqualTo(Hex(erwartet[..32])));
                Assert.That(Hex(authKey),  Is.EqualTo(Hex(erwartet[32..64])));
                Assert.That(Hex(iv),       Is.EqualTo(Hex(erwartet[64..])));
            });

        }

        #endregion

        #region TheHeader_IsEncodedAsSpecified()

        /// <summary>
        /// Der Kopf als <c>OMEMOMessage.proto</c> - Feld für Feld
        /// nachgerechnet.
        /// </summary>
        /// <remarks>
        /// <b>Zum vierten Mal dieselbe Vorsichtsmassnahme wie in D62 und
        /// D63.</b> Diese Bytes gehen in die Beigabe ein; beide Seiten müssen
        /// aus demselben Kopf dieselben bilden. Eine falsche Feldnummer oder
        /// eine andere Reihenfolge fiele im Haus nicht auf - beide Seiten
        /// rechnen ja gleich falsch -, und erst ein fremder Client bekäme
        /// lauter ungültige Prüfsummen.
        ///
        /// Deshalb stehen die erwarteten Bytes hier ausgeschrieben:
        /// <c>08</c> ist Feld 1 als Varint, <c>10</c> Feld 2 als Varint,
        /// <c>1a</c> Feld 3 als längenbegrenzt, <c>20</c> die Länge 32.
        /// </remarks>
        [Test]
        public void TheHeader_IsEncodedAsSpecified()
        {

            var dh = new Byte[32];
            for (var i = 0; i < 32; i++)
                dh[i] = (Byte) i;

            var kodiert = new RatchetHeader(dh, 300, 5).Encode();

            Assert.Multiple(() =>
            {

                // n = 5 (Feld 1), pn = 300 (Feld 2, zwei Varint-Byte),
                // dh_pub = 32 Byte (Feld 3).
                Assert.That(Hex(kodiert),
                            Is.EqualTo("0805" + "10ac02" + "1a20" + Hex(dh)));

                // Und die Gegenprobe: gelesen ergibt es wieder dieselben Werte.
                var felder = Protobuf.Read(kodiert).ToList();

                Assert.That(felder, Has.Count.EqualTo(3));
                Assert.That(felder[0].Field, Is.EqualTo(1));
                Assert.That(felder[0].Number, Is.EqualTo(5u));
                Assert.That(felder[1].Field, Is.EqualTo(2));
                Assert.That(felder[1].Number, Is.EqualTo(300u));
                Assert.That(felder[2].Field, Is.EqualTo(3));
                Assert.That(Hex(felder[2].Data), Is.EqualTo(Hex(dh)));

            });

        }

        #endregion

        #region ALongConversation_StaysInStep()

        /// <summary>
        /// Fünfzig Nachrichten in wechselnder Richtung, teils vertauscht
        /// zugestellt.
        /// </summary>
        /// <remarks>
        /// Der Test, der die drei Fälle des Entschlüsselns gegeneinander
        /// stellt: beiseitegelegter Schlüssel, Richtungswechsel und Vorspulen
        /// in der laufenden Kette. Jeder für sich ist leicht; falsch wird es
        /// an ihren Rändern, und die kommen nur in einem längeren Verlauf vor.
        /// </remarks>
        [Test]
        public void ALongConversation_StaysInStep()
        {

            var (alice, bob) = Paar();

            var zufall     = new Random(20260801);
            var unterwegs  = new List<(RatchetMessage Nachricht, String Text, Boolean VonAlice)>();

            for (var runde = 0; runde < 25; runde++)
            {

                var vonAlice  = runde % 3 != 2;
                var text      = $"Nachricht {runde}";

                // Wer gerade nicht senden kann, hört erst zu.
                if (!vonAlice && !bob.CanSend)
                    continue;

                unterwegs.Add(((vonAlice ? alice : bob).Encrypt(Text(text), Beigabe), text, vonAlice));

                // Ab und zu wird zugestellt, was liegt - in beliebiger
                // Reihenfolge.
                if (runde % 4 == 3)
                {

                    foreach (var (nachricht, text_, vonAlice_) in unterwegs.OrderBy(_ => zufall.Next()))
                        Assert.That((vonAlice_ ? bob : alice).Decrypt(nachricht, Beigabe),
                                    Is.EqualTo(Text(text_)),
                                    text_);

                    unterwegs.Clear();

                }

            }

            foreach (var (nachricht, text, vonAlice) in unterwegs.OrderBy(_ => zufall.Next()))
                Assert.That((vonAlice ? bob : alice).Decrypt(nachricht, Beigabe),
                            Is.EqualTo(Text(text)),
                            text);

        }

        #endregion

    }

}
