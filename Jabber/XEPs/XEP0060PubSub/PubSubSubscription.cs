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

using System.Xml.Linq;

#endregion

namespace org.GraphDefined.Vanaheimr.Hermod.XMPP;

/// <summary>
/// Was der Dienst über ein Abonnement gesagt hat (XEP-0060, Abschnitt 6.1.2).
/// </summary>
/// <param name="NodeId">Der Knoten.</param>
/// <param name="ServiceJid">
/// Bei wem abonniert wurde - die Adresse, an die die Anfrage ging.
/// </param>
/// <param name="SubId">
/// Die Kennung des Abonnements, oder null: Ein Dienst muss keine vergeben,
/// solange es nur eines gibt (Abschnitt 12.19).
/// </param>
/// <param name="State">Der Zustand aus der Antwort.</param>
/// <param name="Options">
/// Die Einstellungen dieses Abonnements, oder null - <b>null heisst „nicht
/// gefragt" und nicht „Vorgabe".</b> Was gilt, sagt der Dienst; ein anderes
/// Gerät desselben Kontos kann dasselbe Abonnement umgestellt haben.
/// </param>
/// <remarks>
/// <b>Das ist der Ertrag der Korrelation.</b> Nichts davon kann ein Client
/// wissen, bevor die Antwort da ist - die Kennung schon gar nicht, denn sie
/// kommt vom Dienst. Wer die Anfrage abschickt und sein Abonnement gleich
/// einträgt, verwechselt eine Absicht mit einer Tatsache.
/// </remarks>
public sealed record PubSubSubscription(String                      NodeId,
                                        String                      ServiceJid,
                                        String?                     SubId,
                                        PubSubSubscriptionState     State,
                                        PubSubSubscriptionOptions?  Options = null)
{

    /// <summary>Der Namensraum von XEP-0060.</summary>
    public const String Namespace = "http://jabber.org/protocol/pubsub";

    /// <summary>
    /// Liest die Zusage aus einer IQ-Antwort.
    /// </summary>
    /// <param name="iq">Die Antwort des Dienstes.</param>
    /// <param name="serviceJid">Die Adresse, an die gefragt wurde.</param>
    /// <returns>
    /// false, wenn die Antwort keine Zusage enthält - dann sagt sie über das
    /// Abonnement nichts, und das ist etwas anderes als eine Absage.
    /// </returns>
    public static Boolean TryRead(XElement              iq,
                                  String                serviceJid,
                                  out PubSubSubscription?  subscription)
    {

        subscription = null;

        var zusage = iq.Child(Namespace, "pubsub")?.Child(Namespace, "subscription");

        if (zusage?.Attr("node") is not String node || node.Length == 0)
            return false;

        // Die Adresse, an die gefragt wurde - nicht das 'from' der Antwort.
        //
        // Das ist kein Detail: An dieser Adresse hängt später die Frage, von
        // wem Meldungen über diesen Knoten angenommen werden. Käme sie aus der
        // Antwort, könnte eine Gegenstelle sich selbst zu einer Quelle
        // erklären, nach der niemand gefragt hat.
        subscription = new PubSubSubscription(node,
                                              serviceJid,
                                              zusage.Attr("subid"),
                                              StateOf(zusage.Attr("subscription")));

        return true;

    }

    /// <summary>
    /// Der Zustand hinter seinem Namen - alles Unbekannte gilt als
    /// <see cref="PubSubSubscriptionState.None"/>.
    /// </summary>
    /// <remarks>
    /// Ein Zustand, den dieser Client nicht kennt, darf nicht als Zusage
    /// durchgehen. Die Vorsicht kostet hier nichts: Wer sich zu Unrecht für
    /// nicht abonniert hält, fragt noch einmal - wer sich zu Unrecht für
    /// abonniert hält, wartet auf etwas, das nie kommt.
    /// </remarks>
    public static PubSubSubscriptionState StateOf(String? name)
        => name switch {
               "subscribed"    => PubSubSubscriptionState.Subscribed,
               "pending"       => PubSubSubscriptionState.Pending,
               "unconfigured"  => PubSubSubscriptionState.Unconfigured,
               _               => PubSubSubscriptionState.None
           };

    /// <summary>
    /// Derselbe Name, streng gelesen: false, wenn es kein Zustandsname ist.
    /// </summary>
    /// <remarks>
    /// <b>Dieselbe Unterscheidung wie bei den Formularen.</b> Eine Antwort wird
    /// nachsichtig gelesen - was dieser Client nicht kennt, gilt als „nicht
    /// abonniert", und das ist die sichere Annahme. Eine <i>Anweisung</i> wird
    /// streng gelesen: Dort ist <c>none</c> das Beenden eines Abonnements, und
    /// ein Tippfehler dürfte nicht dasselbe bewirken.
    /// </remarks>
    public static Boolean TryReadState(String? name, out PubSubSubscriptionState state)
    {

        state = StateOf(name);

        return name is "none" or "subscribed" or "pending" or "unconfigured";

    }

}
