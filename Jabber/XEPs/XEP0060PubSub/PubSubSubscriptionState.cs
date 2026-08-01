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
/// Der Zustand eines Abonnements (XEP-0060, Abschnitt 12.19).
/// </summary>
/// <remarks>
/// <b>Vier Zustände und nicht zwei.</b> „Abonniert oder nicht" wäre die
/// naheliegende Verkürzung und genau die, an der ein Client scheitert: Ein
/// <see cref="Pending"/> sieht wie eine Zusage aus - der Dienst hat die
/// Anfrage angenommen -, ist aber keine. Wer beides zusammenwirft, hält sich
/// für abonniert, während noch jemand darüber entscheidet, und wundert sich
/// über die ausbleibenden Benachrichtigungen.
/// </remarks>
public enum PubSubSubscriptionState
{

    /// <summary>Kein Abonnement.</summary>
    None,

    /// <summary>
    /// Beantragt, aber noch nicht genehmigt - der Knoten verlangt die
    /// Zustimmung seines Besitzers.
    /// </summary>
    Pending,

    /// <summary>
    /// Angenommen, aber der Dienst erwartet noch die Konfiguration des
    /// Abonnements, bevor er etwas schickt.
    /// </summary>
    Unconfigured,

    /// <summary>Abonniert - von hier an kommen Benachrichtigungen.</summary>
    Subscribed

}
