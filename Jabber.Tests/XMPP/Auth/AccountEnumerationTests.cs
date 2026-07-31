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
using System.Text.RegularExpressions;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP;
using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// RFC 6120, Abschnitt 13.11 („Directory Harvesting"): Der Server soll
    /// nicht verraten, ob es ein Konto gibt — „not reveal whether or not an
    /// account exists at a server when an entity attempts to authenticate".
    /// </summary>
    /// <remarks>
    /// Der Fehlerwert allein reicht dafür nicht. <c>&lt;not-authorized/&gt;</c>
    /// deckt beide Fälle ausdrücklich ab (Abschnitt 6.5.10: „this might
    /// include, but is not limited to, the case in which the user does not
    /// exist"), und genau den schickte der Server auch schon vorher in beiden
    /// Fällen. Verraten hat ihn der <b>Ablauf</b>: Ein bestehendes Konto bekam
    /// auf seine erste Nachricht eine Aufforderung und scheiterte erst an der
    /// zweiten, ein unbekanntes scheiterte sofort. Eine Runde Unterschied, und
    /// jede Namensliste ist in einem Durchgang sortiert.
    ///
    /// Deshalb prüfen diese Tests nicht, <i>dass</i> abgewiesen wird — das tun
    /// die Tests in <see cref="ScramAuthenticationTests"/> —, sondern dass
    /// beide Abweisungen gleich aussehen.
    /// </remarks>
    [TestFixture]
    public class AccountEnumerationTests : AXMPPTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Ein Client, der genau einen Versuch macht - die Frage ist beim
        /// ersten beantwortet, und jeder weitere legte eine zweite Sitzung an.
        /// </summary>
        private XMPPClient Einzelversuch(String localPart, String password = "pw")
        {

            var client = CreateClient(localPart, password: password);
            client.Connection.MaxReconnectAttempts = 0;

            return client;

        }

        /// <summary>Die Namen der Elemente, die der Server geschickt hat.</summary>
        private static IReadOnlyList<String> Elementfolge(XMPPSession session)
            => [.. session.Sent.Select(f => Regex.Match(f, @"^\s*<([\w:.-]+)").Groups[1].Value)];

        /// <summary>
        /// Die server-first-message aus dem <c>&lt;challenge/&gt;</c> einer
        /// Sitzung, im Klartext.
        /// </summary>
        private static String ServerFirst(XMPPSession session)
        {

            var challenge = session.Sent.FirstOrDefault(f => f.StartsWith("<challenge", StringComparison.Ordinal));

            Assert.That(challenge, Is.Not.Null,
                        "Der Server hat gar nicht erst aufgefordert.");

            var nutzlast = Regex.Match(challenge!, @"<challenge[^>]*>([^<]*)</challenge>").Groups[1].Value;

            return Encoding.UTF8.GetString(Convert.FromBase64String(nutzlast));

        }

        /// <summary>Liest ein Attribut der server-first-message.</summary>
        private static String Wert(String nachricht, String name)
            => nachricht.Split(',')
                        .First(teil => teil.StartsWith($"{name}=", StringComparison.Ordinal))
                        [(name.Length + 1)..];

        #endregion


        #region AnUnknownAccount_LooksLikeAWrongPassword()

        /// <summary>
        /// Ein Name ohne Konto und ein Konto mit falschem Passwort ergeben
        /// denselben Ablauf.
        /// </summary>
        /// <remarks>
        /// Verglichen wird die Folge der Elemente, die der Server geschickt
        /// hat, nicht ihr Inhalt: Nonce und Salt sind verschieden und sollen
        /// es sein. Gleich sein muss, <b>wie viele</b> Schritte es waren und
        /// <b>welche</b> - denn daran und nicht am Fehlerwort liess sich die
        /// Frage bisher beantworten.
        /// </remarks>
        [Test]
        public async Task AnUnknownAccount_LooksLikeAWrongPassword()
        {

            Server.AddAccount("alice");

            await FailingConnectAsync(Einzelversuch("alice", "falsch"));
            await FailingConnectAsync(Einzelversuch("niemand"));

            var sitzungen = Server.AllSessions;

            Assert.That(sitzungen, Has.Count.EqualTo(2),
                        "Erwartet werden genau zwei Anläufe, sonst vergleicht der Test das Falsche.");

            var mitKonto  = Elementfolge(sitzungen[0]);
            var ohneKonto = Elementfolge(sitzungen[1]);

            Assert.Multiple(() =>
            {

                Assert.That(ohneKonto, Is.EqualTo(mitKonto),
                            $"Der Ablauf verrät, ob es das Konto gibt: {String.Join(", ", ohneKonto)} " +
                            $"statt {String.Join(", ", mitKonto)}");

                // Ohne das bestünde der Test auch dann, wenn beide Seiten
                // sofort scheiterten - gleich wäre der Ablauf dann auch.
                Assert.That(mitKonto, Does.Contain("challenge"),
                            "Ohne Aufforderung ist der Vergleich ohne Aussage.");

                Assert.That(sitzungen[1].Sent.Any(f => f.Contains("not-authorized", StringComparison.Ordinal)),
                            Is.True,
                            "Am Ende steht die Abweisung, und zwar dieselbe.");

            });

        }

        #endregion

        #region TheSaltOfAnUnknownAccount_StaysTheSame()

        /// <summary>
        /// Zweimal derselbe unbekannte Name, zweimal dasselbe Salt.
        /// </summary>
        /// <remarks>
        /// Der Teil, den ein zufälliges Salt verdorben hätte: Das Salt eines
        /// bestehenden Kontos steht fest. Ein erfundenes, das bei jedem Versuch
        /// anders ausfällt, beantwortet die Frage genauso zuverlässig wie ein
        /// sofortiger Fehlschlag - man muss nur zweimal fragen.
        /// </remarks>
        [Test]
        public async Task TheSaltOfAnUnknownAccount_StaysTheSame()
        {

            await FailingConnectAsync(Einzelversuch("niemand"));
            await FailingConnectAsync(Einzelversuch("niemand"));

            var sitzungen = Server.AllSessions;

            Assert.That(sitzungen, Has.Count.EqualTo(2));

            var erste  = ServerFirst(sitzungen[0]);
            var zweite = ServerFirst(sitzungen[1]);

            Assert.That(Wert(zweite, "s"), Is.EqualTo(Wert(erste, "s")),
                        "Ein wechselndes Salt ist selbst die Auskunft.");

        }

        #endregion

        #region TwoUnknownAccounts_GetDifferentSalts()

        /// <summary>
        /// Zwei unbekannte Namen bekommen verschiedene Salts.
        /// </summary>
        /// <remarks>
        /// Die Gegenprobe zum vorigen Test, und ohne sie wäre ein festes,
        /// eingebautes Salt eine bestandene Lösung. Es wäre die schlechteste
        /// von allen: Zwei Namen mit demselben Salt gibt es unter echten Konten
        /// nicht, ein Treffer wäre also sofort als erfunden erkannt.
        /// </remarks>
        [Test]
        public async Task TwoUnknownAccounts_GetDifferentSalts()
        {

            await FailingConnectAsync(Einzelversuch("niemand"));
            await FailingConnectAsync(Einzelversuch("auchnicht"));

            var sitzungen = Server.AllSessions;

            Assert.That(sitzungen, Has.Count.EqualTo(2));

            Assert.That(Wert(ServerFirst(sitzungen[1]), "s"),
                        Is.Not.EqualTo(Wert(ServerFirst(sitzungen[0]), "s")),
                        "Ein für alle gleiches Salt verrät genauso viel.");

        }

        #endregion

        #region TheInventedSalt_LooksLikeARealOne()

        /// <summary>
        /// Länge des Salts und Iterationszahl sind dieselben wie bei einem
        /// bestehenden Konto.
        /// </summary>
        /// <remarks>
        /// Was am erfundenen Salt anders wäre, wäre wieder ein
        /// Erkennungszeichen - die Iterationszahl steht offen in der
        /// server-first-message, und die Salt-Länge ist abzuzählen.
        /// </remarks>
        [Test]
        public async Task TheInventedSalt_LooksLikeARealOne()
        {

            Server.AddAccount("alice");

            await FailingConnectAsync(Einzelversuch("alice", "falsch"));
            await FailingConnectAsync(Einzelversuch("niemand"));

            var sitzungen = Server.AllSessions;

            Assert.That(sitzungen, Has.Count.EqualTo(2));

            var echt      = ServerFirst(sitzungen[0]);
            var erfunden  = ServerFirst(sitzungen[1]);

            Assert.Multiple(() =>
            {

                Assert.That(Wert(erfunden, "i"), Is.EqualTo(Wert(echt, "i")),
                            "Die Iterationszahl unterscheidet die beiden.");

                Assert.That(Convert.FromBase64String(Wert(erfunden, "s")).Length,
                            Is.EqualTo(Convert.FromBase64String(Wert(echt, "s")).Length),
                            "Die Länge des Salts unterscheidet die beiden.");

            });

        }

        #endregion

    }

}
