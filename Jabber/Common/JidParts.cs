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
/// Die drei Teile eines JIDs nach RFC 7622, jeder für sich vorbereitet.
/// </summary>
/// <remarks>
/// Nur der Domainpart ist Pflicht: <c>example.com</c> ist ein gültiger JID,
/// <c>juliet@</c> und <c>/foobar</c> sind es nicht.
///
/// Die Teile werden verschieden behandelt, und genau das ist der Grund, warum
/// sie hier einzeln stehen statt als eine Zeichenkette: Local- und Domainpart
/// werden kleingeschrieben und sind damit unabhängig von der Schreibweise,
/// der Resourcepart nicht. <c>alice@example.com/Handy</c> und
/// <c>alice@example.com/handy</c> sind zwei verschiedene Geräte.
/// </remarks>
/// <param name="Localpart">Der Teil vor dem <c>@</c>, oder null.</param>
/// <param name="Domainpart">Der Teil dahinter - das einzige Pflichtstück.</param>
/// <param name="Resourcepart">Der Teil hinter dem ersten <c>/</c>, oder null.</param>
public sealed record JidParts(String?  Localpart,
                              String   Domainpart,
                              String?  Resourcepart)
{

    /// <summary>Der Bare-JID: <c>localpart@domainpart</c> oder nur die Domain.</summary>
    public String Bare

        => Localpart is null
               ? Domainpart
               : $"{Localpart}@{Domainpart}";

    /// <summary>Der vollständige JID in seiner vorbereiteten Form.</summary>
    public override String ToString()

        => Resourcepart is null
               ? Bare
               : $"{Bare}/{Resourcepart}";

}
