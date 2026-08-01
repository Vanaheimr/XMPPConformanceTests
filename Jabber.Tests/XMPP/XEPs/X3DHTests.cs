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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// X3DH (XEP-0384, Abschnitt 4.2): eine Sitzung beginnt, ohne dass beide
    /// gleichzeitig da sind.
    /// </summary>
    /// <remarks>
    /// Der Kern jedes Tests hier ist derselbe: <b>Beide Seiten müssen
    /// dasselbe herausbekommen.</b> Ein Fehler in der Reihenfolge der vier
    /// Diffie-Hellman-Werte, in der Zuordnung der Schlüssel oder in der
    /// Beigabe liefert kein schlechtes Geheimnis - er liefert ein
    /// einwandfreies, das nur die andere Seite nicht kennt. Das fällt ohne
    /// diesen Vergleich erst bei der ersten Nachricht auf, und dort sieht es
    /// aus wie eine Fälschung.
    /// </remarks>
    [TestFixture]
    public class X3DHTests
    {

        #region Hilfsfunktionen

        private static String Hex(Byte[] bytes)
            => Convert.ToHexString(bytes).ToLowerInvariant();

        #endregion


        #region BothSides_DeriveTheSameSecret()

        /// <summary>
        /// Der ganze Zweck: Alice rechnet aus Bobs Bundle, Bob rechnet aus
        /// Alices Nachricht, und beide haben dasselbe.
        /// </summary>
        [Test]
        public void BothSides_DeriveTheSameSecret()
        {

            var alice = OmemoIdentity.Create();
            var bob   = OmemoIdentity.Create();

            var beiAlice = X3DH.Initiate(alice, bob.Bundle());

            var beiBob   = X3DH.Accept(bob,
                                       alice.PublicIdentityKey,
                                       beiAlice.EphemeralKey!,
                                       bob.SignedPreKeyId,
                                       beiAlice.UsedPreKeyId);

            Assert.Multiple(() =>
            {

                Assert.That(Hex(beiBob.SharedSecret), Is.EqualTo(Hex(beiAlice.SharedSecret)),
                            "Die beiden Seiten haben verschiedene Geheimnisse.");

                Assert.That(beiAlice.SharedSecret.Length, Is.EqualTo(32));

                Assert.That(Hex(beiBob.AssociatedData), Is.EqualTo(Hex(beiAlice.AssociatedData)),
                            "Die Beigabe stimmt nicht überein - die Reihenfolge der IdentityKeys.");

                Assert.That(beiAlice.UsedPreKeyId, Is.Not.Null,
                            "Ein frisches Bundle bringt PreKeys mit; es wurde keiner benutzt.");

            });

        }

        #endregion

        #region TwoSessions_DoNotShareASecret()

        /// <summary>
        /// Zwei Sitzungen zum selben Gerät ergeben verschiedene Geheimnisse.
        /// </summary>
        /// <remarks>
        /// Dafür gibt es die Einwegschlüssel und die PreKeys. Wären zwei erste
        /// Nachrichten gleich, liesse sich eine alte noch einmal einspielen -
        /// und die Gegenstelle antwortete darauf, als sei sie neu.
        /// </remarks>
        [Test]
        public void TwoSessions_DoNotShareASecret()
        {

            var alice   = OmemoIdentity.Create();
            var bob     = OmemoIdentity.Create();
            var bundle  = bob.Bundle();

            var erste   = X3DH.Initiate(alice, bundle, bundle.PreKeys[0].Id);
            var zweite  = X3DH.Initiate(alice, bundle, bundle.PreKeys[1].Id);

            Assert.Multiple(() =>
            {

                Assert.That(Hex(zweite.SharedSecret), Is.Not.EqualTo(Hex(erste.SharedSecret)));

                Assert.That(Hex(zweite.EphemeralKey!), Is.Not.EqualTo(Hex(erste.EphemeralKey!)),
                            "Zweimal derselbe Einwegschlüssel.");

                // Und die Beigabe ist in beiden dieselbe: Sie beschreibt, wer
                // miteinander spricht, nicht welche Sitzung.
                Assert.That(Hex(zweite.AssociatedData), Is.EqualTo(Hex(erste.AssociatedData)));

            });

        }

        #endregion

        #region AUsedPreKey_IsGone()

        /// <summary>
        /// Ein benutzter PreKey ist danach fort - und ein zweiter Versuch
        /// darauf scheitert.
        /// </summary>
        /// <remarks>
        /// Entnehmen und Löschen sind ein Schritt. Ein PreKey, der zweimal
        /// gilt, ergibt zweimal dieselbe Sitzung, und damit ist sie
        /// wiederholbar.
        /// </remarks>
        [Test]
        public void AUsedPreKey_IsGone()
        {

            var alice   = OmemoIdentity.Create();
            var bob     = OmemoIdentity.Create();
            var bundle  = bob.Bundle();

            var vorher  = bob.AvailablePreKeys;
            var beiAlice = X3DH.Initiate(alice, bundle);

            X3DH.Accept(bob, alice.PublicIdentityKey, beiAlice.EphemeralKey!,
                        bob.SignedPreKeyId, beiAlice.UsedPreKeyId);

            Assert.Multiple(() =>
            {

                Assert.That(bob.AvailablePreKeys, Is.EqualTo(vorher - 1),
                            "Der PreKey wurde nicht verbraucht.");

                Assert.That(() => X3DH.Accept(bob, alice.PublicIdentityKey, beiAlice.EphemeralKey!,
                                              bob.SignedPreKeyId, beiAlice.UsedPreKeyId),
                            Throws.TypeOf<CryptographicException>(),
                            "Dieselbe erste Nachricht liess sich ein zweites Mal annehmen.");

            });

        }

        #endregion

        #region WithoutAPreKey_TheSessionStillStarts()

        /// <summary>
        /// Ist der Vorrat leer, beginnt die Sitzung trotzdem - nur ohne den
        /// vierten Diffie-Hellman.
        /// </summary>
        /// <remarks>
        /// Das ist ausdrücklich vorgesehen und kostet genau eine Eigenschaft:
        /// Zwei erste Nachrichten an dasselbe Gerät könnten dann dieselbe
        /// Sitzung ergeben, wenn auch der Einwegschlüssel derselbe wäre. Eine
        /// Verweigerung wäre die schlechtere Antwort - sie machte einen
        /// leeren Vorrat zu einem Ausfall der Erreichbarkeit.
        /// </remarks>
        [Test]
        public void WithoutAPreKey_TheSessionStillStarts()
        {

            var alice = OmemoIdentity.Create();
            var bob   = OmemoIdentity.Create();

            var ohnePreKeys = bob.Bundle() with { PreKeys = [] };

            var beiAlice = X3DH.Initiate(alice, ohnePreKeys);

            var beiBob   = X3DH.Accept(bob, alice.PublicIdentityKey, beiAlice.EphemeralKey!,
                                       bob.SignedPreKeyId, null);

            Assert.Multiple(() =>
            {
                Assert.That(beiAlice.UsedPreKeyId, Is.Null);
                Assert.That(Hex(beiBob.SharedSecret), Is.EqualTo(Hex(beiAlice.SharedSecret)));
            });

        }

        #endregion

        #region ATamperedBundle_IsRefused()

        /// <summary>
        /// Ein Bundle mit falscher Signatur führt zum Abbruch, nicht zu einer
        /// Warnung.
        /// </summary>
        /// <remarks>
        /// Das Bundle kommt vom Server der Gegenstelle - also von genau der
        /// Partei, gegen die eine Ende-zu-Ende-Verschlüsselung schützen soll.
        /// Tauschte er den Signed PreKey gegen seinen eigenen, läse er jede
        /// erste Nachricht mit, und der Fingerabdruck des IdentityKey bliebe
        /// dabei unverändert: Der Mensch, der ihn vergleicht, sähe nichts.
        ///
        /// Eine Sitzung auf einem solchen Bundle wäre schlimmer als keine -
        /// sie sähe aus wie eine verschlüsselte.
        /// </remarks>
        [Test]
        public void ATamperedBundle_IsRefused()
        {

            var alice = OmemoIdentity.Create();
            var bob   = OmemoIdentity.Create();
            var boese = OmemoIdentity.Create();

            Assert.Multiple(() =>
            {

                // Der Signed PreKey ausgetauscht, die Signatur stehengelassen.
                var untergeschoben = bob.Bundle() with {
                                         SignedPreKey = boese.SignedPreKey.PublicKey
                                     };

                Assert.That(untergeschoben.SignatureIsValid(), Is.False);
                Assert.That(() => X3DH.Initiate(alice, untergeschoben),
                            Throws.TypeOf<CryptographicException>(),
                            "Ein untergeschobener Signed PreKey kam durch.");

                // Der IdentityKey ausgetauscht, alles andere gelassen: Dann
                // passt die Signatur nicht mehr zum genannten Absender.
                var fremderIk = bob.Bundle() with { IdentityKey = boese.PublicIdentityKey };

                Assert.That(fremderIk.SignatureIsValid(), Is.False);
                Assert.That(() => X3DH.Initiate(alice, fremderIk),
                            Throws.TypeOf<CryptographicException>());

                // Ein einzelnes verbogenes Byte in der Signatur.
                var verbogen = (Byte[]) bob.Bundle().SignedPreKeySignature.Clone();
                verbogen[0] ^= 0x01;

                Assert.That((bob.Bundle() with { SignedPreKeySignature = verbogen }).SignatureIsValid(),
                            Is.False);

            });

        }

        #endregion

        #region AnUnknownSignedPreKey_IsRefused()

        /// <summary>
        /// Nennt die Gegenstelle einen anderen Signed PreKey als den
        /// aktuellen, wird abgewiesen statt geraten.
        /// </summary>
        [Test]
        public void AnUnknownSignedPreKey_IsRefused()
        {

            var alice = OmemoIdentity.Create();
            var bob   = OmemoIdentity.Create();

            var beiAlice = X3DH.Initiate(alice, bob.Bundle());

            bob.RotateSignedPreKey();

            // Alice hat mit Signed PreKey 1 gerechnet; Bob steht inzwischen
            // auf 2 und kann den alten nicht mehr.
            Assert.That(() => X3DH.Accept(bob, alice.PublicIdentityKey, beiAlice.EphemeralKey!,
                                          1u,
                                          beiAlice.UsedPreKeyId),
                        Throws.TypeOf<CryptographicException>(),
                        "Der gewechselte Signed PreKey wurde stillschweigend übergangen.");

        }

        #endregion

        #region TheIdentityKey_TravelsInEdwardsForm()

        /// <summary>
        /// Der IdentityKey geht in Ed25519-Form über die Leitung und wird für
        /// den Diffie-Hellman zurückgerechnet.
        /// </summary>
        /// <remarks>
        /// XEP-0384, Abschnitt 5.3.2: „The public key is ALWAYS transferred in
        /// its Ed25519 form." Beide Richtungen müssen zusammenpassen -
        /// andernfalls rechnet die eine Seite mit einem anderen Punkt als die
        /// andere, und zwar ohne Fehlermeldung: Beides sind 32 gültige Byte.
        /// </remarks>
        [Test]
        public void TheIdentityKey_TravelsInEdwardsForm()
        {

            var eigen = OmemoIdentity.Create();

            Assert.Multiple(() =>
            {

                Assert.That(Hex(eigen.PublicIdentityKey),
                            Is.Not.EqualTo(Hex(eigen.IdentityKey.PublicKey)),
                            "Beide Formen sind gleich - dann wurde nicht umgerechnet.");

                Assert.That(Hex(Curve25519.EdwardsToMontgomery(eigen.PublicIdentityKey)),
                            Is.EqualTo(Hex(eigen.IdentityKey.PublicKey)),
                            "Hin und zurück ergibt nicht denselben Schlüssel.");

                Assert.That(eigen.Fingerprint, Has.Length.EqualTo(64));

                // Und die eigene Signatur prüft sich über die Ed25519-Form -
                // das ist die Fassung, die die Gegenstelle bekommt.
                Assert.That(Curve25519.VerifyEdwards(eigen.PublicIdentityKey,
                                                     eigen.SignedPreKey.PublicKey,
                                                     eigen.SignedPreKeySignature),
                            Is.True);

            });

        }

        #endregion

        #region ThePreKeys_AreDistinctAndNumbered()

        /// <summary>
        /// Hundert PreKeys, alle verschieden, fortlaufend nummeriert - und
        /// nachgefüllt wird ohne Wiederverwendung der Kennungen.
        /// </summary>
        /// <remarks>
        /// Eine wiederverwendete Kennung wäre eine Verwechslung: Eine
        /// Nachricht, die unterwegs liegenblieb und den alten PreKey nennt,
        /// fände beim Ankommen einen neuen unter derselben Nummer - und ergäbe
        /// eine Sitzung, die es nie gab.
        /// </remarks>
        [Test]
        public void ThePreKeys_AreDistinctAndNumbered()
        {

            var eigen  = OmemoIdentity.Create();
            var bundle = eigen.Bundle();

            Assert.Multiple(() =>
            {

                Assert.That(bundle.PreKeys, Has.Count.EqualTo(OmemoIdentity.PreKeyCount));

                Assert.That(bundle.PreKeys.Select(p => p.Id).Distinct().Count(),
                            Is.EqualTo(OmemoIdentity.PreKeyCount),
                            "Zwei PreKeys teilen sich eine Kennung.");

                Assert.That(bundle.PreKeys.Select(p => Hex(p.PublicKey)).Distinct().Count(),
                            Is.EqualTo(OmemoIdentity.PreKeyCount),
                            "Zwei PreKeys sind derselbe Schlüssel.");

                Assert.That(bundle.PreKeys.All(p => p.Id > 0), Is.True,
                            "Abschnitt 5.3.2 verlangt positive Kennungen.");

            });

            // Zwei verbrauchen, nachfüllen: wieder hundert, und die beiden
            // Kennungen kommen nicht wieder.
            var verbraucht = new[] { bundle.PreKeys[0].Id, bundle.PreKeys[1].Id };

            foreach (var id in verbraucht)
                eigen.TakePreKey(id);

            var nachher = eigen.ReplenishPreKeys();

            Assert.Multiple(() =>
            {

                Assert.That(nachher, Has.Count.EqualTo(OmemoIdentity.PreKeyCount));

                foreach (var id in verbraucht)
                    Assert.That(nachher.Any(p => p.Id == id), Is.False,
                                $"Die Kennung {id} wurde wiederverwendet.");

            });

        }

        #endregion

        #region TheRotation_ChangesKeyAndSignature()

        /// <summary>
        /// Der Wechsel des Signed PreKey erneuert Schlüssel, Kennung und
        /// Signatur - und lässt den IdentityKey stehen.
        /// </summary>
        /// <remarks>
        /// Der Wechsel ist der Grund, warum ein gestohlener Schlüssel nicht
        /// rückwirkend alles öffnet. Der IdentityKey darf dabei nicht
        /// mitwechseln: An seinem Fingerabdruck hängt jeder Vergleich, den ein
        /// Mensch je angestellt hat.
        /// </remarks>
        [Test]
        public void TheRotation_ChangesKeyAndSignature()
        {

            var eigen   = OmemoIdentity.Create();
            var vorher  = eigen.Bundle();

            eigen.RotateSignedPreKey();

            var nachher = eigen.Bundle();

            Assert.Multiple(() =>
            {

                Assert.That(Hex(nachher.SignedPreKey), Is.Not.EqualTo(Hex(vorher.SignedPreKey)));
                Assert.That(nachher.SignedPreKeyId,    Is.GreaterThan(vorher.SignedPreKeyId));

                Assert.That(Hex(nachher.SignedPreKeySignature),
                            Is.Not.EqualTo(Hex(vorher.SignedPreKeySignature)));

                Assert.That(nachher.SignatureIsValid(), Is.True,
                            "Der neue Signed PreKey ist nicht gültig unterschrieben.");

                Assert.That(Hex(nachher.IdentityKey), Is.EqualTo(Hex(vorher.IdentityKey)),
                            "Der IdentityKey hat mitgewechselt - jeder Fingerabdruck-Vergleich wäre wertlos.");

            });

        }

        #endregion

        #region TheAssociatedData_IsInitiatorThenResponder()

        /// <summary>
        /// <c>AD = Encode(IK_A) ‖ Encode(IK_B)</c> - der Anrufende zuerst,
        /// und zwar wörtlich.
        /// </summary>
        /// <remarks>
        /// <b>Auch dieser Test kam durch eine überlebende Mutation dazu</b>,
        /// und es ist zum dritten Mal dasselbe Muster: Die Reihenfolge liess
        /// sich in der Hilfsfunktion umdrehen, ohne dass ein Test etwas sagte -
        /// beide Seiten rufen dieselbe Funktion auf und kommen weiterhin
        /// überein. Ein Vergleich „beide bekommen dasselbe" kann so etwas
        /// grundsätzlich nicht finden.
        ///
        /// Der Schaden träte erst gegenüber einem fremden Client auf: Seine
        /// Beigabe sähe anders aus, jede Nachricht scheiterte an einer
        /// Prüfung, die mit ihrem Inhalt nichts zu tun hat - und die
        /// Fehlersuche begänne bei der Verschlüsselung statt bei diesen 64
        /// Byte.
        ///
        /// Deshalb steht hier nicht „beide gleich", sondern welche Hälfte wem
        /// gehört.
        /// </remarks>
        [Test]
        public void TheAssociatedData_IsInitiatorThenResponder()
        {

            var alice = OmemoIdentity.Create();
            var bob   = OmemoIdentity.Create();

            var beiAlice = X3DH.Initiate(alice, bob.Bundle());

            var beiBob   = X3DH.Accept(bob, alice.PublicIdentityKey, beiAlice.EphemeralKey!,
                                       bob.SignedPreKeyId, beiAlice.UsedPreKeyId);

            Assert.Multiple(() =>
            {

                Assert.That(beiAlice.AssociatedData, Has.Length.EqualTo(64));

                Assert.That(Hex(beiAlice.AssociatedData[..32]),
                            Is.EqualTo(Hex(alice.PublicIdentityKey)),
                            "Die erste Hälfte gehört dem Anrufenden.");

                Assert.That(Hex(beiAlice.AssociatedData[32..]),
                            Is.EqualTo(Hex(bob.PublicIdentityKey)),
                            "Die zweite Hälfte gehört dem Angerufenen.");

                Assert.That(Hex(beiBob.AssociatedData), Is.EqualTo(Hex(beiAlice.AssociatedData)),
                            "Und der Angerufene rechnet dieselbe Beigabe aus.");

            });

        }

        #endregion

        #region TheDerivation_MatchesTheSpecificationLiterally()

        /// <summary>
        /// Die Ableitung, mit einem zweiten HKDF und den Vorschriften aus
        /// Abschnitt 4.2 buchstäblich hingeschrieben.
        /// </summary>
        /// <remarks>
        /// <b>Aus derselben Erfahrung wie in D62.</b> Der 0xFF-Vorspann und
        /// der Info-String lassen sich beide ändern, ohne dass irgendein
        /// anderer Test etwas sagt: Beide Seiten rechnen ja mit derselben
        /// Funktion und kommen weiterhin überein. Der Schaden träte erst
        /// gegenüber einem fremden Client auf - und den gibt es hier nicht.
        ///
        /// Also steht die Vorschrift hier ein zweites Mal und wörtlich: 32
        /// Byte 0xFF davor, 32 Nullbyte als Salz, „OMEMO X3DH" als Info, 32
        /// Byte Ausgabe, HKDF über SHA-256. Wer eines davon im Quelltext
        /// ändert, muss es hier mitändern - und sieht dabei, dass er die
        /// Spezifikation verlässt.
        /// </remarks>
        [Test]
        public void TheDerivation_MatchesTheSpecificationLiterally()
        {

            Byte[] dh1 = [.. Enumerable.Repeat((Byte) 0x01, 32)];
            Byte[] dh2 = [.. Enumerable.Repeat((Byte) 0x02, 32)];
            Byte[] dh3 = [.. Enumerable.Repeat((Byte) 0x03, 32)];
            Byte[] dh4 = [.. Enumerable.Repeat((Byte) 0x04, 32)];

            var hkdf = new Org.BouncyCastle.Crypto.Generators.HkdfBytesGenerator(
                           new Org.BouncyCastle.Crypto.Digests.Sha256Digest());

            hkdf.Init(new Org.BouncyCastle.Crypto.Parameters.HkdfParameters(
                          [.. Enumerable.Repeat((Byte) 0xFF, 32), .. dh1, .. dh2, .. dh3, .. dh4],
                          new Byte[32],
                          System.Text.Encoding.UTF8.GetBytes("OMEMO X3DH")));

            var erwartet = new Byte[32];
            hkdf.GenerateBytes(erwartet, 0, erwartet.Length);

            Assert.That(Hex(X3DH.Derive(dh1, dh2, dh3, dh4)), Is.EqualTo(Hex(erwartet)));

        }

        #endregion

        #region TheOrderOfTheFour_Matters()

        /// <summary>
        /// Die vier Diffie-Hellman-Werte gehen in fester Reihenfolge ein.
        /// </summary>
        /// <remarks>
        /// Der Test rechnet die Ableitung mit vertauschten Werten von Hand
        /// nach und stellt fest, dass etwas anderes herauskommt. Das ist keine
        /// Selbstverständlichkeit, sondern die Aussage: Wer hier vertauscht,
        /// bekommt ein ebenso gutes Geheimnis - nur eben ein anderes als die
        /// Gegenstelle. Der Fehler zeigt sich dann nicht in dieser Rechnung,
        /// sondern erst bei der ersten Nachricht, und sieht dort aus wie eine
        /// Fälschung.
        /// </remarks>
        [Test]
        public void TheOrderOfTheFour_Matters()
        {

            var alice  = OmemoIdentity.Create();
            var bob    = OmemoIdentity.Create();
            var bundle = bob.Bundle();

            var richtig = X3DH.Initiate(alice, bundle);

            // Dieselben vier Werte, andere Reihenfolge - von Hand
            // nachgerechnet, damit die Vertauschung sichtbar ist und nicht in
            // einer Mutation versteckt.
            var ihrIk  = bundle.IdentityKeyForAgreement();
            var ihrSpk = bundle.SignedPreKey;
            var preKey = bundle.PreKeys.First(p => p.Id == richtig.UsedPreKeyId);

            // Der Einwegschlüssel ist geheim geblieben; ohne ihn lässt sich
            // die richtige Rechnung nicht wiederholen. Also eine zweite
            // Sitzung mit bekanntem Einwegschlüssel.
            var ephemeral = Curve25519.GenerateKeyPair();

            var dh1 = Curve25519.Agree(alice.IdentityKey.PrivateKey, ihrSpk);
            var dh2 = Curve25519.Agree(ephemeral.PrivateKey,         ihrIk);
            var dh3 = Curve25519.Agree(ephemeral.PrivateKey,         ihrSpk);
            var dh4 = Curve25519.Agree(ephemeral.PrivateKey,         preKey.PublicKey);

            Byte[] Ableiten(params Byte[][] werte)
                => System.Security.Cryptography.HKDF.DeriveKey(
                       HashAlgorithmName.SHA256,
                       ikm:           [.. Enumerable.Repeat((Byte) 0xFF, 32), .. werte.SelectMany(w => w)],
                       salt:          new Byte[32],
                       info:          System.Text.Encoding.UTF8.GetBytes(X3DH.Info),
                       outputLength:  32);

            Assert.That(Hex(Ableiten(dh2, dh1, dh3, dh4)),
                        Is.Not.EqualTo(Hex(Ableiten(dh1, dh2, dh3, dh4))),
                        "Die Reihenfolge der vier Werte ändert nichts - dann prüft hier niemand mit.");

        }

        #endregion

    }

}
