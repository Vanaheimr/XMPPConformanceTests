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

using org.GraphDefined.Vanaheimr.Hermod.DNS;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Findet den S2S-Dienst einer Domain über DNS (RFC 6120, Abschnitt
    /// 3.2.1).
    /// </summary>
    /// <remarks>
    /// Gefragt wird nach <c>_xmpp-server._tcp.&lt;domain&gt;</c>. Bleibt die
    /// Antwort aus, gilt der Rückfall aus Abschnitt 3.2.1: die Domain selbst
    /// auf dem Regelport 5269. Ein ausdrückliches <c>.</c> als Ziel ist
    /// dagegen kein Schweigen, sondern eine Absage - dann wird nichts
    /// versucht.
    ///
    /// <b>Nicht gefragt wird nach <c>_xmpps-server._tcp</c></b> (XEP-0368,
    /// direktes TLS ohne STARTTLS). Es liesse sich ergänzen, sobald jemand es
    /// braucht; die Auswahl zwischen beiden Diensten wäre dann eine eigene
    /// Entscheidung und keine Erweiterung dieser Abfrage.
    /// </remarks>
    public sealed class DnsS2SAddressResolver : IS2SAddressResolver
    {

        #region Data

        private readonly IDNSClient _dns;

        /// <summary>Der Dienstname aus RFC 6120, Abschnitt 3.2.1.</summary>
        public const String ServicePrefix = "_xmpp-server._tcp.";

        /// <summary>Der Regelport, wenn es keinen SRV-Eintrag gibt.</summary>
        public const Int32 DefaultPort = TcpStreamFraming.DefaultPort;

        #endregion

        #region Properties

        /// <summary>
        /// Wie lange auf eine Antwort gewartet wird.
        /// </summary>
        public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Soll bei fehlendem SRV-Eintrag auf die Domain selbst
        /// zurückgefallen werden?
        /// </summary>
        /// <remarks>
        /// RFC 6120, Abschnitt 3.2.1 sieht das vor. Abschaltbar, weil ein
        /// Betreiber, der ausschliesslich über SRV veröffentlichte Ziele
        /// erlauben will, sonst stillschweigend woandershin verbunden würde.
        /// </remarks>
        public Boolean FallBackToDomain { get; init; } = true;

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Legt den Resolver über einen DNS-Client an.
        /// </summary>
        public DnsS2SAddressResolver(IDNSClient dnsClient)
        {
            _dns = dnsClient;
        }

        #endregion


        #region ResolveAsync(domain, cancellationToken)

        public async Task<IReadOnlyList<SrvTarget>> ResolveAsync(String             domain,
                                                                 CancellationToken  cancellationToken = default)
        {

            var eintraege = new List<SrvTarget>();

            try
            {

                var name = DNSServiceName.Parse($"{ServicePrefix}{domain}");

                var antwort = await _dns.Query<SRV>(name,
                                                    Timeout:            Timeout,
                                                    CancellationToken:  cancellationToken);

                foreach (var srv in antwort.FilteredAnswers)
                    eintraege.Add(new SrvTarget(srv.Priority,
                                                srv.Weight,
                                                srv.Target.ToString().TrimEnd('.') is { Length: > 0 } t
                                                    ? t
                                                    : SrvSelection.NoService,
                                                srv.Port.ToUInt16()));

            }
            catch (Exception)
            {
                // Kein DNS, keine Antwort, kaputte Antwort - für den Aufrufer
                // ist das dasselbe wie "kein SRV-Eintrag".
                eintraege.Clear();
            }

            if (eintraege.Count > 0)
                return SrvSelection.Order(eintraege);

            // RFC 6120, Abschnitt 3.2.1: ohne SRV-Eintrag die Domain selbst.
            return FallBackToDomain
                       ? [new SrvTarget(0, 0, domain, DefaultPort)]
                       : [];

        }

        #endregion

    }

}
