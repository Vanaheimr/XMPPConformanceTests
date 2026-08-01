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
/// Die Bedingungen einer Veröffentlichung (XEP-0060, Abschnitt 7.1.5).
/// </summary>
/// <param name="AccessModel">Welches Zugriffsmodell der Knoten haben muss.</param>
/// <param name="MaxItems">Wie viele Einträge er behalten muss.</param>
/// <param name="PersistItems">Ob er ablegen muss.</param>
/// <remarks>
/// <b>Etwas anderes als eine Einstellung: eine Bedingung.</b> Deshalb ist jedes
/// Feld hier <c>null</c>-fähig, und <c>null</c> heisst nicht „Vorgabe", sondern
/// „danach wird nicht gefragt". Wer eine Bedingung mit einer Einstellung
/// verwechselt, setzt beim Veröffentlichen lauter Felder, die niemand nennen
/// wollte.
///
/// Der Sinn steht in XEP-0384, Abschnitt 5.2: Ein OMEMO-Bundle muss offen
/// abrufbar sein, sonst kann niemand verschlüsselt schreiben, der noch in
/// keinem Roster steht. Der Client kann das nicht wissen, ohne den Knoten
/// vorher abzufragen - also sagt er es <i>mit</i> der Veröffentlichung, und der
/// Dienst legt entweder passend an oder weigert sich.
/// </remarks>
public sealed record PubSubPublishOptions(PubSubAccessModel?  AccessModel   = null,
                                          Int32?              MaxItems      = null,
                                          Boolean?            PersistItems  = null)
{

    /// <summary>Der Formulartyp dieser Bedingungen.</summary>
    public const String FormType = "http://jabber.org/protocol/pubsub#publish-options";

    /// <summary>
    /// Liest ein abgeschicktes Bedingungsformular - streng, wie jede
    /// Anweisung.
    /// </summary>
    /// <returns>
    /// false, wenn es keines ist, den falschen Zweck hat oder ein Feld
    /// enthält, über das dieser Dienst nichts zusagen kann. <b>Gerade hier
    /// wäre Nachsicht falsch:</b> Eine Bedingung, die übergangen wird, ist
    /// eine, die der Absender für erfüllt hält.
    /// </returns>
    public static Boolean TryRead(XElement x, out PubSubPublishOptions? options)
    {

        options = null;

        if (!DataForm.Is(x, "submit"))
            return false;

        PubSubAccessModel?  zugriff  = null;
        Int32?              anzahl   = null;
        Boolean?            ablage   = null;

        foreach (var field in DataForm.Fields(x))
        {

            var wert = DataForm.ValueOf(field);

            switch (field.Attr("var"))
            {

                case "FORM_TYPE":
                    if (wert != FormType)
                        return false;
                    break;

                case PubSubNodeConfiguration.AccessModelVariable:
                    if (wert is not ("open" or "presence"))
                        return false;
                    zugriff = wert == "presence" ? PubSubAccessModel.Presence : PubSubAccessModel.Open;
                    break;

                case PubSubNodeConfiguration.MaxItemsVariable:
                    if (!Int32.TryParse(wert, out var gelesen) || gelesen < 1)
                        return false;
                    anzahl = gelesen;
                    break;

                case PubSubNodeConfiguration.PersistItemsVariable:
                    if (!DataForm.TryBoolean(wert, out var ablegen))
                        return false;
                    ablage = ablegen;
                    break;

                default:
                    return false;

            }

        }

        options = new PubSubPublishOptions(zugriff, anzahl, ablage);

        return true;

    }

    /// <summary>
    /// Erfüllt dieser Knoten die Bedingungen?
    /// </summary>
    public Boolean AreMetBy(PubSubNodeConfiguration configuration)

        => (AccessModel  is null || AccessModel  == configuration.AccessModel)  &&
           (MaxItems     is null || MaxItems     == configuration.MaxItems)     &&
           (PersistItems is null || PersistItems == configuration.PersistItems);

    /// <summary>
    /// Die Einstellung, mit der ein neuer Knoten anzulegen ist: die Vorgabe,
    /// überschrieben von dem, was verlangt wurde.
    /// </summary>
    public PubSubNodeConfiguration ApplyTo(PubSubNodeConfiguration configuration)

        => new(AccessModel  ?? configuration.AccessModel,
               MaxItems     ?? configuration.MaxItems,
               PersistItems ?? configuration.PersistItems);

}
