// Connector implementation — AI + human owned (slide 05 "connector/").
// Scaffolded once by integration-cli generate; never regenerated while this file exists.
// Transport + error translation only. All semantics live in SwapiMapper.
#nullable enable
using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
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
            var page = SwapiMapper.DecodePageToken(pageToken);
            var result = await Guard(() => _client.ListPeopleAsync((query ?? string.Empty).Trim(), page, cancellationToken)).ConfigureAwait(false);
            return SwapiMapper.ToCharacterPage(result);
        }

        /// <inheritdoc />
        public async Task<Product.Canonical.Planet> GetPlanetAsync(string externalId, CancellationToken cancellationToken = default)
        {
            var id = ParseId(externalId);
            var planet = await Guard(() => _client.GetPlanetAsync(id, cancellationToken)).ConfigureAwait(false);
            return SwapiMapper.ToPlanet(planet);
        }

        // ---- boundary: provider ids and provider errors never leak past this class ----

        private static int ParseId(string externalId) =>
            int.TryParse(externalId?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0
                ? id
                : throw new ConnectorException("externalId must be a positive integer for this provider.", "not_found");

        /// <summary>Translates provider failures into <see cref="ConnectorException"/> per engineering policy. Retries are the resilience handler's job.</summary>
        private static async Task<T> Guard<T>(Func<Task<T>> call)
        {
            try
            {
                return await call().ConfigureAwait(false);
            }
            catch (SwapiClientException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
            {
                throw new ConnectorException("The requested record does not exist at the provider.", "not_found", ex);
            }
            catch (SwapiClientException ex) when (ex.StatusCode is 401 or 403)
            {
                throw new ConnectorException("The provider rejected the credentials.", "unauthorized", ex);
            }
            catch (SwapiClientException ex) when (ex.StatusCode is 408 or 429 or >= 500)
            {
                throw new ConnectorException("The provider is temporarily unavailable.", "provider_unavailable", ex);
            }
            catch (SwapiClientException ex)
            {
                throw new ConnectorException("The provider returned an unexpected response.", "invalid_response", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new ConnectorException("The provider could not be reached.", "provider_unavailable", ex);
            }
            catch (TaskCanceledException ex) when (!ex.CancellationToken.IsCancellationRequested)
            {
                throw new ConnectorException("The provider timed out.", "provider_unavailable", ex);
            }
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
