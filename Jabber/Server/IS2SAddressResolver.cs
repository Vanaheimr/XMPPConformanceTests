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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP.Server
{

    /// <summary>
    /// Findet heraus, wo eine fremde Domain ihren S2S-Dienst anbietet
    /// (RFC 6120, Abschnitt 3.2).
    /// </summary>
    /// <remarks>
    /// Eigene Schnittstelle und nicht der DNS-Client direkt, aus zwei
    /// Gründen. Erstens ist die von Hand gepflegte Gegenstellenliste eine
    /// ebenso gültige Antwort auf dieselbe Frage - im Testaufbau sogar die
    /// einzige brauchbare. Zweitens hängt an dieser Antwort ein Netzzugriff,
    /// und ein Test, der echtes DNS befragt, prüft die Welt statt den Code.
    ///
    /// <b>Die Antwort sagt, wohin verbunden wird - nicht, mit wem.</b> Ohne
    /// DNSSEC ist die Auskunft nicht authentifiziert. Wer sie fälschen kann,
    /// lenkt die Verbindung um; deshalb bleibt die Identität an das gebunden,
    /// was die Gegenstelle vorweist - Zertifikat oder Dialback -, und geprüft
    /// wird stets gegen die <i>gesuchte</i> Domain, nie gegen den gelieferten
    /// Rechnernamen.
    /// </remarks>
    public interface IS2SAddressResolver
    {

        /// <summary>
        /// Die Ziele für eine Domain, in der Reihenfolge, in der sie versucht
        /// werden sollen.
        /// </summary>
        /// <returns>
        /// Leer, wenn die Domain nicht erreichbar ist oder den Dienst
        /// ausdrücklich nicht anbietet.
        /// </returns>
        Task<IReadOnlyList<SrvTarget>> ResolveAsync(String             domain,
                                                    CancellationToken  cancellationToken = default);

    }

}
