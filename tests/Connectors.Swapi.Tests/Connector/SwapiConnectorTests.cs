// Behavioral tests for the transport/orchestration layer: error translation and paging, against a stub client.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Connectors.Swapi;
using Connectors.Swapi.Generated;

namespace Connectors.Swapi.Tests.Connector
{
    public class SwapiConnectorTests
    {
        private sealed class StubClient : ISwapiClient
        {
            public Func<int, Task<Person>> OnGetPerson = _ => throw new NotSupportedException();
            public Func<string?, int?, Task<PeoplePage>> OnListPeople = (_, _) => throw new NotSupportedException();
            public Func<int, Task<Planet>> OnGetPlanet = _ => throw new NotSupportedException();

            public Task<PeoplePage> ListPeopleAsync(string? search = null, int? page = null, CancellationToken cancellationToken = default) => OnListPeople(search, page);
            public Task<Person> GetPersonAsync(int id, CancellationToken cancellationToken = default) => OnGetPerson(id);
            public Task<PlanetsPage> ListPlanetsAsync(string? search = null, int? page = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
            public Task<Planet> GetPlanetAsync(int id, CancellationToken cancellationToken = default) => OnGetPlanet(id);
            public Task<Film> GetFilmAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }

        private static SwapiClientException ProviderError(int status) =>
            new("provider said no", status, "{\"detail\":\"...\"}", new Dictionary<string, IEnumerable<string>>(), null);

        private static Person Luke() => new()
        {
            Name = "Luke Skywalker", Height = "172", Mass = "77", Gender = "male", Birth_year = "19BBY",
            Url = new Uri("https://swapi.dev/api/people/1/"),
        };

        [Fact]
        public async Task GetCharacter_calls_the_allowed_endpoint_and_maps()
        {
            var stub = new StubClient { OnGetPerson = id => Task.FromResult(Luke()) };
            var result = await new SwapiConnector(stub).GetCharacterAsync("1");
            Assert.Equal("1", result.ExternalId);
            Assert.Equal(-19d, result.BirthYear);
        }

        [Theory]
        [InlineData(404, "not_found")]
        [InlineData(401, "unauthorized")]
        [InlineData(403, "unauthorized")]
        [InlineData(429, "provider_unavailable")]
        [InlineData(503, "provider_unavailable")]
        [InlineData(418, "invalid_response")]
        public async Task Provider_errors_are_translated_to_stable_codes(int status, string expectedCode)
        {
            var stub = new StubClient { OnGetPerson = _ => throw ProviderError(status) };
            var ex = await Assert.ThrowsAsync<ConnectorException>(() => new SwapiConnector(stub).GetCharacterAsync("1"));
            Assert.Equal(expectedCode, ex.Code);
            Assert.DoesNotContain("detail", ex.Message); // provider payload never leaks
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("-1")]
        [InlineData("")]
        public async Task Non_numeric_external_ids_are_not_found_without_calling_the_provider(string externalId)
        {
            var called = false;
            var stub = new StubClient { OnGetPerson = _ => { called = true; return Task.FromResult(Luke()); } };
            var ex = await Assert.ThrowsAsync<ConnectorException>(() => new SwapiConnector(stub).GetCharacterAsync(externalId));
            Assert.Equal("not_found", ex.Code);
            Assert.False(called);
        }

        [Fact]
        public async Task SearchCharacters_decodes_the_page_token_and_forwards_search()
        {
            string? seenSearch = null; int? seenPage = null;
            var stub = new StubClient
            {
                OnListPeople = (s, p) => { seenSearch = s; seenPage = p; return Task.FromResult(new PeoplePage { Count = 1, Results = new List<Person> { Luke() }, Next = null }); }
            };
            var first = await new SwapiConnector(stub).SearchCharactersAsync("  sky ", null);
            Assert.Equal("sky", seenSearch);
            Assert.Null(seenPage);
            Assert.Null(first.NextPageToken);

            var page2 = new PeoplePage { Count = 20, Results = new List<Person>(), Next = new Uri("https://swapi.dev/api/people/?search=sky&page=3") };
            stub.OnListPeople = (s, p) => { seenPage = p; return Task.FromResult(page2); };
            var second = await new SwapiConnector(stub).SearchCharactersAsync("sky", "cGFnZT0y"); // base64url("page=2")
            Assert.Equal(2, seenPage);
            Assert.Equal(3, SwapiMapper.DecodePageToken(second.NextPageToken));
        }
    }
}
