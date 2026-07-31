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
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Die Prüfungen, die der Server im SCRAM-Austausch vornimmt - direkt
    /// gegen <c>SCRAMExchange</c> statt über eine Verbindung.
    ///
    /// Das ist nötig, weil ein echter Client sie nicht auslöst: er schickt
    /// immer die richtige Nonce und den richtigen GS2-Header, und ein falscher
    /// Beweis fällt ihm selbst schon an der Serversignatur auf. Über eine
    /// Verbindung geprüft, bestünden diese Fälle deshalb aus dem falschen
    /// Grund - genau das ist mir bei der ersten Fassung passiert: der Server
    /// nahm jeden Beweis an und die Integrationstests blieben trotzdem grün.
    ///
    /// Die client-final-message wird hier aus den Formeln von RFC 5802,
    /// Abschnitt 3 gebaut, unabhängig von beiden Implementierungen.
    /// </summary>
    [TestFixture]
    public class ScramExchangeTests
    {

        #region Data

        private const String Passwort = "geheim";

        private XMPPAccount _account = null!;

        #endregion

        #region SetUp

        [SetUp]
        public void KontoAnlegen()
        {
            _account = new XMPPAccount("alice@localhost", Passwort);
        }

        #endregion

        #region Hilfsfunktionen

        /// <summary>
        /// Der Serverschlüssel für die erfundenen Zugangsdaten. Fest, damit
        /// der Test nachrechnen kann, was der Server ableiten würde.
        /// </summary>
        private static readonly Byte[] Serverschluessel =
            Encoding.UTF8.GetBytes("Serverschluessel für die Testsammlung");

        /// <summary>
        /// Erfundene Zugangsdaten für einen Namen ohne Konto - dasselbe, was
        /// der Server einsetzt (RFC 6120, Abschnitt 13.11).
        /// </summary>
        private static XMPPCredentials Erfunden(String user)
            => XMPPCredentials.Decoy(user, Serverschluessel);

        /// <summary>Beginnt einen Austausch mit einer festen Client-Nonce.</summary>
        private SCRAMExchange Beginn(String clientNonce = "clientnonce")
        {

            var clientFirst = $"n,,n=alice,r={clientNonce}";

            var exchange = SCRAMExchange.Begin(
                               Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFirst)),
                               SCRAMMechanism.ScramSha256,
                               user => user == "alice" ? _account : null,
                               Erfunden);

            Assert.That(exchange, Is.Not.Null, "Der Austausch hätte beginnen müssen.");

            return exchange!;

        }

        /// <summary>Die server-first-message im Klartext.</summary>
        private static String ServerFirst(SCRAMExchange exchange)
            => Encoding.UTF8.GetString(Convert.FromBase64String(exchange.Challenge));

        /// <summary>
        /// Baut eine client-final-message nach RFC 5802, Abschnitt 3 - mit
        /// allen Stellschrauben, an denen die Tests drehen wollen.
        /// </summary>
        private static String ClientFinal(String   clientFirstBare,
                                          String   serverFirst,
                                          String   passwort,
                                          String?  nonce        = null,
                                          String?  gs2Header    = null)
        {

            var salt        = Convert.FromBase64String(Wert(serverFirst, "s"));
            var iterations  = Int32.Parse(Wert(serverFirst, "i"));

            nonce      ??= Wert(serverFirst, "r");
            gs2Header  ??= "n,,";

            var saltedPassword = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passwort),
                                                           salt,
                                                           iterations,
                                                           HashAlgorithmName.SHA256,
                                                           32);

            var clientKey  = HMACSHA256.HashData(saltedPassword, "Client Key"u8.ToArray());
            var storedKey  = SHA256.HashData(clientKey);

            var ohneBeweis = $"c={Convert.ToBase64String(Encoding.UTF8.GetBytes(gs2Header))},r={nonce}";

            var authMessage      = $"{clientFirstBare},{serverFirst},{ohneBeweis}";
            var clientSignature  = HMACSHA256.HashData(storedKey, Encoding.UTF8.GetBytes(authMessage));

            var beweis = new Byte[clientKey.Length];
            for (var i = 0; i < beweis.Length; i++)
                beweis[i] = (Byte) (clientKey[i] ^ clientSignature[i]);

            return Convert.ToBase64String(
                       Encoding.UTF8.GetBytes($"{ohneBeweis},p={Convert.ToBase64String(beweis)}"));

        }

        /// <summary>Liest ein Attribut, verankert am Anfang oder hinter einem Komma.</summary>
        private static String Wert(String nachricht, String name)
            => nachricht.Split(',')
                        .First(teil => teil.StartsWith($"{name}=", StringComparison.Ordinal))
                        [(name.Length + 1)..];

        #endregion


        #region CorrectProof_IsAccepted()

        /// <summary>
        /// Der richtige Beweis wird angenommen, und die Antwort ist die
        /// server-final-message mit der Serversignatur.
        /// </summary>
        [Test]
        public void CorrectProof_IsAccepted()
        {

            var exchange     = Beginn();
            var serverFirst  = ServerFirst(exchange);

            var ergebnis = exchange.Complete(
                               ClientFinal("n=alice,r=clientnonce", serverFirst, Passwort));

            Assert.That(ergebnis, Is.Not.Null, "Der richtige Beweis muss durchgehen.");

            var serverFinal = Encoding.UTF8.GetString(Convert.FromBase64String(ergebnis!));

            Assert.That(serverFinal, Does.StartWith("v="));

        }

        #endregion

        #region WrongPassword_IsRejectedByTheServer()

        /// <summary>
        /// Der Fall, den die Integrationstests <b>nicht</b> abdecken: der
        /// Server selbst muss einen falschen Beweis zurückweisen.
        /// </summary>
        /// <remarks>
        /// Über eine echte Verbindung scheitert eine Anmeldung mit falschem
        /// Passwort ohnehin, weil der Client die Serversignatur nicht
        /// bestätigt bekommt. Nimmt der Server jeden Beweis an, bleibt das
        /// unbemerkt - hier nicht.
        /// </remarks>
        [Test]
        public void WrongPassword_IsRejectedByTheServer()
        {

            var exchange     = Beginn();
            var serverFirst  = ServerFirst(exchange);

            var ergebnis = exchange.Complete(
                               ClientFinal("n=alice,r=clientnonce", serverFirst, "falsch"));

            Assert.That(ergebnis, Is.Null, "Ein falscher Beweis darf nicht angenommen werden.");

        }

        #endregion

        #region ForeignNonce_IsRejected()

        /// <summary>
        /// Die Nonce des Servers muss zurückgespiegelt werden. Ohne diese
        /// Prüfung liesse sich eine mitgeschnittene client-final-message
        /// wiedereinspielen.
        /// </summary>
        [Test]
        public void ForeignNonce_IsRejected()
        {

            var exchange     = Beginn();
            var serverFirst  = ServerFirst(exchange);

            // Ein vollständig gültiger Beweis - nur zu einer anderen Nonce.
            var ergebnis = exchange.Complete(
                               ClientFinal("n=alice,r=clientnonce",
                                           serverFirst,
                                           Passwort,
                                           nonce: "eine-voellig-andere-nonce"));

            Assert.That(ergebnis, Is.Null, "Eine fremde Nonce darf nicht durchgehen.");

        }

        #endregion

        #region ChangedGs2Header_IsRejected()

        /// <summary>
        /// Der gemeldete GS2-Header muss der geschickte sein (RFC 5802,
        /// Abschnitt 6).
        /// </summary>
        /// <remarks>
        /// Sonst könnte ein Zwischenmann dem Client vorspiegeln, der Server
        /// beherrsche kein Channel Binding, und die Verbindung so auf die
        /// schwächere Variante herunterhandeln, ohne dass es jemandem
        /// auffiele.
        /// </remarks>
        [Test]
        public void ChangedGs2Header_IsRejected()
        {

            var exchange     = Beginn();
            var serverFirst  = ServerFirst(exchange);

            var ergebnis = exchange.Complete(
                               ClientFinal("n=alice,r=clientnonce",
                                           serverFirst,
                                           Passwort,
                                           gs2Header: "y,,"));

            Assert.That(ergebnis, Is.Null, "Ein abweichender GS2-Header darf nicht durchgehen.");

        }

        #endregion

        #region UnknownUser_DoesNotStart()

        /// <summary>
        /// Ein unbekanntes Konto lässt den Austausch trotzdem beginnen - mit
        /// erfundenen, aber gleichbleibenden Zugangsdaten.
        /// </summary>
        /// <remarks>
        /// Hier stand das Gegenteil, samt Begründung: „RFC 5802, Abschnitt 7
        /// empfiehlt stattdessen, mit einem erfundenen Salt weiterzumachen …
        /// bewusst nicht gemacht". Beide Hälften waren falsch. Abschnitt 7 des
        /// RFC 5802 ist die formale Syntax, und der ganze RFC empfiehlt dazu
        /// nichts; er führt im Gegenteil ein <c>unknown-user</c> als
        /// Fehlerwert. Die Empfehlung steht in <b>RFC 6120, Abschnitt
        /// 13.11</b> („Directory Harvesting"): „not reveal whether or not an
        /// account exists at a server when an entity attempts to
        /// authenticate".
        ///
        /// Ein sofortiger Fehlschlag verriet das unabhängig vom Fehlerwort -
        /// die Auskunft steckte im Ablauf: eine Runde statt zweien.
        /// </remarks>
        [Test]
        public void UnknownUser_StartsAnyway()
        {

            SCRAMExchange? Versuch(String benutzer)
                => SCRAMExchange.Begin(
                       Convert.ToBase64String(Encoding.UTF8.GetBytes($"n,,n={benutzer},r=clientnonce")),
                       SCRAMMechanism.ScramSha256,
                       user => user == "alice" ? _account : null,
                       Erfunden);

            var ersterVersuch   = Versuch("niemand");
            var zweiterVersuch  = Versuch("niemand");
            var andererName     = Versuch("auchnicht");

            Assert.That(ersterVersuch,  Is.Not.Null, "Der Austausch hätte beginnen müssen.");
            Assert.That(zweiterVersuch, Is.Not.Null);
            Assert.That(andererName,    Is.Not.Null);

            var erste   = ServerFirst(ersterVersuch!);
            var zweite  = ServerFirst(zweiterVersuch!);
            var andere  = ServerFirst(andererName!);

            Assert.Multiple(() =>
            {

                Assert.That(ersterVersuch!.Account, Is.Null,
                            "Ein Konto gibt es nicht - der Austausch läuft nur zum Schein.");

                Assert.That(Wert(zweite, "s"), Is.EqualTo(Wert(erste, "s")),
                            "Ein Salt, das sich bei jedem Versuch ändert, ist selbst die Auskunft.");

                Assert.That(Wert(andere, "s"), Is.Not.EqualTo(Wert(erste, "s")),
                            "Ein für alle gleiches Salt ebenso.");

                Assert.That(Wert(erste, "i"),
                            Is.EqualTo(_account.Credentials.IterationCount.ToString()),
                            "Eine abweichende Iterationszahl wäre wieder ein Erkennungszeichen.");

                Assert.That(Convert.FromBase64String(Wert(erste, "s")).Length,
                            Is.EqualTo(_account.Credentials.Salt.Length),
                            "Und eine abweichende Salt-Länge auch.");

            });

        }

        #endregion

        #region AValidProof_IsNotEnoughWithoutAnAccount()

        /// <summary>
        /// Selbst ein stimmiger Beweis meldet niemanden an, wenn hinter dem
        /// Namen kein Konto steht.
        /// </summary>
        /// <remarks>
        /// Der Fall ist über die Leitung nicht herstellbar: Die erfundenen
        /// Schlüssel stammen aus dem Serverschlüssel, und wer den nicht kennt,
        /// bringt keinen passenden Beweis zustande. Hier bekommt der Austausch
        /// deshalb die <b>echten</b> Zugangsdaten als erfundene untergeschoben
        /// - der Beweis stimmt dann, und der Austausch muss ihn trotzdem
        /// abweisen.
        ///
        /// Ohne diesen Test wäre die Sicherung in <c>Complete</c> eine
        /// Behauptung: Sie fällt in keinem anderen Test auf, und ihr Preis
        /// wäre eine Anmeldung ohne Konto.
        /// </remarks>
        [Test]
        public void AValidProof_IsNotEnoughWithoutAnAccount()
        {

            const String clientFirstBare = "n=niemand,r=clientnonce";

            var exchange = SCRAMExchange.Begin(
                               Convert.ToBase64String(Encoding.UTF8.GetBytes($"n,,{clientFirstBare}")),
                               SCRAMMechanism.ScramSha256,
                               _ => null,
                               _ => _account.Credentials);

            Assert.That(exchange, Is.Not.Null);

            var serverFirst  = ServerFirst(exchange!);
            var clientFinal  = ClientFinal(clientFirstBare, serverFirst, Passwort);

            Assert.Multiple(() =>
            {

                Assert.That(exchange!.Complete(clientFinal), Is.Null,
                            "Ein Beweis ohne Konto dahinter darf nicht durchkommen.");

                Assert.That(exchange.Account, Is.Null);

            });

        }

        #endregion

        #region EscapedUsername_IsUnescaped()

        /// <summary>
        /// RFC 5802: im Benutzernamen steht <c>=2C</c> für ein Komma und
        /// <c>=3D</c> für ein Gleichheitszeichen.
        /// </summary>
        /// <remarks>
        /// Die Reihenfolge beim Auflösen ist nicht beliebig. Wer zuerst
        /// <c>=3D</c> ersetzt, macht aus dem übertragenen <c>=3D2C</c> - also
        /// dem Text "=2C" - erst "=2C" und dann fälschlich ein Komma.
        /// </remarks>
        [Test]
        public void EscapedUsername_IsUnescaped()
        {

            var konto     = new XMPPAccount("a,b=c@localhost", Passwort);
            var gesucht   = new List<String>();

            var exchange = SCRAMExchange.Begin(
                               Convert.ToBase64String(Encoding.UTF8.GetBytes("n,,n=a=2Cb=3Dc,r=nonce")),
                               SCRAMMechanism.ScramSha256,
                               user => { gesucht.Add(user); return konto; },
                               Erfunden);

            Assert.Multiple(() =>
            {
                Assert.That(exchange, Is.Not.Null);
                Assert.That(gesucht,  Is.EqualTo(new[] { "a,b=c" }));
            });

        }

        #endregion

        #region MalformedMessages_AreRejected()

        /// <summary>
        /// Unsinn darf nicht in eine Ausnahme laufen, sondern in eine
        /// Ablehnung.
        /// </summary>
        [Test]
        public void MalformedMessages_AreRejected()
        {

            Assert.Multiple(() =>
            {

                Assert.That(SCRAMExchange.Begin("kein-base64!!", SCRAMMechanism.ScramSha256, _ => _account, Erfunden),
                            Is.Null, "Kein Base64.");

                Assert.That(SCRAMExchange.Begin(Base64("n,,"), SCRAMMechanism.ScramSha256, _ => _account, Erfunden),
                            Is.Null, "Kein Benutzername und keine Nonce.");

                Assert.That(SCRAMExchange.Begin(Base64("n=alice,r=x"), SCRAMMechanism.ScramSha256, _ => _account, Erfunden),
                            Is.Null, "Kein GS2-Header.");

                Assert.That(SCRAMExchange.Begin(Base64("n,,r=x"), SCRAMMechanism.ScramSha256, _ => _account, Erfunden),
                            Is.Null, "Kein Benutzername.");

            });

            var exchange = Beginn();

            Assert.Multiple(() =>
            {
                Assert.That(exchange.Complete("kein-base64!!"), Is.Null, "Kein Base64.");
                Assert.That(exchange.Complete(Base64("c=biws,r=nonce")), Is.Null, "Kein Beweis.");
                Assert.That(exchange.Complete(Base64("c=biws,p=zu-kurz")), Is.Null, "Keine Nonce.");
            });

        }

        private static String Base64(String text)
            => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

        #endregion

    }

}
