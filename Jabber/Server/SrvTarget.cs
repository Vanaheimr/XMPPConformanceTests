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
    /// Ein Ziel aus einem SRV-Eintrag (RFC 2782): wo ein Dienst einer Domain
    /// zu erreichen ist.
    /// </summary>
    /// <param name="Priority">
    /// Kleiner ist besser. Ziele höherer Priorität werden erst versucht, wenn
    /// alle niedrigeren Nummern erschöpft sind.
    /// </param>
    /// <param name="Weight">
    /// Verteilt die Last innerhalb derselben Priorität. Null heisst nicht
    /// "nie", sondern "nur wenn der Zufall es will" - RFC 2782 gibt auch
    /// gewichtslosen Zielen eine Chance.
    /// </param>
    /// <param name="Host">Der Rechnername, zu dem verbunden wird.</param>
    /// <param name="Port">Der Port.</param>
    /// <remarks>
    /// <b>Ein SRV-Eintrag sagt, wo etwas liegt - nicht, wer dort antwortet.</b>
    /// DNS ist ohne DNSSEC nicht authentifiziert; wer die Auflösung fälschen
    /// kann, lenkt die Verbindung um. Deshalb bleibt die Identität der
    /// Gegenstelle daran gebunden, was sie vorweist: das Zertifikat wird gegen
    /// die <i>gesuchte Domain</i> geprüft und nicht gegen den hier genannten
    /// Rechnernamen (RFC 6120, Abschnitt 13.7.2.1). Andernfalls genügte ein
    /// gefälschter SRV-Eintrag, um jede Prüfung zu bestehen - man liesse den
    /// Angreifer den Massstab mitbringen, an dem er gemessen wird.
    /// </remarks>
    public sealed record SrvTarget(UInt16  Priority,
                                   UInt16  Weight,
                                   String  Host,
                                   Int32   Port)
    {

        public override String ToString()
            => $"{Host}:{Port} (Priorität {Priority}, Gewicht {Weight})";

    }

}
