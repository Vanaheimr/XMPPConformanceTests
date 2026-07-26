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
/// RFC 6120, Abschnitt 8.3.2: Die Fehlerart sagt dem Absender, wie er
/// reagieren soll.
/// </summary>
public enum StanzaErrorType
{

    /// <summary>Erneut versuchen, nachdem eine Authentifizierung erfolgt ist.</summary>
    Auth,

    /// <summary>Endgültig - dieselbe Anfrage nicht wiederholen.</summary>
    Cancel,

    /// <summary>Nur eine Warnung; die Verarbeitung darf fortgesetzt werden.</summary>
    Continue,

    /// <summary>Erneut versuchen, nachdem die Daten korrigiert wurden.</summary>
    Modify,

    /// <summary>Unverändert erneut versuchen, aber später.</summary>
    Wait

}
