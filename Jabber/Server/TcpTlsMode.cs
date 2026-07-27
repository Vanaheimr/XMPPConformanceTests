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
    /// Wie eine TCP-S2S-Strecke zu TLS kommt.
    /// </summary>
    /// <remarks>
    /// Der Unterschied zwischen <see cref="Direct"/> und <see cref="StartTls"/>
    /// ist nicht die Sicherheit, sondern wer wen erreicht. Beide verschlüsseln
    /// gleich gut; <see cref="StartTls"/> ist aber das, was RFC 6120,
    /// Abschnitt 5.4 vorsieht und was ejabberd und Prosody auf Port 5269
    /// erwarten. <see cref="Direct"/> spart eine Umlaufzeit und ist zwischen
    /// zwei Instanzen dieses Servers das Einfachere.
    /// </remarks>
    public enum TcpTlsMode
    {

        /// <summary>
        /// Klartext. Nur für die Fehlersuche mit einem Mitschnitt - RFC 6120,
        /// Abschnitt 13.7 verlangt für S2S Verschlüsselung.
        /// </summary>
        None,

        /// <summary>
        /// TLS ab dem ersten Byte, ohne Aushandlung im Stream.
        /// </summary>
        Direct,

        /// <summary>
        /// STARTTLS nach RFC 6120, Abschnitt 5.4: der Stream beginnt im
        /// Klartext, handelt TLS aus und fängt danach von vorn an.
        /// </summary>
        StartTls

    }

}
