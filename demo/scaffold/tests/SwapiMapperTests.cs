// Behavioral tests — shared ownership (slide 05 "tests/connector/").
// Scaffolded once by integration-cli generate; the AI completes the assertions from the approved mapping,
// engineers review them. `integration-cli validate` fails while any TODO(ai) marker remains.
#nullable enable
using System;
using System.IO;
using System.Text.Json;
using Xunit;
using Connectors.Swapi.Generated;
using Connectors.Swapi;

namespace Connectors.Swapi.Tests.Connector
{
    public class SwapiMapperTests
    {
        private static T Fixture<T>(string name)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path))
                   ?? throw new InvalidOperationException($"fixture {name} did not deserialize");
        }

        [Fact]
        public void ToCharacter_maps_the_recorded_fixture()
        {
            var source = Fixture<Connectors.Swapi.Generated.Person>("Person.json");

            var result = SwapiMapper.ToCharacter(source);

            Assert.NotNull(result);
            // TODO(ai): assert one expectation per approved field mapping in connectors/swapi/mapping/getCharacter.mapping.json:
            //   externalId  <-  url (last non-empty path segment)  (Take the last non-empty segment of `url` (e.g. https://swapi.dev/api/people/1/ -> "1"). Never store the URL as the id.)
            //   provider  <-  (constant)  (Constant "swapi".)
            //   name  <-  name  (Copy, trimmed.)
            //   heightCm  <-  height  (Parse as invariant integer; "unknown", empty or unparsable -> null.)
            //   massKg  <-  mass  (Remove thousands separators (","), parse as invariant double; "unknown", empty or unparsable -> null.)
            //   birthYear  <-  birth_year  (Match /^(\d+(\.\d+)?)(BBY|ABY)$/ case-insensitively; BBY -> negative, ABY -> positive; "unknown" or no match -> null.)  DECISION: Unparsable non-"unknown" values -> null. Bad calendar data must never fail a lookup.
            //   gender  <-  gender  (Case-insensitive lookup: male -> Male, female -> Female, hermaphrodite -> Other, unknown -> Unknown. "n/a" is ambiguous (see unresolvedQuestions).)  DECISION: "n/a" -> Other. Droids are a real category, not missing data. Only "unknown" and unrecognised values -> Unknown.
            //   hairColors  <-  hair_color  (Split on ",", trim each, drop empty; "n/a" and "none" -> empty list.)
            //   eyeColor  <-  eye_color  (Copy trimmed; "unknown" or "n/a" -> null.)
            //   homeworldId  <-  homeworld (last path segment)  (Last non-empty path segment of the homeworld URL; null/empty -> null.)
            //   filmIds  <-  films[] (last path segment of each)  (Map each URL to its last non-empty path segment, preserving order; null -> empty list.)
            //   sourceUrl  <-  url  (Copy.)
            //   lastModifiedUtc  <-  edited  (Copy `edited` converted to UTC; null -> null. (`created` is ignored.))
        }

        [Fact]
        public void ToCharacter_handles_provider_sentinels()
        {
            // TODO(ai): cover "unknown" / "n/a" / empty inputs for every numeric, enum and list target of ToCharacter;
            //           each must produce the null/empty/Unknown value the transform rule specifies — never throw.
            throw new NotImplementedException("TODO(ai): sentinel cases for ToCharacter");
        }

        [Fact]
        public void ToCharacterPage_maps_the_recorded_fixture()
        {
            var source = Fixture<Connectors.Swapi.Generated.PeoplePage>("PeoplePage.json");

            var result = SwapiMapper.ToCharacterPage(source);

            Assert.NotNull(result);
            // TODO(ai): assert one expectation per approved field mapping in connectors/swapi/mapping/searchCharacters.mapping.json:
            //   items  <-  results[]  (Map each Person with the same rules as getCharacter (reuse ToCharacter). Order preserved.)
            //   totalCount  <-  count  (Copy. Provider `count` is the total across all pages, not the page size.)
            //   nextPageToken  <-  next  (null -> null; otherwise base64url-encode the full `next` URL as an opaque token. On the request side, a non-null pageToken is decoded, its `page` query value parsed and passed to listPeople(search, page).)  DECISION: Encode only the page number as base64url of the text "page=<n>". Never put a provider host in a token the product stores.
            //   (request) search  <-  (request) query  (Pass `query` trimmed as the provider `search` parameter. Empty query -> ConnectorException invalid_response? No: product marks query required, so pass through; provider returns all people for empty search.)
        }

        [Fact]
        public void ToCharacterPage_handles_provider_sentinels()
        {
            // TODO(ai): cover "unknown" / "n/a" / empty inputs for every numeric, enum and list target of ToCharacterPage;
            //           each must produce the null/empty/Unknown value the transform rule specifies — never throw.
            throw new NotImplementedException("TODO(ai): sentinel cases for ToCharacterPage");
        }

        [Fact]
        public void ToPlanet_maps_the_recorded_fixture()
        {
            var source = Fixture<Connectors.Swapi.Generated.Planet>("Planet.json");

            var result = SwapiMapper.ToPlanet(source);

            Assert.NotNull(result);
            // TODO(ai): assert one expectation per approved field mapping in connectors/swapi/mapping/getPlanet.mapping.json:
            //   externalId  <-  url (last non-empty path segment)  (Last non-empty path segment of `url`.)
            //   provider  <-  (constant)  (Constant "swapi".)
            //   name  <-  name  (Copy, trimmed.)
            //   diameterKm  <-  diameter  (Parse invariant integer; "unknown"/unparsable -> null.)
            //   rotationPeriodHours  <-  rotation_period  (Parse invariant double; "unknown"/unparsable -> null.)
            //   orbitalPeriodDays  <-  orbital_period  (Parse invariant double; "unknown"/unparsable -> null.)
            //   surfaceWaterPercent  <-  surface_water  (Parse invariant double; "unknown"/unparsable -> null.)
            //   population  <-  population  (Remove thousands separators, parse invariant long; "unknown"/unparsable -> null.)
            //   climates  <-  climate  (Split on ",", trim, drop empty; "unknown" -> empty list.)
            //   terrains  <-  terrain  (Split on ",", trim, drop empty; "unknown" -> empty list.)
            //   sourceUrl  <-  url  (Copy.)
            //   lastModifiedUtc  <-  edited  (Copy converted to UTC; null -> null.)
        }

        [Fact]
        public void ToPlanet_handles_provider_sentinels()
        {
            // TODO(ai): cover "unknown" / "n/a" / empty inputs for every numeric, enum and list target of ToPlanet;
            //           each must produce the null/empty/Unknown value the transform rule specifies — never throw.
            throw new NotImplementedException("TODO(ai): sentinel cases for ToPlanet");
        }
    }
}
