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
/// Die Zustandsübergänge des Subscription-Handshakes (RFC 6121, Abschnitt 3)
/// aus Sicht des Roster-Eigentümers.
/// </summary>
/// <remarks>
/// <c>To</c> und <c>From</c> sind zwei getrennte Hälften und keine Stufen
/// einer Skala: <c>To</c> heisst "ich sehe den Kontakt", <c>From</c> heisst
/// "der Kontakt sieht mich". Jeder Übergang darf deshalb nur seine eigene
/// Hälfte anfassen und muss die andere stehen lassen - wer das als Abfolge
/// None → To → Both begreift, verliert beim Entzug genau die Gegenrichtung.
///
/// Der Server rechnet dieselben Übergänge bewusst mit eigenem Code. Benutzten
/// beide Seiten dieselbe Hilfsfunktion, bliebe ein gemeinsamer Denkfehler
/// unsichtbar.
/// </remarks>
public static class SubscriptionStateExtensions
{

    /// <summary>Wir dürfen den Kontakt nun sehen: None→To, From→Both.</summary>
    public static SubscriptionState GrantTo(this SubscriptionState state)
        => state is SubscriptionState.From or SubscriptionState.Both
               ? SubscriptionState.Both
               : SubscriptionState.To;

    /// <summary>Wir dürfen den Kontakt nicht mehr sehen: To→None, Both→From.</summary>
    public static SubscriptionState RevokeTo(this SubscriptionState state)
        => state is SubscriptionState.From or SubscriptionState.Both
               ? SubscriptionState.From
               : SubscriptionState.None;

    /// <summary>Der Kontakt darf uns nun sehen: None→From, To→Both.</summary>
    public static SubscriptionState GrantFrom(this SubscriptionState state)
        => state is SubscriptionState.To or SubscriptionState.Both
               ? SubscriptionState.Both
               : SubscriptionState.From;

    /// <summary>Der Kontakt darf uns nicht mehr sehen: From→None, Both→To.</summary>
    public static SubscriptionState RevokeFrom(this SubscriptionState state)
        => state is SubscriptionState.To or SubscriptionState.Both
               ? SubscriptionState.To
               : SubscriptionState.None;

}
