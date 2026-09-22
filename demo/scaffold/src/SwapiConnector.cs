// Connector implementation — AI + human owned (slide 05 "connector/").
// Scaffolded once by integration-cli generate; never regenerated while this file exists.
// Fill every TODO(ai) block; `integration-cli validate` fails while any marker remains.
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using Connectors.Swapi.Generated;

namespace Connectors.Swapi
{
    /// <summary>
    /// Transport + orchestration for Star Wars API. Talks to the provider ONLY through
    /// <see cref="ISwapiClient"/>; all field semantics live in <see cref="SwapiMapper"/>.
    /// Allowed provider calls: GetPersonAsync, ListPeopleAsync, GetPlanetAsync. Anything else fails the CONTRACT gate.
    /// </summary>
    public sealed class SwapiConnector : ISwapiConnector
    {
        private readonly ISwapiClient _client;

        public SwapiConnector(ISwapiClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <inheritdoc />
        public async Task<Product.Canonical.Character> GetCharacterAsync(string externalId, CancellationToken cancellationToken = default)
        {
            // TODO(ai): getCharacter — call GetPersonAsync (GET /people/{id}/),
            //           translate provider errors per engineering policy (404 -> not found, 429/5xx -> retried by handler, else ConnectorException),
            //           then return SwapiMapper.ToCharacter(response).
            //           Mapping decisions: connectors/swapi/mapping/getCharacter.mapping.json
            await Task.CompletedTask;
            throw new NotImplementedException("TODO(ai): getCharacter");
        }

        /// <inheritdoc />
        public async Task<Product.Canonical.CharacterPage> SearchCharactersAsync(string query, string? pageToken, CancellationToken cancellationToken = default)
        {
            // TODO(ai): searchCharacters — call ListPeopleAsync (GET /people/),
            //           translate provider errors per engineering policy (404 -> not found, 429/5xx -> retried by handler, else ConnectorException),
            //           then return SwapiMapper.ToCharacterPage(response).
            //           Mapping decisions: connectors/swapi/mapping/searchCharacters.mapping.json
            await Task.CompletedTask;
            throw new NotImplementedException("TODO(ai): searchCharacters");
        }

        /// <inheritdoc />
        public async Task<Product.Canonical.Planet> GetPlanetAsync(string externalId, CancellationToken cancellationToken = default)
        {
            // TODO(ai): getPlanet — call GetPlanetAsync (GET /planets/{id}/),
            //           translate provider errors per engineering policy (404 -> not found, 429/5xx -> retried by handler, else ConnectorException),
            //           then return SwapiMapper.ToPlanet(response).
            //           Mapping decisions: connectors/swapi/mapping/getPlanet.mapping.json
            await Task.CompletedTask;
            throw new NotImplementedException("TODO(ai): getPlanet");
        }
    }

    /// <summary>Domain-safe error raised by the connector. Never carries raw provider payloads or secrets.</summary>
    public sealed class ConnectorException : Exception
    {
        public ConnectorException(string message, string code, Exception? inner = null) : base(message, inner) => Code = code;
        /// <summary>Stable application-level code: not_found | provider_unavailable | invalid_response | unauthorized.</summary>
        public string Code { get; }
    }
}
