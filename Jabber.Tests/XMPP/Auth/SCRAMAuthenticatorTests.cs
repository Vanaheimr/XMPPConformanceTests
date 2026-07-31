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

using System.Text;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// SCRAM gegen die offiziellen Testvektoren:
    /// RFC 5802 Abschnitt 5 für SCRAM-SHA-1 und
    /// RFC 7677 Abschnitt 3 für SCRAM-SHA-256.
    ///
    /// Beide Vektoren nutzen Benutzer "user" und Passwort "pencil". Der
    /// Client-Nonce wird über <c>FixedClientNonce</c> festgenagelt, sonst
    /// liessen sich AuthMessage und Proof nicht reproduzieren.
    /// </summary>
    [TestFixture]
    public class SCRAMAuthenticatorTests
    {

        #region Data

        // ----- RFC 5802, Abschnitt 5 (SCRAM-SHA-1) -----
        private const String Sha1_ClientNonce      = "fyko+d2lbbFgONRv9qkxdawL";
        private const String Sha1_ClientFirst      = "n,,n=user,r=fyko+d2lbbFgONRv9qkxdawL";
        private const String Sha1_ServerFirst      = "r=fyko+d2lbbFgONRv9qkxdawL3rfcNHYJY1ZVvWVs7j," +
                                                     "s=QSXCR+Q6sek8bf92,i=4096";
        private const String Sha1_ClientFinal      = "c=biws,r=fyko+d2lbbFgONRv9qkxdawL3rfcNHYJY1ZVvWVs7j," +
                                                     "p=v0X8v3Bz2T0CJGbJQyF0X+HI4Ts=";
        private const String Sha1_ServerFinal      = "v=rmF9pqV8S7suAoZWja4dJRkFsKQ=";

        // ----- RFC 7677, Abschnitt 3 (SCRAM-SHA-256) -----
        private const String Sha256_ClientNonce    = "rOprNGfwEbeRWgbNEkqO";
        private const String Sha256_ClientFirst    = "n,,n=user,r=rOprNGfwEbeRWgbNEkqO";
        private const String Sha256_ServerFirst    = "r=rOprNGfwEbeRWgbNEkqO%hvYDpWUa2RaTCAfuxFIlj)hNlF$k0," +
                                                     "s=W22ZaJ0SNY7soEsUEjb6gQ==,i=4096";
        private const String Sha256_ClientFinal    = "c=biws,r=rOprNGfwEbeRWgbNEkqO%hvYDpWUa2RaTCAfuxFIlj)hNlF$k0," +
                                                     "p=dHzbZapWIk4jUhN+Ute9ytag9zjfMHgsqmmiz7AndVQ=";
        private const String Sha256_ServerFinal    = "v=6rriTRBi23WpRR/wtup+mMhUZUn/dB5nLTJRsjl95G4=";

        #endregion

        #region Hilfsfunktionen

        private static SCRAMAuthenticator Authenticator(SCRAMMechanism mechanism, String clientNonce)
            => new("user", "pencil", mechanism) { FixedClientNonce = clientNonce };

        private static String B64(String s)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

        private static String FromB64(String s)
            => Encoding.UTF8.GetString(Convert.FromBase64String(s));

        #endregion


        #region Rfc5802_Sha1_ClientFirstMessage_MatchesTestVector()

        /// <summary>
        /// Die client-first-message muss dem Beispiel aus RFC 5802 entsprechen.
        /// </summary>
        [Test]
        public void Rfc5802_Sha1_ClientFirstMessage_MatchesTestVector()
        {

            var scram = Authenticator(SCRAMMechanism.ScramSha1, Sha1_ClientNonce);

            Assert.That(FromB64(scram.CreateClientFirstMessage()),
                        Is.EqualTo(Sha1_ClientFirst));

        }

        #endregion

        #region Rfc5802_Sha1_ClientFinalMessage_MatchesTestVector()

        /// <summary>
        /// Der ClientProof muss exakt dem Wert aus RFC 5802 entsprechen. Damit
        /// sind Hi/PBKDF2, ClientKey, StoredKey, AuthMessage, ClientSignature
        /// und die XOR-Verknüpfung gemeinsam abgedeckt.
        /// </summary>
        [Test]
        public void Rfc5802_Sha1_ClientFinalMessage_MatchesTestVector()
        {

            var scram = Authenticator(SCRAMMechanism.ScramSha1, Sha1_ClientNonce);
            scram.CreateClientFirstMessage();

            var clientFinal = scram.ProcessServerFirstMessage(B64(Sha1_ServerFirst));

            Assert.That(FromB64(clientFinal), Is.EqualTo(Sha1_ClientFinal));

        }

        #endregion

        #region Rfc5802_Sha1_ServerSignature_IsAccepted()

        /// <summary>
        /// Die Server-Signatur aus RFC 5802 muss akzeptiert werden.
        /// </summary>
        [Test]
        public void Rfc5802_Sha1_ServerSignature_IsAccepted()
        {

            var scram = Authenticator(SCRAMMechanism.ScramSha1, Sha1_ClientNonce);
            scram.CreateClientFirstMessage();
            scram.ProcessServerFirstMessage(B64(Sha1_ServerFirst));

            Assert.That(scram.VerifyServerFinalMessage(B64(Sha1_ServerFinal)), Is.True);

        }

        #endregion

        #region Rfc7677_Sha256_ClientFirstMessage_MatchesTestVector()

        /// <summary>
        /// Die client-first-message muss dem Beispiel aus RFC 7677 entsprechen.
        /// </summary>
        [Test]
        public void Rfc7677_Sha256_ClientFirstMessage_MatchesTestVector()
        {

            var scram = Authenticator(SCRAMMechanism.ScramSha256, Sha256_ClientNonce);

            Assert.That(FromB64(scram.CreateClientFirstMessage()),
                        Is.EqualTo(Sha256_ClientFirst));

        }

        #endregion

        #region Rfc7677_Sha256_ClientFinalMessage_MatchesTestVector()

        /// <summary>
        /// Der ClientProof muss exakt dem Wert aus RFC 7677 entsprechen.
        /// </summary>
        [Test]
        public void Rfc7677_Sha256_ClientFinalMessage_MatchesTestVector()
        {

            var scram = Authenticator(SCRAMMechanism.ScramSha256, Sha256_ClientNonce);
            scram.CreateClientFirstMessage();

            var clientFinal = scram.ProcessServerFirstMessage(B64(Sha256_ServerFirst));

            Assert.That(FromB64(clientFinal), Is.EqualTo(Sha256_ClientFinal));

        }

        #endregion

        #region Rfc7677_Sha256_ServerSignature_IsAccepted()

        /// <summary>
        /// Die Server-Signatur aus RFC 7677 muss akzeptiert werden.
        /// </summary>
        [Test]
        public void Rfc7677_Sha256_ServerSignature_IsAccepted()
        {

            var scram = Authenticator(SCRAMMechanism.ScramSha256, Sha256_ClientNonce);
            scram.CreateClientFirstMessage();
            scram.ProcessServerFirstMessage(B64(Sha256_ServerFirst));

            Assert.That(scram.VerifyServerFinalMessage(B64(Sha256_ServerFinal)), Is.True);

        }

        #endregion

        #region TamperedServerSignature_IsRejected()

        /// <summary>
        /// Eine verfälschte Server-Signatur muss abgelehnt werden - sonst wäre
        /// die gegenseitige Authentifizierung wertlos.
        /// </summary>
        [Test]
        public void TamperedServerSignature_IsRejected()
        {

            var scram = Authenticator(SCRAMMechanism.ScramSha1, Sha1_ClientNonce);
            scram.CreateClientFirstMessage();
            scram.ProcessServerFirstMessage(B64(Sha1_ServerFirst));

            // Ein Bit in der Signatur kippen
            var signature     = Convert.FromBase64String("rmF9pqV8S7suAoZWja4dJRkFsKQ=");
            signature[0]     ^= 0x01;
            var tampered      = $"v={Convert.ToBase64String(signature)}";

            Assert.That(scram.VerifyServerFinalMessage(B64(tampered)), Is.False);

        }

        #endregion

        #region ServerNonceWithoutClientNonce_IsRejected()

        /// <summary>
        /// Enthält die kombinierte Nonce nicht den Client-Nonce als Präfix,
        /// liegt ein möglicher MITM vor (RFC 5802, Abschnitt 5.1).
        /// </summary>
        [Test]
        public void ServerNonceWithoutClientNonce_IsRejected()
        {

            var scram = Authenticator(SCRAMMechanism.ScramSha1, Sha1_ClientNonce);
            scram.CreateClientFirstMessage();

            var evil = "r=AAAAAAAAAAAAAAAAAAAAAAAA3rfcNHYJY1ZVvWVs7j,s=QSXCR+Q6sek8bf92,i=4096";

            Assert.That(() => scram.ProcessServerFirstMessage(B64(evil)),
                        Throws.TypeOf<AuthenticationException>());

        }

        #endregion

        #region ServerFinalWithError_ThrowsAuthenticationException()

        /// <summary>
        /// Eine server-final-message mit e= ist ein Fehler und keine Signatur.
        /// </summary>
        [Test]
        public void ServerFinalWithError_ThrowsAuthenticationException()
        {

            var scram = Authenticator(SCRAMMechanism.ScramSha1, Sha1_ClientNonce);
            scram.CreateClientFirstMessage();
            scram.ProcessServerFirstMessage(B64(Sha1_ServerFirst));

            Assert.That(() => scram.VerifyServerFinalMessage(B64("e=invalid-proof")),
                        Throws.TypeOf<AuthenticationException>());

        }

        #endregion

        #region MechanismNames_MatchIanaRegistry()

        /// <summary>
        /// Die Mechanismus-Namen müssen exakt den IANA-registrierten
        /// Bezeichnungen entsprechen, sonst lehnt der Server die Auswahl ab.
        /// </summary>
        [Test]
        public void MechanismNames_MatchIanaRegistry()
        {

            Assert.Multiple(() =>
            {
                Assert.That(new SCRAMAuthenticator("u", "p", SCRAMMechanism.ScramSha1).MechanismName,
                            Is.EqualTo("SCRAM-SHA-1"));

                Assert.That(new SCRAMAuthenticator("u", "p", SCRAMMechanism.ScramSha256).MechanismName,
                            Is.EqualTo("SCRAM-SHA-256"));
            });

        }

        #endregion

        #region IterationCountFollowingNonceWithPadding_IsParsedCorrectly()

        /// <summary>
        /// REGRESSIONSTEST - ExtractValue muss seine Suche am Anfang oder hinter
        /// einem Komma verankern.
        ///
        /// Mit dem früheren, unverankerten Muster <c>{key}=([^,]+)</c> traf die
        /// Suche nach dem Iterationszähler ein 'i=' innerhalb der kombinierten
        /// Nonce und lieferte "=", woraufhin Int32.Parse eine FormatException
        /// statt einer sauberen AuthenticationException warf.
        /// </summary>
        [Test]
        public void IterationCountFollowingNonceWithPadding_IsParsedCorrectly()
        {

            var scram = Authenticator(SCRAMMechanism.ScramSha1, "cnonce");
            scram.CreateClientFirstMessage();

            // Kombinierte Nonce endet auf "i==" - gueltig nach RFC 5802,
            // denn erlaubt ist jedes druckbare Zeichen ausser dem Komma.
            var serverFirst = "r=cnonceZZi==,s=QSXCR+Q6sek8bf92,i=4096";

            Assert.That(() => scram.ProcessServerFirstMessage(B64(serverFirst)),
                        Throws.Nothing);

        }

        #endregion

    }

}
