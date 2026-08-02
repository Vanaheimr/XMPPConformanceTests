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

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// XEP-0060: Art eines PubSub-Events.
/// </summary>
public enum PubSubEventType
{
    Items,      // Neue/aktualisierte Items
    Retract,    // Items gelöscht
    Purge,      // Node geleert
    Delete,     // Node gelöscht
    Configuration, // Node-Config geändert

    /// <summary>
    /// Ein Abonnement wurde beendet, ohne dass dieser Client danach gefragt
    /// hätte (XEP-0060, Abschnitt 8.8.4).
    /// </summary>
    /// <remarks>
    /// <b>Beendet und nicht „geändert".</b> Die andere Richtung - eine Zusage
    /// per Meldung - trägt dieser Client nicht ein: Eine Zusage kommt auf eine
    /// Anfrage. Wer sie ungefragt annähme, liesse sich von einem Dienst
    /// anmelden, und genau das weist der Server dieses Projekts auf der
    /// anderen Seite ab.
    /// </remarks>
    SubscriptionEnded,

    /// <summary>
    /// Ein beantragtes Abonnement wurde zugesagt (XEP-0060, Abschnitt 8.6).
    /// </summary>
    /// <remarks>
    /// <b>Die Antwort auf eine eigene Frage, und nur die.</b> Sie kommt später
    /// als die Frage - dazwischen liegt ein Mensch, der sie beantwortet -, und
    /// deshalb kommt sie als Meldung und nicht als Antwort auf das IQ. Wer
    /// dazu keinen offenen Antrag hat, bekommt dieses Ereignis nicht: Eine
    /// unverlangte Zusage bliebe eine Anmeldung durch einen anderen.
    /// </remarks>
    SubscriptionApproved
}
