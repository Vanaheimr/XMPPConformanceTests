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

using System.Numerics;
using System.Security.Cryptography;
using System.Text;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die Kryptobausteine für OMEMO (XEP-0384) - gegen veröffentlichte
    /// Vektoren.
    /// </summary>
    /// <remarks>
    /// <b>Warum fremde Zahlen und nicht die eigenen.</b> Eine Verschlüsselung
    /// prüft sich selbst zu leicht: Wer entschlüsseln kann, was er selbst
    /// verschlüsselt hat, hat gezeigt, dass er zweimal denselben Fehler macht.
    /// Beweiskraft haben nur Zahlen, die jemand anders aufgeschrieben hat -
    /// RFC 7748 für X25519, RFC 8032 für die Punktarithmetik, RFC 5869 für
    /// HKDF, RFC 4231 für HMAC, NIST SP 800-38A für AES-CBC.
    ///
    /// Die Vektoren stehen hier ausserdem als Aussage darüber, <i>welches</i>
    /// Verfahren gemeint ist. Ein Austausch von SHA-256 gegen SHA-1 fiele
    /// sonst nirgends auf - beides liefert Bytes, und beides lässt sich wieder
    /// entschlüsseln.
    /// </remarks>
    [TestFixture]
    public class OmemoCryptoTests
    {

        #region Hilfsfunktionen

        private static Byte[] Hex(String hex)
            => Convert.FromHexString(hex.Replace(" ", "").Replace("\n", ""));

        private static String Hex(Byte[] bytes)
            => Convert.ToHexString(bytes).ToLowerInvariant();

        #endregion


        #region X25519_MatchesRfc7748Section61()

        /// <summary>
        /// RFC 7748, Abschnitt 6.1: Alice und Bob, ihre Schlüssel und der
        /// gemeinsame Geheimwert.
        /// </summary>
        [Test]
        public void X25519_MatchesRfc7748Section61()
        {

            var alice = Curve25519.KeyPairFromPrivate(
                            Hex("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a"));

            var bob   = Curve25519.KeyPairFromPrivate(
                            Hex("5dab087e624a8a4b79e17f8b83800ee66f3bb1292618b6fd1c2f8b27ff88e0eb"));

            Assert.Multiple(() =>
            {

                Assert.That(Hex(alice.PublicKey),
                            Is.EqualTo("8520f0098930a754748b7ddcb43ef75a0dbf3a0d26381af4eba4a98eaa9b4e6a"),
                            "Alices öffentlicher Schlüssel");

                Assert.That(Hex(bob.PublicKey),
                            Is.EqualTo("de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f"),
                            "Bobs öffentlicher Schlüssel");

                Assert.That(Hex(Curve25519.Agree(alice.PrivateKey, bob.PublicKey)),
                            Is.EqualTo("4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742"),
                            "Der gemeinsame Geheimwert");

                // Und beide Richtungen ergeben denselben - das ist der Sinn
                // der Übung und wäre mit einer einseitigen Prüfung nicht
                // gesagt.
                Assert.That(Hex(Curve25519.Agree(bob.PrivateKey, alice.PublicKey)),
                            Is.EqualTo(Hex(Curve25519.Agree(alice.PrivateKey, bob.PublicKey))));

            });

        }

        #endregion

        #region X25519_MatchesRfc7748Section52()

        /// <summary>
        /// RFC 7748, Abschnitt 5.2: eine einzelne Skalarmultiplikation mit
        /// einer u-Koordinate, die nicht der Basispunkt ist.
        /// </summary>
        /// <remarks>
        /// Der Vektor aus 6.1 allein liesse eine Verwechslung durchgehen, die
        /// hier auffällt: Er benutzt nur den Basispunkt und den jeweils
        /// anderen öffentlichen Schlüssel, beides wohlgeformte Werte aus der
        /// eigenen Erzeugung.
        /// </remarks>
        [Test]
        public void X25519_MatchesRfc7748Section52()
        {
            Assert.That(
                Hex(Curve25519.Agree(
                        Hex("a546e36bf0527c9d3b16154b82465edd62144c0ac1fc5a18506a2244ba449ac4"),
                        Hex("e6db6867583030db3594c1a424b15f7c726624ec26b3353b10a903a6d0ab1c4c"))),
                Is.EqualTo("c3da55379de9c6908e94ea4df28d084f32eccf03491c71f754b4075577a28552"));
        }

        #endregion

        #region ALowOrderPoint_IsRefused()

        /// <summary>
        /// Ein Punkt kleiner Ordnung ergibt lauter Nullen - und wird
        /// abgewiesen.
        /// </summary>
        /// <remarks>
        /// Das Ergebnis wäre kein Geheimnis, sondern eine Zahl, die der
        /// Angreifer vorher kennt: Er schickt einen solchen Punkt als sein
        /// Bundle, und jede daraus abgeleitete Sitzung hat einen Schlüssel,
        /// den er mitgerechnet hat. RFC 7748, Abschnitt 6.1 stellt die
        /// Prüfung frei; frei ist sie nur da, wo der öffentliche Schlüssel aus
        /// vertrauenswürdiger Quelle stammt - ein OMEMO-Bundle kommt vom
        /// Server.
        /// </remarks>
        [Test]
        public void ALowOrderPoint_IsRefused()
        {

            var eigen = Curve25519.GenerateKeyPair();

            Assert.Multiple(() =>
            {

                foreach (var punkt in new[] {
                             "0000000000000000000000000000000000000000000000000000000000000000",
                             "0100000000000000000000000000000000000000000000000000000000000000",
                             "e0eb7a7c3b41b8ae1656e3faf19fc46ada098deb9c32b1fd866205165f49b800"
                         })
                    Assert.That(() => Curve25519.Agree(eigen.PrivateKey, Hex(punkt)),
                                Throws.TypeOf<CryptographicException>(),
                                punkt);

            });

        }

        #endregion

        #region TheScalarMultiplication_MatchesRfc8032Section71()

        /// <summary>
        /// Die eigene Punktarithmetik gegen RFC 8032, Abschnitt 7.1.
        /// </summary>
        /// <remarks>
        /// <b>Der wichtigste Test dieser Datei.</b> Die Rechnung in
        /// <c>Ed25519Math</c> steht dort, weil BouncyCastle sein
        /// <c>ScalarMultBase</c> nicht herausgibt - und selbstgeschriebene
        /// Kurvenarithmetik ist genau der Ort, an dem ein Fehler kein falsches
        /// Ergebnis liefert, sondern ein plausibles.
        ///
        /// Geprüft wird über den Umweg, den Ed25519 selbst nimmt: Aus dem
        /// Seed wird mit SHA-512 und Klammerung der Skalar gebildet, und
        /// <c>sB</c> muss den im RFC abgedruckten öffentlichen Schlüssel
        /// ergeben. Damit hängt die Prüfung an fremden Zahlen und nicht an der
        /// eigenen Rechnung.
        /// </remarks>
        [Test]
        public void TheScalarMultiplication_MatchesRfc8032Section71()
        {

            (String Seed, String PublicKey)[] vektoren = [

                ("9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60",
                 "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a"),

                ("4ccd089b28ff96da9db6c346ec114e0f5b8a319f35aba624da8cf6ed4fb8a6fb",
                 "3d4017c3e843895a92b70aa74d1b7ebc9c982ccf2ec4968cc0cd55f12af4660c"),

                ("c5aa8df43f9f837bedb7442f31dcb7b166d38535076f094b85ce3a2e0b4458f7",
                 "fc51cd8e6218a1a38da47ed00230f0580816ed13ba3303ac5deb911548908025")

            ];

            Assert.Multiple(() =>
            {

                foreach (var (seed, erwartet) in vektoren)
                {

                    var h = SHA512.HashData(Hex(seed))[..32];

                    h[0]  &= 248;
                    h[31] &= 127;
                    h[31] |= 64;

                    Assert.That(Hex(Ed25519Math.ScalarMultBaseEncoded(
                                        new BigInteger(h, isUnsigned: true, isBigEndian: false))),
                                Is.EqualTo(erwartet),
                                seed);

                }

            });

        }

        #endregion

        #region TheBasePoints_AreTheSamePoint()

        /// <summary>
        /// Die Umrechnung von der Montgomery- in die Edwards-Form, geprüft am
        /// einzigen Punkt, den beide Seiten benennen.
        /// </summary>
        /// <remarks>
        /// X25519 rechnet ab <c>u = 9</c> (RFC 7748, Abschnitt 4.1), Ed25519
        /// ab seinem Basispunkt (RFC 8032, Abschnitt 5.1) - und es ist
        /// derselbe Punkt in zwei Schreibweisen. Ergibt die Umrechnung ihn
        /// nicht, ist sie falsch, und zwar so, dass jede Signaturprüfung
        /// danach scheitert, ohne zu sagen warum.
        /// </remarks>
        [Test]
        public void TheBasePoints_AreTheSamePoint()
        {

            var u9 = new Byte[32];
            u9[0] = 9;

            Assert.That(Hex(Curve25519.MontgomeryToEdwards(u9)),
                        Is.EqualTo(Hex(Ed25519Math.ScalarMultBaseEncoded(BigInteger.One))
                                       // Das Vorzeichenbit kennt die u-Koordinate nicht;
                                       // die Umrechnung liefert es immer gelöscht.
                                       .Substring(0, 62) + "66"));

        }

        #endregion

        #region ASignature_VerifiesWithAForeignVerifier()

        /// <summary>
        /// XEdDSA: unterschrieben mit dem Montgomery-Schlüssel, geprüft mit
        /// dem gewöhnlichen Ed25519-Prüfer aus BouncyCastle.
        /// </summary>
        /// <remarks>
        /// Das ist die Aussage von XEdDSA und zugleich die einzige unabhängige
        /// Prüfung, die hier zu haben ist: Es gibt keine veröffentlichten
        /// XEdDSA-Vektoren. Der Prüfer stammt aber aus fremder Hand und kennt
        /// die eigene Rechnung nicht - er akzeptiert nur, was auch jeder
        /// andere Ed25519-Prüfer akzeptiert, und genau darauf kommt es an:
        /// Die Gegenstelle prüft mit ihrem eigenen.
        /// </remarks>
        [Test]
        public void ASignature_VerifiesWithAForeignVerifier()
        {

            var schluessel  = Curve25519.GenerateKeyPair();
            var nachricht   = Encoding.UTF8.GetBytes("Signed PreKey Nummer 1");

            var signatur    = Curve25519.Sign(schluessel.PrivateKey, nachricht);

            Assert.Multiple(() =>
            {

                Assert.That(signatur.Length, Is.EqualTo(64));

                Assert.That(Curve25519.Verify(schluessel.PublicKey, nachricht, signatur), Is.True,
                            "Die eigene Signatur prüft sich nicht.");

                // Ein anderer Schlüssel, dieselbe Nachricht.
                Assert.That(Curve25519.Verify(Curve25519.GenerateKeyPair().PublicKey, nachricht, signatur),
                            Is.False,
                            "Die Signatur gilt auch für einen fremden Schlüssel.");

            });

        }

        #endregion

        #region ATamperedSignature_IsRefused()

        /// <summary>
        /// Jede veränderte Stelle - in der Nachricht wie in der Signatur -
        /// führt zur Ablehnung.
        /// </summary>
        [Test]
        public void ATamperedSignature_IsRefused()
        {

            var schluessel  = Curve25519.GenerateKeyPair();
            var nachricht   = Encoding.UTF8.GetBytes("Signed PreKey Nummer 1");
            var signatur    = Curve25519.Sign(schluessel.PrivateKey, nachricht);

            Assert.Multiple(() =>
            {

                var andere = Encoding.UTF8.GetBytes("Signed PreKey Nummer 2");
                Assert.That(Curve25519.Verify(schluessel.PublicKey, andere, signatur), Is.False,
                            "Eine andere Nachricht wird angenommen.");

                // Jedes Byte der Signatur einzeln - R und s.
                for (var i = 0; i < signatur.Length; i++)
                {

                    var verbogen = (Byte[]) signatur.Clone();
                    verbogen[i] ^= 0x01;

                    Assert.That(Curve25519.Verify(schluessel.PublicKey, nachricht, verbogen), Is.False,
                                $"Byte {i} der Signatur darf sich nicht ändern lassen.");

                }

                Assert.That(Curve25519.Verify(schluessel.PublicKey, nachricht, signatur[..63]), Is.False,
                            "Eine zu kurze Signatur wird angenommen.");

            });

        }

        #endregion

        #region ASpuriousHighBit_IsIgnored()

        /// <summary>
        /// Das oberste Bit der u-Koordinate wird beim Lesen verworfen
        /// (RFC 7748, Abschnitt 5).
        /// </summary>
        /// <remarks>
        /// Eigene Schlüssel tragen es nie - eine u-Koordinate ist kleiner als
        /// 2^255. Ein fremdes Bundle kommt aber vom Server, und wer das Bit
        /// setzt, veränderte ohne diese Maskierung den Schlüssel: Die Signatur
        /// des Signed PreKey prüfte sich nicht mehr, und die Gegenstelle sähe
        /// einen Angriff, wo eine Kleinigkeit steht.
        /// </remarks>
        [Test]
        public void ASpuriousHighBit_IsIgnored()
        {

            var paar       = Curve25519.GenerateKeyPair();
            var nachricht  = Encoding.UTF8.GetBytes("mit gesetztem Bit 255");
            var signatur   = Curve25519.Sign(paar.PrivateKey, nachricht);

            var verbogen   = (Byte[]) paar.PublicKey.Clone();
            verbogen[31]  |= 0x80;

            Assert.That(Curve25519.Verify(verbogen, nachricht, signatur), Is.True,
                        "Das oberste Bit wurde mitgerechnet, statt verworfen zu werden.");

        }

        #endregion

        #region BothSignsOfTheScalar_Work()

        /// <summary>
        /// Beide Vorzeichen des Skalars, und zwar zuverlässig.
        /// </summary>
        /// <remarks>
        /// XEdDSA rechnet mit <c>-k</c> weiter, wenn <c>kB</c> das
        /// Vorzeichenbit trägt - das ist bei der Hälfte aller Schlüssel der
        /// Fall. Ein Test mit einem erzeugten Schlüssel prüft diesen Zweig
        /// also in jedem zweiten Lauf nicht, und ein Fehler darin sähe aus wie
        /// ein sprunghafter Test.
        ///
        /// Genau das ist beim ersten Durchlauf passiert: Die Negation lief
        /// über die Gruppenordnung hinaus und ergab eine negative Zahl. Die
        /// Rechnung ging dann nicht falsch aus, sondern gar nicht - was ein
        /// Glück ist. Der stille Fall wäre eine Signatur gewesen, die niemand
        /// prüfen kann.
        ///
        /// Deshalb hier so viele Schlüssel, dass beide Zweige mit an Sicherheit
        /// grenzender Wahrscheinlichkeit vorkommen - und die Zählung sagt
        /// hinterher, dass es auch so war.
        /// </remarks>
        [Test]
        public void BothSignsOfTheScalar_Work()
        {

            var nachricht  = Encoding.UTF8.GetBytes("beide Vorzeichen");
            var negiert    = 0;

            for (var i = 0; i < 32; i++)
            {

                var paar      = Curve25519.GenerateKeyPair();
                var signatur  = Curve25519.Sign(paar.PrivateKey, nachricht);

                Assert.That(Curve25519.Verify(paar.PublicKey, nachricht, signatur), Is.True,
                            $"Schlüssel {i} unterschreibt nicht prüfbar.");

                // Trägt kB das Vorzeichenbit, musste negiert werden.
                var kB = Ed25519Math.ScalarMultBaseEncoded(
                             new BigInteger(paar.PrivateKey, isUnsigned: true, isBigEndian: false));

                if ((kB[31] & 0x80) != 0)
                    negiert++;

            }

            Assert.That(negiert, Is.GreaterThan(0).And.LessThan(32),
                        "Es kamen nur Schlüssel eines Vorzeichens vor - der Test prüft dann nur den halben Weg.");

        }

        #endregion

        #region TwoSignatures_AreNotTheSame()

        /// <summary>
        /// Zweimal dieselbe Nachricht ergibt zwei verschiedene Signaturen.
        /// </summary>
        /// <remarks>
        /// XEdDSA mischt 64 zufällige Byte in den Nonce (Abschnitt 2.4). Ohne
        /// sie wäre die Signatur allein von Schlüssel und Nachricht bestimmt -
        /// bei Ed25519 ist das Absicht, hier wäre es eine Preisgabe: Der
        /// Signed PreKey wird über seine Lebenszeit mehrfach unterschrieben,
        /// und zwei gleiche Signaturen sagten einem Mitlesenden, dass sich
        /// nichts geändert hat.
        /// </remarks>
        [Test]
        public void TwoSignatures_AreNotTheSame()
        {

            var schluessel  = Curve25519.GenerateKeyPair();
            var nachricht   = Encoding.UTF8.GetBytes("zweimal dasselbe");

            Assert.That(Hex(Curve25519.Sign(schluessel.PrivateKey, nachricht)),
                        Is.Not.EqualTo(Hex(Curve25519.Sign(schluessel.PrivateKey, nachricht))));

        }

        #endregion

        #region TheKdf_IsHkdfSha256()

        /// <summary>
        /// RFC 5869, Anhang A.1 - welches Verfahren hier gemeint ist.
        /// </summary>
        /// <remarks>
        /// Nicht die eigene Ableitung wird geprüft, sondern der Baustein
        /// darunter: Ein Austausch von SHA-256 gegen SHA-1 fiele sonst
        /// nirgends auf. Beides liefert Bytes, und beides lässt sich wieder
        /// entschlüsseln.
        /// </remarks>
        [Test]
        public void TheKdf_IsHkdfSha256()
        {
            Assert.That(
                Hex(HKDF.DeriveKey(HashAlgorithmName.SHA256,
                                   ikm:           Hex("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"),
                                   salt:          Hex("000102030405060708090a0b0c"),
                                   info:          Hex("f0f1f2f3f4f5f6f7f8f9"),
                                   outputLength:  42)),
                Is.EqualTo("3cb25f25faacd57a90434f64d0362f2a" +
                           "2d2d0a90cf1a5a4c5db02d56ecc4c5bf" +
                           "34007208d5b887185865"));
        }

        #endregion

        #region TheMac_IsHmacSha256()

        /// <summary>RFC 4231, Testfall 1 - dasselbe für den HMAC.</summary>
        [Test]
        public void TheMac_IsHmacSha256()
        {
            Assert.That(
                Hex(HMACSHA256.HashData(Hex("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"),
                                        Encoding.UTF8.GetBytes("Hi There"))),
                Is.EqualTo("b0344c61d8db38535ca8afceaf0bf12b881dc200c9833da726e9376c2e32cff7"));
        }

        #endregion

        #region TheBlockCipher_IsAes256Cbc()

        /// <summary>
        /// NIST SP 800-38A, Anhang F.2.5 - der erste Block von AES-256-CBC.
        /// </summary>
        [Test]
        public void TheBlockCipher_IsAes256Cbc()
        {

            using var aes = Aes.Create();
            aes.Key = Hex("603deb1015ca71be2b73aef0857d77811f352c073b6108d72d9810a30914dff4");

            Assert.That(
                Hex(aes.EncryptCbc(Hex("6bc1bee22e409f96e93d7e117393172a"),
                                   Hex("000102030405060708090a0b0c0d0e0f"),
                                   PaddingMode.None)),
                Is.EqualTo("f58c4c04d6e5f1ba779eabfb5f7bfbd6"));

        }

        #endregion

        #region ThePayloadMaterial_IsSplitAsSpecified()

        /// <summary>
        /// XEP-0384, Abschnitt 4.4: 80 Byte aus dem Nachrichtenschlüssel -
        /// 32 Byte Schlüssel, 32 Byte Authentisierung, 16 Byte IV.
        /// </summary>
        [Test]
        public void ThePayloadMaterial_IsSplitAsSpecified()
        {

            var schluessel = RandomNumberGenerator.GetBytes(32);

            var (key, authKey, iv) = OmemoPayloadCipher.Material(schluessel);

            Assert.Multiple(() =>
            {

                Assert.That(key.Length,      Is.EqualTo(32));
                Assert.That(authKey.Length,  Is.EqualTo(32));
                Assert.That(iv.Length,       Is.EqualTo(16));

                // Die drei Teile stammen aus einer Ableitung und müssen
                // verschieden sein - ein Schlüssel, der zugleich sein eigener
                // IV ist, hebt die Betriebsart auf.
                Assert.That(Hex(key), Is.Not.EqualTo(Hex(authKey)));

                // Dieselbe Eingabe, dasselbe Material: Der IV reist nicht mit
                // der Nachricht, der Empfänger muss ihn ableiten.
                var (key2, _, iv2) = OmemoPayloadCipher.Material(schluessel);
                Assert.That(Hex(key2), Is.EqualTo(Hex(key)));
                Assert.That(Hex(iv2),  Is.EqualTo(Hex(iv)));

                // Ein anderer Schlüssel, ein anderer IV - sonst wiederholte
                // sich der IV über alle Nachrichten einer Sitzung.
                Assert.That(Hex(OmemoPayloadCipher.Material(RandomNumberGenerator.GetBytes(32)).Iv),
                            Is.Not.EqualTo(Hex(iv)));

            });

        }

        #endregion

        #region ThePayloadMaterial_MatchesASecondImplementation()

        /// <summary>
        /// Dieselbe Ableitung, mit einem zweiten HKDF gerechnet - und mit den
        /// Parametern aus XEP-0384, Abschnitt 4.4 buchstäblich hingeschrieben.
        /// </summary>
        /// <remarks>
        /// <b>Dieser Test kam durch eine überlebende Mutation dazu.</b> Der
        /// Info-String liess sich auf <c>""</c> setzen, ohne dass ein Test
        /// etwas sagte - denn alle prüften nur die Struktur der 80 Byte, nicht
        /// ihren Wert. Der Fehler wäre in diesem Haus nie aufgefallen: Zwei
        /// Clients mit demselben falschen String verstehen sich bestens. Erst
        /// eine fremde Gegenstelle - Conversations, Dino, Gajim - bekäme
        /// Buchstabensalat, und die gibt es hier nicht.
        ///
        /// Deshalb steht die Vorschrift hier ein zweites Mal und
        /// buchstäblich: 32 Nullbyte als Salz, „OMEMO Payload" als Info,
        /// 80 Byte Ausgabe. Wer den Wert im Quelltext ändert, muss ihn hier
        /// mitändern - und dann sieht er, dass er die Spezifikation verlässt.
        ///
        /// Gerechnet wird mit BouncyCastles HKDF und nicht mit dem der BCL:
        /// Sonst prüfte dieselbe Rechnung sich selbst.
        /// </remarks>
        [Test]
        public void ThePayloadMaterial_MatchesASecondImplementation()
        {

            var schluessel = Hex("000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");

            var hkdf = new Org.BouncyCastle.Crypto.Generators.HkdfBytesGenerator(
                           new Org.BouncyCastle.Crypto.Digests.Sha256Digest());

            hkdf.Init(new Org.BouncyCastle.Crypto.Parameters.HkdfParameters(
                          schluessel,
                          new Byte[32],
                          Encoding.UTF8.GetBytes("OMEMO Payload")));

            var erwartet = new Byte[80];
            hkdf.GenerateBytes(erwartet, 0, erwartet.Length);

            var (key, authKey, iv) = OmemoPayloadCipher.Material(schluessel);

            Assert.Multiple(() =>
            {
                Assert.That(Hex(key),      Is.EqualTo(Hex(erwartet[..32])),  "Chiffrierschlüssel");
                Assert.That(Hex(authKey),  Is.EqualTo(Hex(erwartet[32..64])), "Authentisierungsschlüssel");
                Assert.That(Hex(iv),       Is.EqualTo(Hex(erwartet[64..])),   "IV");
            });

        }

        #endregion

        #region ThePayload_IsEncryptedAndAuthenticated()

        /// <summary>
        /// Der übliche Weg: verschlüsseln, entschlüsseln, und die 48 Byte, die
        /// je Empfänger durch den Ratchet gehen.
        /// </summary>
        [Test]
        public void ThePayload_IsEncryptedAndAuthenticated()
        {

            var klartext = Encoding.UTF8.GetBytes("Treffen wir uns um acht?");

            var nutzlast = OmemoPayloadCipher.Encrypt(klartext);

            Assert.Multiple(() =>
            {

                Assert.That(nutzlast.KeyAndHmac.Length, Is.EqualTo(48),
                            "32 Byte Schlüssel und 16 Byte HMAC gehen durch den Ratchet.");

                Assert.That(nutzlast.Ciphertext.Length % 16, Is.EqualTo(0),
                            "AES-CBC mit PKCS#7 endet auf einer Blockgrenze.");

                Assert.That(Hex(nutzlast.Ciphertext), Does.Not.Contain(Hex(klartext)),
                            "Der Klartext steht im Geheimtext.");

                Assert.That(OmemoPayloadCipher.Decrypt(nutzlast.Ciphertext, nutzlast.KeyAndHmac),
                            Is.EqualTo(klartext));

            });

        }

        #endregion

        #region ATamperedPayload_IsRefused()

        /// <summary>
        /// Ein verändertes Byte im Geheimtext oder im HMAC führt zur
        /// Ablehnung - und nicht zu Buchstabensalat.
        /// </summary>
        /// <remarks>
        /// Geprüft wird <b>vor</b> dem Entschlüsseln (Encrypt-then-MAC).
        /// Andersherum müsste der Empfänger entschlüsseln, bevor er weiss, ob
        /// er darf - und ein Angreifer bekäme mit den Fehlermeldungen des
        /// Paddings ein Orakel, mit dem sich CBC Byte für Byte aufrollen lässt.
        /// </remarks>
        [Test]
        public void ATamperedPayload_IsRefused()
        {

            var nutzlast = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes("Treffen wir uns um acht?"));

            Assert.Multiple(() =>
            {

                var verbogen = (Byte[]) nutzlast.Ciphertext.Clone();
                verbogen[0] ^= 0x01;

                Assert.That(() => OmemoPayloadCipher.Decrypt(verbogen, nutzlast.KeyAndHmac),
                            Throws.TypeOf<CryptographicException>(),
                            "Ein verändertes Byte im Geheimtext kommt durch.");

                var falscherHmac = (Byte[]) nutzlast.KeyAndHmac.Clone();
                falscherHmac[47] ^= 0x01;

                Assert.That(() => OmemoPayloadCipher.Decrypt(nutzlast.Ciphertext, falscherHmac),
                            Throws.TypeOf<CryptographicException>(),
                            "Ein veränderter HMAC kommt durch.");

                var falscherSchluessel = (Byte[]) nutzlast.KeyAndHmac.Clone();
                falscherSchluessel[0] ^= 0x01;

                Assert.That(() => OmemoPayloadCipher.Decrypt(nutzlast.Ciphertext, falscherSchluessel),
                            Throws.TypeOf<CryptographicException>(),
                            "Ein falscher Schlüssel kommt durch - der HMAC hängt am selben Material.");

            });

        }

        #endregion

    }

}
