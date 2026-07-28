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
    /// XEP-0115 Entity Capabilities gegen den Testvektor aus Abschnitt 5.2
    /// ("Simple Generation Example").
    ///
    /// Stimmt der Verification String nicht, verwerfen Gegenstellen, die den
    /// Hash nachrechnen, die eigenen Capabilities - der Fehler bleibt im
    /// Betrieb aber unsichtbar, weil viele Clients nicht prüfen.
    /// </summary>
    [TestFixture]
    public class EntityCapsManagerTests
    {

        #region Data

        // XEP-0115, Abschnitt 5.2
        private const String Xep0115_SimpleVer =
            "QgayPKawpkPSDYmwT/WM94uAlu0=";

        private const String Xep0115_SimpleS =
            "client/pc//Exodus 0.9.1<" +
            "http://jabber.org/protocol/caps<" +
            "http://jabber.org/protocol/disco#info<" +
            "http://jabber.org/protocol/disco#items<" +
            "http://jabber.org/protocol/muc<";

        #endregion

        #region Hilfsfunktionen

        /// <summary>
        /// Ein DiscoManager mit genau den angegebenen Identitäten und Features.
        /// </summary>
        private static DiscoManager Disco(DiscoIdentity identity, params String[] features)
        {

            var disco = new DiscoManager(_ => Task.CompletedTask);

            disco.LocalIdentities.Clear();
            disco.LocalIdentities.Add(identity);

            disco.LocalFeatures.Clear();
            disco.LocalFeatures.AddRange(features);

            return disco;

        }

        /// <summary>
        /// Ein DiscoManager mit den angegebenen Identitäten und ohne Features.
        /// </summary>
        private static DiscoManager DiscoWithIdentities(params DiscoIdentity[] identities)
        {

            var disco = new DiscoManager(_ => Task.CompletedTask);

            disco.LocalIdentities.Clear();
            disco.LocalIdentities.AddRange(identities);

            disco.LocalFeatures.Clear();

            return disco;

        }

        private static String Sha1Base64(String s)
            => Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(s)));

        #endregion


        #region Xep0115_SimpleGenerationExample_ProducesExpectedVer()

        /// <summary>
        /// Der Verification String aus XEP-0115 Abschnitt 5.2 muss exakt
        /// reproduziert werden.
        /// </summary>
        [Test]
        public void Xep0115_SimpleGenerationExample_ProducesExpectedVer()
        {

            var disco = Disco(new DiscoIdentity("client", "pc", "Exodus 0.9.1"),
                              "http://jabber.org/protocol/caps",
                              "http://jabber.org/protocol/disco#info",
                              "http://jabber.org/protocol/disco#items",
                              "http://jabber.org/protocol/muc");

            var caps = new EntityCapsManager(disco);

            Assert.That(caps.CalculateVerificationString(), Is.EqualTo(Xep0115_SimpleVer));

        }

        #endregion

        #region Xep0115_TestVector_MatchesIndependentlyComputedHash()

        /// <summary>
        /// Gegenprobe: Der veröffentlichte ver-Wert ist tatsächlich der
        /// SHA-1-Hash des im XEP abgedruckten S-Strings. Damit ist belegt,
        /// dass der Testvektor selbst stimmt.
        /// </summary>
        [Test]
        public void Xep0115_TestVector_MatchesIndependentlyComputedHash()
        {
            Assert.That(Sha1Base64(Xep0115_SimpleS), Is.EqualTo(Xep0115_SimpleVer));
        }

        #endregion

        #region VerificationString_WorksOnForeignDataToo()

        /// <summary>
        /// Dieselbe Rechnung über fremde Angaben - der Testvektor aus
        /// Abschnitt 5.2, diesmal nicht aus den eigenen Listen.
        /// </summary>
        /// <remarks>
        /// Bis dahin liess sich der Hash nur über die eigenen Features bilden.
        /// Damit war er ein Wert, den dieser Client zwar erzeugt, aber nie
        /// nachprüft — und genau das Nachprüfen ist der Zweck des Verfahrens.
        /// </remarks>
        [Test]
        public void VerificationString_WorksOnForeignDataToo()
        {

            var ver = EntityCapsManager.VerificationString(
                          [new DiscoIdentity("client", "pc", "Exodus 0.9.1")],
                          ["http://jabber.org/protocol/caps",
                           "http://jabber.org/protocol/disco#info",
                           "http://jabber.org/protocol/disco#items",
                           "http://jabber.org/protocol/muc"]);

            Assert.That(ver, Is.EqualTo(Xep0115_SimpleVer));

        }

        #endregion

        #region VerificationString_IsIndependentOfInsertionOrder()

        /// <summary>
        /// Die Reihenfolge, in der Features registriert werden, darf den Hash
        /// nicht beeinflussen - sonst berechnen zwei Instanzen desselben
        /// Clients unterschiedliche Werte.
        /// </summary>
        [Test]
        public void VerificationString_IsIndependentOfInsertionOrder()
        {

            var identity = new DiscoIdentity("client", "pc", "Exodus 0.9.1");

            var forward = new EntityCapsManager(Disco(identity,
                              "http://jabber.org/protocol/caps",
                              "http://jabber.org/protocol/disco#info",
                              "http://jabber.org/protocol/disco#items",
                              "http://jabber.org/protocol/muc"));

            var reverse = new EntityCapsManager(Disco(identity,
                              "http://jabber.org/protocol/muc",
                              "http://jabber.org/protocol/disco#items",
                              "http://jabber.org/protocol/disco#info",
                              "http://jabber.org/protocol/caps"));

            Assert.That(reverse.CalculateVerificationString(),
                        Is.EqualTo(forward.CalculateVerificationString()));

        }

        #endregion

        #region CapsElement_CarriesSha1HashAndVer()

        /// <summary>
        /// Das c-Element für die Presence muss Namespace, hash='sha-1', node
        /// und den berechneten ver-Wert tragen.
        /// </summary>
        [Test]
        public void CapsElement_CarriesSha1HashAndVer()
        {

            var caps = new EntityCapsManager(
                           Disco(new DiscoIdentity("client", "pc", "Exodus 0.9.1"),
                                 "http://jabber.org/protocol/caps",
                                 "http://jabber.org/protocol/disco#info",
                                 "http://jabber.org/protocol/disco#items",
                                 "http://jabber.org/protocol/muc"))
                       {
                           Node = "https://example.org/client"
                       };

            var element = caps.GetCapsElement();

            Assert.Multiple(() =>
            {
                Assert.That(element, Does.Contain("xmlns='http://jabber.org/protocol/caps'"));
                Assert.That(element, Does.Contain("hash='sha-1'"));
                Assert.That(element, Does.Contain("node='https://example.org/client'"));
                Assert.That(element, Does.Contain($"ver='{Xep0115_SimpleVer}'"));
            });

        }

        #endregion

        #region Features_AreSortedByOctetOrder()

        /// <summary>
        /// REGRESSIONSTEST - XEP-0115 Abschnitt 5.1 verlangt eine Sortierung in
        /// Oktett-Reihenfolge.
        ///
        /// Früher nutzte CalculateVerificationString <c>Order()</c>, also den
        /// kulturabhängigen Standardvergleich: dort steht 'a' vor 'B', in
        /// Oktett-Reihenfolge dagegen 'B' (0x42) vor 'a' (0x61). Für die aktuelle
        /// Feature-Liste des Clients fallen beide Reihenfolgen zufällig zusammen,
        /// der offizielle Testvektor allein deckt den Fehler also nicht auf.
        /// </summary>
        [Test]
        public void Features_AreSortedByOctetOrder()
        {

            var identity = new DiscoIdentity("client", "pc", "Test");

            var caps = new EntityCapsManager(Disco(identity, "urn:test:a", "urn:test:B"));

            // Oktett-Reihenfolge: 'B' (0x42) vor 'a' (0x61)
            var expected = Sha1Base64("client/pc//Test<urn:test:B<urn:test:a<");

            Assert.That(caps.CalculateVerificationString(), Is.EqualTo(expected));

        }

        #endregion

        #region Identities_AreSortedByOctetOrderIncludingName()

        /// <summary>
        /// REGRESSIONSTEST - XEP-0115 Abschnitt 5.1 sortiert Identitäten über
        /// category/type/xml:lang/name in Oktett-Reihenfolge.
        ///
        /// Zwei Identitäten mit gleicher category/type müssen sich also über den
        /// Namen ordnen, und zwar oktettweise ('B' 0x42 vor 'a' 0x61). Früher
        /// sortierte CalculateVerificationString nur über category/type; für
        /// gleiche Präfixe blieb damit die Einfügereihenfolge stehen.
        /// </summary>
        [Test]
        public void Identities_AreSortedByOctetOrderIncludingName()
        {

            var caps = new EntityCapsManager(
                           DiscoWithIdentities(new DiscoIdentity("client", "pc", "a"),
                                               new DiscoIdentity("client", "pc", "B")));

            var expected = Sha1Base64("client/pc//B<client/pc//a<");

            Assert.That(caps.CalculateVerificationString(), Is.EqualTo(expected));

        }

        #endregion

    }

}
