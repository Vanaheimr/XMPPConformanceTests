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
/// Die vier Werte, die das <c>type</c>-Attribut einer IQ-Stanza annehmen darf
/// (RFC 6120, Abschnitt 8.2.3, Regel 2).
/// </summary>
/// <remarks>
/// Hier und nicht bei Server oder Client, weil die Regel beide angeht: Sie
/// verpflichtet „the recipient <b>or an intermediate router</b>", und dieses
/// Projekt hat von jedem einen. Zwei Aufzählungen könnten auseinanderlaufen,
/// und die Wirkung wäre still - ein Wert, den die eine Seite kennt und die
/// andere nicht, käme je nach Weg durch oder nicht.
/// </remarks>
public static class IqTypes
{

    /// <summary>
    /// Ist das ein vorgesehener Wert? <c>null</c> ist keiner: Das Attribut ist
    /// nach Regel 2 zwingend.
    /// </summary>
    public static Boolean IsKnown(String? type)

        => type is "get" or "set" or "result" or "error";

}
