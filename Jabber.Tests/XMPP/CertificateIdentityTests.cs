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
using System.Security.Cryptography.X509Certificates;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Hermod.XMPP.Server;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.Tests.XMPP
{

    /// <summary>
    /// Welche Domains ein Zertifikat belegt - die Prüfung, auf der
    /// SASL-EXTERNAL steht.
    /// </summary>
    /// <remarks>
    /// Hier steckt die gesamte Beweislast des Verfahrens. Dialback fragt bei
    /// einer hinterlegten Adresse nach und merkt einen Fehler daran, dass die
    /// Antwort ausbleibt; SASL-EXTERNAL merkt gar nichts, wenn diese Funktion
    /// zu grosszügig ist - die Verbindung käme zustande und sähe von aussen
    /// aus wie eine gelungene Prüfung.
    /// </remarks>
    [TestFixture]
    public class CertificateIdentityTests
    {

        #region Hilfsfunktionen

        /// <summary>
        /// Baut ein Zertifikat mit wählbarem Common Name und wählbaren
        /// dNSName-Einträgen.
        /// </summary>
        /// <param name="dnsNamen">
        /// null lässt die SAN-Erweiterung ganz weg; ein leeres Feld erzeugt
        /// sie mit einem Eintrag, der keine Domain ist - beides sind
        /// verschiedene Fälle.
        /// </param>
        private static X509Certificate2 Zertifikat(String    commonName,
                                                   String[]? dnsNamen = null,
                                                   Boolean   nurIpSan = false)
        {

            using var key = RSA.Create(2048);

            var request = new CertificateRequest($"CN={commonName}",
                                                 key,
                                                 HashAlgorithmName.SHA256,
                                                 RSASignaturePadding.Pkcs1);

            if (dnsNamen is not null || nurIpSan)
            {

                var san = new SubjectAlternativeNameBuilder();

                foreach (var name in dnsNamen ?? [])
                    san.AddDnsName(name);

                if (nurIpSan)
                    san.AddIpAddress(System.Net.IPAddress.Loopback);

                request.CertificateExtensions.Add(san.Build());

            }

            var erzeugt = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),
                                                   DateTimeOffset.UtcNow.AddYears(1));

            return X509CertificateLoader.LoadPkcs12(erzeugt.Export(X509ContentType.Pfx), null);

        }

        #endregion


        #region TheSubjectAlternativeNamesAreTheIdentities()

        [Test]
        public void TheSubjectAlternativeNamesAreTheIdentities()
        {

            using var zert = Zertifikat("links.example", ["links.example", "im.links.example"]);

            Assert.Multiple(() =>
            {
                Assert.That(CertificateIdentity.DomainsOf(zert),
                            Is.EquivalentTo(new[] { "links.example", "im.links.example" }));
                Assert.That(CertificateIdentity.Authorises(zert, "links.example"),     Is.True);
                Assert.That(CertificateIdentity.Authorises(zert, "im.links.example"),  Is.True);
            });

        }

        #endregion

        #region AForeignDomain_IsNotAuthorised()

        [Test]
        public void AForeignDomain_IsNotAuthorised()
        {

            using var zert = Zertifikat("links.example", ["links.example"]);

            Assert.That(CertificateIdentity.Authorises(zert, "rechts.example"), Is.False);

        }

        #endregion

        #region TheComparisonIgnoresCase()

        /// <summary>
        /// Domainnamen unterscheiden sich nicht in der Schreibweise.
        /// </summary>
        [Test]
        public void TheComparisonIgnoresCase()
        {

            using var zert = Zertifikat("links.example", ["Links.EXAMPLE"]);

            Assert.That(CertificateIdentity.Authorises(zert, "links.example"), Is.True);

        }

        #endregion

        #region WithoutAnySan_TheCommonNameCounts()

        /// <summary>
        /// Ein Zertifikat ganz ohne SAN-Erweiterung wird über den Common Name
        /// gelesen.
        /// </summary>
        [Test]
        public void WithoutAnySan_TheCommonNameCounts()
        {

            using var zert = Zertifikat("links.example");

            Assert.Multiple(() =>
            {
                Assert.That(CertificateIdentity.DomainsOf(zert), Is.EquivalentTo(new[] { "links.example" }));
                Assert.That(CertificateIdentity.Authorises(zert, "links.example"), Is.True);
            });

        }

        #endregion

        #region WithASan_TheCommonNameNoLongerCounts()

        /// <summary>
        /// Sobald es eine SAN-Erweiterung gibt, zählt der Common Name nicht
        /// mehr (RFC 6125, Abschnitt 6.4.4).
        /// </summary>
        /// <remarks>
        /// Das ist die wichtigste Zeile dieser Datei. Zöge die Prüfung den
        /// Common Name hilfsweise heran, genügte ein Zertifikat mit
        /// <c>CN=opfer.example</c> und irgendeiner harmlosen SAN, um für
        /// <c>opfer.example</c> zu sprechen - und ein solches Zertifikat
        /// bekommt man von jeder CA, die den CN nicht prüft, weil er nach
        /// heutigem Verständnis ohnehin nichts bedeutet.
        /// </remarks>
        [Test]
        public void WithASan_TheCommonNameNoLongerCounts()
        {

            using var zert = Zertifikat("opfer.example", ["harmlos.example"]);

            Assert.Multiple(() =>
            {
                Assert.That(CertificateIdentity.Authorises(zert, "harmlos.example"), Is.True);
                Assert.That(CertificateIdentity.Authorises(zert, "opfer.example"),   Is.False,
                            "Bei vorhandener SAN darf der Common Name nicht mehr gelten.");
            });

        }

        #endregion

        #region ASanWithoutAnyDnsName_AuthorisesNothing()

        /// <summary>
        /// Eine SAN-Erweiterung, die nur eine IP-Adresse nennt, belegt keine
        /// Domain - auch nicht die aus dem Common Name.
        /// </summary>
        [Test]
        public void ASanWithoutAnyDnsName_AuthorisesNothing()
        {

            using var zert = Zertifikat("opfer.example", nurIpSan: true);

            Assert.Multiple(() =>
            {
                Assert.That(CertificateIdentity.DomainsOf(zert), Is.Empty);
                Assert.That(CertificateIdentity.Authorises(zert, "opfer.example"), Is.False);
            });

        }

        #endregion

        #region AWildcard_AuthorisesNothing()

        /// <summary>
        /// Platzhalter gelten hier nicht - weder für die Unterdomain noch für
        /// sich selbst.
        /// </summary>
        /// <remarks>
        /// Bewusst so, und der Test hält es fest, damit die Entscheidung nicht
        /// unbemerkt kippt. Wer Platzhalter zulässt, muss sich auf genau eine
        /// Auslegung festlegen; die geläufigen Fehler dabei - der Platzhalter
        /// deckt mehrere Labels, oder er deckt auch die nackte Domain - sind
        /// beide zu grosszügig.
        /// </remarks>
        [Test]
        public void AWildcard_AuthorisesNothing()
        {

            using var zert = Zertifikat("*.links.example", ["*.links.example"]);

            Assert.Multiple(() =>
            {
                Assert.That(CertificateIdentity.Authorises(zert, "im.links.example"), Is.False);
                Assert.That(CertificateIdentity.Authorises(zert, "links.example"),    Is.False);
            });

        }

        #endregion

        #region AnEmptyDomain_AuthorisesNothing()

        /// <summary>
        /// Nach nichts zu fragen ist keine bestandene Prüfung.
        /// </summary>
        [Test]
        public void AnEmptyDomain_AuthorisesNothing()
        {

            using var zert = Zertifikat("links.example", ["links.example"]);

            Assert.Multiple(() =>
            {
                Assert.That(CertificateIdentity.Authorises(zert, ""),    Is.False);
                Assert.That(CertificateIdentity.Authorises(zert, "   "), Is.False);
            });

        }

        #endregion

    }

}
