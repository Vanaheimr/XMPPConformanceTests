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
/// XEP-0060, Abschnitt 8.6: Der Antrag auf ein Abonnement, wie er dem
/// Eigentümer vorgelegt und von ihm beantwortet wird.
/// </summary>
/// <param name="NodeId">Der Knoten, um den gebeten wird.</param>
/// <param name="SubscriberJid">Wer bittet.</param>
/// <param name="SubId">
/// Die Kennung des beantragten Abonnements. <b>Sie ist der eigentliche
/// Gegenstand der Antwort</b> - derselbe JID kann mehrfach beantragen, und ohne
/// sie wüsste der Dienst nicht, welcher Antrag beschieden wurde.
/// </param>
/// <param name="Allow">Die Antwort: zusagen oder ablehnen.</param>
/// <remarks>
/// <b>Die zweite Tür zu derselben Entscheidung, und deshalb keine zweite
/// Entscheidung.</b> Genehmigen lässt sich ein Antrag auch über die
/// Abonnentenliste (Abschnitt 8.8.2), und der Server dieses Projekts tut
/// intern beide Male dasselbe. Zwei Türen sind trotzdem nötig: Die Liste ist
/// die Sicht eines Verwalters, das Formular die eines Menschen, dem sein Client
/// eine Frage anzeigt. Wer nur die Liste hätte, verlangte von jedem Client,
/// dass er Abonnenten verwalten kann.
///
/// <b>Ein Formular, das niemand beantworten kann, wäre schlimmer als keines.</b>
/// Deshalb steht das Lesen hier neben dem Schreiben: Wer die Frage stellt, muss
/// die Antwort annehmen - sonst genehmigt ein Mensch etwas, und es geschieht
/// nichts.
/// </remarks>
public sealed record PubSubSubscribeAuthorization(String   NodeId,
                                                  String   SubscriberJid,
                                                  String?  SubId,
                                                  Boolean  Allow = false)
{

    /// <summary>Der Formulartyp dieses Antrags.</summary>
    public const String FormType = "http://jabber.org/protocol/pubsub#subscribe_authorization";

    /// <summary>Das Feld für den Knoten.</summary>
    public const String NodeVariable = "pubsub#node";

    /// <summary>Das Feld für die Kennung des Antrags.</summary>
    public const String SubIdVariable = "pubsub#subid";

    /// <summary>Das Feld für den Antragsteller.</summary>
    public const String SubscriberVariable = "pubsub#subscriber_jid";

    /// <summary>Das Feld für die Antwort.</summary>
    public const String AllowVariable = "pubsub#allow";

    /// <summary>
    /// Die Frage an den Eigentümer (<c>type='form'</c>).
    /// </summary>
    /// <remarks>
    /// Die Vorbelegung von <c>pubsub#allow</c> ist <c>false</c>. Ein Formular,
    /// das schon auf „ja" steht, macht aus dem Wegklicken eine Zusage.
    /// </remarks>
    public XElement ToForm()
        => DataForm.Form("form", FormType,
               DataForm.Field(NodeVariable,       "text-single", "Knoten",         NodeId),
               DataForm.Field(SubIdVariable,      "text-single", "Kennung",        SubId ?? ""),
               DataForm.Field(SubscriberVariable, "jid-single",  "Antragsteller",  SubscriberJid),
               DataForm.Field(AllowVariable,      "boolean",     "Zusagen?",       DataForm.Boolean(Allow)));

    /// <summary>Die Antwort des Eigentümers (<c>type='submit'</c>).</summary>
    public XElement ToSubmit()
        => DataForm.Form("submit", FormType,
               DataForm.Field(NodeVariable,       null, null, NodeId),
               DataForm.Field(SubIdVariable,      null, null, SubId ?? ""),
               DataForm.Field(SubscriberVariable, null, null, SubscriberJid),
               DataForm.Field(AllowVariable,      null, null, DataForm.Boolean(Allow)));

    /// <summary>
    /// Liest eine abgeschickte Antwort - streng, wie jede Anweisung.
    /// </summary>
    /// <returns>
    /// false, wenn es kein abgeschicktes Formular dieses Zwecks ist, ein Feld
    /// fehlt oder einen Wert trägt, der keiner ist.
    /// </returns>
    /// <remarks>
    /// <b>Ohne Knoten, Antragsteller und Antwort ist es keine Antwort.</b> Die
    /// Kennung darf fehlen - ein Antragsteller mit nur einem offenen Antrag
    /// ist auch ohne sie eindeutig, und ein Client, der sie verliert, soll
    /// nicht mit einer erfundenen antworten müssen.
    /// </remarks>
    public static Boolean TryRead(XElement x, out PubSubSubscribeAuthorization? authorization)
        => TryRead(x, "submit", allowRequired: true, out authorization);

    /// <summary>
    /// Liest den vorgelegten Antrag (<c>type='form'</c>).
    /// </summary>
    /// <remarks>
    /// <b>Ohne <c>pubsub#allow</c>, und das ist der Unterschied.</b> Im
    /// vorgelegten Formular ist das Feld die Frage; in der abgeschickten
    /// Antwort ist es die Antwort. Ein Antrag ohne Vorbelegung ist deshalb
    /// vollständig, eine Antwort ohne Entscheidung nicht.
    /// </remarks>
    public static Boolean TryReadRequest(XElement x, out PubSubSubscribeAuthorization? request)
        => TryRead(x, "form", allowRequired: false, out request);

    private static Boolean TryRead(XElement                          x,
                                   String                            art,
                                   Boolean                           allowRequired,
                                   out PubSubSubscribeAuthorization?  authorization)
    {

        authorization = null;

        if (!DataForm.Is(x, art))
            return false;

        String?   node        = null;
        String?   wer         = null;
        String?   kennung     = null;
        Boolean?  zusagen     = null;
        var       richtigeArt = false;

        foreach (var field in DataForm.Fields(x))
        {

            var wert = DataForm.ValueOf(field);

            switch (field.Attr("var"))
            {

                case "FORM_TYPE":
                    if (wert != FormType)
                        return false;
                    richtigeArt = true;
                    break;

                case NodeVariable:        node    = wert;  break;
                case SubscriberVariable:  wer     = wert;  break;

                // Ein leeres Feld ist keine Kennung: Der Antragsteller hat
                // eine, oder er hat keine - eine leere Zeichenkette wäre eine
                // dritte Möglichkeit, die es nicht gibt.
                case SubIdVariable:
                    kennung = String.IsNullOrEmpty(wert) ? null : wert;
                    break;

                case AllowVariable:
                    if (!DataForm.TryBoolean(wert, out var erlaubt))
                        return false;
                    zusagen = erlaubt;
                    break;

            }

        }

        if (!richtigeArt ||
            String.IsNullOrEmpty(node) ||
            String.IsNullOrEmpty(wer)  ||
            (allowRequired && zusagen is null))
        {
            return false;
        }

        authorization = new PubSubSubscribeAuthorization(node, wer, kennung, zusagen ?? false);

        return true;

    }

}
