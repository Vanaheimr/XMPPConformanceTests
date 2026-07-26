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
/// Zugriff auf Stanza-Bestandteile über den XML-Parser statt über Textmuster.
///
/// Gesucht wird bewusst nur über den lokalen Namen, ohne den Namespace zu
/// prüfen: Server binden <c>jabber:client</c> mal als Default-Namespace, mal
/// über ein Präfix, und manche lassen ihn in den Kindelementen ganz weg. Für
/// die Bestandteile einer Stanza - <c>from</c>, <c>body</c>, <c>show</c> und
/// so weiter - ist der lokale Name eindeutig genug.
/// </summary>
public static class StanzaExtensions
{

    /// <summary>
    /// Der Wert eines Attributs, unabhängig von einem Präfix.
    /// </summary>
    public static string? Attr(this XElement element, string name)
        => element.Attributes()
                  .FirstOrDefault(attribute => attribute.Name.LocalName == name)
                  ?.Value;

    /// <summary>
    /// Das erste <b>direkte</b> Kindelement mit diesem Namen.
    ///
    /// Dass nur direkte Kinder zählen, ist der eigentliche Punkt: eine nach
    /// XEP-0297 weitergeleitete Nachricht bringt ihren eigenen
    /// <c>&lt;body/&gt;</c> mit, und der darf den der äusseren Stanza nicht
    /// verdrängen.
    /// </summary>
    public static XElement? Child(this XElement element, string name)
        => element.Elements()
                  .FirstOrDefault(child => child.Name.LocalName == name);

    /// <summary>
    /// Der Textinhalt des ersten direkten Kindelements mit diesem Namen, mit
    /// aufgelösten Entities. Null, wenn es das Element nicht gibt.
    /// </summary>
    public static string? ChildValue(this XElement element, string name)
        => element.Child(name)?.Value;

    /// <summary>
    /// Trägt die Stanza irgendwo ein Element aus diesem Namespace?
    /// </summary>
    public static bool HasNamespace(this XElement element, string namespaceName)
        => element.DescendantsAndSelf()
                  .Any(child => child.Name.NamespaceName == namespaceName);

}
