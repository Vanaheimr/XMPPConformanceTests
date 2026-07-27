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

using System.Security.Cryptography.X509Certificates;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Für welche Domains ein Zertifikat sprechen darf - die Frage, auf der
    /// SASL-EXTERNAL beruht (XEP-0178, Abschnitt 3).
    /// </summary>
    /// <remarks>
    /// Das ist der Unterschied zwischen SASL-EXTERNAL und Dialback. Dialback
    /// belegt eine Domain, indem es bei der hinterlegten Adresse nachfragt;
    /// SASL-EXTERNAL belegt sie, indem es das Zertifikat liest, das die
    /// Gegenstelle im TLS-Handshake vorgelegt hat. Kein zweiter
    /// Verbindungsaufbau, keine Nachfrage - dafür steht und fällt alles mit
    /// dieser Prüfung.
    ///
    /// <b>Was hier absichtlich streng ist:</b>
    ///
    /// <list type="bullet">
    ///   <item>
    ///     <b>Gibt es eine SAN-Erweiterung, zählt der Common Name nicht
    ///     mehr.</b> So will es RFC 6125, Abschnitt 6.4.4, und der Grund ist
    ///     handfest: sonst genügte ein Zertifikat mit passendem CN und
    ///     harmlosen SANs, um jede Prüfung zu bestehen, die den CN noch
    ///     hilfsweise heranzieht.
    ///   </item>
    ///   <item>
    ///     <b>Keine Platzhalter.</b> <c>*.example.com</c> gilt hier für keine
    ///     einzige Domain. XEP-0178 stellt Platzhalter frei; sie richtig zu
    ///     behandeln ist überraschend fehlerträchtig, und eine zu grosszügige
    ///     Auslegung verschenkt genau die Genauigkeit, um derentwillen es
    ///     diese Klasse gibt.
    ///   </item>
    /// </list>
    ///
    /// <b>Was fehlt:</b> <c>id-on-xmppAddr</c> (OID 1.3.6.1.5.5.7.8.5) wird
    /// nicht gelesen, obwohl XEP-0178 es als die eigentlich vorgesehene Form
    /// nennt. Es steckt als <c>otherName</c> in der SAN-Erweiterung, und die
    /// Bibliothek zählt nur dNSName und IP-Adressen auf; es zu lesen hiesse,
    /// ASN.1 von Hand zu zerlegen. Die Folge ist zu benennen: eine
    /// Gegenstelle, deren Zertifikat sie <i>nur</i> über <c>xmppAddr</c>
    /// ausweist, wird hier abgelehnt, obwohl sie im Recht ist. Für sie bleibt
    /// Dialback.
    /// </remarks>
    public static class CertificateIdentity
    {

        #region Data

        /// <summary>OID der Subject Alternative Name-Erweiterung.</summary>
        private const String SubjectAlternativeNameOid = "2.5.29.17";

        #endregion

        #region DomainsOf(certificate)

        /// <summary>
        /// Die Domains, für die dieses Zertifikat ausgestellt ist.
        /// </summary>
        /// <returns>
        /// Die dNSName-Einträge der SAN-Erweiterung; hilfsweise der Common
        /// Name, aber <b>nur</b> wenn es gar keine SAN-Erweiterung gibt.
        /// </returns>
        public static IReadOnlyList<String> DomainsOf(X509Certificate2 certificate)
        {

            var san = certificate.Extensions
                                 .FirstOrDefault(e => e.Oid?.Value == SubjectAlternativeNameOid);

            if (san is not null)
            {

                try
                {

                    var namen = new X509SubjectAlternativeNameExtension(san.RawData, san.Critical);

                    // Auch eine leere Liste ist ein Ergebnis: die Erweiterung
                    // gibt es, sie nennt nur keine Domain. Auf den Common Name
                    // auszuweichen wäre genau das, was RFC 6125 untersagt.
                    return [.. namen.EnumerateDnsNames()];

                }
                catch (Exception)
                {
                    // Unlesbare Erweiterung - dann gilt keine Domain als belegt.
                    return [];
                }

            }

            var commonName = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);

            return String.IsNullOrWhiteSpace(commonName)
                       ? []
                       : [commonName];

        }

        #endregion

        #region Authorises(certificate, domain)

        /// <summary>
        /// Darf dieses Zertifikat für diese Domain sprechen?
        /// </summary>
        /// <remarks>
        /// Verglichen wird ohne Rücksicht auf Gross- und Kleinschreibung -
        /// Domainnamen sind danach nicht zu unterscheiden - aber sonst genau.
        /// </remarks>
        public static Boolean Authorises(X509Certificate2 certificate, String domain)

            => !String.IsNullOrWhiteSpace(domain) &&
               DomainsOf(certificate).Any(d => String.Equals(d, domain, StringComparison.OrdinalIgnoreCase));

        #endregion

    }

}
