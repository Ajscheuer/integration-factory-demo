// Mapper — AI + human owned. Explicit transformations only (slide 05 "example-crm-mapper.ts").
// Scaffolded once by integration-cli generate from the APPROVED mapping files; never regenerated while this file exists.
// Each TODO(ai) lists the approved field mappings and the human decisions the code must honour.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Connectors.Swapi.Generated;

namespace Connectors.Swapi
{
    public static class SwapiMapper
    {
        private const string ProviderKey = "swapi";

        /// <summary>
        /// Person → Character (operation getCharacter).
        /// Approved mapping: connectors/swapi/mapping/getCharacter.mapping.json
        /// </summary>
        public static Product.Canonical.Character ToCharacter(Connectors.Swapi.Generated.Person source)
        {
            ArgumentNullException.ThrowIfNull(source);
            // TODO(ai): implement the approved field mappings below. Do not add, drop or reinterpret a mapping.
            //   url (last non-empty path segment) -> externalId  [0.97]  Take the last non-empty segment of `url` (e.g. https://swapi.dev/api/people/1/ -> "1"). Never store the URL as the id.
            //   (constant) -> provider  [1.00]  Constant "swapi".
            //   name -> name  [0.99]  Copy, trimmed.
            //   height -> heightCm  [0.90]  Parse as invariant integer; "unknown", empty or unparsable -> null.
            //   mass -> massKg  [0.85]  Remove thousands separators (","), parse as invariant double; "unknown", empty or unparsable -> null.
            //   birth_year -> birthYear  [0.62]  Match /^(\d+(\.\d+)?)(BBY|ABY)$/ case-insensitively; BBY -> negative, ABY -> positive; "unknown" or no match -> null.
            //       HUMAN DECISION: Unparsable non-"unknown" values -> null. Bad calendar data must never fail a lookup.
            //   gender -> gender  [0.55]  Case-insensitive lookup: male -> Male, female -> Female, hermaphrodite -> Other, unknown -> Unknown. "n/a" is ambiguous (see unresolvedQuestions).
            //       HUMAN DECISION: "n/a" -> Other. Droids are a real category, not missing data. Only "unknown" and unrecognised values -> Unknown.
            //   hair_color -> hairColors  [0.88]  Split on ",", trim each, drop empty; "n/a" and "none" -> empty list.
            //   eye_color -> eyeColor  [0.90]  Copy trimmed; "unknown" or "n/a" -> null.
            //   homeworld (last path segment) -> homeworldId  [0.95]  Last non-empty path segment of the homeworld URL; null/empty -> null.
            //   films[] (last path segment of each) -> filmIds  [0.95]  Map each URL to its last non-empty path segment, preserving order; null -> empty list.
            //   url -> sourceUrl  [0.99]  Copy.
            //   edited -> lastModifiedUtc  [0.80]  Copy `edited` converted to UTC; null -> null. (`created` is ignored.)
            throw new NotImplementedException("TODO(ai): ToCharacter");
        }

        /// <summary>
        /// PeoplePage → CharacterPage (operation searchCharacters).
        /// Approved mapping: connectors/swapi/mapping/searchCharacters.mapping.json
        /// </summary>
        public static Product.Canonical.CharacterPage ToCharacterPage(Connectors.Swapi.Generated.PeoplePage source)
        {
            ArgumentNullException.ThrowIfNull(source);
            // TODO(ai): implement the approved field mappings below. Do not add, drop or reinterpret a mapping.
            //   results[] -> items  [0.95]  Map each Person with the same rules as getCharacter (reuse ToCharacter). Order preserved.
            //   count -> totalCount  [0.90]  Copy. Provider `count` is the total across all pages, not the page size.
            //   next -> nextPageToken  [0.72]  null -> null; otherwise base64url-encode the full `next` URL as an opaque token. On the request side, a non-null pageToken is decoded, its `page` query value parsed and passed to listPeople(search, page).
            //       HUMAN DECISION: Encode only the page number as base64url of the text "page=<n>". Never put a provider host in a token the product stores.
            //   (request) query -> (request) search  [0.90]  Pass `query` trimmed as the provider `search` parameter. Empty query -> ConnectorException invalid_response? No: product marks query required, so pass through; provider returns all people for empty search.
            throw new NotImplementedException("TODO(ai): ToCharacterPage");
        }

        /// <summary>
        /// Planet → Planet (operation getPlanet).
        /// Approved mapping: connectors/swapi/mapping/getPlanet.mapping.json
        /// </summary>
        public static Product.Canonical.Planet ToPlanet(Connectors.Swapi.Generated.Planet source)
        {
            ArgumentNullException.ThrowIfNull(source);
            // TODO(ai): implement the approved field mappings below. Do not add, drop or reinterpret a mapping.
            //   url (last non-empty path segment) -> externalId  [0.97]  Last non-empty path segment of `url`.
            //   (constant) -> provider  [1.00]  Constant "swapi".
            //   name -> name  [0.99]  Copy, trimmed.
            //   diameter -> diameterKm  [0.90]  Parse invariant integer; "unknown"/unparsable -> null.
            //   rotation_period -> rotationPeriodHours  [0.85]  Parse invariant double; "unknown"/unparsable -> null.
            //   orbital_period -> orbitalPeriodDays  [0.85]  Parse invariant double; "unknown"/unparsable -> null.
            //   surface_water -> surfaceWaterPercent  [0.80]  Parse invariant double; "unknown"/unparsable -> null.
            //   population -> population  [0.82]  Remove thousands separators, parse invariant long; "unknown"/unparsable -> null.
            //   climate -> climates  [0.90]  Split on ",", trim, drop empty; "unknown" -> empty list.
            //   terrain -> terrains  [0.90]  Split on ",", trim, drop empty; "unknown" -> empty list.
            //   url -> sourceUrl  [0.99]  Copy.
            //   edited -> lastModifiedUtc  [0.80]  Copy converted to UTC; null -> null.
            throw new NotImplementedException("TODO(ai): ToPlanet");
        }
    }
}
