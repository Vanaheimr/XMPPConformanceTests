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
    /// Wie ein S2S-Stream eingepackt ist - der einzige Unterschied zwischen
    /// der WebSocket- und der TCP-Strecke, den die Protokollschicht kennen
    /// muss.
    /// </summary>
    /// <remarks>
    /// Diese Schnittstelle ist der Preis für eine Behauptung aus S4b-1:
    /// <see cref="S2SStream"/> sollte für TCP unverändert bleiben. Sie blieb es
    /// nicht. An fünf Stellen stand die Rahmung nach RFC 7395 fest verdrahtet
    /// im Code - <c>&lt;open/&gt;</c>, <c>&lt;close/&gt;</c> und die beiden
    /// Erkennungen dazu. Die Abstraktion hatte also die Form ihrer ersten
    /// Implementierung angenommen, genau wie es im Arbeitsplan als Risiko
    /// vermerkt war. Was <i>tatsächlich</i> gehalten hat, ist alles andere:
    /// Handshake-Ablauf, Dialback, Absenderprüfung, Fehlerbehandlung,
    /// Lebenszyklus.
    ///
    /// Bewusst klein gehalten. Alles, was hier nicht steht, ist beiden
    /// Strecken gemeinsam - unter anderem, dass Stanzas ohne eigenen
    /// Namensraum hinausgehen. Über TCP erben sie damit den
    /// Vorgabe-Namensraum <c>jabber:server</c> des Stream-Wurzelelements, was
    /// genau richtig ist; über WebSocket trägt jeder Rahmen ohnehin für sich.
    /// </remarks>
    public interface IS2SFraming
    {

        /// <summary>
        /// Der Stream-Kopf.
        /// </summary>
        /// <param name="from">Die eigene Domain.</param>
        /// <param name="to">Die Domain der Gegenstelle.</param>
        /// <param name="id">
        /// Die vergebene Stream-ID - nur der antwortende Server setzt sie.
        /// </param>
        String StreamOpen(String from, String? to, String? id);

        /// <summary>Das Ende des Streams.</summary>
        String StreamClose();

        /// <summary>Ist dieser Rahmen ein Stream-Kopf?</summary>
        Boolean IsStreamOpen(String frame);

        /// <summary>Ist dieser Rahmen das Stream-Ende?</summary>
        Boolean IsStreamClose(String frame);

    }

}
